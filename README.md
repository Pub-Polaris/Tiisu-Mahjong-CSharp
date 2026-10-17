# Tiisu-Mahjong-CSharp
七子麻将的 C# 实现（原 Web 版：`Pub-Polaris/Tiisu-Mahjong-main`）。

> 本目录放 **C# 实现**；原 HTML/CSS/JS 版在 `..\Web\`，作为**行为基准（oracle）**，不改动。
> 外部知识库在 `..\Web\docs\knowledge\`（随 Web 仓库版本化）。

## 结构

```
NET\
  global.json          # 固定 SDK 10.0.402（避免误用 11 RC）
  Tiisu.slnx           # 新解决方案格式（.NET 10 SDK 生成）
  src\Tiisu.Core\      # 纯逻辑核心（无 HTTP / DOM / 时间 / 随机）
  tests\Tiisu.Core.Tests\  # xUnit
  tools\Tiisu.Tools\   # golden 生成 / 对比 / fuzz 控制台
```

## 常用命令

```powershell
cd E:\工程文件\TiisuMahjong\NET
dotnet build                 # 构建
dotnet test                  # 跑测试
dotnet run --project tools/Tiisu.Tools
```

## 约束

- **不修改 `..\Web\` 下的产品代码**（它是基准）。
- Core 内禁止出现 HTTP、文件、DOM、时间、随机数（用接口注入）。
- 目标框架 `net10.0`；SDK 由 `global.json` 固定在 10.0.402。
- 迁移策略：先用现有 JS 生成 **golden corpus**，C# 逐字段对比，**0 差异**才继续。

## 当前进度

| 里程碑 | 状态 |
|---|---|
| **M1 判役核心**（tiles/notation/handAnalyzer/tenpai/winchecker） | ✅ **完成**：10115 条 golden 逐字段 **0 差异** |
| M2 engine（状态机 + 持久化接口） | 未开始 |
| M3 API（ASP.NET Core Minimal API） | 未开始 |

### M1 产物

| 文件 | 内容 |
|---|---|
| `src/Tiisu.Core/Tile.cs` | `Suit` / `Tile`（`readonly record struct`）与解析 |
| `src/Tiisu.Core/Group.cs` | 面子与雀头 |
| `src/Tiisu.Core/HandKind.cs` | 和牌形状枚举 |
| `src/Tiisu.Core/YakuData.cs` | 读 `ike.json`（含 fu/kaishoku/optional/menzen/doubleYakuman/selfDrawDouble/notIf/tokurei/fixedPoints…） |
| `src/Tiisu.Core/WinChecker.cs` | 判定流程：国士旁路 → 七对 → 标准形 → 高役优先权链 → 普通役 → 门槛/奖赏/点数 |
| `src/Tiisu.Core/WinChecker.Split.cs` | 拆牌：单解 + 全部解（高目优先） |
| `src/Tiisu.Core/WinChecker.Conditions.cs` | 61 个判据 + 9 个流程例外 |
| `src/Tiisu.Core/WinResult.cs` | 结果（success/reason/totalFu/activeYaku/specialPoints/sevenHonors） |
| `tests/Tiisu.Core.Tests/GoldenTests.cs` | 数据驱动：读两份 fixture、逐字段比对、聚合差异 |

### 跑验收

```powershell
cd E:\工程文件\TiisuMahjong\NET
dotnet test Tiisu.slnx
# 期望：语料总数 10115，差异 0
```

### 注意：`ike.json` 是副本

`src/Tiisu.Core/Data/ike.json` 是从 `..\Web\ike.json` **复制**来的（2026-10-18 哈希一致）。
Web 侧一改役表就必须同步：

```powershell
Copy-Item ..\Web\ike.json src\Tiisu.Core\Data\ike.json -Force
```

### 语料重生成（Web 判役变动后）

```powershell
cd ..\Web
python scripts\gen_golden.py                     # 断言语料（115 条）
python scripts\gen_golden_random.py -n 10000     # 随机语料（种子 20261018）
# 产物写到 ..\NET\tests\fixtures\，然后在本仓提交
```

> 每条语料带 `maxSplit` 字段（缺省 true）：验证台的「高目优先」用例会临时关掉开关做对照，必须如实记录。

## M1 移植踩坑（C# 复刻 JS 时实际踩到）

| 坑 | 说明 |
|---|---|
| 字牌 `num` 是字符串 | JS 里 `tile.num` 对字牌是 `'1'..'7'`，C# 若统一转 int 会让数牌比较误吃字牌。`two_color_ko` / `three_color_ko` / `one_color_triple` / `three_kingdoms` / `north_south_self` / `_tokureiBonus` 都要显式排除字牌 |
| 合成字牌 `z9` | 验证台用 `ytBuild('z',99)` 造过 `z9`，语料里真的存在 → `Tile` 的字牌范围放宽到 1..9、计数槽 36 格，不要断言「字牌只有 7 种」 |
| `isChidaichi` 的 `extra` | 是「**非万子的张数**」（含字牌），不是「非万子数牌张数」 |
| 高目枚举顺序 | `splitStandardAll` 的产出顺序必须与 JS 一致，否则「并列取先出现」会漂 |
| 门槛与奖赏 | 先判 `totalFu < 7` 返回 `no_yaku`，再叠加嵌张/边张/单钓/双碰/筋和 |

以上已由 10115 条语料验证；改判役后按上文重生成即可回归。

