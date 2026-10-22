using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Tiisu.Core;
using Tiisu.Engine;
using CoreMeld = Tiisu.Core.Meld;
using EngineMeld = Tiisu.Engine.Meld;

if (args.Length > 0)
{
    switch (args[0])
    {
        case "--regen-yaku-fixtures":
            return RegenYakuFixtures.Run();
        case "--regen-ai-fixtures":
            return RegenAiFixtures.Run();
        case "--probe-last-tile":
            return ProbeLastTile.Run();
    }
}

Console.WriteLine("Tiisu.Tools");
Console.WriteLine("  --regen-yaku-fixtures  用当前 C# 实现重算 yaku golden 语料的期望值（就地重写）");
Console.WriteLine("  --probe-last-tile      校验和绝张的引擎可见张数口径（河里 + 副露，手牌不计）");
return 0;

/// <summary>
/// 用当前 C# 判役实现重算两份 yaku golden 语料的 result 字段（只改 result，输入原样保留）。
/// 输出与输入保持同样的排版：yaku-golden.json 缩进 1 空格，yaku-golden-random.json 紧凑单行。
/// </summary>
internal static class RegenYakuFixtures
{
    private static readonly string[] FixtureFiles =
    {
        "yaku-golden.json",
        "yaku-golden-random.json",
    };

    private static readonly JsonSerializerOptions CompactJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static int Run()
    {
        string fixturesDir = FindFixturesDir();
        var checker = new WinChecker(YakuData.LoadDefault());
        int grandTotal = 0;
        int grandChanged = 0;

        foreach (string fileName in FixtureFiles)
        {
            string path = Path.Combine(fixturesDir, fileName);
            string raw = File.ReadAllText(path, Encoding.UTF8);
            bool pretty = raw.Length > 1 && (raw[1] == '\n' || raw[1] == '\r');
            var records = JsonNode.Parse(raw)?.AsArray()
                ?? throw new InvalidOperationException($"语料解析失败: {path}");

            int changed = 0;
            for (int index = 0; index < records.Count; index++)
            {
                var record = records[index]?.AsObject()
                    ?? throw new InvalidOperationException($"{fileName}[{index}] 不是对象");
                var input = ReadInput(record);

                checker.MaxSplit = input.MaxSplit ?? true;
                WinResult actual = checker.CheckWin(
                    input.Hand,
                    input.SelfDraw,
                    input.HasCalled,
                    input.Ankan,
                    input.Minkan,
                    input.Melds,
                    input.FirstTurn,
                    input.WinTile,
                    input.Context);

                JsonNode newNode = JsonSerializer.SerializeToNode(BuildResult(actual), CompactJson)
                    ?? throw new InvalidOperationException("result 序列化为空");
                string oldText = record["result"]?.ToJsonString(CompactJson) ?? "null";
                string newText = newNode.ToJsonString(CompactJson);
                if (!string.Equals(oldText, newText, StringComparison.Ordinal))
                {
                    changed++;
                    if (changed <= 5)
                    {
                        Console.WriteLine($"  {fileName}[{index}] 变化:");
                        Console.WriteLine($"    - {oldText}");
                        Console.WriteLine($"    + {newText}");
                    }
                }

                record["result"] = newNode;
            }

            checker.MaxSplit = true;

            var writeOptions = new JsonSerializerOptions
            {
                WriteIndented = pretty,
                IndentSize = 1,
                NewLine = "\n",
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
            string output = records.ToJsonString(writeOptions) + "\n";
            File.WriteAllText(path, output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Console.WriteLine($"[{fileName}] 共 {records.Count} 条，期望值变化 {changed} 条");
            grandTotal += records.Count;
            grandChanged += changed;
        }

        Console.WriteLine($"合计: 共 {grandTotal} 条，期望值变化 {grandChanged} 条");
        return 0;
    }

    private static GoldenInput ReadInput(JsonObject record)
    {
        var hand = ReadStringList(record["hand"]).Select(Tile.Parse).ToList();

        var melds = new List<CoreMeld>();
        if (record["melds"] is JsonArray meldArray)
        {
            foreach (var node in meldArray)
            {
                var meld = node?.AsObject()
                    ?? throw new InvalidOperationException("melds 元素不是对象");
                string type = meld["type"]?.GetValue<string>()
                    ?? throw new InvalidOperationException("melds[].type 缺失");
                var tiles = ReadStringList(meld["tiles"]).Select(Tile.Parse).ToList();
                melds.Add(new CoreMeld(type, tiles));
            }
        }

        WinContext? context = null;
        if (record["ctx"] is JsonObject ctx)
        {
            context = new WinContext
            {
                SeaMoon = ReadBool(ctx, "seaMoon"),
                HaiDei = ReadBool(ctx, "haiDei"),
                RiverFish = ReadBool(ctx, "riverFish"),
                LastTileVisible = ReadInt(ctx, "lastTileVisible"),
                SeatWind = ReadString(ctx, "seatWind"),
                RoundWind = ReadString(ctx, "roundWind"),
                WaterDrop = ReadBool(ctx, "waterDrop"),
                RinshanGun = ReadBool(ctx, "rinshanGun"),
                RinshanFlower = ReadBool(ctx, "rinshanFlower"),
                KanRiichi = ReadBool(ctx, "kanRiichi"),
                Renhe = ReadBool(ctx, "renhe"),
            };
        }

        string? winTile = ReadString(record, "winTile");
        return new GoldenInput
        {
            Hand = hand,
            Melds = melds,
            SelfDraw = ReadBool(record, "selfDraw"),
            HasCalled = ReadBool(record, "hasCalled"),
            Ankan = ReadInt(record, "ankan"),
            Minkan = ReadInt(record, "minkan"),
            FirstTurn = ReadBool(record, "firstTurn"),
            WinTile = winTile is null ? null : Tile.Parse(winTile),
            Context = context,
            MaxSplit = record["maxSplit"] is null ? null : ReadBool(record, "maxSplit"),
        };
    }

    private static RegenResult BuildResult(WinResult result)
    {
        var yaku = new List<RegenYaku>(result.ActiveYaku.Count);
        foreach (var item in result.ActiveYaku)
        {
            yaku.Add(new RegenYaku
            {
                Name = item.Name,
                Fu = item.Fu,
                Id = item.Id,
                IsBonus = item.IsBonus,
            });
        }

        return new RegenResult
        {
            Success = result.Success,
            Reason = result.Reason,
            TotalFu = result.TotalFu,
            HandType = result.HandType?.ToWire(),
            Yaku = yaku,
            SpecialPoints = result.SpecialPoints,
            SevenHonors = result.SevenHonors,
        };
    }

    private static List<string> ReadStringList(JsonNode? node)
    {
        var result = new List<string>();
        if (node is not JsonArray array)
        {
            return result;
        }

        foreach (var item in array)
        {
            result.Add(item?.GetValue<string>() ?? string.Empty);
        }

        return result;
    }

    private static string? ReadString(JsonObject obj, string name) =>
        obj[name] is JsonValue value ? value.GetValue<string>() : null;

    private static bool ReadBool(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.GetValue<bool>();

    private static int ReadInt(JsonObject obj, string name) =>
        obj[name] is JsonValue value ? value.GetValue<int>() : 0;

    /// <summary>从输出目录向上找仓库根的 tests/fixtures。</summary>
    private static string FindFixturesDir()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "fixtures", "yaku-golden.json");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("找不到 tests/fixtures/yaku-golden.json");
    }

    private sealed class GoldenInput
    {
        public required List<Tile> Hand { get; init; }

        public required List<CoreMeld> Melds { get; init; }

        public bool SelfDraw { get; init; }

        public bool HasCalled { get; init; }

        public int Ankan { get; init; }

        public int Minkan { get; init; }

        public bool FirstTurn { get; init; }

        public Tile? WinTile { get; init; }

        public WinContext? Context { get; init; }

        public bool? MaxSplit { get; init; }
    }

    private sealed class RegenResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("reason")]
        public string? Reason { get; init; }

        [JsonPropertyName("totalFu")]
        public int? TotalFu { get; init; }

        [JsonPropertyName("handType")]
        public string? HandType { get; init; }

        [JsonPropertyName("yaku")]
        public List<RegenYaku> Yaku { get; init; } = new();

        [JsonPropertyName("specialPoints")]
        public int? SpecialPoints { get; init; }

        [JsonPropertyName("sevenHonors")]
        public bool SevenHonors { get; init; }
    }

    private sealed class RegenYaku
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("fu")]
        public int Fu { get; init; }

        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("isBonus")]
        public bool IsBonus { get; init; }
    }
}

/// <summary>
/// 用当前 AI 决策实现重算 ai-golden.json 的 out 字段。
/// 规则变更（尤其是和绝张只看河里+副露）会改变部分荣和/自摸判定，AI 语料必须随之更新。
/// </summary>
internal static class RegenAiFixtures
{
    private static readonly JsonSerializerOptions CompactJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static int Run()
    {
        string path = FindCorpus();
        string raw = File.ReadAllText(path, Encoding.UTF8);
        var root = JsonNode.Parse(raw)?.AsObject()
            ?? throw new InvalidOperationException($"语料解析失败: {path}");
        var decisions = root["decisions"]?.AsArray()
            ?? throw new InvalidOperationException("ai-golden.json 缺少 decisions");
        var checker = new WinChecker(YakuData.LoadDefault());

        int changed = 0;
        for (int index = 0; index < decisions.Count; index++)
        {
            var record = decisions[index]?.AsObject()
                ?? throw new InvalidOperationException($"decisions[{index}] 不是对象");
            string kind = record["kind"]?.GetValue<string>()
                ?? throw new InvalidOperationException($"decisions[{index}].kind 缺失");
            var input = record["in"]?.AsObject()
                ?? throw new InvalidOperationException($"decisions[{index}].in 缺失");
            var obs = BuildObservation(input);

            JsonNode? newNode = BuildOut(kind, record, obs, checker);
            string before = record["out"]?.ToJsonString(CompactJson) ?? "null";
            string after = newNode?.ToJsonString(CompactJson) ?? "null";
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                changed++;
                if (changed <= 20)
                {
                    Console.WriteLine($"  [{index}] {kind} p={input["p"]}");
                    Console.WriteLine($"    - {before}");
                    Console.WriteLine($"    + {after}");
                }
            }

            record["out"] = newNode;
        }

        string output = root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + "\n";
        File.WriteAllText(path, output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Console.WriteLine($"[ai-golden.json] 共 {decisions.Count} 条决策，out 变化 {changed} 条");
        return 0;
    }

    private static JsonNode? BuildOut(
        string kind,
        JsonObject record,
        AiPlayerObservation obs,
        WinChecker checker)
    {
        switch (kind)
        {
            case "discard":
            {
                double rnd = Rnd(record);
                var hand = obs.Players[obs.PlayerIndex].Hand;
                int idx = AiPlayer.ChooseDiscardIndex(hand, rnd);
                return new JsonObject
                {
                    ["tile"] = idx >= 0 && idx < hand.Count ? JsonValue.Create(hand[idx].Id) : null,
                };
            }

            case "kan":
            {
                var decision = AiPlayer.DecideKan(obs);
                if (decision is null)
                {
                    return null;
                }

                return new JsonObject
                {
                    ["id"] = decision.Tile.Id,
                    ["type"] = decision.Type.Wire(),
                };
            }

            case "meld":
            {
                double rnd = Rnd(record);
                var decision = AiPlayer.DecideMeld(obs, rnd);
                if (!decision.Applied)
                {
                    return new JsonObject { ["action"] = "pass" };
                }

                JsonArray? trio = null;
                if (decision.ChiTrio is not null)
                {
                    trio = new JsonArray();
                    foreach (var tile in decision.ChiTrio)
                    {
                        trio.Add(tile.Id);
                    }
                }

                return new JsonObject
                {
                    ["action"] = "meld",
                    ["type"] = decision.Type.Wire(),
                    ["target"] = decision.Target,
                    ["chiTrio"] = trio,
                };
            }

            case "ron":
            {
                var winners = new JsonArray();
                foreach (int winner in AiPlayer.CheckRon(obs, checker))
                {
                    winners.Add(winner);
                }

                return new JsonObject { ["winners"] = winners };
            }

            case "tsumo":
                return new JsonObject { ["win"] = AiPlayer.CheckTsumo(obs, checker).Success };

            default:
                throw new InvalidOperationException($"未知决策类型: {kind}");
        }
    }

    private static double Rnd(JsonObject record) =>
        record["rnd"] is JsonArray array && array.Count > 0
            ? array[0]!.GetValue<double>()
            : 0.0;

    private static AiPlayerObservation BuildObservation(JsonObject input)
    {
        var obs = new AiPlayerObservation
        {
            PlayerIndex = ReadInt(input, "p"),
            TurnIndex = ReadInt(input, "turn"),
            DiscPlayer = input["discPlayer"] is null ? -1 : ReadInt(input, "discPlayer"),
            Discarded = ReadTileOrNull(input, "disc"),
            WallCount = ReadInt(input, "wall"),
            RinshanCount = ReadInt(input, "rinshan"),
            RoundWind = ParseWind(ReadString(input, "round")),
            DealerIndex = ReadInt(input, "dealer"),
            Honba = ReadInt(input, "honba"),
            HandNumber = ReadInt(input, "hn"),
            LastDiscarded = ReadTileOrNull(input, "last"),
            KanThisTurn = ReadBool(input, "kanTurn"),
        };

        if (input["scores"] is JsonArray scores)
        {
            for (int i = 0; i < scores.Count && i < obs.Scores.Length; i++)
            {
                obs.Scores[i] = scores[i]!.GetValue<int>();
            }
        }

        foreach (var node in input["players"]!.AsArray())
        {
            var p = node!.AsObject();
            var view = new AiPlayerView
            {
                SeatWind = ParseWind(ReadString(p, "sw")),
                IsDealer = ReadBool(p, "dealer"),
                FirstTurn = ReadBool(p, "first"),
                AnkanCount = ReadInt(p, "ankan"),
                MinkanCount = ReadInt(p, "minkan"),
                LastDraw = ReadTileOrNull(p, "ld"),
                SeaMoon = ReadBool(p, "sea"),
                HaiDei = ReadBool(p, "hai"),
                RinshanDrawn = ReadBool(p, "rin"),
                FirstTurnTenpai = ReadBool(p, "ftt"),
                SkipDraw = ReadBool(p, "skip"),
            };

            AddTiles(view.Hand, p["h"]);
            AddTiles(view.Discards, p["d"]);
            if (p["m"] is JsonArray melds)
            {
                foreach (var meldNode in melds)
                {
                    var meld = meldNode!.AsObject();
                    string type = ReadString(meld, "t");
                    var tiles = new List<Tile>();
                    AddTiles(tiles, meld["x"]);
                    view.Melds.Add(new EngineMeld(EngineWire.ParseMeldType(type), tiles));
                }
            }

            obs.Players.Add(view);
        }

        return obs;
    }

    private static void AddTiles(List<Tile> target, JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return;
        }

        foreach (var item in array)
        {
            target.Add(Tile.Parse(item!.GetValue<string>()));
        }
    }

    private static Tile? ReadTileOrNull(JsonObject obj, string name)
    {
        string id = ReadString(obj, name);
        return string.IsNullOrEmpty(id) ? null : Tile.Parse(id);
    }

    private static string ReadString(JsonObject obj, string name) =>
        obj[name] is JsonValue value ? value.GetValue<string>() : string.Empty;

    private static bool ReadBool(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.GetValue<bool>();

    private static int ReadInt(JsonObject obj, string name) =>
        obj[name] is JsonValue value ? value.GetValue<int>() : 0;

    private static SeatWind ParseWind(string wire) => wire switch
    {
        "east" => SeatWind.East,
        "south" => SeatWind.South,
        "west" => SeatWind.West,
        "north" => SeatWind.North,
        _ => throw new InvalidOperationException("未知门风: " + wire),
    };

    private static string FindCorpus()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "fixtures", "ai-golden.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("找不到 tests/fixtures/ai-golden.json");
    }
}

/// <summary>
/// 和绝张的引擎口径探针：构造两个荣和场景，确认可见张数 = 河里 + 副露（排除和牌张本身），
/// 且手牌不计。对应 WinCheckerRuleTests 的「河里 2 + 副露 1 / 河里 2 + 手牌 1」两条断言。
/// </summary>
internal static class ProbeLastTile
{
    public static int Run()
    {
        var checker = new WinChecker(YakuData.LoadDefault());
        int failures = 0;

        // 场景 A：河里已有 2 张 + 别家副露 1 张，第三张放铳 → 和绝张成立。
        bool caseA = RunCase(
            "河里2+副露1",
            meldWithTarget: true,
            expectLastTile: true,
            checker);
        if (!caseA)
        {
            failures++;
        }

        // 场景 B：河里已有 2 张 + 和牌者手里 1 张（暗牌不可见），第三张放铳 → 和绝张不成立。
        bool caseB = RunCase(
            "河里2+手牌1",
            meldWithTarget: false,
            expectLastTile: false,
            checker);
        if (!caseB)
        {
            failures++;
        }

        Console.WriteLine(failures == 0 ? "[probe] 全部通过" : $"[probe] 失败 {failures} 项");
        return failures == 0 ? 0 : 1;
    }

    private static bool RunCase(string label, bool meldWithTarget, bool expectLastTile, WinChecker checker)
    {
        var target = Tile.Parse("m5");
        var observation = new AiPlayerObservation
        {
            TurnIndex = 0,
            DiscPlayer = 0,
            Discarded = target,
            WallCount = 30,
        };

        for (int pi = 0; pi < 4; pi++)
        {
            observation.Players.Add(new AiPlayerView
            {
                SeatWind = (SeatWind)pi,
                IsDealer = pi == 0,
                IsHuman = false,
                FirstTurn = false,
            });
        }

        // 放铳者：弃牌河里有 3 张 m5（前两张 + 本次放铳的第三张）。
        observation.Players[0].Discards.AddRange(new[] { target, target, target });
        observation.Players[0].Hand.AddRange(Tiles("p1 p2 p3 s1 s2 s3 z1 z2 z3 z4 z5 z6 z7"));

        // 和牌者（1 号）：13 张听 m5。
        if (meldWithTarget)
        {
            // 场景 A：和牌者手里没有 m5；别家（2 号）副露 m4m5m6 提供第 3 张可见。
            observation.Players[1].Hand.AddRange(Tiles("p1 p2 p3 p4 p5 p6 s2 s3 s4 s8 s8 m4 m6"));
            observation.Players[2].Melds.Add(new EngineMeld(MeldType.Chi, Tiles("m4 m5 m6")));
            observation.Players[2].Hand.AddRange(Tiles("p7 p8 p9 s5 s6 s7 z1 z2 z3 z4"));
        }
        else
        {
            // 场景 B：和牌者暗手已有 1 张 m5（不可见），四组面子 + 单张 m5（清一色保证成和）。
            observation.Players[1].Hand.AddRange(Tiles("m1 m2 m3 m4 m5 m6 m7 m8 m9 m2 m3 m4 m5"));
            observation.Players[2].Hand.AddRange(Tiles("p7 p8 p9 s5 s6 s7 z1 z2 z3 z4 z5 z6 z7"));
        }

        observation.Players[3].Hand.AddRange(Tiles("p1 p1 p2 p2 p3 p3 s1 s1 s2 s2 s3 s3 z1"));

        var candidates = AiPlayer.CheckRonDetailed(observation, checker);
        var winner = candidates.FirstOrDefault(c => c.PlayerIndex == 1);
        if (winner is null)
        {
            Console.WriteLine($"[probe] {label}: 未产生荣和（无法验证和绝张）");
            return false;
        }

        bool lastTile = winner.Result.ActiveYaku.Any(y => y.Id == "last_tile");
        string yaku = string.Join(", ", winner.Result.ActiveYaku.Select(y => $"{y.Name}/{y.Fu}"));
        Console.WriteLine($"[probe] {label}: success={winner.Result.Success}, totalFu={winner.Result.TotalFu}, 和绝张={lastTile}");
        Console.WriteLine($"[probe]   yaku=[{yaku}]");
        if (lastTile != expectLastTile)
        {
            Console.WriteLine($"[probe] {label}: 期望和绝张={expectLastTile}，实际={lastTile}");
            return false;
        }

        return true;
    }

    private static List<Tile> Tiles(string spec) =>
        spec.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Tile.Parse).ToList();
}
