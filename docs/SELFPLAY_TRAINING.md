# 自博弈策略训练

最后更新：2026-11-14。

## 目标

用项目自己的引擎自走产生样本，训练弃牌策略；不再依赖 MCR / Tenhou 数据集。
七对按正常和牌型处理，奖励按实际结算点数。

## 数据

```powershell
dotnet "...\Tiisu.DevTools.dll" --export-selfplay dist/selfplay.jsonl `
  --games 500 --start-seed 1 --mode 4 --jobs 8 --sample-every 10 --explore 0.1
```

每条样本：

- `Features`：`SelfPlayFeatures` v4，339 维（见下）
- `Action`：弃牌牌种 0..33
- `Player/Round/Game/Step`
- `ShantenBefore/After`
- `FinalScore/InitialScore/FinalRank/Lead`
- `Melds/CallWins/DealsIn/Misfires`

## 特征（339 维）

| 区间 | 内容 |
|---|---|
| 0..33 | 自家手牌计数 |
| 34..169 | 四家牌河计数（/4） |
| 170..305 | 四家副露牌计数（/4） |
| 306..309 | 四家副露组数（/4） |
| 310..313 | 四家点数（/10000） |
| 314..317 | 四家点差（相对平均，/10000） |
| 318 | 本场（/8） |
| 319..322 | 场风 one-hot |
| 323..326 | 座次 one-hot |
| 327..338 | 牌型结构：顺子/刻子/对子、对对倾向、清混一色、五门齐、三色同顺、三色同刻、役牌对子 |

不包含牌山；不包含海底/抢杠/胡绝张等偶发役。

## 奖励

`ml/train_policy_selfplay.py` 的 `RewardConfig`：

- 特征与奖励都做归一化：特征 z-score（均值/方差随权重保存），奖励 `tanh(raw/20)` 压到 [-1,1]
- 一位大幅领先时，正收益按领先幅度衰减
- 吃碰和牌扣分；向听变差、连续多巡不降向听扣分
- 错和、放铳额外扣分
- 四位劣势时弃和/不冒进给极小正分

## 探索

- `--explore 0.1`：自走时按概率换一张合法弃牌，生成带探索的样本。
- 训练默认 `--label-smoothing 0.02`，避免模型只记住自走函数而不是探索规则边界。

## 训练

```powershell
python ml/train_policy_selfplay.py `
  --data dist/selfplay.jsonl `
  --out ml/policy_weights_v4.json `
  --epochs 8 --hidden 128
```

输出 JSON 与 C# `NeuralPolicyAi` 对齐：`features=339` 时走 v4 特征；权重放到
`src/Tiisu.App/Assets/ml/policy_weights_v4.json` 即优先加载，缺失时回退 v2。

## 当前状态

- 数据导出、训练脚本、运行时 v4 特征支持已通。
- 还没训练出可发布的 v4 权重；当前包仍用 v2。
- Steam 前要么用 v4 替换 v2，要么继续确认 v2 训练数据许可。
