# 7Neuu Policy Net Provenance

Last updated: 2026-11-14.

## Current artifact

| File | Generation | Status |
|---|---|---|
| `ml/policy_weights_v4.json` | `--export-selfplay` 100,000 games → `ml/train_policy_selfplay.py` | Current shipped weights |

## Training data

- Engine self-play plus 10% random legal-discard exploration only.
- No MCR / Tenhou or other external datasets; the old v2 weights are removed.
- 339 features: all table state except the wall, plus hand-pattern structure (three-colour sequences/triplets, five gates, triplets, etc.).
- Reward: final point difference + calls/shanten/misfire/deal-in/fold/lead decay, normalized with `tanh(raw/20)`.
- Feature z-score normalization is saved with the weights.

## Old v2 (removed)

Old `policy_weights*.json` files were once trained on MCR/Tenhou data and have been deleted from the repository; see Git history for provenance.
