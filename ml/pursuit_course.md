# 7Neuu 自动追役：稀有课程数据与 Phase 2 训练口径（原型）

最后更新：2026-11-19（版本 0.0.7-pre，仅原型，不训练、不替换权重）。

## 本轮边界

- 只做运行时原型 + 五项风格权重 + 课程数据导出接口/文档。
- **不重训、不替换 `ml/policy_weights_v4.json`**；本轮没有任何新权重文件。
- 若 Phase 2 需要新权重，另起实验文件（例如 `ml/policy_weights_v5_pursuit.json`），
  确认通过 A/B 门槛前**不启用**，也不改 C# 的加载顺序。

## 21k 稀有课程

来源：`dotnet Tiisu.DevTools.dll --export-selfplay dist/selfplay.jsonl --games ... --mode 4`
（特征布局见 `docs/SELFPLAY_TRAINING.md`，339 维）。

导出：

```powershell
python ml/export_pursuit_course.py --data dist/selfplay.jsonl --out dist/pursuit_course.jsonl --max 21000
```

筛选口径（与 C# `AiPursuit.AssessTarget` 近似，均为「结构形态」）：

| 目标 | 口径 |
|---|---|
| 七对子 `SevenPairs` | pairUnits = Σ(count÷2) ≥ 4，门清 |
| 混/清一色 `Flush` | 主花色（手牌+副露）占比 ≥ 0.55 |
| 字一色 `AllHonors` | 字牌 ≥ 8 张且占比 ≥ 0.55 |
| 大七星 `BigSevenStars` | 七种字牌里 count==2 的 ≥ 3，门清 |
| 九莲宝灯 `NineGates` | 门清、主花色 ≥ 9 张、占比 ≥ 0.65、有 1 或 9 |
| 四暗杠 `FourAnkan` | 门清、刻子 ≥ 2 组（近似；特征里没有暗杠数） |

每条输出样本追加两个字段：

- `PursuitTarget`：目标名（上表枚举）
- `SampleWeight`：**1.1**

实现：单遍扫描 + 每目标蓄水池采样（`--seed` 决定结果），内存有界；总量受 `--max` 限制。
特征里**没有牌山剩余**，所以「早期/中期、非关键局」不在这里筛，仍由运行时
`AiPursuit.Observe` 判断（牌山 ≥ 35、非终盘、非大比分关键局）。

## 1.1x 权重说明

- 稀有目标在自博弈数据里占比极低（七对以外接近 0），直接训练仍是 0 样本。
- 1.1x 是**温和上采样**：不改变整体样本分布的形状，只让稀有目标在 loss 里略微放大；
  相对地，5x/10x 过采样会让和牌率与平均顺位明显变差，超出本轮硬预算。
- 与现有奖励权重的关系：`train_policy_selfplay.py` 先用奖励算 `weights` 再
  `weights /= weights.mean()`；Phase 2 的改动点是**归一化之后**再乘 `SampleWeight`
  并重新归一化（约 3 行），不碰奖励函数。
- 课程样本只做 Phase 2 的 BC 微调输入，不改变运行时行为；运行时是否追役由
  `AiStyle.Pursuit`（设置页滑条）+ 手牌结构决定。

## Phase 2 训练安全清单（必须全部满足才允许产出权重）

1. **梯度裁剪**：全局 `clip_grad_norm_(params, 1.0)`。
2. **finite 检查**：每步 loss / 梯度 / 参数出现 NaN 或 ∞ 立即中止，不写权重文件。
3. **权重校验**：导出前检查层维度（339→128→128→34）、`feature_mean/std` 长度、
   无 NaN/∞、L2 范数量级与 v4 同阶（异常放大视为训练发散）。
4. **早停**：按验证集 `val_top1`（同现有脚本）patience=3；连续 3 轮不升就停。
5. **A/B 硬预算**（100k 局，7Neuu 追役开/关对比）：
   - 和牌率最多 **-1 个百分点**；
   - 平均顺位最多 **+0.02**；
   - 稀有役次数**必须上升**；
   - 同时记录：副露率、策略熵（温度采样下的动作分布熵）、役种覆盖。
6. **不改默认档位**：新权重只接 7Neuu；四档 `AiWeights` 语义不变。

## 运行时指标（C# 侧）

`MatchRuntime` 暴露（供 A/B / 自走统计）：

- `AiPursuitEntries` / `AiPursuitExits`：本场进入 / 退出追役次数（四家合计）
- `AiPursuitDecisions`：追役状态下的弃牌决策数
- `AiPursuitStates`：四家当前 `PursuitState` 快照
- 原有 `NeuralDecisions` / `NeuralDifferent`（策略网 vs TieredAi 决策差异）
