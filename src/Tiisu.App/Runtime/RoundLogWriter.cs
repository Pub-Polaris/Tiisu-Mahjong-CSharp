using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tiisu.Core;
using Tiisu.Engine;
using EngineMeld = Tiisu.Engine.Meld;

namespace Tiisu.App.Runtime;

/// <summary>
/// 牌型日志（<c>recordTiles</c> 开启时）：<c>&lt;数据目录&gt;\logs\roundlog_YYYYMMDD.jsonl</c>。
/// 一行一个 JSON 事件，字段与 <c>src/Tiisu.Api/LOG-FORMAT.md</c> 一致；动作码沿用
/// draw / discard / pon / chi / kan_minkan / kan_ankan / kan_kakan / rinshan_draw /
/// win_tsumo / win_ron / observe / special_flow / flow / tiisuin_win / witness，
/// 报听新增 call（宣言）/ call_foul（错和）。
/// <para>
/// 存储层**只写 ASCII**：玩家名写 slug（human/ai_south/…），流局写原因码，序列化器再把非 ASCII 转义。
/// </para>
/// </summary>
public sealed class RoundLogWriter
{
    public const string FilePrefix = "roundlog_";
    public const string FileExtension = ".jsonl";

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        // 默认转义器把非 ASCII 写成 \uXXXX —— 即使上游漏了汉字也不会落成 CJK 字节。
        Encoder = JavaScriptEncoder.Default,
    };

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly object _gate = new();
    private readonly List<(int Player, JsonObject Event)> _entries = new();

    public RoundLogWriter(string? directory = null)
    {
        DirectoryPath = Path.GetFullPath(directory ?? Settings.DefaultDirectory);
        LogsDirectory = Path.Combine(DirectoryPath, "logs");
    }

    public string DirectoryPath { get; }

    public string LogsDirectory { get; }

    /// <summary>是否记录（对应 <c>settings.recordTiles</c>）。</summary>
    public bool Enabled { get; set; }

    public string FilePath(DateTime? localDate = null) =>
        Path.Combine(LogsDirectory, FilePrefix + (localDate ?? DateTime.Now).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + FileExtension);

    /// <summary>追加一条动作事件（<paramref name="action"/> 用动作码）。</summary>
    public void Step(MahjongEngine engine, int playerIndex, string action, Tile? tile = null,
        string? reason = null, string? via = null)
    {
        if (!Enabled || engine is null)
        {
            return;
        }

        if (playerIndex < 0 || playerIndex >= MahjongEngine.PlayerCount)
        {
            return;
        }

        lock (_gate)
        {
            var player = engine.Players[playerIndex];
            var ev = new JsonObject
            {
                ["type"] = "step",
                ["a"] = action,
                ["p"] = playerIndex,
            };

            if (tile is Tile value)
            {
                ev["t"] = TileNotation.TileToStr(value);
            }

            ev["hand"] = TileNotation.HandToStr(player.Hand);
            ev["melds"] = MeldsJson(player.Melds);
            ev["discards"] = DiscardsJson(player.Discards);
            ev["wall"] = engine.Wall.Count;
            ev["rinshan"] = engine.Rinshan.Count;
            ev["h"] = engine.Honba;
            ev["rw"] = engine.RoundWindIdx;
            ev["dc"] = engine.DealerCount;
            ev["x"] = engine.InExtension ? 1 : 0;
            ev["w"] = player.SeatWind.Wire();
            ev["d"] = player.IsDealer ? 1 : 0;
            ev["r"] = 0;

            if (!string.IsNullOrEmpty(reason))
            {
                ev["reason"] = reason;
            }

            if (!string.IsNullOrEmpty(via))
            {
                ev["via"] = via;
            }

            _entries.Add((playerIndex, ev));
        }
    }

    /// <summary>局末：把本局 4 份 round 头 + 全部事件追加到当日文件。</summary>
    public void Flush(MahjongEngine engine, string session)
    {
        if (!Enabled || engine is null)
        {
            Clear();
            return;
        }

        List<JsonObject> lines;
        lock (_gate)
        {
            lines = new List<JsonObject>(_entries.Count + MahjongEngine.PlayerCount);
            for (int pi = 0; pi < MahjongEngine.PlayerCount; pi++)
            {
                lines.Add(RoundHeader(engine, session, pi));
            }

            foreach (var (_, ev) in _entries)
            {
                lines.Add(ev);
            }

            _entries.Clear();
        }

        if (lines.Count == 0)
        {
            return;
        }

        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.Append(line.ToJsonString(SerializeOptions)).Append('\n');
        }

        try
        {
            Directory.CreateDirectory(LogsDirectory);
            File.AppendAllText(FilePath(), builder.ToString(), Utf8NoBom);
        }
        catch (IOException)
        {
            // 日志写失败不应影响对局。
        }
    }

    /// <summary>丢弃本局未落盘的事件（新局 / 重置）。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    private static JsonObject RoundHeader(MahjongEngine engine, string session, int playerIndex)
    {
        var player = engine.Players[playerIndex];
        return new JsonObject
        {
            ["type"] = "round",
            ["round"] = engine.HandNumber,
            ["honba"] = engine.Honba,
            ["player"] = playerIndex,
            ["name"] = PlayerSlug(playerIndex),
            ["wind"] = player.SeatWind.Wire(),
            ["dealer"] = player.IsDealer,
            ["riichi"] = false,
            ["uuid"] = string.Empty,
            ["session"] = session,
            ["roundWindIdx"] = engine.RoundWindIdx,
            ["dealerCount"] = engine.DealerCount,
            ["extension"] = engine.InExtension,
        };
    }

    private static JsonArray MeldsJson(IReadOnlyList<EngineMeld> melds)
    {
        var array = new JsonArray();
        foreach (var meld in melds)
        {
            if (meld.Tiles.Count == 0)
            {
                continue;
            }

            var nums = new StringBuilder();
            foreach (var tile in meld.Tiles)
            {
                nums.Append(tile.Num.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            array.Add(new JsonObject
            {
                ["k"] = meld.Type.Wire(),
                ["t"] = nums.ToString() + TileNotation.SuitChar(meld.Tiles[0].Suit),
            });
        }

        return array;
    }

    private static JsonArray DiscardsJson(IReadOnlyList<Tile> discards)
    {
        var array = new JsonArray();
        foreach (var tile in discards)
        {
            array.Add(TileNotation.TileToStr(tile));
        }

        return array;
    }

    /// <summary>座次 slug（零汉字）。</summary>
    public static string PlayerSlug(int playerIndex) => playerIndex switch
    {
        0 => "human",
        1 => "ai_south",
        2 => "ai_west",
        3 => "ai_north",
        _ => "p" + playerIndex,
    };
}
