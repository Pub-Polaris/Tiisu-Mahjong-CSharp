# 第三者素材・コンポーネント

本リポジトリのコードは [MIT License](LICENSE)。以下は MIT の対象外で、それぞれ元のライセンスに従う。

## 音楽

| 項目 | 内容 |
|---|---|
| 曲名 | Parisian |
| 作者 | Kevin MacLeod（[incompetech.com](https://incompetech.com/music/royalty-free/)） |
| ライセンス | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) |
| ファイル | `src/Tiisu.App/Assets/bgm/lobby.wav`（44.1 kHz / ステレオ / 41.45秒） |
| 変更 | -5 dB、メニューBGMとしてループ |

リポジトリと一緒に配布する場合は上記の著作表示を残す。メインメニュー下部に作者の音楽ライブラリとライセンスへのリンク付き表示がある。

## 効果音・音声

`src/Tiisu.App/Assets/sounds/` と `src/Tiisu.App/Assets/voices/` はすべて本リポジトリの `tools/Tiisu.AudioGen` で合成（正弦波／ノイズ／TTS）。第三者の録音素材は含まない。

## 第三者のライブラリ

| コンポーネント | バージョン | ライセンス | 用途 |
|---|---|---|---|
| [NAudio](https://github.com/naudio/NAudio) | 2.2.1 | MIT | 音声ミックス・再生（BGM + 効果音） |
| NAudio.Asio / Core / Midi / Wasapi / WinForms / WinMM | 2.2.1 | MIT | NAudio の推移的依存 |
| HarfBuzzSharp / NativeAssets.Win32 | 14.2.1.301 | MIT | SkiaSharp.HarfBuzz の推移的依存 |
| OpenTK / OpenTK.GLControl | 4.8.2 / 4.0.1 | MIT | OpenGL ウィンドウ・コントロール |
| GLFW（OpenTK.redist.glfw） | 3.3.8.39 | Zlib/libpng | ウィンドウ／入力ネイティブライブラリ |
| YogaSharp.Core / SkiaSharp.Views.Desktop.Common | 1.0.6 / 4.153.1 | MIT | レイアウト／デスクトップコントロールの推移的依存 |
| [SkiaSharp](https://github.com/mono/SkiaSharp)（NativeAssets.Win32 / Views.WindowsForms / HarfBuzz 含む） | 4.153.1 | MIT | 2D描画・牌画像レンダリング |
| [YogaSharp.Layout](https://github.com/RaZeR-RaY/YogaSharp) | 1.0.6 | MIT | 牌卓の Flex レイアウト |
| [.NET](https://github.com/dotnet/runtime)（Windows Desktop Runtime 含む） | 10 | MIT | ランタイム・WinForms |
| [xUnit](https://xunit.net/) / xunit.runner.visualstudio | 2.9.3 / 3.1.4 | Apache-2.0 | テスト（開発のみ） |
| [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) / coverlet.collector | 17.14.1 / 6.0.4 | MIT | テスト（開発のみ） |
| [System.Speech](https://github.com/dotnet/runtime) | 9.0.0 | MIT | `Tiisu.AudioGen` の音声合成 |

## 牌画像

| 出典 | バージョン/コミット | ライセンス | 用途 |
|---|---|---|---|
| [lietxia/mahjong_graphic](https://github.com/lietxia/mahjong_graphic) | 3e27580（2023-05-14） | 独自の自由ライセンス：商用可・改変可・再配布可、著作表示不要 | 牌画像ベクター素材。`Assets/tiles.png` に再配置 |

ライセンス全文：`src/Tiisu.App/Assets/licenses/TILES-LICENSE.txt`

## フォント

| コンポーネント | バージョン | ライセンス | 用途 |
|---|---|---|---|
| [WenYuan Rounded SC](https://github.com/takushun-wu/WenYuanFonts) | v1.110 | SIL OFL 1.1 | UI用丸ゴシックのサブセット。OFL予約名を避けて Tiisu Rounded SC に改名 |

- ファイル：`src/Tiisu.App/Assets/fonts/TiisuRoundedSC-subset.ttf`（アプリ文案に合わせたサブセット）
- ライセンス全文：`src/Tiisu.App/Assets/fonts/LICENSE-WenYuan.md`
- 字形は未変更。サブセット化のみ。配布時は本表示とライセンスを残すこと。
