# 素材来源记录

用于回答“这个文件哪来的”。最后更新：2026-11-14。

| 文件 | 来源 | 许可 / 处理 | 状态 |
|---|---|---|---|
| `Assets/tiles.png` | [lietxia/mahjong_graphic](https://github.com/lietxia/mahjong_graphic)，提交 `3e27580`（2023-05-14） | 自定义自由许可：可商用、修改、再分发，无署名要求；本项目重排为 9×4 图集 | ✅ 已记录 |
| `Tiisu.Mahjong.ico` | 本项目自制/生成 | MIT（项目自有） | ✅ 用户确认 |
| `Assets/ui/ui-atlas.png` / `.json` | `Tiisu.DevTools --export-ui-atlas` | 本项目自有 | ✅ 生成器可复现 |
| `Assets/sounds/*.wav` | `tools/Tiisu.AudioGen` 合成 | 本项目自有；`ASSET-HASHES.md` 可逐字节复现 | ✅ |
| `Assets/bgm/lobby.wav` | Parisian · Kevin MacLeod | CC BY 4.0；仅音量调整；菜单与许可页署名 | ✅ |
| `Assets/fonts/TiisuRoundedSC-subset.ttf` | 文渊圆体 v1.110 子集 | SIL OFL 1.1；重命名避开 OFL 保留字体名；许可证随包 | ✅ |
| `Assets/layout/menu.json` | 本项目自有配置 | MIT | ✅ |
| `Assets/licenses/*` | 官方许可文本 / 字体包 / 本项目说明 | 各自原文 | ✅ |
| `ml/policy_weights_v4.json` | 本项目自博弈（100,000 局）+ 探索；`ml/train_policy_selfplay.py` 训练 | 项目自有，无外部训练数据 | ✅ |

## 说明

- 牌面素材按上游自由许可使用；上游无强制署名要求，但本项目在「开源信息」中保留来源与许可全文。
- 图标为本项目自制；如后续替换，需重新记录。
- 音频哈希核对方法见 `ASSET-HASHES.md`。
