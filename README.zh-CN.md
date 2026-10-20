<div align="center">

<img src="Docs/images/logo.png" alt="AtomNES" width="128" />

# AtomNES

**基于 Avalonia 与 AtomUI 构建的周期精确、跨平台 NES / FC 模拟器。**

简体中文 · [English](README.md)

![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![AtomUI](https://img.shields.io/badge/AtomUI-6.2.5-1677FF)
![NativeAOT](https://img.shields.io/badge/NativeAOT-ready-success)
![Mappers](https://img.shields.io/badge/mappers-106-blue)
![License](https://img.shields.io/badge/license-MIT-green)

<br />

<img src="Docs/images/game.png" alt="AtomNES 运行游戏" width="640" />

</div>

AtomNES 是一个支持 Windows 与 Linux 的桌面 NES（FC）模拟器。模拟核心不依赖任何 UI，CPU 按周期运行，PPU 逐点渲染；界面全部使用 AtomUI 组件。

## 功能

- **周期精确的核心。** 6502 CPU 逐周期执行（含哑读/哑写与 DMA 暂停），PPU 逐点渲染，2A03 APU 使用带限合成与模拟滤波输出。
- **106 个 Mapper 编号。** 覆盖绝大多数正式发行游戏，以及外星、南晶、晶太、FK23C 等国产卡带和多合一合卡。
- **扩展音频。** 支持 VRC6、VRC7（移植 emu2413 实现 FM 合成）、Namco 163、Sunsoft 5B、MMC5。
- **电池存档。** 带电池的 PRG RAM 自动保存为 ROM 同目录下的 `.sav` 文件。
- **调试器。** 可查看 CPU 寄存器、反汇编、图案表、调色板、PPU 状态和名称表。
- **按键配置。** 双手柄键位均可自定义，支持音量调节。
- **中英双语界面。** 在“帮助”菜单中切换语言，启动时自动跟随系统主题。
- **发布方式。** 支持 NativeAOT，也支持自包含裁剪单文件发布。

## 截图

<table>
  <tr>
    <td width="50%"><img src="Docs/images/home.png" alt="主窗口" /><br /><sub><b>主窗口。</b> 按 Ctrl+O 打开 ROM，也可以通过命令行传入。</sub></td>
    <td width="50%"><img src="Docs/images/game.png" alt="游戏画面" /><br /><sub><b>游戏画面。</b> 像素精确输出，随窗口缩放。</sub></td>
  </tr>
</table>

## 架构

```
AtomNES（Avalonia + AtomUI 界面，SDL3 音频）
   │
   └──▶ AtomNES.Device   CPU · PPU · APU · 总线 · 卡带/Mapper
              ▲
   AtomNES.Device.Tests  xUnit + 权威测试 ROM
```

| 项目 | 职责 |
|---|---|
| `AtomNES.Device` | 模拟核心：周期精确 6502、逐点 2C02 PPU、2A03 APU、扩展音频、Mapper |
| `AtomNES.Device.Tests` | 基于 nestest、blargg、Holy Mapperel、mmc3_test 等公开测试 ROM 的测试 |
| `AtomNES` | 桌面应用：主窗口、调试器、按键映射、本地化、SDL3 音频输出 |
| `Docs` | 原理文档：[CPU](Docs/CPU.md)、[PPU](Docs/PPU.md)、[APU](Docs/APU.md)、[Mapper 覆盖情况](Docs/Mappers.md) |

## 快速开始

### 环境要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- NativeAOT 发布还需要对应平台的 C 工具链：Windows 上为 MSVC，Linux 上为 `clang` 与 `zlib1g-dev`。

### 构建与运行

```bash
dotnet build AtomNES
dotnet run --project AtomNES -- path/to/game.nes
```

### 运行测试

```bash
dotnet test AtomNES.Device.Tests
```

### 发布

```bash
# 自包含裁剪单文件
dotnet publish AtomNES -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=true
# NativeAOT（需在目标系统上构建）
dotnet publish AtomNES -c Release -r linux-x64 -p:PublishAot=true
```

> Linux 上构建的 NativeAOT 产物依赖构建机的 glibc 版本。

## 默认按键

| NES | 1P | 2P |
|---|---|---|
| 方向键 | W A S D | 方向键 |
| A / B | K / J | 小键盘 2 / 小键盘 1 |
| Select / Start | 右 Ctrl / Enter | 小键盘 + / 小键盘 − |

快捷键：`Ctrl+O` 打开 ROM · `F5` 暂停 · `F2` 重置 · `F8` 调试器 · `F1` 按键设置。

## 致谢

- [AtomUI](https://github.com/AtomUI/AtomUI) 与 [Avalonia](https://avaloniaui.net/)
- [Mesen2](https://github.com/SourMesen/Mesen2)：大量 Mapper 实现的参考
- [emu2413](https://github.com/digital-sound-antiques/emu2413)（作者 Mitsutaka Okazaki，MIT 许可）：VRC7 音频
- NESdev 社区，以及各测试 ROM 的作者

## 许可证

[MIT](LICENSE)。AtomNES 不附带任何商业游戏 ROM，请仅使用您有合法使用权的 ROM。
