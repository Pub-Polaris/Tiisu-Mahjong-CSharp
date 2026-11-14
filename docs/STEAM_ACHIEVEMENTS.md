# Steam 成就映射（草案）

本地成就不单独持久化，由 `StatsStore` 现算；Steam 成就 ID 建议与本地 ID 一一对应，接入时只做 `Unlock` 上报。

| Steam ID | 本地 ID | 名称 | 条件 |
|---|---|---|---|
| `ACH_FIRST_ROUND` | `first-round` | 初战 | 完成 1 个小局 |
| `ACH_TEN_ROUNDS` | `ten-rounds` | 十局 | 完成 10 个小局 |
| `ACH_HUNDRED_ROUNDS` | `hundred-rounds` | 百局 | 完成 100 个小局 |
| `ACH_FIRST_WIN` | `first-win` | 首胜 | 和牌 1 次 |
| `ACH_TEN_WINS` | `ten-wins` | 十胜 | 和牌 10 次 |
| `ACH_HUNDRED_WINS` | `hundred-wins` | 百胜 | 和牌 100 次 |
| `ACH_FIRST_TSUMO` | `first-tsumo` | 自摸 | 自摸 1 次 |
| `ACH_TEN_TSUMO` | `ten-tsumo` | 自摸十次 | 自摸 10 次 |
| `ACH_TIISUIN` | `tiisuin` | 大七星 | 打出一次大七星 |
| `ACH_WITNESS` | `witness` | 见证 | 见证一次大七星 |

## 实现约定

- 本地 ID 是稳定契约；`AchievementsTests` 会锁住顺序与 ID
- Steam ID 可在 Steamworks 后台改名，但本地 ID 不改
- 成就解锁是**幂等**的：启动/局末扫描一次，已解锁的直接跳过
- 离线时只写本地；下次启动接 Steam 时补报
- 不把成就写进 `stats.json`，避免两套真相

## 待确认

- 是否需要隐藏成就（大七星/见证）
- 是否需要 Steam Stats（累计局数/和牌数）
- 是否启用 Steam Cloud 同步 `settings.json`
