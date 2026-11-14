# 自己対戦による方策学習

最終更新：2026-11-14。

## 目的

自前エンジンの自己対戦でサンプルを作り、打牌方策を学習する。MCR/Tenhou データセットには依存しない。七対は通常の和了形として扱い、報酬は実際の精算点に従う。

## データ

```powershell
dotnet "...\Tiisu.DevTools.dll" --export-selfplay dist/selfplay.jsonl `
  --games 500 --start-seed 1 --mode 4 --jobs 8 --sample-every 10 --explore 0.1
```

各サンプル：

- `Features`：`SelfPlayFeatures` v4、339次元
- `Action`：打牌牌種 0..33
- `Player/Round/Game/Step`
- `ShantenBefore/After`
- `FinalScore/InitialScore/FinalRank/Lead`
- `Melds/CallWins/DealsIn/Misfires`

## 特徴量（339次元）

| 範囲 | 内容 |
|---|---|
| 0..33 | 自家手牌カウント |
| 34..169 | 四家の河カウント（/4） |
| 170..305 | 四家の副露牌カウント（/4） |
| 306..309 | 四家の副露数（/4） |
| 310..313 | 四家の点数（/10000） |
| 314..317 | 四家の点差（平均比、/10000） |
| 318 | 本場（/8） |
| 319..322 | 場風 one-hot |
| 323..326 | 座席風 one-hot |
| 327..338 | 手牌構造：順子／刻子／対子、対々傾向、清混一色、五門斉、三色同順、三色同刻、役牌対子 |

牌山は含まない。海底／搶槓／和絶張などの偶発役も含まない。

## 報酬

`ml/train_policy_selfplay.py` の `RewardConfig`：

- 特徴量と報酬を正規化：特徴量 z-score（平均／分散を重みと保存）、報酬 `tanh(raw/20)` で [-1,1]
- 一位で大きくリード時は正報酬をリード量で減衰
- 鳴き和了は減点。向聴悪化、向聴が改善しない巡が続くと減点
- 誤宣言・放銃は追加減点
- 四位劣勢時の降り／無理攻め回避に微小な正報酬

## 探索

- `--explore 0.1`：自己対戦時に確率で合法打牌へ差し替え、探索サンプルを生成。
- 学習は既定 `--label-smoothing 0.02`。自己対戦関数だけを暗記しないようにする。

## 学習

```powershell
python ml/train_policy_selfplay.py `
  --data dist/selfplay.jsonl `
  --out ml/policy_weights_v4.json `
  --epochs 8 --hidden 128
```

出力 JSON は C# `NeuralPolicyAi` と対応：`features=339` で v4 特徴量を使う。重みを
`src/Tiisu.App/Assets/ml/policy_weights_v4.json` に置くと優先読み込み、無ければ v2 にフォールバック。

## 現在の状態

- データ出力、学習スクリプト、実行時 v4 特徴量対応は完了。
- 100,000局自己対戦 / 2,365,513サンプルで v4 を学習、val_top1 0.947。
- 旧 v2 重みを置換。パッケージには `policy_weights_v4.json` のみを同梱し、MCR/Tenhou データは使わない。
