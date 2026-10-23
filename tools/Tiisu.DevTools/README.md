# Tiisu.DevTools

四个分区的小启动器：美工 / 音效 / 布局 / 牌桌样式。按钮直接调用仓库里已构建的
`Tiisu.Mahjong.exe` 与 `Tiisu.AudioGen.exe`，不引用 App 的代码，也不改 `Tiisu.App/Program.cs`。

## 启动

```powershell
dotnet run --project tools/Tiisu.DevTools
```

先构建被调用的工具（否则按钮会提示找不到命令）：

```powershell
dotnet build src/Tiisu.App -c Release
dotnet build tools/Tiisu.AudioGen -c Release
```

## 路径解析

1. `TIISU_ROOT` 环境变量（可指向仓库根 `NET` 或它的上级 `TiisuMahjong`）
2. 从程序目录 / 当前目录向上找同时含 `src/Tiisu.App` 与 `Tiisu.slnx` 的目录
3. 可执行文件优先
   `src/Tiisu.App/bin/Release/net10.0-windows10.0.19041/Tiisu.Mahjong.exe`，
   再试 Debug / 其它配置，最后在 `bin/` 下递归找最新的。

## 命令

| 分区 | 按钮 | 命令 |
|---|---|---|
| 美工 | 导出合并 SVG | `Tiisu.Mahjong.exe --export-ui-svg artifacts/uitools/ui-merged.svg` |
| 美工 | 按钮三态图 | `Tiisu.Mahjong.exe --render-actions artifacts/uitools/btn-colors.png` |
| 音效 | 生成音效+语音 | `Tiisu.AudioGen.exe`（默认输出到 `src/Tiisu.App/Assets`） |
| 音效 | 只生成音效 | `Tiisu.AudioGen.exe --sfx` |
| 布局 | 启动游戏（布局编辑） | `Tiisu.Mahjong.exe`，进入牌桌按 F2 |
| 布局 | 查看当前自定义布局 | 打开 `%APPDATA%\TiisuMahjong\layout-custom.json` |
| 布局 | 删除自定义布局（恢复默认） | 删除上面的 json（先确认） |
| 牌桌样式 | Flat / Light25D / Medium25D / Mixed | `Tiisu.Mahjong.exe --render-25d artifacts/uitools/style-<x>.png --variant <x>` |

生成成功后自动在资源管理器里定位输出文件；`全部生成并打开目录` 依次跑四档再开目录。
状态栏显示每条的 exit code 与耗时，找不到命令时给出构建提示。
