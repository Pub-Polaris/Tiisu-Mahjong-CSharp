# Steamworks 接入预研

状态：**未接入**。`ISteamService` / `NullSteamService` / `SteamAchievementSync` 骨架已完成；等 AppID/DepotID 下来后替换真实适配器。

## 1. 依赖选择

| 方案 | 优点 | 代价 |
|---|---|---|
| Steamworks.NET | C# 绑定成熟、纯 C# 调用 | 需要随包 `steam_api64.dll` |
| Facepunch.Steamworks | API 更友好 | 依赖版本与 Steamworks 更新节奏绑定 |

建议先用 **Steamworks.NET**；只包一层 `ISteamService`，避免业务代码直接碰 SDK。

## 2. 构建与文件

- NuGet：`Steamworks.NET`（版本固定，不浮动）
- 原生库：`steam_api64.dll` 随包，放在 exe 同级
- 开发期 `steam_appid.txt` 放在工作目录，**不随 Depot 发布**
- 正式 AppID 通过构建常量注入：`SteamAppId`，缺省 0 = 离线
- 可选编译符号 `STEAM`；无符号时使用 `NullSteamService`

SteamPipe Depot 里只允许：

```
Tiisu.Mahjong.exe
*.dll（含 steam_api64.dll）
Assets/
*.json（配置）
```

排除：

```
steam_appid.txt
*.pdb
artifacts/
logs/
voices/
```

## 3. 生命周期

```
Program.Main
  ├─ 创建 SteamService
  ├─ TryInitialize (≤2s 超时；失败 → Null)
  ├─ Application.Run(MainForm)
  └─ Shutdown
```

原则：

- 初始化失败**不能阻塞单机**；不联网也能完整对局
- 不把 Steam 回调放在 UI 线程之外直接操作控件
- 关闭窗口/退出菜单统一走 `Shutdown`

## 4. 成就

本地成就由 `Achievements.From(StatsSummary)` 现算，Steam 只负责上报。对局结束时 `ScreenHost` 触发一次扫描；服务不可用时写入 `steam-pending.json`，下次可用时补报。

- 映射：`SteamAchievementMap`（10 项，已加测试）
- 时机：启动后扫描一次 + 每局结束扫描一次
- 幂等：已解锁跳过；SDK 未初始化不报错
- 离线：写入 `%APPDATA%\TiisuMahjong\steam-pending.json`，下次初始化后补报
- 失败：不弹窗，写日志；不阻塞结算页

待确认：大七星/见证是否设为隐藏成就（`docs/STEAM_ACHIEVEMENTS.md` 已标 hidden）。

## 5. Stats（可选）

Steam Stats 只做展示用途，不参与判分：

- `rounds` / `wins` / `tsumo_wins` / `ron_wins` / `tiisuin_count` / `witness_count`
- `StoreStats` 后 `StoreStats` 周期上报；失败忽略

## 6. Cloud

仅同步：

- `settings.json`
- `stats.json`

不同步：

- `logs/`（体积大、含牌局）
- `voices/`（本机 SAPI 生成）
- `layout-custom.json`（可选项，按需）

## 7. 测试

1. 无 SDK / 无 AppID：`NullSteamService`，所有调用 no-op，293→294 测试仍绿
2. 假 `ISteamService`：验证成就只上报一次、离线队列补报
3. 用 Steamworks 测试 App **480 (Spacewar)** 验证：
   - `SteamAPI.Init` 成功
   - 成就面板可见
   - Depot 内容不含 `steam_appid.txt`
4. 断网启动：不弹错误，正常单机

## 8. 风险

- `SteamAPI.Init` 在某些环境会阻塞；必须加超时/异步初始化
- `steam_api64.dll` 架构必须 x64，避免 x86 混入
- 成就 ID 一旦上传不可随意改；本地 ID 已锁测试
- 不把 Steam 初始化失败当成启动失败
- 上线前用 Spacewar 480 跑通，再切正式 AppID

## 9. 待办

- [ ] 申请 AppID / DepotID
- [ ] 加 Steamworks.NET 包与原生库
- [x] 实现 `ISteamService` / `NullSteamService` / `SteamAchievementSync` 骨架
- [x] 接入成就扫描（对局结束触发）与离线补报
- [ ] Steam Cloud 配置
- [ ] Depot 模板填真实 ID
