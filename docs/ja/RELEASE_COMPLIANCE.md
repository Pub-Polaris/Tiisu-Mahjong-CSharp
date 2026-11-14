# リリース確認リスト

基準：`261114-7cd486c` 以降。  
注意：本リストはエンジニア／コンプライアンスの自己確認であり、法的助言ではない。地域要件は専門家に確認すること。

## A. ライセンスと表示

| # | 項目 | 状態 | 根拠 / 次 |
|---|---|---|---|
| A1 | プロジェクト自身の MIT ライセンス | OK | `LICENSE` |
| A2 | 第三者コンポーネント一覧 | OK | `THIRD-PARTY-NOTICES.md` とゲーム内一覧に HarfBuzzSharp、OpenTK/GLFW、NAudio.*、YogaSharp.Core などを追加済み |
| A3 | ライセンス全文を同梱 | OK | `Assets/licenses/MIT.txt`、`Apache-2.0.txt`、`OFL-1.1.txt`、`CC-BY-4.0.txt`、`REFERENCES.txt` |
| A4 | ゲーム内で閲覧可能 | OK | メニュー「ライセンス」→ 各ページ「全文を見る」 |
| A5 | フォントの OFL 予約名 | OK | サブセットを `Tiisu Rounded SC` に改名。著作権・OFL 表示を保持 |
| A6 | BGM の CC BY 4.0 表示 | OK | メニュー下部の著作表示 + ライセンス画面の Parisian 項目 |
| A7 | 読み上げ音声を同梱しない | OK | `Assets/voices` を削除。ローカル SAPI で `%LOCALAPPDATA%\TiisuMahjong\voices` に生成 |
| A8 | 開発/テスト依存を同梱しない | OK | publish 確認済み：xunit/testhost/coverlet/TestPlatform なし |
| A9 | MIT 著作権行の完全性 | OK | `MIT.txt` に NAudio/OpenTK/HarfBuzzSharp/YogaSharp 等を追加。GLFW は別途 Zlib 全文 |

## B. 素材と出典

| # | 項目 | 状態 | 根拠 / 次 |
|---|---|---|---|
| B1 | 効果音の再現性 | OK | `ASSET-HASHES.md`。9個の効果音を `Tiisu.AudioGen` でバイト単位に再現 |
| B2 | 牌画像 `tiles.png` | OK | `lietxia/mahjong_graphic`（自由ライセンス、商用/改変/再配布可）。ライセンス画面と `TILES-LICENSE.txt` を参照 |
| B3 | アイコン `Tiisu.Mahjong.ico` | OK | 自作／生成（ユーザー確認済み） |
| B4 | UI アトラス `ui-atlas.png` | OK | `--export-ui-atlas` で現行 UI 描画コードから生成 |
| B5 | レイアウト `layout/menu.json` | OK | プロジェクト所有の設定 |
| B6 | BGM `lobby.wav` | OK | Kevin MacLeod · CC BY 4.0、表示済み。変更は音量のみ |
| B7 | フォントサブセット | OK | WenYuan Rounded v1.110、OFL、改名・ライセンス同梱済み |

## C. リバースエンジニアリング / EULA / 商標

| # | 項目 | 状態 | 根拠 / 次 |
|---|---|---|---|
| C1 | Alice in Cradle | OK | 空間オーディオ構成の調査のみ。コード/ミドルウェア/素材は未使用。`docs/REFERENCES.md` とライセンス画面を参照 |
| C2 | 明星三缺一 2002 | OK | 包絡・長さの分析のみ。効果音は独自合成。原音声/コードは未使用。`ASSET-HASHES.md` |
| C3 | 天鳳 Tenhou | OK | 聴牌形の参考のみ。コード/データは未使用 |
| C4 | 旧コミット内の固有名詞コメント | 注意 | 履歴として保持。`REFERENCES.md` に現状を記載。問い合わせ時は同ファイルを提示 |
| C5 | 商標・宣伝 | 注意 | タイトル/ロゴ/ストアページに上記作品名を使用しない。ルール互換の説明のみ |
| C6 | 視覚的実質類似 | 注意 | 牌卓/UI が参考作品の外観を再現していないか確認する |

## D. 配布とビルド

| # | 項目 | 状態 | 根拠 / 次 |
|---|---|---|---|
| D1 | リリースに音声を含めない | OK | publish に `Assets/voices` がないことを確認 |
| D2 | ライセンス全文を同梱 | OK | publish に `Assets/licenses/*` を確認 |
| D3 | インストーラー/ルールのチェックサム | 注意 | Release に SHA256 を記載済み。毎回更新する |
| D4 | 音声ハッシュ一覧 | OK | `ASSET-HASHES.md` |
| D5 | 未署名インストーラー / SmartScreen | 注意 | コンプライアンス上の致命傷ではないが信頼に影響。コード署名を検討 |
| D6 | 自動更新の完全性 | OK | `Tiisu.Updater --sha256 <hex>` が適用前に zip ハッシュを検証。署名は未対応 |
| D7 | リリース自己確認 | OK | `Tiisu.DevTools --check-publish dist/publish`：ライセンス/フォント/ハッシュ/除外項目、現在 OK |

## E. 配布戦略（2026-11-14 確認）

| 項目 | 決定 |
|---|---|
| 現在 | GitHub 公開リポジトリ、自由にビルド/実行 |
| 将来 | Steam 全世界配信（B） |
| 中国本土での公開配布/運営 | 予定なし |

## E. プラットフォームと地域

| # | 項目 | 状態 | 根拠 / 次 |
|---|---|---|---|
| E1 | 現在の GitHub 公開リポジトリ | OK | MIT + 第三者表示 + ライセンス全文 |
| E2 | 中国本土の配信資格 | N/A | 本土での公開配布/運営を行わない方針 |
| E3 | プライバシーポリシー | OK | アカウント・テレメトリ・オンライン収集なし。Steam 統計/通信を追加する場合は再検討 |
| E4 | 年齢/コンテンツ区分 | 将来 | Steam のコンテンツ調査を記入。麻雀/賭博的表現はあるが実際のお金は扱わない |
| E5 | 音楽のトレーラー/ストア利用 | 将来 | CC BY は表示付きで宣伝利用可。Steam ページ/トレーラーは個別確認 |

## F. 作業順

1. 推移的依存の表示（A2）と MIT 著作権行（A9）
2. 素材出典監査（B2/B3/C6）
3. ML 重みの出典（旧リスト外、下記）
4. リリース成果物の再確認：テスト依存、ハッシュ、アップデータ（A8/D3/D6）
5. プラットフォーム/地域の確認（E2/E4/E5）

## G. 補足：ML 重み

| 項目 | 状態 | 説明 |
|---|---|---|
| `ml/policy_weights_v4.json` | OK | 本作の自己対戦 100,000局で学習。旧 v2 を削除。MCR/Tenhou データなし |
| 自己対戦 v4 パイプライン | OK | 100,000局、val_top1 0.947。v2 を置換済み |
