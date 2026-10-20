using System.Runtime.CompilerServices;
using AtomNES.Device.Bus;
using AtomNES.Device.Display;

namespace AtomNES.Device.Ppu;

/// <summary>
/// Ricoh 2C02 (NTSC) PPU，逐点（dot）精确实现。
/// <para>相对旧实现（Olc2C02）修复/新增的硬件行为：</para>
/// <list type="bullet">
/// <item>VBlank 置位/清除时序、$2002 读取竞争（提前 1 点读取会吞掉 VBlank 与 NMI）；NMI 由电平线输出，开启 NMI 时若 VBlank 已置位立即触发。</item>
/// <item>奇数帧在预渲染行跳过最后一个点（仅渲染开启时）。</item>
/// <item>逐周期精灵评估状态机：次级 OAM 清除、OAMADDR 起始偏移、8 个精灵后的溢出 bug（n/m 同时递增）。</item>
/// <item>精灵 0 命中：x=255 不命中、左侧 8 像素裁剪、背景/精灵必须同时开启。</item>
/// <item>精灵图样按真实时序（257-320 点）抓取，空槽抓取 tile $FF，MMC3 等依赖 A12 的 Mapper 计数正确。</item>
/// <item>渲染期间读写 $2007 触发 coarse X / Y 同时递增的硬件故障；渲染期间写 $2004 只递增地址。</item>
/// <item>OAMADDR 在 257-320 点被清零；OAM 第 3 字节 bit2-4 不存在（读回被屏蔽）。</item>
/// <item>PPU 开放总线（I/O 锁存器）与约 600ms 的逐位衰减；调色板读取高 2 位来自开放总线。</item>
/// <item>$2007 读缓冲：调色板读取直接返回，同时用下方名称表镜像填充缓冲。</item>
/// <item>渲染关闭时若 v 指向调色板区，输出该调色板颜色（"背景色 hack"）。</item>
/// <item>灰度与 RGB 强调位；上电调色板初值。</item>
/// </list>
/// </summary>
public sealed class Ppu2C02 : IPpuDevice
{
    public const int DotsPerScanline = 341;
    public const int ScanlinesPerFrame = 262;
    public const int PreRenderScanline = 261;
    public const int VBlankScanline = 241;

    // 约 600ms（36 帧）后开放总线位衰减为 0
    private const int OpenBusDecayFrames = 36;

    private static ReadOnlySpan<byte> PowerUpPalette =>
    [
        0x09, 0x01, 0x00, 0x01, 0x00, 0x02, 0x02, 0x0D, 0x08, 0x10, 0x08, 0x24, 0x00, 0x00, 0x04, 0x2C,
        0x09, 0x01, 0x34, 0x03, 0x00, 0x04, 0x00, 0x14, 0x08, 0x3A, 0x00, 0x02, 0x00, 0x20, 0x2C, 0x08
    ];

    private readonly byte[] _palette = new byte[32];
    private readonly byte[] _oam = new byte[256];
    private readonly byte[] _secondaryOam = new byte[32];
    private readonly long[] _openBusStamp = new long[8];

    private ICartridgeDevice? _cart;

    // ---------- 时序 ----------
    private int _scanline;
    private int _dot;
    private long _frame;
    private bool _oddFrame;
    private bool _renderingEnabled;

    // ---------- 寄存器 ----------
    private byte _ctrl;
    private byte _mask;
    private bool _vblank;
    private bool _sprite0Hit;
    private bool _spriteOverflow;
    private bool _suppressVblank;

    private ushort _v;
    private ushort _t;
    private byte _fineX;
    private bool _w;

    private byte _readBuffer;
    private byte _oamAddr;
    private byte _openBus;

    // ---------- 背景流水线 ----------
    private byte _ntLatch;
    private byte _atLatch;
    private byte _patLoLatch;
    private byte _patHiLatch;
    private ushort _bgShiftLo;
    private ushort _bgShiftHi;
    private ushort _atShiftLo;
    private ushort _atShiftHi;

    // ---------- 精灵评估 ----------
    private byte _oamCopyBuffer;
    private int _secondaryAddr;
    private int _spriteAddrH;
    private int _spriteAddrL;
    private bool _spriteInRange;
    private bool _oamCopyDone;
    private int _overflowBugCounter;
    private bool _sprite0Added;

    // ---------- 当前行精灵（由上一行评估 + 257-320 抓取得到） ----------
    private readonly byte[] _sprX = new byte[8];
    private readonly byte[] _sprAttr = new byte[8];
    private readonly byte[] _sprLo = new byte[8];
    private readonly byte[] _sprHi = new byte[8];
    private int _sprCount;
    private bool _sprite0OnLine;

    // 抓取阶段的下一行精灵（避免抓取时覆盖本行仍在使用的数据——实际上 257 之后本行已绘制完毕）
    private int _fetchCount;
    private bool _fetchSprite0;

    public Ppu2C02(FrameBuffer frameBuffer)
    {
        Screen = frameBuffer;
        PowerOn();
    }

    public FrameBuffer Screen { get; }

    public bool FrameComplete { get; set; }

    public bool NmiLine => _vblank && (_ctrl & 0x80) != 0;

    public int Scanline => _scanline;

    public int Dot => _dot;

    public long FrameNumber => _frame;

    public ReadOnlySpan<byte> PaletteRam => _palette;

    public ReadOnlySpan<byte> Oam => _oam;

    public byte Ctrl => _ctrl;

    public byte Mask => _mask;

    public ushort V => _v;

    public ushort T => _t;

    public byte FineX => _fineX;

    public bool VBlank => _vblank;

    /// <summary>调试器：将图案表渲染为 128x128 BGRA（使用指定调色板 0-7）。</summary>
    public void RenderPatternTable(int table, int paletteIndex, Span<uint> output)
    {
        if (_cart is null || output.Length < 128 * 128) return;
        var colors = NesPalette.Table;
        var baseAddr = table * 0x1000;
        for (var tile = 0; tile < 256; tile++)
        {
            var tx = (tile & 15) * 8;
            var ty = (tile >> 4) * 8;
            for (var row = 0; row < 8; row++)
            {
                var addr = (ushort)(baseAddr + tile * 16 + row);
                var lo = _cart.PpuPeek(addr);
                var hi = _cart.PpuPeek((ushort)(addr + 8));
                for (var col = 0; col < 8; col++)
                {
                    var bit = 7 - col;
                    var px = ((lo >> bit) & 1) | (((hi >> bit) & 1) << 1);
                    var idx = px == 0 ? _palette[0] : _palette[(paletteIndex << 2) + px];
                    output[(ty + row) * 128 + tx + col] = colors[idx & 0x3F];
                }
            }
        }
    }

    /// <summary>调试器：将 4 个名称表渲染为 512x480 BGRA。</summary>
    public void RenderNameTables(Span<uint> output)
    {
        if (_cart is null || output.Length < 512 * 480) return;
        var colors = NesPalette.Table;
        var bgTable = (_ctrl & 0x10) != 0 ? 0x1000 : 0;
        for (var nt = 0; nt < 4; nt++)
        {
            var ntBase = (ushort)(0x2000 + nt * 0x400);
            var ox = (nt & 1) * 256;
            var oy = (nt >> 1) * 240;
            for (var cy = 0; cy < 30; cy++)
            for (var cx = 0; cx < 32; cx++)
            {
                var tileId = _cart.PpuPeek((ushort)(ntBase + cy * 32 + cx));
                var attr = _cart.PpuPeek((ushort)(ntBase + 0x3C0 + (cy >> 2) * 8 + (cx >> 2)));
                var pal = (attr >> (((cy & 2) << 1) | (cx & 2))) & 3;
                for (var row = 0; row < 8; row++)
                {
                    var addr = (ushort)(bgTable + tileId * 16 + row);
                    var lo = _cart.PpuPeek(addr);
                    var hi = _cart.PpuPeek((ushort)(addr + 8));
                    var o = (oy + cy * 8 + row) * 512 + ox + cx * 8;
                    for (var col = 0; col < 8; col++)
                    {
                        var bit = 7 - col;
                        var px = ((lo >> bit) & 1) | (((hi >> bit) & 1) << 1);
                        var idx = px == 0 ? _palette[0] : _palette[(pal << 2) + px];
                        output[o + col] = colors[idx & 0x3F];
                    }
                }
            }
        }
    }

    public void ConnectCartridge(ICartridgeDevice? cartridge)
    {
        _cart = cartridge;
        if (cartridge is Cartridges.Mapper mapper) mapper.Ppu = this;
    }

    public void PowerOn()
    {
        PowerUpPalette.CopyTo(_palette);
        Array.Clear(_oam);
        Array.Clear(_secondaryOam);
        Array.Clear(_openBusStamp);
        _openBus = 0;
        _vblank = false;
        _sprite0Hit = false;
        _spriteOverflow = false;
        _oamAddr = 0;
        _v = 0;
        Reset();
    }

    public void Reset()
    {
        _ctrl = 0;
        _mask = 0;
        _t = 0;
        _fineX = 0;
        _w = false;
        _readBuffer = 0;
        _scanline = 0;
        _dot = 0;
        _frame = 0;
        _oddFrame = false;
        _renderingEnabled = false;
        _suppressVblank = false;
        _sprCount = 0;
        _fetchCount = 0;
        FrameComplete = false;
    }

    #region CPU 寄存器

    public byte ReadRegister(int register)
    {
        switch (register & 7)
        {
            case 2:
            {
                var value = (byte)((_vblank ? 0x80 : 0) | (_sprite0Hit ? 0x40 : 0) | (_spriteOverflow ? 0x20 : 0));
                value |= (byte)(GetOpenBus() & 0x1F);
                RefreshOpenBus(0xE0, value);
                _w = false;
                _vblank = false;

                // 在 VBlank 置位前 1 个点读取：本帧 VBlank 不会置位，NMI 也不会产生
                if (_scanline == VBlankScanline && _dot == 1)
                {
                    _suppressVblank = true;
                }

                return value;
            }
            case 4:
            {
                var value = ReadOamData();
                RefreshOpenBus(0xFF, value);
                return value;
            }
            case 7:
            {
                byte value;
                var address = (ushort)(_v & 0x3FFF);
                if (address >= 0x3F00)
                {
                    value = (byte)((ReadPalette(address) & 0x3F) | (GetOpenBus() & 0xC0));
                    _readBuffer = ReadVram((ushort)(address & 0x2FFF));
                    RefreshOpenBus(0x3F, value);
                }
                else
                {
                    value = _readBuffer;
                    _readBuffer = ReadVram(address);
                    RefreshOpenBus(0xFF, value);
                }

                IncrementVramAfterAccess();
                return value;
            }
            default:
                return GetOpenBus();
        }
    }

    public byte PeekRegister(int register)
    {
        return (register & 7) switch
        {
            0 => _ctrl,
            1 => _mask,
            2 => (byte)((_vblank ? 0x80 : 0) | (_sprite0Hit ? 0x40 : 0) | (_spriteOverflow ? 0x20 : 0) | (_openBus & 0x1F)),
            3 => _oamAddr,
            4 => _oam[_oamAddr],
            7 => _readBuffer,
            _ => _openBus
        };
    }

    public void WriteRegister(int register, byte value)
    {
        RefreshOpenBus(0xFF, value);
        switch (register & 7)
        {
            case 0:
                _ctrl = value;
                _t = (ushort)((_t & 0xF3FF) | ((value & 0x03) << 10));
                break;
            case 1:
                _mask = value;
                break;
            case 3:
                _oamAddr = value;
                break;
            case 4:
                if (IsRenderingLine && _renderingEnabled)
                {
                    // 渲染期间写入被忽略，只发生地址的高 6 位递增
                    _oamAddr += 4;
                }
                else
                {
                    _oam[_oamAddr] = (_oamAddr & 3) == 2 ? (byte)(value & 0xE3) : value;
                    _oamAddr++;
                }

                break;
            case 5:
                if (!_w)
                {
                    _t = (ushort)((_t & 0xFFE0) | (value >> 3));
                    _fineX = (byte)(value & 0x07);
                }
                else
                {
                    _t = (ushort)((_t & 0x8C1F) | ((value & 0x07) << 12) | ((value & 0xF8) << 2));
                }

                _w = !_w;
                break;
            case 6:
                if (!_w)
                {
                    _t = (ushort)((_t & 0x00FF) | ((value & 0x3F) << 8));
                }
                else
                {
                    _t = (ushort)((_t & 0xFF00) | value);
                    _v = _t;
                    if (!(IsRenderingLine && _renderingEnabled)) _cart?.NotifyPpuAddress(_v);
                }

                _w = !_w;
                break;
            case 7:
            {
                var address = (ushort)(_v & 0x3FFF);
                if (address >= 0x3F00)
                {
                    WritePalette(address, value);
                }
                else
                {
                    _cart?.PpuWrite(address, value);
                }

                IncrementVramAfterAccess();
                break;
            }
        }
    }

    private bool IsRenderingLine => _scanline < 240 || _scanline == PreRenderScanline;

    private void IncrementVramAfterAccess()
    {
        if (IsRenderingLine && _renderingEnabled)
        {
            // 渲染期间访问 $2007：硬件同时执行一次 coarse X 与 Y 递增
            IncrementX();
            IncrementY();
        }
        else
        {
            _v = (ushort)((_v + ((_ctrl & 0x04) != 0 ? 32 : 1)) & 0x7FFF);
            _cart?.NotifyPpuAddress(_v);
        }
    }

    private byte ReadOamData()
    {
        if (IsRenderingLine && _renderingEnabled)
        {
            if (_dot >= 1 && _dot <= 64)
            {
                return 0xFF;
            }

            if (_dot <= 256)
            {
                return _oamCopyBuffer;
            }

            if (_dot <= 320)
            {
                return _secondaryOam[Math.Min(31, ((_dot - 257) >> 3) * 4 + Math.Min(3, ((_dot - 257) & 7)))];
            }

            return _secondaryOam[0];
        }

        return _oam[_oamAddr];
    }

    public void WriteOam(byte address, byte value)
    {
        _oam[address] = (address & 3) == 2 ? (byte)(value & 0xE3) : value;
    }

    private void RefreshOpenBus(byte mask, byte value)
    {
        _openBus = (byte)((_openBus & ~mask) | (value & mask));
        for (var i = 0; i < 8; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                _openBusStamp[i] = _frame;
            }
        }
    }

    private byte GetOpenBus()
    {
        for (var i = 0; i < 8; i++)
        {
            if ((_openBus & (1 << i)) != 0 && _frame - _openBusStamp[i] > OpenBusDecayFrames)
            {
                _openBus &= (byte)~(1 << i);
            }
        }

        return _openBus;
    }

    #endregion

    #region PPU 内存

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte ReadVram(ushort address) => _cart?.PpuRead(address) ?? 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PaletteIndex(ushort address)
    {
        var index = address & 0x1F;
        if ((index & 0x13) == 0x10)
        {
            index &= 0x0F;
        }

        return index;
    }

    private byte ReadPalette(ushort address)
    {
        var value = _palette[PaletteIndex(address)];
        return (_mask & 0x01) != 0 ? (byte)(value & 0x30) : value;
    }

    private void WritePalette(ushort address, byte value)
    {
        _palette[PaletteIndex(address)] = (byte)(value & 0x3F);
    }

    #endregion

    #region 滚动寄存器操作（loopy）

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void IncrementX()
    {
        if ((_v & 0x001F) == 31)
        {
            _v = (ushort)((_v & ~0x001F) ^ 0x0400);
        }
        else
        {
            _v++;
        }
    }

    private void IncrementY()
    {
        if ((_v & 0x7000) != 0x7000)
        {
            _v += 0x1000;
            return;
        }

        _v &= 0x0FFF;
        var y = (_v & 0x03E0) >> 5;
        if (y == 29)
        {
            y = 0;
            _v ^= 0x0800;
        }
        else if (y == 31)
        {
            y = 0;
        }
        else
        {
            y++;
        }

        _v = (ushort)((_v & ~0x03E0) | (y << 5));
    }

    #endregion

    #region 时钟

    public void Clock()
    {
        if (_scanline < 240)
        {
            if (_renderingEnabled)
            {
                RenderingCycle(false);
            }

            if (_dot >= 1 && _dot <= 256)
            {
                OutputPixel();
            }
        }
        else if (_scanline == VBlankScanline)
        {
            if (_dot == 1)
            {
                if (!_suppressVblank)
                {
                    _vblank = true;
                }

                _suppressVblank = false;
                Screen.Present();
                FrameComplete = true;
            }
        }
        else if (_scanline == PreRenderScanline)
        {
            if (_dot == 1)
            {
                _vblank = false;
                _sprite0Hit = false;
                _spriteOverflow = false;
            }

            if (_renderingEnabled)
            {
                RenderingCycle(true);

                // 奇数帧：跳过预渲染行最后一个点
                if (_dot == 339 && _oddFrame)
                {
                    _dot = 340;
                }
            }
        }

        _renderingEnabled = (_mask & 0x18) != 0;

        if (++_dot > 340)
        {
            _dot = 0;
            if (++_scanline > PreRenderScanline)
            {
                _scanline = 0;
                _frame++;
                _oddFrame = !_oddFrame;
            }
        }
    }

    private void RenderingCycle(bool preRender)
    {
        var dot = _dot;
        if (dot == 0)
        {
            return;
        }

        // ---- 背景 ----
        if (dot <= 257 || (dot >= 321 && dot <= 337))
        {
            if (dot >= 2 && dot != 321)
            {
                _bgShiftLo <<= 1;
                _bgShiftHi <<= 1;
                _atShiftLo <<= 1;
                _atShiftHi <<= 1;
            }

            if (dot <= 256 || dot >= 321)
            {
                switch (dot & 7)
                {
                    case 1:
                        if (dot >= 9 || dot == 329 || dot == 337)
                        {
                            ReloadBackgroundShifters();
                        }

                        _ntLatch = ReadVram((ushort)(0x2000 | (_v & 0x0FFF)));
                        break;
                    case 3:
                    {
                        var at = ReadVram((ushort)(0x23C0 | (_v & 0x0C00) | ((_v >> 4) & 0x38) | ((_v >> 2) & 0x07)));
                        var shift = ((_v >> 4) & 0x04) | (_v & 0x02);
                        _atLatch = (byte)((at >> shift) & 0x03);
                        break;
                    }
                    case 5:
                        _patLoLatch = ReadVram(BackgroundPatternAddress());
                        break;
                    case 7:
                        _patHiLatch = ReadVram((ushort)(BackgroundPatternAddress() + 8));
                        break;
                    case 0:
                        IncrementX();
                        if (dot == 256)
                        {
                            IncrementY();
                        }

                        break;
                }
            }
            else
            {
                // dot == 257
                ReloadBackgroundShifters();
                _v = (ushort)((_v & ~0x041F) | (_t & 0x041F));
            }
        }
        else if (dot == 339)
        {
            ReadVram((ushort)(0x2000 | (_v & 0x0FFF)));
        }

        if (preRender && dot >= 280 && dot <= 304)
        {
            _v = (ushort)((_v & ~0x7BE0) | (_t & 0x7BE0));
        }

        // ---- 精灵 ----
        if (dot <= 256)
        {
            if (!preRender)
            {
                EvaluateSprites(dot);
            }
        }
        else if (dot <= 320)
        {
            _oamAddr = 0;
            FetchSprites(dot, preRender);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ushort BackgroundPatternAddress() =>
        (ushort)(((_ctrl & 0x10) << 8) | (_ntLatch << 4) | ((_v >> 12) & 0x07));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReloadBackgroundShifters()
    {
        _bgShiftLo = (ushort)((_bgShiftLo & 0xFF00) | _patLoLatch);
        _bgShiftHi = (ushort)((_bgShiftHi & 0xFF00) | _patHiLatch);
        _atShiftLo = (ushort)((_atShiftLo & 0xFF00) | ((_atLatch & 1) != 0 ? 0xFF : 0x00));
        _atShiftHi = (ushort)((_atShiftHi & 0xFF00) | ((_atLatch & 2) != 0 ? 0xFF : 0x00));
    }

    #endregion

    #region 精灵评估（逐周期，含溢出 bug）

    private void EvaluateSprites(int dot)
    {
        if (dot <= 64)
        {
            _oamCopyBuffer = 0xFF;
            if ((dot & 1) == 0)
            {
                _secondaryOam[(dot - 1) >> 1] = 0xFF;
            }

            return;
        }

        if (dot == 65)
        {
            _sprite0Added = false;
            _spriteInRange = false;
            _secondaryAddr = 0;
            _overflowBugCounter = 0;
            _oamCopyDone = false;
            _spriteAddrH = (_oamAddr >> 2) & 0x3F;
            _spriteAddrL = _oamAddr & 0x03;
        }

        if ((dot & 1) != 0)
        {
            _oamCopyBuffer = _oam[_oamAddr];
        }
        else
        {
            if (_oamCopyDone)
            {
                _spriteAddrH = (_spriteAddrH + 1) & 0x3F;
                if (_secondaryAddr >= 0x20)
                {
                    _oamCopyBuffer = _secondaryOam[_secondaryAddr & 0x1F];
                }
            }
            else
            {
                var height = (_ctrl & 0x20) != 0 ? 16 : 8;
                if (!_spriteInRange && _scanline >= _oamCopyBuffer && _scanline < _oamCopyBuffer + height)
                {
                    _spriteInRange = true;
                }

                if (_secondaryAddr < 0x20)
                {
                    _secondaryOam[_secondaryAddr] = _oamCopyBuffer;
                    if (_spriteInRange)
                    {
                        _spriteAddrL++;
                        _secondaryAddr++;
                        if (_spriteAddrH == 0)
                        {
                            _sprite0Added = true;
                        }

                        if ((_secondaryAddr & 0x03) == 0)
                        {
                            _spriteInRange = false;
                            _spriteAddrL = 0;
                            _spriteAddrH = (_spriteAddrH + 1) & 0x3F;
                            if (_spriteAddrH == 0)
                            {
                                _oamCopyDone = true;
                            }
                        }
                    }
                    else
                    {
                        _spriteAddrH = (_spriteAddrH + 1) & 0x3F;
                        if (_spriteAddrH == 0)
                        {
                            _oamCopyDone = true;
                        }
                    }
                }
                else
                {
                    // 已找到 8 个精灵：写次级 OAM 被禁止，变为读取
                    _oamCopyBuffer = _secondaryOam[_secondaryAddr & 0x1F];
                    if (_spriteInRange)
                    {
                        _spriteOverflow = true;
                        _spriteAddrL++;
                        if (_spriteAddrL == 4)
                        {
                            _spriteAddrH = (_spriteAddrH + 1) & 0x3F;
                            _spriteAddrL = 0;
                        }

                        if (_overflowBugCounter == 0)
                        {
                            _overflowBugCounter = 3;
                        }
                        else if (--_overflowBugCounter == 0)
                        {
                            _oamCopyDone = true;
                            _spriteAddrL = 0;
                        }
                    }
                    else
                    {
                        // 硬件 bug：n 与 m 同时递增，导致把 tile/attr/x 字节当作 Y 比较
                        _spriteAddrH = (_spriteAddrH + 1) & 0x3F;
                        _spriteAddrL = (_spriteAddrL + 1) & 0x03;
                        if (_spriteAddrH == 0)
                        {
                            _oamCopyDone = true;
                        }
                    }
                }
            }

            _oamAddr = (byte)((_spriteAddrL & 0x03) | (_spriteAddrH << 2));
        }

        if (dot == 256)
        {
            _fetchCount = _secondaryAddr >> 2;
            _fetchSprite0 = _sprite0Added;
        }
    }

    private void FetchSprites(int dot, bool preRender)
    {
        var offset = (dot - 257) & 7;
        var slot = (dot - 257) >> 3;

        if (dot == 257)
        {
            _sprCount = preRender ? 0 : _fetchCount;
            _sprite0OnLine = !preRender && _fetchSprite0;
        }

        switch (offset)
        {
            case 0:
                _oamCopyBuffer = _secondaryOam[slot * 4];
                break;
            case 1:
            case 3:
                // 抓取精灵时名称表/属性表读取为无用的“哑读”
                ReadVram((ushort)(0x2000 | (_v & 0x0FFF)));
                break;
            case 4:
            case 6:
            {
                var y = _secondaryOam[slot * 4];
                var tile = _secondaryOam[slot * 4 + 1];
                var attr = _secondaryOam[slot * 4 + 2];
                var row = (_scanline - y) & 0xFF;
                var tall = (_ctrl & 0x20) != 0;
                if ((attr & 0x80) != 0)
                {
                    row = (tall ? 15 : 7) - row;
                }

                ushort address;
                if (tall)
                {
                    address = (ushort)(((tile & 1) << 12) | ((tile & 0xFE) << 4) | ((row & 0x08) << 1) | (row & 0x07));
                }
                else
                {
                    address = (ushort)(((_ctrl & 0x08) << 9) | (tile << 4) | (row & 0x07));
                }

                if (offset == 6)
                {
                    address += 8;
                }

                var data = ReadVram(address);
                if (slot < _sprCount)
                {
                    if ((attr & 0x40) != 0)
                    {
                        data = ReverseBits(data);
                    }

                    if (offset == 4)
                    {
                        _sprLo[slot] = data;
                        _sprAttr[slot] = attr;
                        _sprX[slot] = _secondaryOam[slot * 4 + 3];
                    }
                    else
                    {
                        _sprHi[slot] = data;
                    }
                }

                break;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ReverseBits(byte b)
    {
        b = (byte)((b & 0xF0) >> 4 | (b & 0x0F) << 4);
        b = (byte)((b & 0xCC) >> 2 | (b & 0x33) << 2);
        b = (byte)((b & 0xAA) >> 1 | (b & 0x55) << 1);
        return b;
    }

    #endregion

    #region 像素输出

    private void OutputPixel()
    {
        var x = _dot - 1;
        var mask = _mask;
        int color;

        if (!_renderingEnabled)
        {
            // 渲染关闭：v 指向调色板时输出该颜色，否则输出通用背景色
            var index = (_v & 0x3F00) == 0x3F00 ? PaletteIndex(_v) : 0;
            color = _palette[index];
        }
        else
        {
            var bgPixel = 0;
            if ((mask & 0x08) != 0 && (x >= 8 || (mask & 0x02) != 0))
            {
                var bit = 15 - _fineX;
                bgPixel = ((_bgShiftLo >> bit) & 1) | (((_bgShiftHi >> bit) & 1) << 1);
                if (bgPixel != 0)
                {
                    bgPixel |= (((_atShiftLo >> bit) & 1) | (((_atShiftHi >> bit) & 1) << 1)) << 2;
                }
            }

            var sprPixel = 0;
            var sprBehind = false;
            if ((mask & 0x10) != 0 && (x >= 8 || (mask & 0x04) != 0))
            {
                for (var i = 0; i < _sprCount; i++)
                {
                    var offset = x - _sprX[i];
                    if ((uint)offset > 7)
                    {
                        continue;
                    }

                    var shift = 7 - offset;
                    var p = ((_sprLo[i] >> shift) & 1) | (((_sprHi[i] >> shift) & 1) << 1);
                    if (p == 0)
                    {
                        continue;
                    }

                    if (i == 0 && _sprite0OnLine && bgPixel != 0 && x != 255 && !_sprite0Hit)
                    {
                        _sprite0Hit = true;
                    }

                    sprPixel = 0x10 | ((_sprAttr[i] & 0x03) << 2) | p;
                    sprBehind = (_sprAttr[i] & 0x20) != 0;
                    break;
                }
            }

            int paletteIndex;
            if (sprPixel != 0 && (bgPixel == 0 || !sprBehind))
            {
                paletteIndex = sprPixel;
            }
            else
            {
                paletteIndex = bgPixel;
            }

            color = _palette[paletteIndex];
        }

        if ((mask & 0x01) != 0)
        {
            color &= 0x30;
        }

        Screen.SetPixel(x, _scanline, NesPalette.Table[((mask & 0xE0) << 1) | (color & 0x3F)]);
    }

    #endregion
}
