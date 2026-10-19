using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tiisu.Core;
using Tiisu.Engine;
using Xunit.Abstractions;

namespace Tiisu.Engine.Tests;

/// <summary>
/// AI 决策语料（tests/fixtures/ai-golden.json）：对每条决策用记录的状态与 <c>rnd</c> 复算，
/// 逐字段比对 <c>out</c>。聚合差异，失败时输出前 30 条 + 总数。
/// </summary>
public sealed class AiGoldenTests
{
    private readonly ITestOutputHelper _output;

    public AiGoldenTests(ITestOutputHelper output) => _output = output;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    [Fact]
    public void EveryDecision_Matches()
    {
        var corpus = LoadCorpus();
        var checker = new WinChecker(YakuData.LoadDefault());

        var diffs = new List<string>();
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        var covered = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        for (int i = 0; i < corpus.Decisions.Count; i++)
        {
            var rec = corpus.Decisions[i];
            var obs = BuildObservation(rec.In);
            totals.TryGetValue(rec.Kind, out int n);
            totals[rec.Kind] = n + 1;

            string expected;
            string actual;
            switch (rec.Kind)
            {
                case "discard":
                {
                    double rnd = rec.Rnd.Count > 0 ? rec.Rnd[0] : 0.0;
                    var hand = obs.Players[obs.PlayerIndex].Hand;
                    int idx = AiPlayer.ChooseDiscardIndex(hand, rnd);
                    string? tile = idx >= 0 && idx < hand.Count ? hand[idx].Id : null;
                    expected = rec.Out.GetProperty("tile").ValueKind == JsonValueKind.Null
                        ? "<none>"
                        : rec.Out.GetProperty("tile").GetString()!;
                    actual = tile ?? "<none>";
                    break;
                }

                case "kan":
                {
                    var decision = AiPlayer.DecideKan(obs);
                    expected = DescribeKan(rec.Out);
                    actual = decision is null ? "<none>" : decision.Tile.Id + ":" + decision.Type.Wire();
                    break;
                }

                case "tsumo":
                {
                    bool win = AiPlayer.CheckTsumo(obs, checker).Success;
                    expected = rec.Out.GetProperty("win").GetBoolean() ? "true" : "false";
                    actual = win ? "true" : "false";
                    break;
                }

                case "ron":
                {
                    var winners = AiPlayer.CheckRon(obs, checker);
                    expected = JoinInts(rec.Out.GetProperty("winners"));
                    actual = JoinInts(winners);
                    break;
                }

                case "meld":
                {
                    double rnd = rec.Rnd.Count > 0 ? rec.Rnd[0] : 0.0;
                    var decision = AiPlayer.DecideMeld(obs, rnd);
                    expected = DescribeMeldOut(rec.Out);
                    actual = decision.Applied
                        ? "meld:" + decision.Type.Wire() + ":" + decision.Target + ":" + JoinTiles(decision.ChiTrio)
                        : "pass";
                    break;
                }

                default:
                    diffs.Add($"[{i}] g={rec.Game} 未知决策类型 {rec.Kind}");
                    continue;
            }

            if (!covered.TryGetValue(rec.Kind, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                covered[rec.Kind] = set;
            }

            set.Add(SubKind(rec));

            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                diffs.Add($"[{i}] g={rec.Game} {rec.Kind} p={rec.In.P} expected={expected} actual={actual}");
            }
        }

        _output.WriteLine("决策总数: " + corpus.Decisions.Count);
        _output.WriteLine("按类型: " + string.Join(", ", totals.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value)));
        _output.WriteLine("覆盖: " + string.Join("; ", covered.OrderBy(kv => kv.Key)
            .Select(kv => kv.Key + "[" + string.Join("/", kv.Value.OrderBy(x => x, StringComparer.Ordinal)) + "]")));
        _output.WriteLine("差异数: " + diffs.Count);
        foreach (var line in diffs.Take(30))
        {
            _output.WriteLine(line);
        }

        if (diffs.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"AI 决策差异 {diffs.Count} / {corpus.Decisions.Count}");
            sb.AppendLine("按类型: " + string.Join(", ", totals.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value)));
            sb.AppendLine("覆盖: " + string.Join("; ", covered.OrderBy(kv => kv.Key)
                .Select(kv => kv.Key + "[" + string.Join("/", kv.Value.OrderBy(x => x, StringComparer.Ordinal)) + "]")));
            sb.AppendLine("前 30 条:");
            foreach (var line in diffs.Take(30))
            {
                sb.AppendLine(line);
            }

            Assert.Fail(sb.ToString());
        }
    }

    /// <summary>语料覆盖面守门：数量下限 + 必须出现过的分支（防止语料退化后测试变成空跑）。</summary>
    [Fact]
    public void Corpus_CoversAllDecisionBranches()
    {
        var corpus = LoadCorpus();
        Assert.True(corpus.Decisions.Count >= 1000, $"决策数过少: {corpus.Decisions.Count}");

        var byKind = corpus.Decisions.GroupBy(d => d.Kind, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var kind in new[] { "discard", "meld", "kan", "ron", "tsumo" })
        {
            Assert.True(byKind.GetValueOrDefault(kind) > 0, $"缺少决策类型: {kind}");
        }

        Assert.True(byKind.GetValueOrDefault("discard") >= 300, "discard 条数不足 300");

        var meldSub = corpus.Decisions.Where(d => d.Kind == "meld")
            .Select(d => d.Out.GetProperty("action").GetString() == "pass"
                ? "pass"
                : d.Out.GetProperty("type").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var sub in new[] { "pass", "pon", "chi", "minkan" })
        {
            Assert.True(meldSub.Contains(sub), $"缺少副露分支: {sub}");
        }

        var kanSub = corpus.Decisions.Where(d => d.Kind == "kan")
            .Select(d => d.Out.ValueKind == JsonValueKind.Null ? "none" : d.Out.GetProperty("type").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var sub in new[] { "none", "ankan", "kakan" })
        {
            Assert.True(kanSub.Contains(sub), $"缺少杠分支: {sub}");
        }

        Assert.Contains(corpus.Decisions, d => d.Kind == "ron" && d.Out.GetProperty("winners").GetArrayLength() > 0);
        Assert.Contains(corpus.Decisions, d => d.Kind == "tsumo" && d.Out.GetProperty("win").GetBoolean());
        Assert.Contains(corpus.Decisions, d => d.Kind == "tsumo" && d.AfterKan);
        Assert.Contains(corpus.Decisions, d => d.Rnd.Count > 0);
    }

    /// <summary>反向校验：故意改坏一条 discard 的期望值，比对必须报出来（避免比较退化成空操作）。</summary>
    [Fact]
    public void Comparator_ReportsCorruptedDecision()
    {
        var corpus = LoadCorpus();
        var target = corpus.Decisions.First(d => d.Kind == "discard");
        var tile = target.Out.GetProperty("tile").GetString()!;
        var corrupted = tile == "m1" ? "m2" : "m1";

        var hand = BuildObservation(target.In).Players[target.In.P].Hand;
        int idx = AiPlayer.ChooseDiscardIndex(hand, target.Rnd.Count > 0 ? target.Rnd[0] : 0.0);
        string actual = idx >= 0 && idx < hand.Count ? hand[idx].Id : "<none>";

        Assert.True(!string.Equals(actual, corrupted, StringComparison.Ordinal),
            "改坏后的期望值恰好与复算结果相同，反向校验失效");
    }

    private static string SubKind(DecisionRecord rec) => rec.Kind switch
    {
        "meld" => rec.Out.GetProperty("action").GetString() == "pass"
            ? "pass"
            : rec.Out.GetProperty("type").GetString()!,
        "kan" => rec.Out.ValueKind == JsonValueKind.Null ? "none" : rec.Out.GetProperty("type").GetString()!,
        "tsumo" => (rec.Out.GetProperty("win").GetBoolean() ? "win" : "no") + (rec.AfterKan ? "/afterKan" : string.Empty),
        "ron" => rec.Out.GetProperty("winners").GetArrayLength() > 0 ? "hit" : "empty",
        "discard" => "all",
        _ => "?",
    };

    private static string DescribeKan(JsonElement outEl)
    {
        if (outEl.ValueKind == JsonValueKind.Null)
        {
            return "<none>";
        }

        string id = outEl.GetProperty("id").GetString()!;
        string type = outEl.GetProperty("type").GetString()!;
        return id + ":" + type;
    }

    private static string DescribeMeldOut(JsonElement outEl)
    {
        string action = outEl.GetProperty("action").GetString()!;
        if (action == "pass")
        {
            return "pass";
        }

        string type = outEl.GetProperty("type").GetString()!;
        int target = outEl.GetProperty("target").GetInt32();
        var trio = outEl.GetProperty("chiTrio");
        var ids = trio.ValueKind == JsonValueKind.Null
            ? new List<string>()
            : trio.EnumerateArray().Select(e => e.GetString()!).ToList();
        return "meld:" + type + ":" + target + ":" + string.Join(",", ids);
    }

    private static string JoinInts(JsonElement array) =>
        string.Join(",", array.EnumerateArray().Select(e => e.GetInt32()));

    private static string JoinInts(IEnumerable<int> values) => string.Join(",", values);

    private static string JoinTiles(IReadOnlyList<Tile>? tiles) =>
        tiles is null ? string.Empty : string.Join(",", tiles.Select(t => t.Id));

    // ─────────────────────────────────────────────────────────────
    // 语料读取
    // ─────────────────────────────────────────────────────────────

    private static AiGoldenCorpus LoadCorpus()
    {
        string path = FindCorpus();
        var corpus = JsonSerializer.Deserialize<AiGoldenCorpus>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("AI 语料解析为空");
        Assert.False(corpus.Decisions.Count == 0, "AI 语料没有 decisions");
        return corpus;
    }

    /// <summary>
    /// 从测试输出目录向上找 <c>tests/fixtures/ai-golden.json</c>。
    /// 不走 csproj 的 CopyToOutputDirectory，避免改动既有工程文件。
    /// </summary>
    private static string FindCorpus()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "tests", "fixtures", "ai-golden.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("找不到 tests/fixtures/ai-golden.json（从 " + AppContext.BaseDirectory + " 向上查找）");
    }

    // ─────────────────────────────────────────────────────────────
    // 语料 → 决策输入
    // ─────────────────────────────────────────────────────────────

    private static AiPlayerObservation BuildObservation(AiInputDto input)
    {
        var obs = new AiPlayerObservation
        {
            PlayerIndex = input.P,
            TurnIndex = input.Turn,
            DiscPlayer = input.DiscPlayer ?? -1,
            Discarded = ParseTileOrNull(input.Disc),
            WallCount = input.Wall,
            RinshanCount = input.Rinshan,
            RoundWind = ParseWind(input.Round),
            DealerIndex = input.Dealer,
            Honba = input.Honba,
            HandNumber = input.Hn,
            Scores = input.Scores.ToArray(),
            LastDiscarded = ParseTileOrNull(input.Last),
            KanThisTurn = input.KanTurn,
        };

        if (input.Players.Count != MahjongEngine.PlayerCount)
        {
            throw new InvalidOperationException($"语料 players 数量为 {input.Players.Count}，期望 4");
        }

        foreach (var p in input.Players)
        {
            var view = new AiPlayerView
            {
                SeatWind = ParseWind(p.Sw),
                IsDealer = p.Dealer,
                FirstTurn = p.First,
                AnkanCount = p.Ankan,
                MinkanCount = p.Minkan,
                LastDraw = ParseTileOrNull(p.Ld),
                SeaMoon = p.Sea,
                HaiDei = p.Hai,
                RinshanDrawn = p.Rin,
                FirstTurnTenpai = p.Ftt,
                SkipDraw = p.Skip,
            };

            view.Hand.AddRange(p.H.Select(Tile.Parse));
            view.Discards.AddRange(p.D.Select(Tile.Parse));
            foreach (var m in p.M)
            {
                view.Melds.Add(new Meld(EngineWire.ParseMeldType(m.T), m.X.Select(Tile.Parse).ToList()));
            }

            obs.Players.Add(view);
        }

        return obs;
    }

    private static Tile? ParseTileOrNull(string? id) =>
        string.IsNullOrEmpty(id) ? null : Tile.Parse(id);

    private static SeatWind ParseWind(string? wire) => wire switch
    {
        "east" => SeatWind.East,
        "south" => SeatWind.South,
        "west" => SeatWind.West,
        "north" => SeatWind.North,
        _ => throw new InvalidOperationException("未知门风: " + wire),
    };

    // ─────────────────────────────────────────────────────────────
    // DTO（字段名与语料一致）
    // ─────────────────────────────────────────────────────────────

    private sealed class AiGoldenCorpus
    {
        [JsonPropertyName("seed")]
        public long Seed { get; set; }

        [JsonPropertyName("games")]
        public int Games { get; set; }

        [JsonPropertyName("hands")]
        public int Hands { get; set; }

        [JsonPropertyName("decisions")]
        public List<DecisionRecord> Decisions { get; set; } = new();
    }

    private sealed class DecisionRecord
    {
        [JsonPropertyName("g")]
        public int Game { get; set; }

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = string.Empty;

        [JsonPropertyName("p")]
        public int P { get; set; }

        [JsonPropertyName("afterKan")]
        public bool AfterKan { get; set; }

        [JsonPropertyName("in")]
        public AiInputDto In { get; set; } = new();

        [JsonPropertyName("rnd")]
        public List<double> Rnd { get; set; } = new();

        [JsonPropertyName("out")]
        public JsonElement Out { get; set; }
    }

    private sealed class AiInputDto
    {
        [JsonPropertyName("p")]
        public int P { get; set; }

        [JsonPropertyName("players")]
        public List<AiPlayerDto> Players { get; set; } = new();

        [JsonPropertyName("wall")]
        public int Wall { get; set; }

        [JsonPropertyName("rinshan")]
        public int Rinshan { get; set; }

        [JsonPropertyName("round")]
        public string Round { get; set; } = "east";

        [JsonPropertyName("dealer")]
        public int Dealer { get; set; }

        [JsonPropertyName("honba")]
        public int Honba { get; set; }

        [JsonPropertyName("hn")]
        public int Hn { get; set; }

        [JsonPropertyName("scores")]
        public int[] Scores { get; set; } = Array.Empty<int>();

        [JsonPropertyName("turn")]
        public int Turn { get; set; }

        [JsonPropertyName("last")]
        public string? Last { get; set; }

        [JsonPropertyName("kanTurn")]
        public bool KanTurn { get; set; }

        [JsonPropertyName("disc")]
        public string? Disc { get; set; }

        [JsonPropertyName("discPlayer")]
        public int? DiscPlayer { get; set; }
    }

    private sealed class AiPlayerDto
    {
        [JsonPropertyName("h")]
        public List<string> H { get; set; } = new();

        [JsonPropertyName("m")]
        public List<AiMeldDto> M { get; set; } = new();

        [JsonPropertyName("d")]
        public List<string> D { get; set; } = new();

        [JsonPropertyName("sw")]
        public string Sw { get; set; } = "east";

        [JsonPropertyName("dealer")]
        public bool Dealer { get; set; }

        [JsonPropertyName("first")]
        public bool First { get; set; }

        [JsonPropertyName("ankan")]
        public int Ankan { get; set; }

        [JsonPropertyName("minkan")]
        public int Minkan { get; set; }

        [JsonPropertyName("ld")]
        public string? Ld { get; set; }

        [JsonPropertyName("sea")]
        public bool Sea { get; set; }

        [JsonPropertyName("hai")]
        public bool Hai { get; set; }

        [JsonPropertyName("rin")]
        public bool Rin { get; set; }

        [JsonPropertyName("ftt")]
        public bool Ftt { get; set; }

        [JsonPropertyName("skip")]
        public bool Skip { get; set; }
    }

    private sealed class AiMeldDto
    {
        [JsonPropertyName("t")]
        public string T { get; set; } = string.Empty;

        [JsonPropertyName("x")]
        public List<string> X { get; set; } = new();
    }
}
