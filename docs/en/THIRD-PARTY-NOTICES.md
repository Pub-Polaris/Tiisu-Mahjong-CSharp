# Third-party Assets and Components

This repository's code is under the [MIT License](LICENSE). The items below are not covered by MIT and follow their original licenses.

## Music

| Item | Content |
|---|---|
| Track | Parisian |
| Author | Kevin MacLeod ([incompetech.com](https://incompetech.com/music/royalty-free/)) |
| License | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) |
| File | `src/Tiisu.App/Assets/bgm/lobby.wav` (44.1 kHz / stereo / 41.45 s) |
| Modification | -5 dB, looped as menu BGM |

Distributed with the repository; keep the attribution above. The main menu has a clickable credit pointing to the author's music library and license.

## SFX and voice

`src/Tiisu.App/Assets/sounds/` and `src/Tiisu.App/Assets/voices/` are entirely synthesized by this repository's `tools/Tiisu.AudioGen` (sine/noise and TTS); no third-party recordings.

## Third-party libraries

| Component | Version | License | Use |
|---|---|---|---|
| [NAudio](https://github.com/naudio/NAudio) | 2.2.1 | MIT | Audio mixing/playback (BGM + SFX) |
| NAudio.Asio / Core / Midi / Wasapi / WinForms / WinMM | 2.2.1 | MIT | NAudio transitive dependencies |
| HarfBuzzSharp / NativeAssets.Win32 | 14.2.1.301 | MIT | SkiaSharp.HarfBuzz transitive dependency |
| OpenTK / OpenTK.GLControl | 4.8.2 / 4.0.1 | MIT | OpenGL window/control |
| GLFW (OpenTK.redist.glfw) | 3.3.8.39 | Zlib/libpng | Window/input native library |
| YogaSharp.Core / SkiaSharp.Views.Desktop.Common | 1.0.6 / 4.153.1 | MIT | Layout/desktop control transitive dependencies |
| [SkiaSharp](https://github.com/mono/SkiaSharp) (incl. NativeAssets.Win32 / Views.WindowsForms / HarfBuzz) | 4.153.1 | MIT | 2D drawing and tile rendering |
| [YogaSharp.Layout](https://github.com/RaZeR-RaY/YogaSharp) | 1.0.6 | MIT | Table Flex layout |
| [.NET](https://github.com/dotnet/runtime) (incl. Windows Desktop Runtime) | 10 | MIT | Runtime and WinForms |
| [xUnit](https://xunit.net/) / xunit.runner.visualstudio | 2.9.3 / 3.1.4 | Apache-2.0 | Tests (development only) |
| [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) / coverlet.collector | 17.14.1 / 6.0.4 | MIT | Tests (development only) |
| [System.Speech](https://github.com/dotnet/runtime) | 9.0.0 | MIT | `Tiisu.AudioGen` TTS |

## Tile art

| Source | Version/commit | License | Use |
|---|---|---|---|
| [lietxia/mahjong_graphic](https://github.com/lietxia/mahjong_graphic) | 3e27580 (2023-05-14) | Custom permissive: commercial use, modification, redistribution allowed without attribution | Tile vector source; re-packed as `Assets/tiles.png` |

Full license: `src/Tiisu.App/Assets/licenses/TILES-LICENSE.txt`

## Fonts

| Component | Version | License | Use |
|---|---|---|---|
| [WenYuan Rounded SC](https://github.com/takushun-wu/WenYuanFonts) | v1.110 | SIL OFL 1.1 | UI rounded font subset; renamed Tiisu Rounded SC (OFL reserved name) |

- File: `src/Tiisu.App/Assets/fonts/TiisuRoundedSC-subset.ttf` (subset for the app's text)
- Full license: `src/Tiisu.App/Assets/fonts/LICENSE-WenYuan.md`
- Glyphs unmodified; subset only. Keep this notice and license when distributing.
