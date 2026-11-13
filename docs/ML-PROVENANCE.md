# 7Neuu 小策略网来源记录

最后更新：2026-11-14。

## 产物

| 文件 | 生成脚本 | 说明 |
|---|---|---|
| `ml/policy_weights.json` | `ml/train_policy.py` | 34+8 维特征，34 类弃牌 softmax |
| `ml/policy_weights_v2.json` | `ml/train_policy_v2.py` | 181 维场面特征版本 |

权重由本项目训练流程生成，未直接复制第三方模型权重。

## 训练数据

| 数据 | 来源 | 许可 | 用途 |
|---|---|---|---|
| 自走/人类日志 | 本项目 `--export-dataset`、自走对局 | 项目自有 | 弃牌样本 |
| `riichi.jsonl` | [Tenhou MJAI Riichi Training](https://huggingface.co/datasets/1nfrared218/tenhou-mjai-riichi-training) | CC BY 4.0 | 人类听牌形/弃牌风格 |
| `guobiao*.jsonl` | [MCR Final-2026 Test Set](https://huggingface.co/datasets/Dannibal/mcr-final2026-testset)，提交 `6477e62` | competition-game-logs（自定义） | 国标规则/局面样本 |

转换脚本：`tools/riichi_npz_to_jsonl.py`、`tools/mcr_to_jsonl.py`。

## 许可结论

- Tenhou 数据：CC BY 4.0，允许商用与改编，**必须署名**；已在游戏内许可页/第三方声明列出。
- MCR 数据：自定义 `competition-game-logs`，明确允许研究/工程使用；**商用范围未明确**。
- 自走数据：项目自有。

## 风险与建议

- 如果发行是纯免费/研究性质：保留两份数据署名即可。
- 如果商业发行：先向 MCR 数据集作者确认商用许可，或用「纯自走 + CC BY 数据」重训一版权重，避免把 MCR 训练产物带进商业包。
- 数据文件本身不随安装包分发；只分发训练后的权重。
