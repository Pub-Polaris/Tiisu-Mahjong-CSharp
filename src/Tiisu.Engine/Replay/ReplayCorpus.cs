using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tiisu.Engine.Replay;

/// <summary>
/// 回放语料（tests/fixtures/engine-golden.json）。格式见仓库 README / 任务说明：
/// 顶层是种子与场次数，<c>events</c> 是逐事件的动作 + 处理后的状态快照。
/// </summary>
public sealed class ReplayCorpus
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    [JsonPropertyName("seed")]
    public long Seed { get; set; }

    [JsonPropertyName("games")]
    public int Games { get; set; }

    /// <summary>每场的局数（语料为 1：一场一手）。</summary>
    [JsonPropertyName("hands")]
    public int Hands { get; set; }

    [JsonPropertyName("events")]
    public List<ReplayEvent> Events { get; set; } = new();

    public static ReplayCorpus LoadFromFile(string path) => Parse(File.ReadAllText(path));

    public static ReplayCorpus Parse(string json)
    {
        var corpus = JsonSerializer.Deserialize<ReplayCorpus>(json, JsonOptions)
            ?? throw new InvalidOperationException("回放语料解析结果为空");
        if (corpus.Events.Count == 0)
        {
            throw new InvalidOperationException("回放语料没有 events");
        }

        return corpus;
    }
}

/// <summary>一个回放事件。字段按事件类型可选，未用到的保持 null。</summary>
public sealed class ReplayEvent
{
    [JsonPropertyName("g")]
    public int Game { get; set; }

    [JsonPropertyName("e")]
    public string Type { get; set; } = string.Empty;

    /// <summary>动作发起者（draw / discard / meld / kan / win）。</summary>
    [JsonPropertyName("p")]
    public int? Player { get; set; }

    /// <summary>draw / discard / meld 的牌。</summary>
    [JsonPropertyName("t")]
    public string? Tile { get; set; }

    /// <summary>draw 前的墙牌数。</summary>
    [JsonPropertyName("bw")]
    public int? WallBefore { get; set; }

    [JsonPropertyName("sea")]
    public bool? SeaMoon { get; set; }

    [JsonPropertyName("hai")]
    public bool? HaiDei { get; set; }

    /// <summary>meld 类型：chi / pon / minkan / ankan / kakan。</summary>
    [JsonPropertyName("mt")]
    public string? MeldType { get; set; }

    /// <summary>放铳者 / 被副露者。</summary>
    [JsonPropertyName("from")]
    public int? From { get; set; }

    /// <summary>吃牌组合（已排序），非吃牌为 null。</summary>
    [JsonPropertyName("trio")]
    public List<string>? Trio { get; set; }

    /// <summary>kan 类型：ankan / minkan / kakan。</summary>
    [JsonPropertyName("kt")]
    public string? KanType { get; set; }

    /// <summary>杠的牌 id。</summary>
    [JsonPropertyName("id")]
    public string? KanTile { get; set; }

    [JsonPropertyName("self")]
    public bool? SelfDraw { get; set; }

    /// <summary>和牌赋数（不含赐马）。</summary>
    [JsonPropertyName("fu")]
    public int? Fu { get; set; }

    [JsonPropertyName("sp")]
    public int? SpecialPoints { get; set; }

    /// <summary>役种列表，元素形如 "缺门:4"。</summary>
    [JsonPropertyName("y")]
    public List<string>? Yaku { get; set; }

    [JsonPropertyName("w")]
    public List<ReplayWinner>? Winners { get; set; }

    [JsonPropertyName("why")]
    public string? Why { get; set; }

    /// <summary>语料生成时的网络请求清单（引擎不联网，仅为可审计保留）。</summary>
    [JsonPropertyName("fetched")]
    public List<string>? Fetched { get; set; }

    [JsonPropertyName("s")]
    public ReplaySnapshot Snapshot { get; set; } = new();
}

/// <summary>事件处理后的状态快照。</summary>
public sealed class ReplaySnapshot
{
    [JsonPropertyName("rw")]
    public int RoundWindIdx { get; set; }

    [JsonPropertyName("dealer")]
    public int Dealer { get; set; }

    [JsonPropertyName("hn")]
    public int HandNumber { get; set; }

    [JsonPropertyName("hp")]
    public int HandsPlayed { get; set; }

    [JsonPropertyName("turn")]
    public int Turn { get; set; }

    [JsonPropertyName("wall")]
    public int Wall { get; set; }

    [JsonPropertyName("rinshan")]
    public int Rinshan { get; set; }

    [JsonPropertyName("wang")]
    public int Wang { get; set; }

    [JsonPropertyName("honba")]
    public int Honba { get; set; }

    [JsonPropertyName("scores")]
    public List<int> Scores { get; set; } = new();

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("p")]
    public List<ReplayPlayerSnapshot> Players { get; set; } = new();
}

/// <summary>一家在快照里的牌面。</summary>
public sealed class ReplayPlayerSnapshot
{
    /// <summary>暗手（已排序）。</summary>
    [JsonPropertyName("h")]
    public List<string> Hand { get; set; } = new();

    /// <summary>副露（顺序 = 加入顺序）。</summary>
    [JsonPropertyName("m")]
    public List<ReplayMeldSnapshot> Melds { get; set; } = new();

    /// <summary>弃牌（顺序 = 打出顺序）。</summary>
    [JsonPropertyName("d")]
    public List<string> Discards { get; set; } = new();

    [JsonPropertyName("sw")]
    public string SeatWind { get; set; } = string.Empty;

    [JsonPropertyName("dl")]
    public bool Dealer { get; set; }
}

/// <summary>一组副露快照。</summary>
public sealed class ReplayMeldSnapshot
{
    [JsonPropertyName("t")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("x")]
    public List<string> Tiles { get; set; } = new();
}

/// <summary>一个荣和者（multi_ron 的 w[]）。</summary>
public sealed class ReplayWinner
{
    [JsonPropertyName("playerIdx")]
    public int PlayerIndex { get; set; }

    [JsonPropertyName("result")]
    public ReplayWinResult Result { get; set; } = new();

    /// <summary>含赐马的总赋。</summary>
    [JsonPropertyName("totalFu")]
    public int? TotalFu { get; set; }

    [JsonPropertyName("finalPoints")]
    public int? FinalPoints { get; set; }
}

/// <summary>判役结果（语料里的 result）。</summary>
public sealed class ReplayWinResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>不含赐马的赋数。</summary>
    [JsonPropertyName("totalFu")]
    public int? TotalFu { get; set; }

    [JsonPropertyName("activeYaku")]
    public List<ReplayYaku> ActiveYaku { get; set; } = new();

    [JsonPropertyName("handType")]
    public string? HandType { get; set; }

    [JsonPropertyName("sevenHonors")]
    public bool SevenHonors { get; set; }

    [JsonPropertyName("specialPoints")]
    public int? SpecialPoints { get; set; }
}

/// <summary>一条役 / 加计项。</summary>
public sealed class ReplayYaku
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("fu")]
    public int Fu { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("isBonus")]
    public bool? IsBonus { get; set; }

    [JsonPropertyName("ma")]
    public bool? Ma { get; set; }

    [JsonPropertyName("pointsBonus")]
    public int? PointsBonus { get; set; }

    [JsonPropertyName("fixedPoints")]
    public int? FixedPoints { get; set; }

    [JsonPropertyName("kaishoku")]
    public bool? Kaishoku { get; set; }
}
