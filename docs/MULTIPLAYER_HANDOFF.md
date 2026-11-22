# 联机对战（Polaris）交接与待查清单

给 OpenCode / Gemini 4 Argon 的交接文档。
目标：**只做检查与报告，不改代码**。发现的问题写清楚「怎么复现 / 预期 / 实际 / 可能原因 / 相关文件」，交回给 DSH 修改。

By DSH

---

## 0. 仓库与分支

| 项 | 值 |
| --- | --- |
| 仓库 | `E:\工程文件\TiisuMahjong\NET` |
| 上游 | https://github.com/Pub-Polaris/Tiisu-Mahjong-CSharp |
| 联机分支 | **`Polaris`**（联机代码只在这条分支） |
| 稳定分支 | `main`（**没有**联机代码，停在 `6aa259c`） |
| 当前联机 HEAD | `4bff6b8` |
| 预发布 | tag `r7-MP-Polaris`（GitHub pre-release） |

**注意**：在 `main` 上找不到 `src/Tiisu.Net`、`MultiplayerScreen.cs`、`HostEngine.cs` —— 必须先 `git checkout Polaris`。

架构总览见 `docs/MULTIPLAYER.md`。

---

## 1. 系统结构（一句话版）

- **Host 权威**：房主进程内跑唯一的 `MahjongEngine` + `MatchRuntime`，持有牌山与全部手牌。
- **Client 零牌局逻辑**：只收按自己座位脱敏的 `TableViewModel` 快照，只把鼠标动作转成 `CommandRequest`。
- **传输**：TCP 行 JSON 或 WebSocket，二选一，默认 TCP；UDP 17700 做局域网发现。
- **座位旋转**：Host 发快照前把「观察者座位」旋转到索引 0，客机永远坐在牌桌底部。
- **大厅**：A–D 四区 × 每区 2 桌，共 8 个桌位；每个桌位是独立 P2P Host，没有中央服务器。

### 关键文件

```text
src/Tiisu.Net/
  NetworkModels.cs       RoomInfo（Zone/Table/Transport/Mode/AiFill）、NetworkConstants
  PeerProtocol.cs        PeerEnvelope、IPeerChannel、TcpJsonLineChannel、WebSocketPeerChannel
  PeerServer.cs          房主端：accept、握手、hello/welcome、指令校验入口、大厅、快照发送
  PeerClient.cs          客户端：连接、hello、快照/大厅/结算/聊天接收、发送指令
  WebSocketUpgrade.cs    在 TcpListener 上手工完成 RFC6455 握手（不引入 Kestrel）
  LanDiscovery.cs        UDP 查询 + 每秒信标

src/Tiisu.App/Runtime/
  HostEngine.cs          房主端引擎封装：StartMatch、ValidateCommand、快照广播、聊天、停止
  NetworkClientSession.cs 客户端会话：解析脱敏快照、发指令、结算
  SnapshotRotation.cs    把绝对座位旋转成「自己 = 索引 0」
  SnapshotBuilder.cs     生成脱敏 TableViewModel（只有 viewerSeat 的手牌）
  MatchRuntime.cs        对局驱动；联机相关：_networkMode / _activeHuman / _humanSeats

src/Tiisu.App/Ui/
  MultiplayerScreen.cs   大厅：A–D 槽位、玩家名、协议/区域/模式/AI 补位、确认准备、大厅聊天
  ScreenHost.cs          网络会话生命周期：CreateNetworkHostAsync / JoinRoomAsync / CloseNetworkSessionAsync
  Screens.cs             TableScreen：手牌点击、自动出牌、报牌音效
```

---

## 2. 已知已修的问题（**不要重复报告**）

| 问题 | 状态 | 提交 |
| --- | --- | --- |
| 主机离开房间后 17700 不释放 | 已修 | `260aaa3` |
| 客机手牌盖住（按绝对座位 0 判断本地手牌） | 已修 | `c9aefef` |
| 客户加入即进牌桌，不等房主开始 | 已修（现在停留大厅 + 确认准备） | `64e4433` |
| 输入框每次按键输入两遍 | 已修（`ScreenHostCore.KeyDown` 恒返回 true 导致 `OnKeyPress` 二次输入） | `0eb796c` |
| 中文/日文 IME 完全无法输入 | 已修（新增 `ICharRouter` + `Screen.OnTextInput`，走 `MainForm.OnKeyPress`） | `0eb796c` |
| 非当前行动者点自己手牌，导致别家出牌 | 已修（`TableScreen` 手牌点击要求 `HumanAction.Discard` 在 `vm.Actions` 里） | `4bff6b8` |

自测基线：`dotnet test Tiisu.slnx` → **413 passed / 0 failed**（Net 11、App 113、Core 68、Engine 196、Updater 7、DevTools 18）。

---

## 3. 需要检查的内容（按优先级）

### P0 — 直接影响能不能打完一局

1. **四真人整局**
   4 个窗口各自建房/加入同一房，打完整一局（含吃碰杠、荣和窗口、流局、结算、下一局）。
   - 重点看：轮到谁 → 谁的手牌可点 → 是否只有当前行动者能出牌。
   - 关注 `MatchRuntime.ActiveHumanSeat`、`AvailableActionsFor(seat)` 与快照里 `actions` 是否一致。

2. **多家荣和 / 副露优先级仲裁**
   联机是逐个 seat 判定，`FindHumanResponse(discPlayer)` 只返回**第一个**人类座位。
   - 若同时有两个座位能荣和或能碰/吃，请求方是否按正确优先级（荣和 > 碰/杠 > 吃，且距离顺序）处理？
   - 是否会漏掉第二个能荣和的人？

3. **局末与下一局**
   结算广播（`round_end`）、`AutoConfirmRounds`、Host 点「下一局」后客机是否同步进入下一局。
   有没有可能客机卡在结算界面、或 Host 已开下一局客机还停在上一局。

4. **中途加入 / 离开**
   - 对局中有人退出房间：其余三人是否卡住（当前行动者逃走 → 谁接替？）
   - 大廳有人加入后 Host 已开局：该客户端会被分配 seat 但不参与本局，行为是否合理。

### P1 — 网络与生命周期

5. **端口与资源**
   每条退出路径都验证 17700 的 TCP/UDP 不再监听：
   - 大厅返回 / ESC
   - 牌桌 → 菜单 → 主菜单
   - 直接关窗口
   - 客机断开后房主仍在
   命令：
   ```powershell
   Get-NetTCPConnection -LocalPort 17700 -ErrorAction SilentlyContinue
   Get-NetUDPEndpoint   -LocalPort 17700 -ErrorAction SilentlyContinue
   ```
   检查 `HostEngine.StopAsync` / `PeerServer.StopAsync` / `PeerClient.DisposeAsync` 是否有未等待的路径。

6. **WebSocket 路径**
   `WS` 建房 + 客机用 WS 加入的完整流程；以及
   - 分片大消息（快照接近 1MB 上限）
   - 服务端异常关闭 / 客户端异常断开
   - 非 WebSocket 的裸 HTTP 请求被拒绝
   - TCP 房间与 WS 房间不能互连（同端口单协议）

7. **重连与异常**
   拔网线 / 禁用网卡 / 防火墙拦截后的行为；是否永远卡在「等待房主快照…」而不是给出可读错误。

### P2 — 规则与公平性

8. **快照脱敏**
   客机收到的 `snapshot` JSON 里，只有索引 0 的 `hand` 有牌，其他座位 `hand` 为空数组但 `handCount` 正确。
   不能从快照里反推出牌山或别家手牌（检查 `rinshan`、`legalHand`、`drawnTileId`、`tenpai` 提示是否泄露）。

9. **防作弊校验**
   `HostEngine.ValidateCommand` 应校验：座位、`Revision`、`SnapshotHash`、`actions`。
   用伪造包测试：
   - 改 `seat`
   - 用旧 `revision`
   - 用别的座位的 `snapshotHash`
   - 发自己 `actions` 里没有的动作
   预期：拒绝 + 广播 `cheat` + 本局取消（提示「发现作弊者，本局取消」）。

10. **超时自动出牌**
    `TableScreen` 的长考倒计时到期会 `AutoAct`。
    - 非行动者不应触发
    - 行动者超时后是否按预期出牌，而不是出到别人身上

### P3 — 大厅体验

11. 大厅 A–D 槽位：8 桌满时创建失败、释放后能立即复用、发现延迟（2 秒刷新）是否可接受。
12. 玩家名：改名后是否影响到下一次建房/加入；空名是否回退系统用户名。
13. 大厅聊天：长文本截断（120 字符）、emoji/IME、消息是否按顺序到达。
14. 语言切换（中/英/日）后大厅文案是否立即更新（`UiText`）。

---

## 4. 怎么跑起来

### 构建与测试

```powershell
cd "E:\工程文件\TiisuMahjong\NET"
git checkout Polaris
dotnet test Tiisu.slnx
```

### 安装版（验收标准：必须用安装版，不用 Debug 进程）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\build_installer.ps1
Start-Process .\dist\TiisuMahjong-Setup.exe -ArgumentList '/S' -Wait
& "$env:LOCALAPPDATA\Programs\TiisuMahjong\Tiisu.Mahjong.exe"
```

装好后启动**两个实例**即可单机测联机：

1. 实例 A：主菜单 → 联机对战 → 选 A 区 / TCP → 创建房间
2. 实例 B：主菜单 → 联机对战 → 等 2 秒自动发现 → 点 A1 槽位加入
3. B 点「确认准备」，A 点「开始对局」
4. 验证 B 的手牌在**底部正面**，A 的手牌也在底部正面

### 现成预发布

tag `r7-MP-Polaris`：https://github.com/Pub-Polaris/Tiisu-Mahjong-CSharp/releases/tag/r7-MP-Polaris

---

## 5. 报告格式（请照这个写）

每个问题一段：

```text
### [P0] 一句话标题
- 复现步骤：
- 预期：
- 实际：
- 截图/日志：
- 可能原因（文件:行）：
- 严重度：阻断 / 严重 / 一般 / 体验
```

另外单独一节写：

- 已确认**没问题**的检查项（避免 DSH 重复查）
- 需要产品决策的问题（例如「对局中玩家逃走该怎么处理」）

---

## 6. 约束

- **只读检查**：不要改源码、不要提交、不要动 `main` 分支、不要发布。
- 不要修改第三方源码。
- 不要用 PowerShell 重定向写源码（会写 BOM）；如需临时脚本请用 `[IO.File]::WriteAllText(..., UTF8Encoding($false))`。
- 本机 `pwsh` 实为 PowerShell 5.1，中文源码会被编码破坏。
- 报告里引用文件请写 `相对路径:行号`，方便 DSH 直接定位。
