/*
 * emu2413 v1.5.9 的 C# 移植
 * https://github.com/digital-sound-antiques/emu2413
 * Copyright (C) 2020 Mitsutaka Okazaki
 *
 * MIT License
 *
 * This source refers to the following documents. The author would like to thank all the authors who have
 * contributed to the writing of them.
 * - [YM2413 notes](http://www.smspower.org/Development/YM2413) by andete
 * - ymf262.c by Jarek Burczynski
 * - [VRC7 presets](https://siliconpr0n.org/archive/doku.php?id=vendor:yamaha:opl2#opll_vrc7_patch_format) by Nuke.YKT
 * - YMF281B presets by Chabin
 */

using System;

namespace AtomNES.Device.Audio;

/// <summary>
/// YM2413 / VRC7 OPLL FM 合成器（emu2413 移植，单声道输出）。
/// </summary>
internal sealed class Opll
{
    private const int ToneNum = 3;

    private static readonly byte[][] DefaultInst =
    [
        [
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x71,0x61,0x1e,0x17,0xd0,0x78,0x00,0x17,
            0x13,0x41,0x1a,0x0d,0xd8,0xf7,0x23,0x13,
            0x13,0x01,0x99,0x00,0xf2,0xc4,0x21,0x23,
            0x11,0x61,0x0e,0x07,0x8d,0x64,0x70,0x27,
            0x32,0x21,0x1e,0x06,0xe1,0x76,0x01,0x28,
            0x31,0x22,0x16,0x05,0xe0,0x71,0x00,0x18,
            0x21,0x61,0x1d,0x07,0x82,0x81,0x11,0x07,
            0x33,0x21,0x2d,0x13,0xb0,0x70,0x00,0x07,
            0x61,0x61,0x1b,0x06,0x64,0x65,0x10,0x17,
            0x41,0x61,0x0b,0x18,0x85,0xf0,0x81,0x07,
            0x33,0x01,0x83,0x11,0xea,0xef,0x10,0x04,
            0x17,0xc1,0x24,0x07,0xf8,0xf8,0x22,0x12,
            0x61,0x50,0x0c,0x05,0xd2,0xf5,0x40,0x42,
            0x01,0x01,0x55,0x03,0xe9,0x90,0x03,0x02,
            0x41,0x41,0x89,0x03,0xf1,0xe4,0xc0,0x13,
            0x01,0x01,0x18,0x0f,0xdf,0xf8,0x6a,0x6d,
            0x01,0x01,0x00,0x00,0xc8,0xd8,0xa7,0x68,
            0x05,0x01,0x00,0x00,0xf8,0xaa,0x59,0x55,
        ],
        [
            // VRC7 音色（Nuke.YKT）
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x03,0x21,0x05,0x06,0xe8,0x81,0x42,0x27,
            0x13,0x41,0x14,0x0d,0xd8,0xf6,0x23,0x12,
            0x11,0x11,0x08,0x08,0xfa,0xb2,0x20,0x12,
            0x31,0x61,0x0c,0x07,0xa8,0x64,0x61,0x27,
            0x32,0x21,0x1e,0x06,0xe1,0x76,0x01,0x28,
            0x02,0x01,0x06,0x00,0xa3,0xe2,0xf4,0xf4,
            0x21,0x61,0x1d,0x07,0x82,0x81,0x11,0x07,
            0x23,0x21,0x22,0x17,0xa2,0x72,0x01,0x17,
            0x35,0x11,0x25,0x00,0x40,0x73,0x72,0x01,
            0xb5,0x01,0x0f,0x0F,0xa8,0xa5,0x51,0x02,
            0x17,0xc1,0x24,0x07,0xf8,0xf8,0x22,0x12,
            0x71,0x23,0x11,0x06,0x65,0x74,0x18,0x16,
            0x01,0x02,0xd3,0x05,0xc9,0x95,0x03,0x02,
            0x61,0x63,0x0c,0x00,0x94,0xC0,0x33,0xf6,
            0x21,0x72,0x0d,0x00,0xc1,0xd5,0x56,0x06,
            0x01,0x01,0x18,0x0f,0xdf,0xf8,0x6a,0x6d,
            0x01,0x01,0x00,0x00,0xc8,0xd8,0xa7,0x68,
            0x05,0x01,0x00,0x00,0xf8,0xaa,0x59,0x55,
        ],
        [
            // YMF281B 音色
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x62,0x21,0x1a,0x07,0xf0,0x6f,0x00,0x16,
            0x40,0x10,0x45,0x00,0xf6,0x83,0x73,0x63,
            0x13,0x01,0x99,0x00,0xf2,0xc3,0x21,0x23,
            0x01,0x61,0x0b,0x0f,0xf9,0x64,0x70,0x17,
            0x32,0x21,0x1e,0x06,0xe1,0x76,0x01,0x28,
            0x60,0x01,0x82,0x0e,0xf9,0x61,0x20,0x27,
            0x21,0x61,0x1c,0x07,0x84,0x81,0x11,0x07,
            0x37,0x32,0xc9,0x01,0x66,0x64,0x40,0x28,
            0x01,0x21,0x07,0x03,0xa5,0x71,0x51,0x07,
            0x06,0x01,0x5e,0x07,0xf3,0xf3,0xf6,0x13,
            0x00,0x00,0x18,0x06,0xf5,0xf3,0x20,0x23,
            0x17,0xc1,0x24,0x07,0xf8,0xf8,0x22,0x12,
            0x35,0x64,0x00,0x00,0xff,0xf3,0x77,0xf5,
            0x11,0x31,0x00,0x07,0xdd,0xf3,0xff,0xfb,
            0x3a,0x21,0x00,0x07,0x80,0x84,0x0f,0xf5,
            0x01,0x01,0x18,0x0f,0xdf,0xf8,0x6a,0x6d,
            0x01,0x01,0x00,0x00,0xc8,0xd8,0xa7,0x68,
            0x05,0x01,0x00,0x00,0xf8,0xaa,0x59,0x55,
        ],
    ];

    private const int PgBits = 10;
    private const int PgWidth = 1 << PgBits;
    private const int DpBits = 19;
    private const int DpWidth = 1 << DpBits;
    private const int DpBaseBits = DpBits - PgBits;

    private const double EgStep = 0.375;
    private const int EgBits = 7;
    private const int EgMute = (1 << EgBits) - 1;
    private const int EgMax = EgMute - 4;
    private const int TlBits = 6;
    private const int DamperRate = 12;

    private const int SlotBd1 = 12;
    private const int SlotHh = 14;
    private const int SlotSd = 15;
    private const int SlotTom = 16;
    private const int SlotCym = 17;

    private const int Attack = 0, Decay = 1, Sustain = 2, Release = 3, Damp = 4;

    private const int UpdateWs = 1, UpdateTll = 2, UpdateRks = 4, UpdateEg = 8, UpdateAll = 255;

    private const int MaskHh = 1 << 9, MaskCym = 1 << 10, MaskTom = 1 << 11, MaskSd = 1 << 12, MaskBd = 1 << 13;

    private static readonly ushort[] ExpTable =
    [
        0,    3,    6,    8,    11,   14,   17,   20,   22,   25,   28,   31,   34,   37,   40,   42,
        45,   48,   51,   54,   57,   60,   63,   66,   69,   72,   75,   78,   81,   84,   87,   90,
        93,   96,   99,   102,  105,  108,  111,  114,  117,  120,  123,  126,  130,  133,  136,  139,
        142,  145,  148,  152,  155,  158,  161,  164,  168,  171,  174,  177,  181,  184,  187,  190,
        194,  197,  200,  204,  207,  210,  214,  217,  220,  224,  227,  231,  234,  237,  241,  244,
        248,  251,  255,  258,  262,  265,  268,  272,  276,  279,  283,  286,  290,  293,  297,  300,
        304,  308,  311,  315,  318,  322,  326,  329,  333,  337,  340,  344,  348,  352,  355,  359,
        363,  367,  370,  374,  378,  382,  385,  389,  393,  397,  401,  405,  409,  412,  416,  420,
        424,  428,  432,  436,  440,  444,  448,  452,  456,  460,  464,  468,  472,  476,  480,  484,
        488,  492,  496,  501,  505,  509,  513,  517,  521,  526,  530,  534,  538,  542,  547,  551,
        555,  560,  564,  568,  572,  577,  581,  585,  590,  594,  599,  603,  607,  612,  616,  621,
        625,  630,  634,  639,  643,  648,  652,  657,  661,  666,  670,  675,  680,  684,  689,  693,
        698,  703,  708,  712,  717,  722,  726,  731,  736,  741,  745,  750,  755,  760,  765,  770,
        774,  779,  784,  789,  794,  799,  804,  809,  814,  819,  824,  829,  834,  839,  844,  849,
        854,  859,  864,  869,  874,  880,  885,  890,  895,  900,  906,  911,  916,  921,  927,  932,
        937,  942,  948,  953,  959,  964,  969,  975,  980,  986,  991,  996, 1002, 1007, 1013, 1018,
    ];

    // 前 1/4 周期，其余在静态构造中补全
    private static readonly ushort[] FullSinQuarter =
    [
        2137, 1731, 1543, 1419, 1326, 1252, 1190, 1137, 1091, 1050, 1013, 979,  949,  920,  894,  869,
        846,  825,  804,  785,  767,  749,  732,  717,  701,  687,  672,  659,  646,  633,  621,  609,
        598,  587,  576,  566,  556,  546,  536,  527,  518,  509,  501,  492,  484,  476,  468,  461,
        453,  446,  439,  432,  425,  418,  411,  405,  399,  392,  386,  380,  375,  369,  363,  358,
        352,  347,  341,  336,  331,  326,  321,  316,  311,  307,  302,  297,  293,  289,  284,  280,
        276,  271,  267,  263,  259,  255,  251,  248,  244,  240,  236,  233,  229,  226,  222,  219,
        215,  212,  209,  205,  202,  199,  196,  193,  190,  187,  184,  181,  178,  175,  172,  169,
        167,  164,  161,  159,  156,  153,  151,  148,  146,  143,  141,  138,  136,  134,  131,  129,
        127,  125,  122,  120,  118,  116,  114,  112,  110,  108,  106,  104,  102,  100,  98,   96,
        94,   92,   91,   89,   87,   85,   83,   82,   80,   78,   77,   75,   74,   72,   70,   69,
        67,   66,   64,   63,   62,   60,   59,   57,   56,   55,   53,   52,   51,   49,   48,   47,
        46,   45,   43,   42,   41,   40,   39,   38,   37,   36,   35,   34,   33,   32,   31,   30,
        29,   28,   27,   26,   25,   24,   23,   23,   22,   21,   20,   20,   19,   18,   17,   17,
        16,   15,   15,   14,   13,   13,   12,   12,   11,   10,   10,   9,    9,    8,    8,    7,
        7,    7,    6,    6,    5,    5,    5,    4,    4,    4,    3,    3,    3,    2,    2,    2,
        2,    1,    1,    1,    1,    1,    1,    1,    0,    0,    0,    0,    0,    0,    0,    0,
    ];

    private static readonly ushort[] FullSinTable = new ushort[PgWidth];
    private static readonly ushort[] HalfSinTable = new ushort[PgWidth];
    private static readonly ushort[][] WaveTableMap = [FullSinTable, HalfSinTable];

    private static readonly sbyte[,] PmTable =
    {
        { 0, 0, 0, 0, 0, 0, 0, 0 },
        { 0, 0, 1, 0, 0, 0, -1, 0 },
        { 0, 1, 2, 1, 0, -1, -2, -1 },
        { 0, 1, 3, 1, 0, -1, -3, -1 },
        { 0, 2, 4, 2, 0, -2, -4, -2 },
        { 0, 2, 5, 2, 0, -2, -5, -2 },
        { 0, 3, 6, 3, 0, -3, -6, -3 },
        { 0, 3, 7, 3, 0, -3, -7, -3 },
    };

    private static readonly byte[] AmTable =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1,
        2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3,
        4, 4, 4, 4, 4, 4, 4, 4, 5, 5, 5, 5, 5, 5, 5, 5,
        6, 6, 6, 6, 6, 6, 6, 6, 7, 7, 7, 7, 7, 7, 7, 7,
        8, 8, 8, 8, 8, 8, 8, 8, 9, 9, 9, 9, 9, 9, 9, 9,
        10, 10, 10, 10, 10, 10, 10, 10, 11, 11, 11, 11, 11, 11, 11, 11,
        12, 12, 12, 12, 12, 12, 12, 12,
        13, 13, 13,
        12, 12, 12, 12, 12, 12, 12, 12,
        11, 11, 11, 11, 11, 11, 11, 11, 10, 10, 10, 10, 10, 10, 10, 10,
        9, 9, 9, 9, 9, 9, 9, 9, 8, 8, 8, 8, 8, 8, 8, 8,
        7, 7, 7, 7, 7, 7, 7, 7, 6, 6, 6, 6, 6, 6, 6, 6,
        5, 5, 5, 5, 5, 5, 5, 5, 4, 4, 4, 4, 4, 4, 4, 4,
        3, 3, 3, 3, 3, 3, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2,
        1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0,
    ];

    private static readonly byte[,] EgStepTables =
    {
        { 0, 1, 0, 1, 0, 1, 0, 1 },
        { 0, 1, 0, 1, 1, 1, 0, 1 },
        { 0, 1, 1, 1, 0, 1, 1, 1 },
        { 0, 1, 1, 1, 1, 1, 1, 1 },
    };

    private static readonly uint[] MlTable = [1, 1 * 2, 2 * 2, 3 * 2, 4 * 2, 5 * 2, 6 * 2, 7 * 2,
        8 * 2, 9 * 2, 10 * 2, 10 * 2, 12 * 2, 12 * 2, 15 * 2, 15 * 2];

    private static readonly double[] KlTable =
    [
        0.000 * 2, 9.000 * 2, 12.000 * 2, 13.875 * 2, 15.000 * 2, 16.125 * 2, 16.875 * 2, 17.625 * 2,
        18.000 * 2, 18.750 * 2, 19.125 * 2, 19.500 * 2, 19.875 * 2, 20.250 * 2, 20.625 * 2, 21.000 * 2,
    ];

    // [blk_fnum>>5][TL][KL] 展平
    private static readonly uint[] TllTable = new uint[8 * 16 * (1 << TlBits) * 4];
    private static readonly int[] RksTable = new int[8 * 2 * 2];
    private static readonly Patch NullPatch = new();
    private static readonly Patch[][] DefaultPatch = new Patch[ToneNum][];

    private sealed class Patch
    {
        public uint TL, FB, EG, ML, AR, DR, SL, RR, KR, KL, AM, PM, WS;

        public void CopyFrom(Patch p)
        {
            TL = p.TL; FB = p.FB; EG = p.EG; ML = p.ML; AR = p.AR; DR = p.DR; SL = p.SL;
            RR = p.RR; KR = p.KR; KL = p.KL; AM = p.AM; PM = p.PM; WS = p.WS;
        }
    }

    private sealed class Slot
    {
        public int Number;
        public int Type;
        public Patch Patch = NullPatch;
        public int Output0, Output1;
        public ushort[] WaveTable = FullSinTable;
        public uint PgPhase;
        public uint PgOut;
        public bool PgKeep;
        public int BlkFnum;
        public int Fnum;
        public int Blk;
        public int EgState;
        public int Volume;
        public bool KeyFlag;
        public bool SusFlag;
        public int Tll;
        public int Rks;
        public int EgRateH;
        public int EgRateL;
        public int EgShift;
        public uint EgOut;
        public int UpdateRequests;
    }

    static Opll()
    {
        // TLL 表
        for (int fnum = 0; fnum < 16; fnum++)
        for (int block = 0; block < 8; block++)
        for (int tl = 0; tl < 64; tl++)
        for (int kl = 0; kl < 4; kl++)
        {
            int idx = ((((block << 4) | fnum) * 64) + tl) * 4 + kl;
            if (kl == 0)
            {
                TllTable[idx] = (uint)(tl << 1);
            }
            else
            {
                int tmp = (int)(KlTable[fnum] - 3.000 * 2 * (7 - block));
                TllTable[idx] = tmp <= 0
                    ? (uint)(tl << 1)
                    : (uint)((tmp >> (3 - kl)) / EgStep) + (uint)(tl << 1);
            }
        }

        // RKS 表
        for (int fnum8 = 0; fnum8 < 2; fnum8++)
        for (int block = 0; block < 8; block++)
        {
            RksTable[((block << 1) | fnum8) * 2 + 1] = (block << 1) + fnum8;
            RksTable[((block << 1) | fnum8) * 2 + 0] = block >> 1;
        }

        // 正弦表
        Array.Copy(FullSinQuarter, FullSinTable, PgWidth / 4);
        for (int x = 0; x < PgWidth / 4; x++)
            FullSinTable[PgWidth / 4 + x] = FullSinTable[PgWidth / 4 - x - 1];
        for (int x = 0; x < PgWidth / 2; x++)
            FullSinTable[PgWidth / 2 + x] = (ushort)(0x8000 | FullSinTable[x]);
        for (int x = 0; x < PgWidth / 2; x++)
            HalfSinTable[x] = FullSinTable[x];
        for (int x = PgWidth / 2; x < PgWidth; x++)
            HalfSinTable[x] = 0xfff;

        // 默认音色
        for (int i = 0; i < ToneNum; i++)
        {
            DefaultPatch[i] = new Patch[19 * 2];
            for (int j = 0; j < 19; j++)
            {
                var m = new Patch();
                var c = new Patch();
                DumpToPatch(DefaultInst[i], j * 8, m, c);
                DefaultPatch[i][j * 2] = m;
                DefaultPatch[i][j * 2 + 1] = c;
            }
        }
    }

    private static void DumpToPatch(byte[] dump, int o, Patch p0, Patch p1)
    {
        p0.AM = (uint)(dump[o] >> 7) & 1;
        p1.AM = (uint)(dump[o + 1] >> 7) & 1;
        p0.PM = (uint)(dump[o] >> 6) & 1;
        p1.PM = (uint)(dump[o + 1] >> 6) & 1;
        p0.EG = (uint)(dump[o] >> 5) & 1;
        p1.EG = (uint)(dump[o + 1] >> 5) & 1;
        p0.KR = (uint)(dump[o] >> 4) & 1;
        p1.KR = (uint)(dump[o + 1] >> 4) & 1;
        p0.ML = (uint)dump[o] & 15;
        p1.ML = (uint)dump[o + 1] & 15;
        p0.KL = (uint)(dump[o + 2] >> 6) & 3;
        p1.KL = (uint)(dump[o + 3] >> 6) & 3;
        p0.TL = (uint)dump[o + 2] & 63;
        p1.TL = 0;
        p0.FB = (uint)dump[o + 3] & 7;
        p1.FB = 0;
        p0.WS = (uint)(dump[o + 3] >> 3) & 1;
        p1.WS = (uint)(dump[o + 3] >> 4) & 1;
        p0.AR = (uint)(dump[o + 4] >> 4) & 15;
        p1.AR = (uint)(dump[o + 5] >> 4) & 15;
        p0.DR = (uint)dump[o + 4] & 15;
        p1.DR = (uint)dump[o + 5] & 15;
        p0.SL = (uint)(dump[o + 6] >> 4) & 15;
        p1.SL = (uint)(dump[o + 7] >> 4) & 15;
        p0.RR = (uint)dump[o + 6] & 15;
        p1.RR = (uint)dump[o + 7] & 15;
    }

    private readonly uint _clk;
    private readonly uint _rate;
    private int _chipType;
    private double _inpStep, _outStep, _outTime;
    private readonly byte[] _reg = new byte[0x40];
    private int _testFlag;
    private uint _slotKeyStatus;
    private int _rhythmMode;
    private uint _egCounter;
    private uint _pmPhase;
    private int _amPhase;
    private int _lfoAm;
    private uint _noise;
    private int _shortNoise;
    private readonly int[] _patchNumber = new int[9];
    private readonly Slot[] _slot = new Slot[18];
    private readonly Patch[] _patch = new Patch[19 * 2];
    private readonly int _mask = 0;
    private readonly short[] _chOut = new short[14];
    private short _mixOut;
    private RateConv? _conv;

    public Opll(uint clock, uint rate)
    {
        for (int i = 0; i < _patch.Length; i++) _patch[i] = new Patch();
        for (int i = 0; i < _slot.Length; i++) _slot[i] = new Slot();
        _clk = clock;
        _rate = rate;
        Reset();
        SetChipType(0);
        ResetPatch(0);
    }

    public void SetChipType(int type) => _chipType = type;

    public void ResetPatch(int type)
    {
        var src = DefaultPatch[type % ToneNum];
        for (int i = 0; i < 19 * 2; i++) _patch[i].CopyFrom(src[i]);
    }

    public void Reset()
    {
        _pmPhase = 0;
        _amPhase = 0;
        _noise = 1;
        _rhythmMode = 0;
        _slotKeyStatus = 0;
        _egCounter = 0;

        ResetRateConversionParams();

        for (int i = 0; i < 18; i++) ResetSlot(_slot[i], i);
        for (int i = 0; i < 9; i++) SetPatch(i, 0);
        for (int i = 0; i < 0x40; i++) WriteReg(i, 0);
        Array.Clear(_chOut);
    }

    private void ResetRateConversionParams()
    {
        double fOut = _rate;
        double fInp = _clk / 72.0;
        _outTime = 0;
        _outStep = fInp;
        _inpStep = fOut;
        _conv = null;
        if (Math.Floor(fInp) != fOut && Math.Floor(fInp + 0.5) != fOut)
            _conv = new RateConv(fInp, fOut);
    }

    private Slot Mod(int ch) => _slot[ch << 1];
    private Slot Car(int ch) => _slot[(ch << 1) | 1];

    private static int GetParameterRate(Slot slot)
    {
        if ((slot.Type & 1) == 0 && !slot.KeyFlag) return 0;
        return slot.EgState switch
        {
            Attack => (int)slot.Patch.AR,
            Decay => (int)slot.Patch.DR,
            Sustain => slot.Patch.EG != 0 ? 0 : (int)slot.Patch.RR,
            Release => slot.SusFlag ? 5 : slot.Patch.EG != 0 ? (int)slot.Patch.RR : 7,
            Damp => DamperRate,
            _ => 0,
        };
    }

    private static void RequestUpdate(Slot slot, int flag) => slot.UpdateRequests |= flag;

    private static void CommitSlotUpdate(Slot slot)
    {
        if ((slot.UpdateRequests & UpdateWs) != 0)
            slot.WaveTable = WaveTableMap[slot.Patch.WS];

        if ((slot.UpdateRequests & UpdateTll) != 0)
        {
            int tl = (slot.Type & 1) == 0 ? (int)slot.Patch.TL : slot.Volume;
            slot.Tll = (ushort)TllTable[(((slot.BlkFnum >> 5) * 64) + tl) * 4 + (int)slot.Patch.KL];
        }

        if ((slot.UpdateRequests & UpdateRks) != 0)
            slot.Rks = (byte)RksTable[(slot.BlkFnum >> 8) * 2 + (int)slot.Patch.KR];

        if ((slot.UpdateRequests & (UpdateRks | UpdateEg)) != 0)
        {
            int pRate = GetParameterRate(slot);
            if (pRate == 0)
            {
                slot.EgShift = 0;
                slot.EgRateH = 0;
                slot.EgRateL = 0;
                return;
            }

            slot.EgRateH = Math.Min(15, pRate + (slot.Rks >> 2));
            slot.EgRateL = slot.Rks & 3;
            if (slot.EgState == Attack)
                slot.EgShift = (0 < slot.EgRateH && slot.EgRateH < 12) ? 13 - slot.EgRateH : 0;
            else
                slot.EgShift = slot.EgRateH < 13 ? 13 - slot.EgRateH : 0;
        }

        slot.UpdateRequests = 0;
    }

    private static void ResetSlot(Slot slot, int number)
    {
        slot.Number = number;
        slot.Type = number % 2;
        slot.PgKeep = false;
        slot.WaveTable = WaveTableMap[0];
        slot.PgPhase = 0;
        slot.Output0 = 0;
        slot.Output1 = 0;
        slot.EgState = Release;
        slot.EgShift = 0;
        slot.Rks = 0;
        slot.Tll = 0;
        slot.KeyFlag = false;
        slot.SusFlag = false;
        slot.BlkFnum = 0;
        slot.Blk = 0;
        slot.Fnum = 0;
        slot.Volume = 0;
        slot.PgOut = 0;
        slot.EgOut = EgMute;
        slot.Patch = NullPatch;
    }

    private void SlotOn(int i)
    {
        var slot = _slot[i];
        slot.KeyFlag = true;
        slot.EgState = Damp;
        RequestUpdate(slot, UpdateEg);
    }

    private void SlotOff(int i)
    {
        var slot = _slot[i];
        slot.KeyFlag = false;
        if ((slot.Type & 1) != 0)
        {
            slot.EgState = Release;
            RequestUpdate(slot, UpdateEg);
        }
    }

    private void UpdateKeyStatus()
    {
        int r14 = _reg[0x0e];
        int rhythmMode = (r14 >> 5) & 1;
        uint newStatus = 0;

        for (int ch = 0; ch < 9; ch++)
            if ((_reg[0x20 + ch] & 0x10) != 0)
                newStatus |= 3u << (ch * 2);

        if (rhythmMode != 0)
        {
            if ((r14 & 0x10) != 0) newStatus |= 3u << SlotBd1;
            if ((r14 & 0x01) != 0) newStatus |= 1u << SlotHh;
            if ((r14 & 0x08) != 0) newStatus |= 1u << SlotSd;
            if ((r14 & 0x04) != 0) newStatus |= 1u << SlotTom;
            if ((r14 & 0x02) != 0) newStatus |= 1u << SlotCym;
        }

        uint updated = _slotKeyStatus ^ newStatus;
        if (updated != 0)
        {
            for (int i = 0; i < 18; i++)
            {
                if (((updated >> i) & 1) == 0) continue;
                if (((newStatus >> i) & 1) != 0) SlotOn(i);
                else SlotOff(i);
            }
        }

        _slotKeyStatus = newStatus;
    }

    private void SetPatch(int ch, int num)
    {
        _patchNumber[ch] = num;
        Mod(ch).Patch = _patch[num * 2 + 0];
        Car(ch).Patch = _patch[num * 2 + 1];
        RequestUpdate(Mod(ch), UpdateAll);
        RequestUpdate(Car(ch), UpdateAll);
    }

    private void SetSusFlag(int ch, bool flag)
    {
        Car(ch).SusFlag = flag;
        RequestUpdate(Car(ch), UpdateEg);
        if ((Mod(ch).Type & 1) != 0)
        {
            Mod(ch).SusFlag = flag;
            RequestUpdate(Mod(ch), UpdateEg);
        }
    }

    private void SetVolume(int ch, int volume)
    {
        Car(ch).Volume = volume;
        RequestUpdate(Car(ch), UpdateTll);
    }

    private static void SetSlotVolume(Slot slot, int volume)
    {
        slot.Volume = volume;
        RequestUpdate(slot, UpdateTll);
    }

    private void SetFnumber(int ch, int fnum)
    {
        var car = Car(ch);
        var mod = Mod(ch);
        car.Fnum = fnum & 0xffff;
        car.BlkFnum = (car.BlkFnum & 0xe00) | (fnum & 0x1ff);
        mod.Fnum = fnum & 0xffff;
        mod.BlkFnum = (mod.BlkFnum & 0xe00) | (fnum & 0x1ff);
        RequestUpdate(car, UpdateEg | UpdateRks | UpdateTll);
        RequestUpdate(mod, UpdateEg | UpdateRks | UpdateTll);
    }

    private void SetBlock(int ch, int blk)
    {
        var car = Car(ch);
        var mod = Mod(ch);
        car.Blk = blk;
        car.BlkFnum = ((blk & 7) << 9) | (car.BlkFnum & 0x1ff);
        mod.Blk = blk;
        mod.BlkFnum = ((blk & 7) << 9) | (mod.BlkFnum & 0x1ff);
        RequestUpdate(car, UpdateEg | UpdateRks | UpdateTll);
        RequestUpdate(mod, UpdateEg | UpdateRks | UpdateTll);
    }

    private void UpdateRhythmMode()
    {
        int newMode = (_reg[0x0e] >> 5) & 1;
        if (_rhythmMode != newMode)
        {
            if (newMode != 0)
            {
                _slot[SlotHh].Type = 3;
                _slot[SlotHh].PgKeep = true;
                _slot[SlotSd].Type = 3;
                _slot[SlotTom].Type = 3;
                _slot[SlotCym].Type = 3;
                _slot[SlotCym].PgKeep = true;
                SetPatch(6, 16);
                SetPatch(7, 17);
                SetPatch(8, 18);
                SetSlotVolume(_slot[SlotHh], ((_reg[0x37] >> 4) & 15) << 2);
                SetSlotVolume(_slot[SlotTom], ((_reg[0x38] >> 4) & 15) << 2);
            }
            else
            {
                _slot[SlotHh].Type = 0;
                _slot[SlotHh].PgKeep = false;
                _slot[SlotSd].Type = 1;
                _slot[SlotTom].Type = 0;
                _slot[SlotCym].Type = 1;
                _slot[SlotCym].PgKeep = false;
                SetPatch(6, _reg[0x36] >> 4);
                SetPatch(7, _reg[0x37] >> 4);
                SetPatch(8, _reg[0x38] >> 4);
            }
        }
        _rhythmMode = newMode;
    }

    private void UpdateAmPm()
    {
        if ((_testFlag & 2) != 0)
        {
            _pmPhase = 0;
            _amPhase = 0;
        }
        else
        {
            _pmPhase += (_testFlag & 8) != 0 ? 1024u : 1u;
            _amPhase += (_testFlag & 8) != 0 ? 64 : 1;
        }
        _lfoAm = AmTable[(uint)(_amPhase >> 6) % (uint)AmTable.Length];
    }

    private void UpdateNoise(int cycle)
    {
        for (int i = 0; i < cycle; i++)
        {
            if ((_noise & 1) != 0) _noise ^= 0x800200;
            _noise >>= 1;
        }
    }

    private void UpdateShortNoise()
    {
        uint pgHh = _slot[SlotHh].PgOut;
        uint pgCym = _slot[SlotCym].PgOut;
        uint hBit2 = (pgHh >> (PgBits - 8)) & 1;
        uint hBit7 = (pgHh >> (PgBits - 3)) & 1;
        uint hBit3 = (pgHh >> (PgBits - 7)) & 1;
        uint cBit3 = (pgCym >> (PgBits - 7)) & 1;
        uint cBit5 = (pgCym >> (PgBits - 5)) & 1;
        _shortNoise = (int)((hBit2 ^ hBit7) | (hBit3 ^ cBit5) | (cBit3 ^ cBit5));
    }

    private static void CalcPhase(Slot slot, int pmPhase, bool reset)
    {
        int pm = slot.Patch.PM != 0 ? PmTable[(slot.Fnum >> 6) & 7, (pmPhase >> 10) & 7] : 0;
        if (reset) slot.PgPhase = 0;
        uint inc = (uint)((slot.Fnum & 0x1ff) * 2 + pm) * MlTable[slot.Patch.ML];
        slot.PgPhase += (inc << slot.Blk) >> 2;
        slot.PgPhase &= DpWidth - 1;
        slot.PgOut = slot.PgPhase >> DpBaseBits;
    }

    private static int LookupAttackStep(Slot slot, uint counter)
    {
        int index;
        switch (slot.EgRateH)
        {
            case 12:
                index = (int)(counter & 0xc) >> 1;
                return 4 - EgStepTables[slot.EgRateL, index];
            case 13:
                index = (int)(counter & 0xc) >> 1;
                return 3 - EgStepTables[slot.EgRateL, index];
            case 14:
                index = (int)(counter & 0xc) >> 1;
                return 2 - EgStepTables[slot.EgRateL, index];
            case 0:
            case 15:
                return 0;
            default:
                index = (int)(counter >> slot.EgShift);
                return EgStepTables[slot.EgRateL, index & 7] != 0 ? 4 : 0;
        }
    }

    private static int LookupDecayStep(Slot slot, uint counter)
    {
        int index;
        switch (slot.EgRateH)
        {
            case 0:
                return 0;
            case 13:
                index = (int)(((counter & 0xc) >> 1) | (counter & 1));
                return EgStepTables[slot.EgRateL, index];
            case 14:
                index = (int)((counter & 0xc) >> 1);
                return EgStepTables[slot.EgRateL, index] + 1;
            case 15:
                return 2;
            default:
                index = (int)(counter >> slot.EgShift);
                return EgStepTables[slot.EgRateL, index & 7];
        }
    }

    private static void StartEnvelope(Slot slot)
    {
        if (Math.Min(15, (int)slot.Patch.AR + (slot.Rks >> 2)) == 15)
        {
            slot.EgState = Decay;
            slot.EgOut = 0;
        }
        else
        {
            slot.EgState = Attack;
        }
        RequestUpdate(slot, UpdateEg);
    }

    private static void CalcEnvelope(Slot slot, Slot? buddy, ushort egCounter, bool test)
    {
        uint counter = egCounter;
        uint mask = (1u << slot.EgShift) - 1;

        if (slot.EgState == Attack)
        {
            if (0 < slot.EgOut && 0 < slot.EgRateH && (counter & mask & ~3u) == 0)
            {
                int s = LookupAttackStep(slot, counter);
                if (0 < s)
                    slot.EgOut = (uint)Math.Max(0, (int)slot.EgOut - (int)(slot.EgOut >> s) - 1);
            }
        }
        else
        {
            if (slot.EgRateH > 0 && (counter & mask) == 0)
                slot.EgOut = (uint)Math.Min(EgMute, (int)slot.EgOut + LookupDecayStep(slot, counter));
        }

        switch (slot.EgState)
        {
            case Damp:
                if (slot.EgOut >= EgMax && (counter & mask) == 0)
                {
                    StartEnvelope(slot);
                    if ((slot.Type & 1) != 0)
                    {
                        if (!slot.PgKeep) slot.PgPhase = 0;
                        if (buddy != null && !buddy.PgKeep) buddy.PgPhase = 0;
                    }
                }
                break;
            case Attack:
                if (slot.EgOut == 0)
                {
                    slot.EgState = Decay;
                    RequestUpdate(slot, UpdateEg);
                }
                break;
            case Decay:
                if ((slot.EgOut >> 3) == slot.Patch.SL)
                {
                    slot.EgState = Sustain;
                    RequestUpdate(slot, UpdateEg);
                }
                break;
        }

        if (test) slot.EgOut = 0;
    }

    private void UpdateSlots()
    {
        _egCounter++;
        for (int i = 0; i < 18; i++)
        {
            var slot = _slot[i];
            Slot? buddy = null;
            if (slot.Type == 0) buddy = _slot[i + 1];
            if (slot.Type == 1) buddy = _slot[i - 1];
            if (slot.UpdateRequests != 0) CommitSlotUpdate(slot);
            CalcEnvelope(slot, buddy, (ushort)_egCounter, (_testFlag & 1) != 0);
            CalcPhase(slot, (int)_pmPhase, (_testFlag & 4) != 0);
        }
    }

    // 输出范围 -4095..4095
    private static short LookupExpTable(ushort i)
    {
        int t = (short)(ExpTable[(i & 0xff) ^ 0xff] + 1024);
        int res = (short)(t >> ((i & 0x7f00) >> 8));
        return (short)(((i & 0x8000) != 0 ? ~res : res) << 1);
    }

    private static short ToLinear(ushort h, Slot slot, int am)
    {
        if (slot.EgOut > EgMax) return 0;
        ushort att = (ushort)(Math.Min(EgMute, (int)slot.EgOut + slot.Tll + am) << 4);
        return LookupExpTable((ushort)(h + att));
    }

    private short CalcSlotCar(int ch, short fm)
    {
        var slot = Car(ch);
        int am = slot.Patch.AM != 0 ? _lfoAm : 0;
        slot.Output1 = slot.Output0;
        slot.Output0 = ToLinear(slot.WaveTable[((int)slot.PgOut + 2 * (fm >> 1)) & (PgWidth - 1)], slot, am);
        return (short)slot.Output0;
    }

    private short CalcSlotMod(int ch)
    {
        var slot = Mod(ch);
        short fm = slot.Patch.FB > 0 ? (short)((slot.Output1 + slot.Output0) >> (int)(9 - slot.Patch.FB)) : (short)0;
        int am = slot.Patch.AM != 0 ? _lfoAm : 0;
        slot.Output1 = slot.Output0;
        slot.Output0 = ToLinear(slot.WaveTable[((int)slot.PgOut + fm) & (PgWidth - 1)], slot, am);
        return (short)slot.Output0;
    }

    private short CalcSlotTom()
    {
        var slot = Mod(8);
        return ToLinear(slot.WaveTable[slot.PgOut], slot, 0);
    }

    private short CalcSlotSnare()
    {
        var slot = Car(7);
        int phase;
        if (((slot.PgOut >> (PgBits - 2)) & 1) != 0)
            phase = (_noise & 1) != 0 ? 0x300 : 0x200;
        else
            phase = (_noise & 1) != 0 ? 0x0 : 0x100;
        return ToLinear(slot.WaveTable[phase], slot, 0);
    }

    private short CalcSlotCym()
    {
        var slot = Car(8);
        int phase = _shortNoise != 0 ? 0x300 : 0x100;
        return ToLinear(slot.WaveTable[phase], slot, 0);
    }

    private short CalcSlotHat()
    {
        var slot = Mod(7);
        int phase;
        if (_shortNoise != 0)
            phase = (_noise & 1) != 0 ? 0x2d0 : 0x234;
        else
            phase = (_noise & 1) != 0 ? 0x34 : 0xd0;
        return ToLinear(slot.WaveTable[phase], slot, 0);
    }

    private static short Mo(short x) => (short)(-x >> 1);

    private void UpdateOutput()
    {
        UpdateAmPm();
        if (_chipType == 0) UpdateShortNoise();
        UpdateSlots();

        var o = _chOut;
        for (int i = 0; i < 6; i++)
            if ((_mask & (1 << i)) == 0)
                o[i] = Mo(CalcSlotCar(i, CalcSlotMod(i)));

        if (_chipType != 0) return;

        if (_rhythmMode == 0)
        {
            if ((_mask & (1 << 6)) == 0) o[6] = Mo(CalcSlotCar(6, CalcSlotMod(6)));
        }
        else if ((_mask & MaskBd) == 0)
        {
            o[9] = CalcSlotCar(6, CalcSlotMod(6));
        }
        UpdateNoise(14);

        if (_rhythmMode == 0)
        {
            if ((_mask & (1 << 7)) == 0) o[7] = Mo(CalcSlotCar(7, CalcSlotMod(7)));
        }
        else
        {
            if ((_mask & MaskHh) == 0) o[10] = CalcSlotHat();
            if ((_mask & MaskSd) == 0) o[11] = CalcSlotSnare();
        }
        UpdateNoise(2);

        if (_rhythmMode == 0)
        {
            if ((_mask & (1 << 8)) == 0) o[8] = Mo(CalcSlotCar(8, CalcSlotMod(8)));
        }
        else
        {
            if ((_mask & MaskTom) == 0) o[12] = CalcSlotTom();
            if ((_mask & MaskCym) == 0) o[13] = CalcSlotCym();
        }
        UpdateNoise(2);
    }

    private void MixOutput()
    {
        short sum = 0;
        int n = _chipType == 0 ? 14 : 6;
        for (int i = 0; i < n; i++) sum += _chOut[i];
        if (_conv != null) _conv.PutData(sum);
        else _mixOut = sum;
    }

    public void WriteReg(int reg, byte data)
    {
        if (reg < 0 || reg >= 0x40) return;

        // 镜像寄存器
        if ((0x19 <= reg && reg <= 0x1f) || (0x29 <= reg && reg <= 0x2f) || (0x39 <= reg && reg <= 0x3f))
            reg -= 9;

        _reg[reg] = data;
        int ch;

        switch (reg)
        {
            case 0x00:
                _patch[0].AM = (uint)(data >> 7) & 1;
                _patch[0].PM = (uint)(data >> 6) & 1;
                _patch[0].EG = (uint)(data >> 5) & 1;
                _patch[0].KR = (uint)(data >> 4) & 1;
                _patch[0].ML = (uint)data & 15;
                for (int i = 0; i < 9; i++)
                    if (_patchNumber[i] == 0) RequestUpdate(Mod(i), UpdateRks | UpdateEg);
                break;
            case 0x01:
                _patch[1].AM = (uint)(data >> 7) & 1;
                _patch[1].PM = (uint)(data >> 6) & 1;
                _patch[1].EG = (uint)(data >> 5) & 1;
                _patch[1].KR = (uint)(data >> 4) & 1;
                _patch[1].ML = (uint)data & 15;
                for (int i = 0; i < 9; i++)
                    if (_patchNumber[i] == 0) RequestUpdate(Car(i), UpdateRks | UpdateEg);
                break;
            case 0x02:
                _patch[0].KL = (uint)(data >> 6) & 3;
                _patch[0].TL = (uint)data & 63;
                for (int i = 0; i < 9; i++)
                    if (_patchNumber[i] == 0) RequestUpdate(Mod(i), UpdateTll);
                break;
            case 0x03:
                _patch[1].KL = (uint)(data >> 6) & 3;
                _patch[1].WS = (uint)(data >> 4) & 1;
                _patch[0].WS = (uint)(data >> 3) & 1;
                _patch[0].FB = (uint)data & 7;
                for (int i = 0; i < 9; i++)
                {
                    if (_patchNumber[i] != 0) continue;
                    RequestUpdate(Mod(i), UpdateWs);
                    RequestUpdate(Car(i), UpdateWs | UpdateTll);
                }
                break;
            case 0x04:
                _patch[0].AR = (uint)(data >> 4) & 15;
                _patch[0].DR = (uint)data & 15;
                for (int i = 0; i < 9; i++)
                    if (_patchNumber[i] == 0) RequestUpdate(Mod(i), UpdateEg);
                break;
            case 0x05:
                _patch[1].AR = (uint)(data >> 4) & 15;
                _patch[1].DR = (uint)data & 15;
                for (int i = 0; i < 9; i++)
                    if (_patchNumber[i] == 0) RequestUpdate(Car(i), UpdateEg);
                break;
            case 0x06:
                _patch[0].SL = (uint)(data >> 4) & 15;
                _patch[0].RR = (uint)data & 15;
                for (int i = 0; i < 9; i++)
                    if (_patchNumber[i] == 0) RequestUpdate(Mod(i), UpdateEg);
                break;
            case 0x07:
                _patch[1].SL = (uint)(data >> 4) & 15;
                _patch[1].RR = (uint)data & 15;
                for (int i = 0; i < 9; i++)
                    if (_patchNumber[i] == 0) RequestUpdate(Car(i), UpdateEg);
                break;
            case 0x0e:
                if (_chipType == 1) break;
                UpdateRhythmMode();
                UpdateKeyStatus();
                break;
            case 0x0f:
                _testFlag = data;
                break;
            case >= 0x10 and <= 0x18:
                if (_chipType == 1 && reg >= 0x16) break;
                ch = reg - 0x10;
                SetFnumber(ch, data + ((_reg[0x20 + ch] & 1) << 8));
                break;
            case >= 0x20 and <= 0x28:
                if (_chipType == 1 && reg >= 0x26) break;
                ch = reg - 0x20;
                SetFnumber(ch, ((data & 1) << 8) + _reg[0x10 + ch]);
                SetBlock(ch, (data >> 1) & 7);
                SetSusFlag(ch, ((data >> 5) & 1) != 0);
                UpdateKeyStatus();
                break;
            case >= 0x30 and <= 0x38:
                if (_chipType == 1 && reg >= 0x36) break;
                if ((_reg[0x0e] & 32) != 0 && reg >= 0x36)
                {
                    if (reg == 0x37) SetSlotVolume(Mod(7), ((data >> 4) & 15) << 2);
                    else if (reg == 0x38) SetSlotVolume(Mod(8), ((data >> 4) & 15) << 2);
                }
                else
                {
                    SetPatch(reg - 0x30, (data >> 4) & 15);
                }
                SetVolume(reg - 0x30, (data & 15) << 2);
                break;
        }
    }

    public short Calc()
    {
        while (_outStep > _outTime)
        {
            _outTime += _inpStep;
            UpdateOutput();
            MixOutput();
        }
        _outTime -= _outStep;
        if (_conv != null) _mixOut = _conv.GetData();
        return _mixOut;
    }

    // 内部采样率转换器（仅在 rate != clk/72 时使用，单声道）
    private sealed class RateConv
    {
        private const int Lw = 16;
        private const int SincReso = 256;
        private const int SincAmpBits = 12;

        private readonly double _fRatio;
        private readonly short[] _sincTable = new short[SincReso * Lw / 2];
        private readonly short[] _buf = new short[Lw];
        private double _timer;

        public RateConv(double fInp, double fOut)
        {
            _fRatio = fInp / fOut;
            for (int i = 0; i < _sincTable.Length; i++)
            {
                double x = (double)i / SincReso;
                _sincTable[i] = fOut < fInp
                    ? (short)((1 << SincAmpBits) * WindowedSinc(x / _fRatio) / _fRatio)
                    : (short)((1 << SincAmpBits) * WindowedSinc(x));
            }
        }

        private static double Blackman(double x) => 0.42 - 0.5 * Math.Cos(2 * Math.PI * x) + 0.08 * Math.Cos(4 * Math.PI * x);
        private static double Sinc(double x) => x == 0.0 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);
        private static double WindowedSinc(double x) => Blackman(0.5 + 0.5 * x / (Lw / 2)) * Sinc(x);

        private short Lookup(double x)
        {
            int index = (short)(x * SincReso);
            if (index < 0) index = -index;
            return _sincTable[Math.Min(SincReso * Lw / 2 - 1, index)];
        }

        public void PutData(short data)
        {
            Array.Copy(_buf, 1, _buf, 0, Lw - 1);
            _buf[Lw - 1] = data;
        }

        public short GetData()
        {
            int sum = 0;
            _timer += _fRatio;
            double dn = _timer - Math.Floor(_timer);
            _timer = dn;
            for (int k = 0; k < Lw; k++)
            {
                double x = (k - (Lw / 2 - 1)) - dn;
                sum += _buf[k] * Lookup(x);
            }
            return (short)(sum >> SincAmpBits);
        }
    }
}
