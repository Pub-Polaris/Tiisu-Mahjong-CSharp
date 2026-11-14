# Steam 前置清单

状态：**准备阶段，未接入 Steamworks SDK**。

## 1. 构建与 Depot

| 项 | 计划 |
|---|---|
| 可执行文件 | `Tiisu.Mahjong.exe` |
| 构建产物 | `dist/publish`（自包含或依赖 .NET 10 Desktop Runtime） |
| 目标平台 | Windows 10/11 x64 |
| 启动参数 | 无必填参数；`--fullscreen`、`--lang en-US`、`--cpu-render`（排障） |
| Depot | 单个 Windows content depot，根目录 = `dist/publish` |
| 安装后数据 | `%APPDATA%\TiisuMahjong`（settings/stats/logs/voices） |
| 框架依赖 | 若不做 self-contained，需要在商店页声明 .NET 10 Desktop Runtime |

发布前必须验证：

```powershell
dotnet test Tiisu.slnx
dotnet run --project tools/Tiisu.DevTools -- --check-publish dist/publish

# 生成 Steam depot 内容、manifest 与 VDF 模板
powershell -NoProfile -ExecutionPolicy Bypass -File tools/steam_package.ps1
```

## 2. Steamworks 接入点（待做）

- [ ] 申请 AppID，创建 Windows depot
- [ ] 选择 Steamworks.NET 或 Facepunch.Steamworks 绑定
- [ ] 启动时 `SteamAPI.Init`，失败时静默降级（不阻塞离线单机）
- [ ] 成就：本地成就 ID 映射到 Steam Achievement（见 `docs/STEAM_ACHIEVEMENTS.md`）
- [ ] 统计：把本地 `stats.json` 映射到 Steam Stats（可选）
- [ ] Cloud：同步 `settings.json` / `stats.json`，不同步 `voices/` 与 `logs/`
- [ ] 关闭按钮/退出时 `SteamAPI.Shutdown`

## 3. 商店素材清单

| 素材 | 要求 | 现状 |
|---|---|---|
| 截图 | 1920×1080，≥5 张 | 可出牌桌/菜单/结算/战绩/成就 |
| 主胶囊 | 616×353 | 已生成 `artifacts/steam/capsules/main-616x353.png` |
| 小胶囊 | 231×87 | 已生成 `artifacts/steam/capsules/small-231x87.png` |
| 页头胶囊 | 460×215 | 已生成 `artifacts/steam/capsules/header-460x215.png` |
| 库胶囊 | 600×900 | 已生成 `artifacts/steam/capsules/library-600x900.png` |
| 库头图 | 920×430 | 已生成 `artifacts/steam/capsules/library-hero-920x430.png` |
| 预告片 | 可选 | 未做 |

可用出图命令：

```powershell
# 一次导出 6 张 1920×1080 商店截图
dotnet run --project tools/Tiisu.DevTools -- --export-steam-shots artifacts/steam
# 英文版
dotnet run --project tools/Tiisu.DevTools -- --export-steam-shots artifacts/steam-en --lang en-US

# 胶囊图（主图/页头/小图/库图/库头图）
dotnet run --project tools/Tiisu.DevTools -- --export-steam-capsules artifacts/steam/capsules

# 单张出图
dotnet run --project tools/Tiisu.DevTools -- --render-screen artifacts/steam/table.png table
dotnet run --project tools/Tiisu.DevTools -- --render-screen artifacts/steam/menu.png menu
dotnet run --project tools/Tiisu.DevTools -- --render-screen artifacts/steam/stats.png stats
dotnet run --project tools/Tiisu.DevTools -- --render-roundend artifacts/steam/settlement.png
```

## 4. 商店文案

见 `docs/STEAM_STORE_COPY.md`。需要提交：

- 简短描述（中文 + 英文）
- 长描述（中文 + 英文）
- 标签、类型、系统需求
- 内容问卷（无真实赌博、无在线联机、无内购）

## 5. 合规

- 字体：文渊圆体子集，SIL OFL 1.1，已重命名 `Tiisu Rounded SC`
- 牌面：`lietxia/mahjong_graphic`，许可见游戏内「开源信息」
- 音乐：Parisian · Kevin MacLeod · CC BY 4.0，署名保留
- 语音：本机 Windows SAPI 生成，不随包分发
- 第三方参考：见 `docs/REFERENCES.md` 与 `docs/ASSET-PROVENANCE.md`
- 自博弈权重：`ml/policy_weights_v4.json` 仅参考，不是棋力承诺
- 不含真实货币赌博、抽卡、内购或在线收集

## 6. 上线前人工验收

- [ ] 全新 Windows 用户安装后能启动
- [ ] 无 .NET 10 时安装器提示正确
- [ ] 全屏 F11 / 大字号 130% 不裁切
- [ ] 新手引导、战绩、成就、牌谱回看可打开
- [ ] 报听/和牌二次确认生效
- [ ] 离线（断网）可完整对局
- [ ] 存档/统计写入失败不崩
- [ ] 卸载后残留仅 `%APPDATA%\TiisuMahjong`
