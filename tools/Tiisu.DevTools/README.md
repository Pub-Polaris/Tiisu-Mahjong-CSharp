# Tiisu.DevTools

任务中心 + 旧版四分区启动器。第一轮迁移「发布 / 合规」，其余分类留入口；
旧版页保留美工 / 音效 / 布局 / 牌桌样式。按钮直接调用仓库里已构建的
`Tiisu.Mahjong.exe` 与 `Tiisu.AudioGen.exe`，不引用 App 的代码，也不改 `Tiisu.App/Program.cs`。

任务中心与旧版页共享底部运行日志；发布任务不需要记 CLI。

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

1. `%APPDATA%\TiisuMahjong\config.toml` 的 `[dev] root`（可指向仓库根 `NET` 或它的上级 `TiisuMahjong`）
2. 从程序目录 / 当前目录向上找同时含 `src/Tiisu.App` 与 `Tiisu.slnx` 的目录
3. 可执行文件优先
   `src/Tiisu.App/bin/Release/net10.0-windows10.0.19041/Tiisu.Mahjong.exe`，
   再试 Debug / 其它配置，最后在 `bin/` 下递归找最新的。

## 任务中心（发布）

| 任务 | 说明 |
|---|---|
| 发布自检 | 检查 `dist/publish` 的许可、字体、资源哈希与禁带项 |
| 生成安装包 | 重新 publish、打包 Setup 并复制到桌面（会确认覆盖） |
| 生成规则库 | 生成规则库独立 zip |
| 复制产物哈希 | 把 `dist` 下文件的 SHA256 写进日志 |
| 打开 dist | 定位产物目录 |
| 打开桌面安装包 | 定位 `TiisuMahjong-Setup.exe` |

## 任务中心（出图）

| 任务 | 说明 |
|---|---|
| 牌桌/菜单/设置/新手引导/参考文档截图 | `--render-screen`，输出到上方目录 |
| 结算页截图 | `--render-roundend` |
| 按钮三态图 | `--render-actions` |
| 导出合并 SVG | `--export-ui-svg` |
| 牌桌样式：平面 | `--render-25d --variant flat` |
| 打开出图目录 | 定位本次输出 |

出图完成自动在右侧显示缩略图；目录可输入或浏览选择。

## 任务中心（AI 数据）

| 任务 | 说明 |
|---|---|
| 导出自博弈数据 | 弹窗选局数/jobs/每 N 步采样/探索率/输出 JSONL |
| 验证 7Neuu 权重 | `--smoke-neural` |
| 验证 7Neuu 决策 | `--smoke-7neuu` |
| UI 快照性能 | `--bench-snapshot 20000` |
| 打开数据目录 | 定位 `artifacts/selfplay` |

## 任务中心（审局 / 资源）

| 分类 | 任务 | 说明 |
|---|---|---|
| 审局 | 批量审局 | 弹窗选局数/模式/jobs/种子，可开 UI 不变量与报听分支 |
| 审局 | UI 不变量审计 | 固定 50 局 `--ui` |
| 审局 | 报听分支审计 | 固定 50 局 `--call --passive` |
| 审局 | 整场冒烟 | `--smoke-run 1 4` |
| 资源 | 字体缺字检查 | `--check-font`，覆盖版本号/UI/docs，有缺字退出 1 |
| 资源 | 导出 UI 图集 | `--export-ui-atlas <dir>`，完成后右侧预览 |
| 资源 | 打开 Assets / docs | 定位资源与文档目录 |

所有分类均已迁移；旧版工具页保留，后续决定是否移除。

## 命令

| 分区 | 按钮 | 命令 |
|---|---|---|
| 美工 | 导出合并 SVG | `Tiisu.Mahjong.exe --export-ui-svg artifacts/uitools/ui-merged.svg` |
| 美工 | 按钮三态图 | `Tiisu.DevTools.exe --render-actions artifacts/uitools/btn-colors.png` |
| 音效 | 生成音效+语音 | `Tiisu.AudioGen.exe`（默认输出到 `src/Tiisu.App/Assets`） |
| 音效 | 只生成音效 | `Tiisu.AudioGen.exe --sfx` |
| 布局 | 启动游戏（布局编辑） | `Tiisu.Mahjong.exe`，进入牌桌按 F2 |
| 布局 | 查看当前自定义布局 | 打开 `%APPDATA%\TiisuMahjong\layout-custom.json` |
| 布局 | 删除自定义布局（恢复默认） | 删除上面的 json（先确认） |
| 牌桌样式 | Flat / Light25D / Medium25D / Mixed | `Tiisu.DevTools.exe --render-25d artifacts/uitools/style-<x>.png --variant <x>` |
| 发布 | 发布自检 | `Tiisu.DevTools.exe --check-publish dist/publish` |
| 发布 | 生成安装包 | `installer/build_installer.ps1 -CopyToDesktop` |
| 发布 | 生成规则库 | `tools/build_rules_lib.ps1` |

生成成功后自动在资源管理器里定位输出文件；`全部生成并打开目录` 依次跑四档再开目录。
状态栏显示每条的 exit code 与耗时，找不到命令时给出构建提示。
