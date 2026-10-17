using System.Text.Json;
using System.Text.Json.Serialization;
using Tiisu.Core;
using Xunit.Abstractions;

namespace Tiisu.Core.Tests;

/// <summary>
/// 两份 golden 语料（断言语料 + 构造式随机语料）的逐字段对比。
/// 一个测试跑全量并聚合差异，输出前 30 条摘要与总数。
/// </summary>
public sealed class GoldenTests
{
    private readonly ITestOutputHelper _output;

    public GoldenTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void GoldenCorpora_MatchOracleFieldByField()
    {
        var checker = new WinChecker(YakuData.LoadDefault());
        var corpora = new (string Name, List<GoldenRecord> Records)[]
        {
            ("yaku-golden.json", Load("yaku-golden.json")),
            ("yaku-golden-random.json", Load("yaku-golden-random.json")),
        };

        var differences = new List<string>();
        int total = 0;
        foreach (var (name, records) in corpora)
        {
            for (int index = 0; index < records.Count; index++)
            {
                total++;
                var record = records[index];
                // fixture 现在按调用记录 maxSplit（对照用例会临时关掉）；缺省视为 true
                checker.MaxSplit = record.MaxSplit ?? true;
                string? diff = Compare(checker, record);
                if (diff is not null)
                {
                    differences.Add($"{name}[{index}] {diff}");
                }
            }
        }

        checker.MaxSplit = true;
        _output.WriteLine($"语料总数: {total}, 差异: {differences.Count}");
        _output.WriteLine("注: 每条按 fixture 的 maxSplit 字段设置（缺省 true）；验证台的 maxSplit=false 对照调用也如实记录。");
        foreach (string difference in differences.Take(30))
        {
            _output.WriteLine(difference);
        }

        Assert.True(
            differences.Count == 0,
            $"golden 差异 {differences.Count} / {total} 条。前 30 条见测试输出。");
    }

    private static string? Compare(WinChecker checker, GoldenRecord record)
    {
        var hand = record.Hand.Select(Tile.Parse).ToList();
        var melds = record.Melds
            .Select(m => new Meld(m.Type, m.Tiles.Select(Tile.Parse).ToList()))
            .ToList();
        Tile? winTile = record.WinTile is null ? null : Tile.Parse(record.WinTile);
        var context = record.Ctx is null
            ? null
            : new WinContext
            {
                SeaMoon = record.Ctx.SeaMoon,
                HaiDei = record.Ctx.HaiDei,
                RiverFish = record.Ctx.RiverFish,
                LastTileVisible = record.Ctx.LastTileVisible,
                SeatWind = record.Ctx.SeatWind,
                RoundWind = record.Ctx.RoundWind,
                WaterDrop = record.Ctx.WaterDrop,
                RinshanGun = record.Ctx.RinshanGun,
                RinshanFlower = record.Ctx.RinshanFlower,
                KanRiichi = record.Ctx.KanRiichi,
                Renhe = record.Ctx.Renhe,
            };

        WinResult actual = checker.CheckWin(
            hand,
            record.SelfDraw,
            record.HasCalled,
            record.Ankan,
            record.Minkan,
            melds,
            record.FirstTurn,
            winTile,
            context);

        var problems = new List<string>();
        if (actual.Success != record.Result.Success)
        {
            problems.Add($"success={actual.Success} expected={record.Result.Success}");
        }

        if (actual.Reason != record.Result.Reason)
        {
            problems.Add($"reason={Show(actual.Reason)} expected={Show(record.Result.Reason)}");
        }

        if (actual.TotalFu != record.Result.TotalFu)
        {
            problems.Add($"totalFu={Show(actual.TotalFu)} expected={Show(record.Result.TotalFu)}");
        }

        string? handType = actual.HandType?.ToWire();
        if (handType != record.Result.HandType)
        {
            problems.Add($"handType={Show(handType)} expected={Show(record.Result.HandType)}");
        }

        var actualYaku = actual.ActiveYaku;
        var expectedYaku = record.Result.Yaku;
        if (actualYaku.Count != expectedYaku.Count)
        {
            problems.Add($"yakuCount={actualYaku.Count} expected={expectedYaku.Count}");
        }
        else
        {
            for (int i = 0; i < actualYaku.Count; i++)
            {
                var a = actualYaku[i];
                var e = expectedYaku[i];
                if (a.Name != e.Name || a.Fu != e.Fu || a.Id != e.Id || a.IsBonus != e.IsBonus)
                {
                    problems.Add(
                        $"yaku[{i}]={a.Name}/{a.Fu}/{Show(a.Id)}/{a.IsBonus} "
                        + $"expected={e.Name}/{e.Fu}/{Show(e.Id)}/{e.IsBonus}");
                    break;
                }
            }
        }

        if (actual.SpecialPoints != record.Result.SpecialPoints)
        {
            problems.Add($"specialPoints={Show(actual.SpecialPoints)} expected={Show(record.Result.SpecialPoints)}");
        }

        if (actual.SevenHonors != record.Result.SevenHonors)
        {
            problems.Add($"sevenHonors={actual.SevenHonors} expected={record.Result.SevenHonors}");
        }

        if (problems.Count == 0)
        {
            return null;
        }

        return string.Join("; ", problems)
            + " || got: " + Describe(actual)
            + " || expected: " + Describe(record.Result);
    }

    private static string Show<T>(T value) => value is null ? "null" : value.ToString() ?? "null";

    private static string Describe(WinResult result)
    {
        string yaku = string.Join(
            ", ",
            result.ActiveYaku.Select(y => $"{y.Name}/{y.Fu}/{y.Id ?? "null"}/{y.IsBonus}"));
        return $"success={result.Success}, reason={result.Reason ?? "null"}, totalFu={result.TotalFu?.ToString() ?? "null"}, "
            + $"handType={result.HandType?.ToWire() ?? "null"}, specialPoints={result.SpecialPoints?.ToString() ?? "null"}, "
            + $"sevenHonors={result.SevenHonors}, yaku=[{yaku}]";
    }

    private static string Describe(GoldenResult result)
    {
        string yaku = string.Join(
            ", ",
            result.Yaku.Select(y => $"{y.Name}/{y.Fu}/{y.Id ?? "null"}/{y.IsBonus}"));
        return $"success={result.Success}, reason={result.Reason ?? "null"}, totalFu={result.TotalFu?.ToString() ?? "null"}, "
            + $"handType={result.HandType ?? "null"}, specialPoints={result.SpecialPoints?.ToString() ?? "null"}, "
            + $"sevenHonors={result.SevenHonors}, yaku=[{yaku}]";
    }

    private static List<GoldenRecord> Load(string fileName)
    {
        string path = FindFixture(fileName);
        string json = File.ReadAllText(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<List<GoldenRecord>>(json, options)
            ?? throw new InvalidOperationException($"fixture 反序列化为空: {path}");
    }

    private static string FindFixture(string fileName)
    {
        string direct = Path.Combine(AppContext.BaseDirectory, "fixtures", fileName);
        if (File.Exists(direct))
        {
            return direct;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "fixtures", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"找不到 fixture: {fileName}", fileName);
    }
}

internal sealed class GoldenRecord
{
    public List<string> Hand { get; set; } = new();

    public List<GoldenMeld> Melds { get; set; } = new();

    public bool SelfDraw { get; set; }

    public bool HasCalled { get; set; }

    public int Ankan { get; set; }

    public int Minkan { get; set; }

    public bool FirstTurn { get; set; }

    public string? WinTile { get; set; }

    public GoldenCtx? Ctx { get; set; }

    /// <summary>记录本次调用时 WinChecker.maxSplit 的取值；缺省（老语料）视为 true。</summary>
    public bool? MaxSplit { get; set; }

    public GoldenResult Result { get; set; } = new();
}

internal sealed class GoldenMeld
{
    public string Type { get; set; } = string.Empty;

    public List<string> Tiles { get; set; } = new();
}

internal sealed class GoldenCtx
{
    public bool SeaMoon { get; set; }

    public bool HaiDei { get; set; }

    public bool RiverFish { get; set; }

    public int LastTileVisible { get; set; }

    public string? SeatWind { get; set; }

    public string? RoundWind { get; set; }

    public bool WaterDrop { get; set; }

    public bool RinshanGun { get; set; }

    public bool RinshanFlower { get; set; }

    public bool KanRiichi { get; set; }

    public bool Renhe { get; set; }
}

internal sealed class GoldenResult
{
    public bool Success { get; set; }

    public string? Reason { get; set; }

    public int? TotalFu { get; set; }

    public string? HandType { get; set; }

    public List<GoldenYaku> Yaku { get; set; } = new();

    public int? SpecialPoints { get; set; }

    public bool SevenHonors { get; set; }
}

internal sealed class GoldenYaku
{
    public string Name { get; set; } = string.Empty;

    public int Fu { get; set; }

    public string? Id { get; set; }

    public bool IsBonus { get; set; }
}
