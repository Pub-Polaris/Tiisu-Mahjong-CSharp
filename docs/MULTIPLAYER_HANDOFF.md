# 联机对战（Polaris）代码审读交接清单

给 OpenCode / Gemini 4 Argon 的交接文档。
**她不能操作游戏**，所以这份文档只列「读代码 + 跑测试」能查的东西。
所有需要点鼠标、开多窗口、看画面的检查由 DSH 做，见文末「由 DSH 负责的部分」。

By DSH

---

## 0. 仓库与分支

| 项 | 值 |
| --- | --- |
| 仓库 | `E:\工程文件\TiisuMahjong\NET` |
| 上游 | https://github.com/Pub-Polaris/Tiisu-Mahjong-CSharp |
| 联机分支 | **`Polaris`** |
| 稳定分支 | `main`（**没有**联机代码，停在 `6aa259c`） |
| 当前联机 HEAD | `4bff6b8` |
| 预发布 | tag `r7-MP-Polaris` |

**必须先 `git checkout Polaris`**，否则 `src/Tiisu.Net`、`HostEngine.cs`、`MultiplayerScreen.cs` 都不存在。

架构总览：`docs/MULTIPLAYER.md`。

---

## 1. 系统结构

- **Host 权威**：房主进程内跑唯一 `MahjongEngine` + `MatchRuntime`，持有牌山和全部手牌。
- **Client 零牌局逻辑**：只收按自己座位脱敏的 `TableViewModel`，只把动作转成 `CommandRequest`。
- **传输**：TCP 行 JSON 或 WebSocket（二选一，默认 TCP）；UDP 17700 做发现。
- **座位旋转**：Host 发快照前把观察者座位旋到索引 0。
- **大厅**：A–D 四区 × 每区 2 桌；每桌一个独立 P2P Host。

### 关键文件

```text
src/Tiisu.Net/
  NetworkModels.cs       RoomInfo、NetworkConstants
  PeerProtocol.cs        PeerEnvelope、IPeerChannel、TcpJsonLineChannel、WebSocketPeerChannel
  PeerServer.cs          accept、握手、指令入口、大厅、快照发送
  PeerClient.cs          连接、hello、快照/大厅/结算/聊天接收
  WebSocketUpgrade.cs    TcpListener 上手工 RFC6455 握手
  LanDiscovery.cs        UDP 查询 + 每秒信标

src/Tiisu.App/Runtime/
  HostEngine.cs          房主端：StartMatch、ValidateCommand、快照广播、聊天、停止
  NetworkClientSession.cs 客户端会话：解析快照、发指令
  SnapshotRotation.cs    绝对座位 → 观察者座位
  SnapshotBuilder.cs     生成脱敏 TableViewModel
  MatchRuntime.cs        对局驱动（_networkMode / _activeHuman / _humanSeats）

src/Tiisu.App/Ui/
  MultiplayerScreen.cs   大厅：槽位、玩家名、协议、确认准备、聊天
  ScreenHost.cs          网络会话生命周期
  Screens.cs             TableScreen：手牌点击、自动出牌
```

---

## 2. 已修问题（**不要重复报告**）

| 问题 | 提交 |
| --- | --- |
| 主机离开房间后 17700 不释放 | `260aaa3` |
| 客机手牌盖住 | `c9aefef` |
| 客户加入即进牌桌 | `64e4433` |
| 输入框每次按键输入两遍 | `0eb796c` |
| 中文 IME 无法输入 | `0eb796c` |
| 非当前行动者点手牌触发别家出牌 | `4bff6b8` |

测试基线：`dotnet test Tiisu.slnx` → **413 passed / 0 failed**。

---

## 3. 你可以做的检查（静态审读 + 单元测试）

### P0 — 牌局正确性

**3.1 多家荣和 / 副露优先级仲裁**

`MatchRuntime.AfterDiscard`、`CheckMelds`、`FindHumanResponse(discPlayer)`。

- 问题：`FindHumanResponse` 只返回**第一个**人类座位。
- 请确认：同一张弃牌同时能被两人荣和、或一人荣和一人吃碰时，判定顺序是否符合规则（荣和 > 碰杠 > 吃；同级按离放铳者距离）。
- 是否会漏掉第二个能荣和的人。
- 联机（`_networkMode = true`）与单机（`false`）在 `FindHumanResponse` 上的差异是否会造成行为不一致。

**3.2 行动者唯一性**

`AvailableActionsFor(human)` 的守卫：

```csharp
if (human < 0 || (_activeHuman >= 0 && human != _activeHuman)) return HumanAction.None;
```

- `_activeHuman` 为 -1 时是否可能让多个座位同时拿到动作权限。
- `_activeHuman` 在哪些路径被重置为 -1，是否存在重置后其他座位仍可操作的窗口。
- `SnapshotForSeat(seat)` 里的 `actions` 与该座位真实可执行动作是否始终一致。

**3.3 快照脱敏**

`SnapshotBuilder.Build(..., viewerSeat)`：

- 非 `viewerSeat` 的座位 `hand` 是否恒为空、`handCount` 是否准确。
- `legalHand`、`drawnTileId`、`drawnIndex`、`tenpai`、`rinshan`、`noten` 是否泄露别家信息。
- Host 本地（`viewerSeat = -1`）与远程路径是否共用同一套裁剪逻辑，有没有绕过口。

**3.4 防作弊校验**

`HostEngine.ValidateCommand`：

- 是否覆盖：座位、`Revision`、`SnapshotHash`、`actions` 白名单。
- `OnCommand` 与 `ValidateCommand` 是否会重复执行同一校验、或漏掉某一项。
- `RejectAsync` 广播 `cheat` 后，本地引擎是否确实停止推进（`MatchCancelled` + `ResetAll`）。

**3.5 座位旋转**

`SnapshotRotation.RotateForViewer`：

- `TurnIndex`、`DrawnIndex`、`HandRects` 相关字段是否都跟着旋转。
- `Seats[i].Index` 重写后，`SettlementOverlay` / `RoundResult.WinnerIndex` 等仍按**绝对座位**使用的字段是否会错位（这是重点，容易出现「结算把赢家的牌显示在别人位置上」）。

### P1 — 网络与生命周期

**3.6 端口释放路径**

`HostEngine.StopAsync()`、`PeerServer.StopAsync()`、`PeerClient.DisposeAsync()`、`ScreenHostCore.CloseNetworkSessionAsync()`：

- 找出所有**未被 await** 的停止调用。
- `_cts.Dispose()` 是否可能在仍有任务使用它时执行。
- `StopAsync` 幂等性：第二次调用是否安全。
- 异常路径（`Stop` 抛 `SocketException`）是否被吞掉后继续。

**3.7 并发与线程**

- `PeerServer._connections` 的读写是否都在 `_lock` 内。
- `HostEngine._lastSentRevision` / `_lastSentHash` 数组被网络线程写、引擎线程读，是否有可见性/竞态问题。
- `MultiplayerScreen` 的 `Post(...)` 封送是否覆盖所有跨线程回调。
- `NetworkClientSession.Dispatch` 在 `_uiContext` 为 null 时的行为。

**3.8 WebSocket 通道**

`WebSocketPeerChannel`：

- 分片消息重组是否正确。
- `MaxMessageBytes` 超限时是否正确关闭。
- 二进制帧、空帧、`Close` 帧的处理。
- 发送与接收并发时的锁粒度（`_writeLock` 只保护发送，接收无锁是否安全）。

**3.9 空指针与健壮性**

- `RoomInfo` 旧 JSON 缺字段的默认值路径。
- `MultiplayerScreen` 中 `_client` 为 null 但仍在回调里使用的可能性。
- `HostEngine.Lobby` 在连接半断开状态下的内容。
- 是否有 `catch {}` 吞掉了本该上报的错误。

### P2 — 可测试性

**3.10 现有测试覆盖缺口**

`tests/Tiisu.Net.Tests/NetworkTests.cs`、`tests/Tiisu.App.Tests/Host*.cs`：

- 哪些分支现在完全没有测试（例如多家荣和、非行动者越权、WS 分片、断线重连）。
- 建议补哪些**纯逻辑**测试（不需要 UI、不需要开窗口）。
- 你可以直接在本地 `dotnet test` 跑现有用例，确认是否 413 全过。

---

## 4. 你**不能**做的（由 DSH 负责）

这些需要鼠标、多窗口、真实画面或安装版，请**不要**尝试：

- 启动游戏、点菜单、建房、加入房间
- 四窗口整局对打
- 看客机手牌位置、牌桌渲染是否正确
- 在真实网络环境断线/重连
- 检查端口是否真的释放（需要运行中的进程）
- 安装版验收、截图

---

## 5. 报告格式

每个问题一段：

```text
### [P0] 一句话标题
- 位置：src/.../文件.cs:行号
- 现象/风险：
- 触发条件：
- 建议改法：
- 严重度：阻断 / 严重 / 一般 / 体验
```

另外单独列出：

- 已确认**没问题**的检查项
- 需要产品决策的问题（例如「对局中玩家逃走怎么处理」）

---

## 6. 约束

- **只读**：不改源码、不提交、不切换/不推送 `main`、不发布。
- 不改第三方源码。
- 本机 `pwsh` 实为 PowerShell 5.1，不要用它重定向写源码（会写 BOM）；临时脚本用 `[IO.File]::WriteAllText(..., UTF8Encoding($false))`。
- 引用请写 `相对路径:行号`，方便 DSH 直接定位。
