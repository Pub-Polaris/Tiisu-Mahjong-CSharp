# 第三方素材与组件

本仓代码采用 [MIT License](LICENSE)。以下内容不属于 MIT 授权范围，各自遵循其原始许可。

## 音乐

| 项 | 内容 |
|---|---|
| 曲名 | Parisian |
| 作者 | Kevin MacLeod（[incompetech.com](https://incompetech.com/music/royalty-free/)） |
| 授权 | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) |
| 文件 | `src/Tiisu.App/Assets/bgm/lobby.wav`（44.1 kHz / 立体声 / 41.45 s） |
| 修改 | 音量 -5 dB、作为菜单 BGM 循环播放 |

随仓库分发，需保留以上署名。主菜单底部提供可点击署名，指向作者音乐库与许可证全文。

## 音效与语音

`src/Tiisu.App/Assets/sounds/` 与 `src/Tiisu.App/Assets/voices/` 全部由本仓
`tools/Tiisu.AudioGen` 程序合成（正弦/噪声与 TTS），不含第三方录音素材。

## 第三方库

| 组件 | 版本 | 许可 | 用途 |
|---|---|---|---|
| [NAudio](https://github.com/naudio/NAudio) | 2.2.1 | MIT | 音频混音与播放（BGM + 音效） |
| NAudio.Asio / Core / Midi / Wasapi / WinForms / WinMM | 2.2.1 | MIT | NAudio 传递依赖 |
| HarfBuzzSharp / NativeAssets.Win32 | 14.2.1.301 | MIT | SkiaSharp.HarfBuzz 传递依赖 |
| OpenTK / OpenTK.GLControl | 4.8.2 / 4.0.1 | MIT | OpenGL 窗口与控件 |
| GLFW（OpenTK.redist.glfw） | 3.3.8.39 | Zlib/libpng | 窗口/输入原生库 |
| YogaSharp.Core / SkiaSharp.Views.Desktop.Common | 1.0.6 / 4.153.1 | MIT | 布局/桌面控件传递依赖 |
| [SkiaSharp](https://github.com/mono/SkiaSharp)（含 NativeAssets.Win32 / Views.WindowsForms / HarfBuzz） | 4.153.1 | MIT | 2D 绘制与牌面渲染 |
| [YogaSharp.Layout](https://github.com/RaZeR-RaY/YogaSharp) | 1.0.6 | MIT | 牌桌 Flex 布局 |
| [.NET](https://github.com/dotnet/runtime)（含 Windows Desktop Runtime） | 10 | MIT | 运行时与 WinForms |
| [xUnit](https://xunit.net/) / xunit.runner.visualstudio | 2.9.3 / 3.1.4 | Apache-2.0 | 测试（仅开发） |
| [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) / coverlet.collector | 17.14.1 / 6.0.4 | MIT | 测试（仅开发） |
| [System.Speech](https://github.com/dotnet/runtime) | 9.0.0 | MIT | `Tiisu.AudioGen` 语音合成 |


## 训练数据（7Neuu 小策略网）

| 数据集 | 许可 | 用途 |
|---|---|---|
| [Tenhou MJAI Riichi Training Dataset](https://huggingface.co/datasets/1nfrared218/tenhou-mjai-riichi-training) | CC BY 4.0 | 人类弃牌风格训练；随权重分发时保留署名 |
| [MCR Final-2026 Engine Correctness Test Set](https://huggingface.co/datasets/Dannibal/mcr-final2026-testset) | competition-game-logs（自定义） | 训练/对照；允许研究/工程使用，商用范围需确认 |

许可证全文：`Assets/licenses/MCR-DATASET-LICENSE.txt`（CC BY 见 `CC-BY-4.0.txt`）

## 牌面素材

| 来源 | 版本/提交 | 许可 | 用途 |
|---|---|---|---|
| [lietxia/mahjong_graphic](https://github.com/lietxia/mahjong_graphic) | 3e27580 (2023-05-14) | 自定义自由许可：可商用、可修改、可再分发，无署名要求 | 牌面矢量素材；本项目重排为 `Assets/tiles.png` |

许可证全文：`src/Tiisu.App/Assets/licenses/TILES-LICENSE.txt`

## 字体

| 组件 | 版本 | 许可 | 用途 |
|---|---|---|---|
| [文渊圆体 WenYuan Rounded SC](https://github.com/takushun-wu/WenYuanFonts) | v1.110 | SIL OFL 1.1 | 界面圆体子集；重命名为 Tiisu Rounded SC（OFL 保留字体名） |

- 文件：`src/Tiisu.App/Assets/fonts/TiisuRoundedSC-subset.ttf`（仅子集化规则页用字）
- 许可证全文：`src/Tiisu.App/Assets/fonts/LICENSE-WenYuan.md`
- 未修改字形，仅按应用文案做子集化；分发时保留本声明与许可证。
