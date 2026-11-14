# Self-play Policy Training

Last updated: 2026-11-14.

## Goal

Generate samples with the project's own engine and train a discard policy; no MCR/Tenhou datasets. Seven pairs are treated as a normal winning shape; rewards follow actual settlement points.

## Data

```powershell
dotnet "...\Tiisu.DevTools.dll" --export-selfplay dist/selfplay.jsonl `
  --games 500 --start-seed 1 --mode 4 --jobs 8 --sample-every 10 --explore 0.1
```

Each sample:

- `Features`: `SelfPlayFeatures` v4, 339 dims (below)
- `Action`: discard tile kind 0..33
- `Player/Round/Game/Step`
- `ShantenBefore/After`
- `FinalScore/InitialScore/FinalRank/Lead`
- `Melds/CallWins/DealsIn/Misfires`

## Features (339 dims)

| Range | Content |
|---|---|
| 0..33 | Own hand counts |
| 34..169 | Four players' river counts (/4) |
| 170..305 | Four players' meld-tile counts (/4) |
| 306..309 | Four players' meld counts (/4) |
| 310..313 | Four players' scores (/10000) |
| 314..317 | Score differences (relative to average, /10000) |
| 318 | Honba (/8) |
| 319..322 | Round-wind one-hot |
| 323..326 | Seat-wind one-hot |
| 327..338 | Hand patterns: sequences/triplets/pairs, triplets tendency, full/half flush, five gates, three-colour sequences, three-colour triplets, value-pair |

Excludes the wall and rare events such as last-tile/robbing-kan wins.

## Reward

`RewardConfig` in `ml/train_policy_selfplay.py`:

- Normalize both features and reward: feature z-score (mean/std saved with weights), reward `tanh(raw/20)` into [-1,1]
- When far ahead in first, decay positive gains by lead
- Penalize calls, worse shanten, and many turns without shanten improvement
- Extra penalty for misfire and deal-in
- Tiny positive reward for folding when far behind in fourth

## Exploration

- `--explore 0.1`: with this probability replace a discard with a random legal tile, producing exploration samples.
- Training defaults to `--label-smoothing 0.02` so the model does not only mimic the self-play function.

## Training

```powershell
python ml/train_policy_selfplay.py `
  --data dist/selfplay.jsonl `
  --out ml/policy_weights_v4.json `
  --epochs 8 --hidden 128
```

Output JSON aligns with C# `NeuralPolicyAi`: `features=339` selects v4 features; weights at
`src/Tiisu.App/Assets/ml/policy_weights_v4.json` are preferred, with v2 fallback when missing.

## Current status

- Data export, training script, and runtime v4 feature support are in place.
- v4 trained on 100,000 self-play games / 2,365,513 samples; val_top1 0.947.
- Replaced old v2 weights; the package ships only `policy_weights_v4.json`, no MCR/Tenhou data.
