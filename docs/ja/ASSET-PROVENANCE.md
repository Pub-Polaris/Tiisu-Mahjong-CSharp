# 素材の出典

「このファイルはどこから来たか」に答えるための記録。最終更新：2026-11-14。

| ファイル | 出典 | ライセンス / 処理 | 状態 |
|---|---|---|---|
| `Assets/tiles.png` | [lietxia/mahjong_graphic](https://github.com/lietxia/mahjong_graphic)、コミット `3e27580`（2023-05-14） | 独自の自由ライセンス：商用利用・改変・再配布可、著作表示不要。本作で 9×4 アトラスに再配置 | 記録済み |
| `Tiisu.Mahjong.ico` | 本作で作成/生成 | MIT（プロジェクト所有） | ユーザー確認済み |
| `Assets/ui/ui-atlas.png` / `.json` | `Tiisu.DevTools --export-ui-atlas` | プロジェクト所有 | 生成器で再現可能 |
| `Assets/sounds/*.wav` | `tools/Tiisu.AudioGen` で合成 | プロジェクト所有。`ASSET-HASHES.md` でバイト単位に再現可能 | OK |
| `Assets/bgm/lobby.wav` | Parisian · Kevin MacLeod | CC BY 4.0。音量調整のみ。メニューとライセンス画面に表示 | OK |
| `Assets/fonts/TiisuRoundedSC-subset.ttf` | WenYuan Rounded v1.110 のサブセット | SIL OFL 1.1。予約名を避けて改名。ライセンス同梱 | OK |
| `Assets/layout/menu.json` | プロジェクト所有の設定 | MIT | OK |
| `Assets/licenses/*` | 公式ライセンス文 / フォントパッケージ / 本作の説明 | 各原文 | OK |
| `ml/policy_weights_v4.json` | 本作の自己対戦（100,000局）+ 探索。`ml/train_policy_selfplay.py` で学習 | プロジェクト所有。外部学習データなし | OK |

## 補足

- 牌画像は上流の自由ライセンスに従う。上流に著作表示義務はないが、本作は「ライセンス情報」に出典と全文を残す。
- アイコンは本作で作成。差し替える場合は出典を再記録すること。
- 音声ハッシュの確認方法は `ASSET-HASHES.md`。
