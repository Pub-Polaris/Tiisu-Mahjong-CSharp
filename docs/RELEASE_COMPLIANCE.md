# 发布合规检查清单

版本基线：`261114-7cd486c` 之后。  
说明：本清单是工程/合规自查，不构成法律意见；涉及发行地区资质时需专业确认。

## A. 许可与声明

| # | 项 | 状态 | 证据 / 下一步 |
|---|---|---|---|
| A1 | 项目自身 MIT 许可证 | ✅ | `LICENSE` |
| A2 | 第三方组件清单 | ✅ | 已补 HarfBuzzSharp、OpenTK/GLFW、NAudio.*、YogaSharp.Core 等到 `THIRD-PARTY-NOTICES.md` 与游戏内列表 |
| A3 | 许可证全文随包分发 | ✅ | `Assets/licenses/MIT.txt`、`Apache-2.0.txt`、`OFL-1.1.txt`、`CC-BY-4.0.txt`、`REFERENCES.txt` |
| A4 | 游戏内可查看 | ✅ | 菜单「开源信息」→ 每页「查看本页许可全文」 |
| A5 | 字体 OFL 保留字体名 | ✅ | 子集重命名为 `Tiisu Rounded SC`，版权与 OFL 声明保留 |
| A6 | BGM CC BY 4.0 署名 | ✅ | 菜单底部署名 + 许可页 Parisian 条目 |
| A7 | 报牌语音不再分发 | ✅ | `Assets/voices` 已删除；本机 SAPI 生成到 `%LOCALAPPDATA%\TiisuMahjong\voices` |
| A8 | 开发/测试依赖不随包 | ✅ | publish 已复核：无 xunit/testhost/coverlet/TestPlatform |
| A9 | MIT 版权行完整 | ✅ | `MIT.txt` 已补 NAudio/OpenTK/HarfBuzzSharp/YogaSharp 等版权行；GLFW 单独 Zlib 文本 |

## B. 素材与来源

| # | 项 | 状态 | 证据 / 下一步 |
|---|---|---|---|
| B1 | 音效可复现 | ✅ | `ASSET-HASHES.md`；9 个音效由 `Tiisu.AudioGen` 逐字节重建 |
| B2 | 牌面 `tiles.png` | ✅ | 来源 `lietxia/mahjong_graphic`（自由许可，可商用/修改/再分发）；见许可页与 `TILES-LICENSE.txt` |
| B3 | 图标 `Tiisu.Mahjong.ico` | ✅ | 自制/生成（用户确认） |
| B4 | UI 图集 `ui-atlas.png` | ✅ | 由 `--export-ui-atlas` 从现有 UI 绘制代码生成 |
| B5 | 布局 `layout/menu.json` | ✅ | 本项目自有配置 |
| B6 | BGM `lobby.wav` | ✅ | Kevin MacLeod · CC BY 4.0，已署名；修改仅音量 |
| B7 | 字体子集 | ✅ | 文渊圆体 v1.110，OFL，已重命名并附许可证 |

## C. 逆向 / EULA / 商标

| # | 项 | 状态 | 证据 / 下一步 |
|---|---|---|---|
| C1 | Alice in Cradle | ✅ | 仅空间音频架构调研；未使用代码/中间件/素材，见 `docs/REFERENCES.md` 与许可页 |
| C2 | 明星三缺一 2002 | ✅ | 仅包络/时长分析；音效独立合成，未使用原音频/代码，见 `ASSET-HASHES.md` |
| C3 | 天凤 Tenhou | ✅ | 仅听牌形参考；未使用代码/数据 |
| C4 | 旧提交中的具名注释 | ⚠️ | 历史保留；`REFERENCES.md` 说明现状，被问时指该文件 |
| C5 | 商标与宣传 | ⚠️ | 不在标题/logo/商店页使用上述作品名；只做规则兼容说明 |
| C6 | 视觉实质性相似 | ⚠️ | 牌桌/UI 需确认未复刻参考游戏的整体外观 |

## D. 分发与构建

| # | 项 | 状态 | 证据 / 下一步 |
|---|---|---|---|
| D1 | 发布包不含语音 | ✅ | publish 已确认无 `Assets/voices` |
| D2 | 发布包含许可证全文 | ✅ | publish 已确认 `Assets/licenses/*`（含数据集/牌面许可） |
| D3 | 安装包/规则库校验和 | ⚠️ | Release 说明已有 SHA256；每次发版需更新 |
| D4 | 音频哈希清单 | ✅ | `ASSET-HASHES.md` |
| D5 | 安装包未签名 / SmartScreen | ⚠️ | 非合规硬伤，但影响信任；可考虑代码签名 |
| D6 | 自动更新完整性 | ✅ | `Tiisu.Updater --sha256 <hex>` 支持应用前校验 zip 哈希；签名仍未做 |

## E. 平台与地区

| # | 项 | 状态 | 证据 / 下一步 |
|---|---|---|---|
| E1 | Steam 非 Steam 快捷方式 | ✅ | 本地列表名可自行改；不涉及 AppID/Steamworks |
| E2 | 中国大陆发行资质 | ❓ | 若在大陆公开运营/分发，需确认版号/软著/出版要求；Steam 全球版另论 |
| E3 | 隐私政策 | ✅ | 当前无账号、无遥测、无联网收集；若以后加统计/联网需补 |
| E4 | 年龄/内容分级 | ⚠️ | 麻将含赌博意象但无真钱；商店页需按平台填写 |
| E5 | 音乐用于预告/商店页 | ⚠️ | CC BY 可用于宣传，但需署名；单独确认 |

## F. 处理顺序

1. 补齐传递依赖声明（A2）+ 完整 MIT 版权行（A9）
2. 素材来源审计（B2/B3/C6）
3. ML 权重来源（当前清单未列，见下）
4. 发布产物复核：测试依赖、哈希、更新器（A8/D3/D6）
5. 平台/地区资质确认（E2/E4/E5）

## G. 待补：ML 权重

| 项 | 状态 | 说明 |
|---|---|---|
| `ml/policy_weights.json` / `policy_weights_v2.json` | ✅ | 本项目 `ml/train_policy*.py` 生成，权重自有 |
| Tenhou MJAI Riichi Training Dataset | ✅ | CC BY 4.0；随权重分发需署名（已加入许可页） |
| MCR Final-2026 Engine Correctness Test Set | ⚠️ | competition-game-logs 自定义许可；研究/工程可用，商用范围需确认；商业发行前建议确认或改用纯自走数据重训 |
