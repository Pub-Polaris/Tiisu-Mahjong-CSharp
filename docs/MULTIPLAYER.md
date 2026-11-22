# P2P 联机架构

## 边界

- **Host**：唯一权威 `MahjongEngine` / `MatchRuntime`。发牌、判役、计分、牌山、全部手牌只在 Host 进程内存在。
- **Client**：不创建引擎、不洗牌、不判役、不算分。只做两件事：
  1. 接收 Host 按自己的座位裁出的 `TableViewModel` 脱敏快照；
  2. 把鼠标动作转成 `CommandRequest` 发回 Host。
- `Tiisu.Net` 只含传输、发现和 DTO，不引用 `Tiisu.Engine`/`Tiisu.Core`。

## 网络

| 通道 | 端口 | 用途 |
| --- | --- | --- |
| UDP | 17700 | Minecraft 风格局域网查询/信标 |
| TCP | 17700 | 长连接、握手、指令、快照 |

1. Host 在 UDP 17700 监听 `TIISU_DISCOVER/1` 并每秒广播房间信标。
2. Client 向回环 + 各网卡广播地址发查询，收集 `RoomInfo`。
3. Client 用 TCP 17700 连接 Host，发送 `hello`；Host 分配 Seat 1–3，Seat 0 是 Host 自己。
4. Host 在四人到齐后开始 `HostEngine.StartMatch`。

## 指令流

```text
UI 鼠标
→ ITableSession.Post(GameCommand)
→ HostEngine.ValidateCommand(CommandRequest)
→ MatchRuntime.Execute
```

Host 校验：

- `Seat` 必须是该连接自己的座位；
- `Revision` 必须等于 Host 最后发给该座位的快照版本；
- `SnapshotHash` 必须等于 Host 最后发给该座位的快照 SHA-256；
- 动作必须包含在当前 `HumanAction` 里；
- 只有当前行动者能操作。

任一项不符：`PeerServer` 拒绝该包、广播 `cheat`，Host 标记 `MatchCancelled`，提示 **发现作弊者，本局取消**。

## 脱敏快照

`SnapshotBuilder.Build(..., viewerSeat)`：

- 只有 `viewerSeat` 的手牌出现在 `hand`；
- 其他座位 `hand` 为空，只保留张数；
- 暗杠仍按只翻 2/4 张的规则；
- Client 不能从快照反推出牌山或别家手牌。

## 关键文件

- `src/Tiisu.Net/`：`LanDiscovery`、`PeerServer`、`PeerClient`、协议 DTO
- `src/Tiisu.App/Runtime/HostEngine.cs`：Host 权威引擎封装
- `src/Tiisu.App/Runtime/NetworkClientSession.cs`：客户端快照/指令会话
- `src/Tiisu.App/Runtime/ITableSession.cs`：本地/联机统一牌桌接口
- `src/Tiisu.App/Ui/MultiplayerScreen.cs`：创建/发现/加入房间入口

By DSH
