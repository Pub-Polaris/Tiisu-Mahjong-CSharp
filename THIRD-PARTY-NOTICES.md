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
| [SkiaSharp](https://github.com/mono/SkiaSharp)（含 NativeAssets.Win32 / Views.WindowsForms / HarfBuzz） | 4.153.1 | MIT | 2D 绘制与牌面渲染 |
| [YogaSharp.Layout](https://github.com/RaZeR-RaY/YogaSharp) | 1.0.6 | MIT | 牌桌 Flex 布局 |
| [.NET](https://github.com/dotnet/runtime)（含 Windows Desktop Runtime） | 10 | MIT | 运行时与 WinForms |
| [Microsoft.Web.WebView2](https://learn.microsoft.com/microsoft-edge/webview2/) | 1.0.4258.31 | Microsoft 专有许可 | `Tiisu.Shell` 使用 |
| [xUnit](https://xunit.net/) / xunit.runner.visualstudio | 2.9.3 / 3.1.4 | Apache-2.0 | 测试（仅开发） |
| [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) / coverlet.collector | 17.14.1 / 6.0.4 | MIT | 测试（仅开发） |
| [System.Speech](https://github.com/dotnet/runtime) | 9.0.0 | MIT | `Tiisu.AudioGen` 语音合成 |
