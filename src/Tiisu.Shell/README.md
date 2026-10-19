# Tiisu.Shell · WinForms + WebView2 外壳

把 `Web/` 的现有 UI 原样跑在 C# 程序里；引擎换成 C#（`Tiisu.Engine`），通过 WebView2 host object 同步桥注入。

## 运行

```powershell
dotnet run --project src/Tiisu.Shell                        # 人类对局（C# 引擎）
dotnet run --project src/Tiisu.Shell -- --web <Web 目录>     # 指定 Web 目录
dotnet run --project src/Tiisu.Shell -- --smoke 15          # 自检：auto=1&mode=1，到时退出
dotnet run --project src/Tiisu.Shell -- --shot out.png      # 加载 3 秒后截图
dotnet run --project src/Tiisu.Shell -- --smoke 20 --drive-human   # 人类模式自动出牌（验证用）
```

Web 目录默认从 `AppContext.BaseDirectory` 上溯找 `Web/index.html`（开发期即 `NET/src/Tiisu.Shell/bin/...` → 仓库根 `Web/`）。

## 数据目录 / 窗口 / 发布

| 项 | 约定 |
| --- | --- |
| 数据根目录 | `%APPDATA%\TiisuMahjong`（`settings.json` / `state.json` / `stats.json` / `logs\`）。首次启动建目录并补齐默认 `settings.json` / `stats.json`；目标为空而 Web 目录有旧数据时一次性拷贝迁移（源文件保留） |
| 窗口 | 客户区默认 1460×900（页面 `.app` / `.table` 的固定尺寸）；可缩放，`ZoomFactor = min((宽−2×12−滚动条)/1460, (高−2×12)/900)`——扣掉 `body{padding:12px}` 与常驻竖滚动条，保证牌桌不被裁；不注入 CSS、不改页面尺寸 |
| 发布 | 框架依赖：`dotnet publish src/Tiisu.Shell -c Release -r win-x64 --self-contained false`，产物 exe 为 `Tiisu.Mahjong.exe`（csproj 的 `AssemblyName`，与 `installer/*.nsi` 默认一致）；publish 时把 `Web/` 的运行时资源一并复制到 `publish\Web\`，装完即可直接运行 |

## 结构

| 文件 | 职责 |
| --- | --- |
| `Program.cs` / `ShellOptions.cs` | 参数解析、控制台接管（仅自检）、Web 目录解析 |
| `MainForm.cs` | 唯一 Form + 铺满的 `WebView2`；虚拟主机映射、host object、`/api/*` 过滤器、等比缩放、`--smoke` / `--shot` |
| `EngineHost.cs` / `EngineHost.Json.cs` | `[ComVisible]` host object（`chrome.webview.hostObjects.sync.tiisu`），与 `engine.js` 同名方法；修改型调用返回完整状态快照 |
| `ApiRouter.cs` | `/api/*` → `Tiisu.Api.DataStore`，响应形状与 `server.py` / `TiisuApiApp` 一致 |
| `ShellDataDirectory.cs` | `%APPDATA%\TiisuMahjong` 解析 + 一次性旧数据迁移 |

Web 侧新增 `Web/js/host_adapter.js`（`class TiisuMahjongHost`），`Web/index.html` 只多了一行 `<script>`。
默认（浏览器 / `run_all.py`）仍走 JS 引擎；只有 `?engine=csharp` + WebView2 宿主才接管。

## 平台限制：虚拟主机上的 WebResourceRequested

WebView2 文档明确：`WebResourceRequested` **不会**为 `SetVirtualHostNameToFolderMapping` 映射的 URL 触发
（[Custom management of network requests](https://learn.microsoft.com/en-us/microsoft-edge/WebView2/how-to/webresourcerequested?tabs=dotnet)）。

因此 `host_adapter.js` 只把同源 `/api/*` 的 origin 改写到 `tiisu-api.local`（path / query / body 原样），
由宿主同一个 `WebResourceRequested` 处理器应答 `Tiisu.Api.DataStore`——仍然没有任何 HTTP 服务器，
页面代码与 URL 语义不变。

## 已知差距

- `recordTiles` 的 roundlog（JSONL 明细）没有迁到 C# 宿主；设置项只回传，不落盘。
- 和牌 / 流局的 HTML overlay（`showMsg` 明细表）没有迁；牌桌、分数、明牌正常。
- `/api/stats` 的和牌计数不再由引擎自动上报（接口本身可用）。
- 终局不自动跳回 `portal.html`。
