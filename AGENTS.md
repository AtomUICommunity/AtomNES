# AtomNES 开发指引

基于 AtomUI 生态构建的跨平台 NES（FC）模拟器。

## 沟通约定

- 使用中文交流，回复简洁。
- 用户说"先聊/先说话/不动手"时只讨论，不改代码；等用户通知再动手。
- 本地 ROM 测试失败时**不要急着改代码**，先把失败清单给用户，由用户用 Mesen 等权威模拟器确认是否是 ROM 本身问题。
- 优先使用网上的权威测试 ROM（blargg、Holy Mapperel 等）验证，用户无法保证本地 ROM 无损。
- 临时文件（调试工程、截图、下载缓存等）用完立即清理。

## 参考项目

- 旧项目（移植来源，代码质量一般，仅作参考，不要照搬）：`D:\Codes\学习项目\AvaloniaNES`
- AtomUI 与 Gallery 源码：`D:\Codes\学习项目\AtomUI`
- Mapper 实现可参考 Mesen2：https://github.com/SourMesen/Mesen2 （`Core/NES/Mappers`）

## 项目结构

- `AtomNES.Device`：模拟核心（周期精确 6502 CPU、逐点 PPU、2A03 APU、Mapper）。
- `AtomNES.Device.Tests`：xUnit 测试，`TestRoms/` 内的测试 ROM 必须全部保留在 git 中。
- `AtomNES`：UI（AtomUI 6.2.5 / Avalonia，net10.0），界面只使用 AtomUI 组件；音频输出使用 SDL3（ppy.SDL3-cs）。
- `Scripts/`：本地发布脚本（已 gitignore，不纳入仓库）。`publish_windows.py` / `publish_linux.py` 默认自包含裁剪发布，`--aot` 为 NativeAOT（不能跨系统编译；Linux AOT 产物依赖构建机 glibc 版本）。
- `Output/`：发布输出（已 gitignore）。

## 构建与测试

```powershell
dotnet build AtomNES
dotnet test AtomNES.Device.Tests
```

本地 ROM 冒烟测试（每个 Mapper 抽 2 个 ROM 各跑 20 秒，**不要全库扫描**）：

```powershell
$env:ATOMNES_ROM_LIBRARY = "D:\资源\rom"; dotnet test AtomNES.Device.Tests --filter "FullyQualifiedName~LocalRomLibrary"
```

## 已知有问题的本地 ROM（已用 Mesen 确认，非模拟器缺陷）

位于 `D:\资源\rom`：

| ROM | Mapper | 现象（Mesen） |
|-----|--------|---------------|
| [031] 最终幻想3 | 245 | 打开即崩溃 |
| [032] 最终幻想2 | 245 | 不崩溃，按键后花屏 |
| [039] 勇者斗恶龙7 | 245 | 打开即崩溃 |
| [040] 勇者斗恶龙6 | 245 | 不崩溃，按键后花屏 |
| [042] 勇者斗恶龙4 | 245 | 打开即崩溃 |
| [229] 东方的传说（萨尔达传说） | 245 | 打开即崩溃 |
| [059] 泰坦尼克号 | 34 | 头信息错误（疑为盗版板），黑屏 |

已删除：[033] 最终幻想1、[041] 勇者斗恶龙5。
