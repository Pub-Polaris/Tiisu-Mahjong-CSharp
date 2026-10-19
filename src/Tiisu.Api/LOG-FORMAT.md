# 牌型日志格式（JSONL v1）

存储层只写 **ASCII**。汉字只允许出现在显示层（`Web/src/logRender.js`、`Web/logview.html`）。
`Web/src/engine.js` 的 `recordTiles` 开启时，每局每玩家产出一个事件数组，
`POST /api/roundlog` 落盘为 `events.jsonl`（一行一个 JSON 对象，UTF-8 无 BOM，`\n` 换行）。

## 落盘路径

```
Web/logs/<session>/round_<NNN>_p<0-3>_<playerUUID>/events.jsonl
```

- `session` 由 `js/game.js` 生成，形如 `20261020_011416`。
- `round` 是 `engine.handNumber`，文件夹中补零到 3 位。
- `player` 是座次索引 `0..3`（0=你，1=AI 南，2=AI 西，3=AI 北）。
- 旧版 `局NNN_玩家名_UUID/log.txt` 不再写入；Python 端与 C# 端读取逻辑都只认新路径。

## 事件

### round（每个文件第一行，一名玩家一局一条）

```json
{"type":"round","round":3,"honba":0,"player":1,"name":"ai_south","wind":"south","dealer":false,"riichi":false,"uuid":"...","session":"20261020_011416","roundWindIdx":0,"dealerCount":0,"extension":false}
```

| 字段 | 含义 |
|---|---|
| `round` | `handNumber`（显示层不用它算局名） |
| `honba` | 本场数 |
| `player` | 座次索引 0-3 |
| `name` | `human` / `ai_south` / `ai_west` / `ai_north`（显示名由 logRender 映射） |
| `wind` | 门风 `east/south/west/north` |
| `dealer` / `riichi` | 是否庄家 / 立直 |
| `uuid` | 玩家 UUID |
| `session` | 会话 id |
| `roundWindIdx` | 场风索引 0=东 1=南 2=西 3=北 |
| `dealerCount` | 庄家更替次数，`(dealerCount % 4) + 1` = 「东N局」的 N |
| `extension` | 是否延长局（南入/西入/北增） |

### step（每次摸打/副露/杠/和牌/流局一条）

```json
{"type":"step","a":"draw","p":1,"t":"1m","hand":"123m456p789s1122z","melds":[],"discards":["z2"],"wall":79,"rinshan":4,"h":0,"rw":0,"dc":0,"x":0,"w":"south","d":false,"r":false}
```

| 字段 | 含义 |
|---|---|
| `a` | 动作码，见下表 |
| `p` | 玩家索引 0-3 |
| `t` | 本动作关联的牌（`1m` / `z1` 这种「数字在前」记号），可省略 |
| `hand` | 动作完成后的暗手，`handToStr` 格式；空手为 `""` |
| `melds` | 副露数组 `[{"k":"chi","t":"234m"}]`，`k` 见动作码同名列 |
| `discards` | 弃牌河数组 `["1m","z2"]` |
| `wall` | 动作后活牌山张数 |
| `rinshan` | 动作后岭上张数 |
| `h` / `rw` / `dc` / `x` | 该条事件发生时的本场数 / 场风索引 / 庄家更替次数 / 延长局（0/1） |
| `w` / `d` / `r` | 该条事件发生时的门风 / 是否庄家（0/1）/ 是否立直（0/1） |
| `reason` | 仅 `special_flow` / `flow`：流局原因码 |
| `via` | 仅 `rinshan_draw` / `kan_*`：`rinshan` 或 `wall` |

> `h/rw/dc/x/w/d/r` 是显示层需要的行首上下文快照。旧 `log.txt` 每行都用「当时」的
> 局名/座次拼前缀，和牌后 `honba`、庄家、门风会变化；不存这些字段就无法逐行还原旧格式。

## 动作码

| 码 | 原中文动作 |
|---|---|
| `draw` | 摸 |
| `discard` | 出 |
| `pon` | 碰 |
| `chi` | 吃 |
| `kan_minkan` | 明杠（`t` 是被杠的牌） |
| `kan_ankan` | 暗杠·岭上摸（`t` 是补摸的牌） |
| `kan_kakan` | 加杠·岭上摸（`t` 是补摸的牌） |
| `rinshan_draw` | 明杠补摸（`via=wall` 时为牌山代摸） |
| `win_tsumo` | 自摸和牌 |
| `win_ron` | 荣和和牌 |
| `observe` | 旁观 |
| `special_flow` | 特殊流局 + `reason` |
| `flow` | 流局 + `reason` |
| `tiisuin_win` | 大七星直接获胜 |
| `witness` | 见证（大七星） |

## 流局原因码（`reason`）

| 码 | 显示 |
|---|---|
| `four_wind_repeat` | 四风连打（`t` 为风牌） |
| `triple_ron` | 三家和牌 → 自动流局 |
| `triple_ron_chankan` | 三家和牌（抢杠）→ 自动流局 |
| `nine_orphans` | 九种九牌流局 |
| `thirteen_unrelated` | 十三不靠流局 |
| `exhaustive` | 牌山已空流局 |
| `special` | 其他特殊流局 |

## HTTP

### POST `/api/roundlog`

```json
{
  "session": "20261020_011416",
  "round": 3,
  "player": 1,
  "playerName": "ai_south",
  "playerUUID": "...",
  "events": [ {"type":"round","...":0}, {"type":"step","...":0} ]
}
```

响应 `ok`。服务端把 `events` 逐行追加到上面的路径。

### GET `/api/roundlog`

查询参数：`session`（可选）、`round`（可选，整数）、`player`（可选，`0-3` /
`human` / `ai_south` / `AI 南` / `你` 等）。返回 `application/x-ndjson; charset=utf-8`，
多个文件按 session 目录、`round_NNN_pN` 目录字典序拼接。

```text
GET /api/roundlog?session=20261020_011416&player=1&round=3
```

## 兼容性

- 旧 `entry`（整段中文文本）不再接收；M3 后 Python 实现只是过渡。
- `settings.json` / `state.json` / `stats.json` 仍是原来的磁盘 JSON，格式不变。
- 显示层：`TiisuLogRender.renderJsonl(text)` / `renderEvents(events)` 输出与旧
  `log.txt` 等价的 `[东1局 1本场] 南家(AI 南) 摸 1m | 手牌=... 副露=... 弃牌=... | 牌山79 岭上4`
  以及 `=== ... · UUID=... ===` 行。
