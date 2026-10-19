using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tiisu.Api;
using Tiisu.Core;
using Tiisu.Engine;
using ChiOption = Tiisu.Engine.ChiOption;
using Meld = Tiisu.Core.Meld;

namespace Tiisu.Shell;

/// <summary>状态快照 / JS 参数解析（EngineHost 的 JSON 半边）。</summary>
public sealed partial class EngineHost
{
    /// <summary>完整状态快照（JS 镜像的直接来源）。</summary>
    public string GetStateJson() => SnapshotJson();

    private string SnapshotJson() => Snapshot().ToJsonString();

    private JsonObject Snapshot()
    {
        FinalizeRound();
        var root = new JsonObject
        {
            ["state"] = CurrentState,
            ["turnIndex"] = _engine.TurnIndex,
            ["dealerIndex"] = _engine.DealerIndex,
            ["roundWind"] = _engine.RoundWind.Wire(),
            ["roundWindIdx"] = _engine.RoundWindIdx,
            ["honba"] = _engine.Honba,
            ["handNumber"] = _engine.HandNumber,
            ["handsPlayed"] = _engine.HandsPlayed,
            ["dealerCount"] = _engine.DealerCount,
            ["modeTarget"] = _engine.ModeTarget == 0 ? _modeTarget : _engine.ModeTarget,
            ["initialPoints"] = _engine.InitialPoints,
            ["finalWindIdx"] = _engine.FinalWindIdx,
            ["lastDiscarded"] = TileNode(_engine.LastDiscarded),
            ["currentDrawn"] = TileNode(_engine.CurrentDrawn),
            ["revealHands"] = _engine.RevealHands,
            ["gameOver"] = _engine.GameOver,
            ["matchOver"] = _engine.MatchOver,
            ["matchOverReason"] = _matchOverReason,
            ["completedRounds"] = CompletedRounds,
            ["juFinished"] = _engine.JuFinished,
            ["kanThisTurn"] = _engine.KanThisTurn,
            ["speed"] = _speed,
            ["recordTiles"] = _recordTiles,
            ["demoMode"] = _demoMode,
            ["allAI"] = _allAI,
            ["settings"] = _settings.DeepClone(),
            ["diceDealer"] = IntArrayNode(_diceDealer),
            ["diceCut"] = IntArrayNode(_diceCut),
            ["cutInfo"] = _cutInfo?.DeepClone(),
            ["actOpts"] = _actOpts?.DeepClone(),
            ["actLeft"] = _actLeft,
            ["actMsg"] = _actMsg,
            ["pendingRon"] = PendingRonNode(),
            ["pendingMeld"] = PendingMeldNode(),
            ["pendingNextRoundDelay"] = NumberNode(PendingNextRoundDelay()),
            ["lastRonMulti"] = LastRonMultiNode(),
        };

        var players = new JsonArray();
        for (int i = 0; i < MahjongEngine.PlayerCount; i++)
        {
            players.Add(PlayerNode(i));
        }

        root["players"] = players;

        var scores = new JsonArray();
        foreach (int score in _engine.Scores)
        {
            scores.Add(score);
        }

        root["scores"] = scores;

        var rankPoints = new JsonArray();
        foreach (double rank in _engine.RankPoints)
        {
            rankPoints.Add(Math.Round(rank, 1));
        }

        root["rankPoints"] = rankPoints;

        root["wall"] = TilesNode(_engine.Wall);
        root["rinshan"] = TilesNode(_engine.Rinshan);
        root["wangDui"] = TilesNode(_engine.WangDui);

        var roundDiscards = new JsonArray();
        foreach (var entry in _engine.RoundDiscards)
        {
            roundDiscards.Add(new JsonObject
            {
                ["player"] = entry.PlayerIndex,
                ["tile"] = TileNode(entry.Tile),
            });
        }

        root["roundDiscards"] = roundDiscards;
        return root;
    }

    private JsonObject PlayerNode(int index)
    {
        var player = _engine.Players[index];
        return new JsonObject
        {
            ["name"] = player.Name,
            ["uuid"] = _uuids[index] ?? string.Empty,
            ["seat"] = Seats[index],
            ["isHuman"] = player.IsHuman,
            ["isDealer"] = player.IsDealer,
            ["seatWind"] = player.SeatWind.Wire(),
            ["hand"] = TilesNode(player.Hand),
            ["discards"] = TilesNode(player.Discards),
            ["melds"] = MeldsNode(player.Melds),
            ["kans"] = TilesNode(player.Kans),
            ["ankanCount"] = player.AnkanCount,
            ["minkanCount"] = player.MinkanCount,
            ["firstTurn"] = player.FirstTurn,
            ["_lastDraw"] = TileNode(player.LastDraw),
            ["_seaMoon"] = player.SeaMoon,
            ["_haiDei"] = player.HaiDei,
            ["_skipDraw"] = player.SkipDraw,
            ["_rinshanDrawn"] = player.RinshanDrawn,
            ["_firstTurnTenpai"] = player.FirstTurnTenpai,
        };
    }

    private static JsonArray MeldsNode(IEnumerable<Tiisu.Engine.Meld> melds)
    {
        var array = new JsonArray();
        foreach (var meld in melds)
        {
            array.Add(new JsonObject
            {
                ["type"] = meld.Type.Wire(),
                ["tiles"] = TilesNode(meld.Tiles),
            });
        }

        return array;
    }

    private static JsonArray TilesNode(IEnumerable<Tile> tiles)
    {
        var array = new JsonArray();
        foreach (var tile in tiles)
        {
            array.Add(TileObject(tile));
        }

        return array;
    }

    private static JsonArray TilesNode(IEnumerable<Tile?> tiles)
    {
        var array = new JsonArray();
        foreach (var tile in tiles)
        {
            array.Add(TileNode(tile));
        }

        return array;
    }

    private static JsonNode? TileNode(Tile? tile) => tile is { } value ? TileObject(value) : null;

    private static JsonObject TileObject(Tile tile)
    {
        // 字牌的 num 与 JS tiles.js 一致用字符串（handAnalyzer / tileDisplay 依赖字符串比较）。
        JsonNode num = tile.Suit == Suit.Honor
            ? JsonValue.Create(tile.Num.ToString(CultureInfo.InvariantCulture))
            : JsonValue.Create(tile.Num);

        return new JsonObject
        {
            ["suit"] = SuitWire(tile.Suit),
            ["num"] = num,
            ["id"] = tile.Id,
        };
    }

    private static string SuitWire(Suit suit) => suit switch
    {
        Suit.Man => "m",
        Suit.Pin => "p",
        Suit.Sou => "s",
        _ => "z",
    };

    private static JsonNode? NumberNode(int? value) => value is { } number ? JsonValue.Create(number) : null;

    private static JsonArray? IntArrayNode(int[]? values)
    {
        if (values is null)
        {
            return null;
        }

        var array = new JsonArray();
        foreach (int value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private JsonArray ChiListJson(IReadOnlyList<ChiOption> options)
    {
        var array = new JsonArray();
        foreach (var option in options)
        {
            array.Add(new JsonObject { ["trio"] = TilesNode(option.Trio) });
        }

        return array;
    }

    private JsonNode? PendingRonNode()
    {
        if (_pendingRon is null)
        {
            return null;
        }

        return new JsonObject
        {
            ["tile"] = TileNode(_pendingRon.Tile),
            ["discarder"] = _pendingRon.Discarder,
            ["result"] = ResultNode(_pendingRon.Result),
        };
    }

    private JsonNode? PendingMeldNode()
    {
        if (_pendingMeld is null)
        {
            return null;
        }

        return new JsonObject
        {
            ["tile"] = TileNode(_pendingMeld.Tile),
            ["discarder"] = _pendingMeld.Discarder,
            ["chiList"] = ChiListJson(_pendingChiList),
        };
    }

    /// <summary>下一局调度延迟：仅当本局已结束、还有下一局时非 null。</summary>
    private int? PendingNextRoundDelay()
    {
        if (!_engine.GameOver || _engine.MatchOver)
        {
            return null;
        }

        return _engine.PendingNextRoundDelay;
    }

    private JsonArray LastRonMultiNode()
    {
        var array = new JsonArray();
        foreach (var winner in _engine.LastRonMulti)
        {
            array.Add(new JsonObject
            {
                ["playerIdx"] = winner.PlayerIndex,
                ["fu"] = winner.TotalFu,
                ["points"] = NumberNode(winner.FinalPoints),
                ["name"] = _engine.Players[winner.PlayerIndex].Name,
            });
        }

        return array;
    }

    // ══════════════════════════════════════════════════════════════
    // 判役桥（UI 按钮可用性 / scoretest）
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>checker.checkWin(...)</c> 的同步桥（旧版单 JSON 入口，保留兼容）。入参是 JSON：
    /// <c>{ hand:[...], selfDraw, hasCalled, ankanCount, minkanCount, melds:[...], firstTurn, winTile, ctx }</c>。
    /// </summary>
    public string CheckWin(string requestJson)
    {
        var request = ParseObject(requestJson) ?? new JsonObject();
        return CheckWinJson(
            ParseTiles(request["hand"] as JsonArray),
            BoolOrNull(request, "selfDraw").GetValueOrDefault(),
            BoolOrNull(request, "hasCalled").GetValueOrDefault(),
            (int)(LongOrNull(request, "ankanCount") ?? 0),
            (int)(LongOrNull(request, "minkanCount") ?? 0),
            ParseMelds(request["melds"]),
            BoolOrNull(request, "firstTurn").GetValueOrDefault(),
            ParseTile(request["winTile"]),
            ParseContext(request["ctx"]));
    }

    /// <summary>
    /// <c>checker.checkWin(hand, selfDraw, hasCalled, ankanCount, minkanCount, melds, kans, firstTurn, discards, winTile, winCtx)</c>
    /// 的逐参同步桥。复杂值（手牌 / 副露 / 杠 / 弃牌 / 和牌张 / 上下文）用 JSON 字符串传，
    /// 其余用原始类型 —— WebView2 同步 host object 对字符串/数字/布尔最可靠。
    /// <c>kans</c> / <c>discards</c> 与 JS 一样只保留参数位，判役本身不使用。
    /// </summary>
    public string CheckerCheckWin(
        string handJson,
        bool selfDraw,
        bool hasCalled,
        double ankanCount,
        double minkanCount,
        string meldsJson,
        string kansJson,
        bool firstTurn,
        string discardsJson,
        string winTileJson,
        string winCtxJson)
    {
        _ = kansJson;
        _ = discardsJson;
        return CheckWinJson(
            ParseTiles(ParseArray(handJson)),
            selfDraw,
            hasCalled,
            (int)ankanCount,
            (int)minkanCount,
            ParseMelds(ParseArray(meldsJson)),
            firstTurn,
            ParseTile(ParseNode(winTileJson)),
            ParseContext(ParseNode(winCtxJson)));
    }

    private string CheckWinJson(
        List<Tile> hand,
        bool selfDraw,
        bool hasCalled,
        int ankanCount,
        int minkanCount,
        List<Meld> melds,
        bool firstTurn,
        Tile? winTile,
        WinContext? context)
    {
        var result = _checker.CheckWin(
            hand,
            selfDraw,
            hasCalled,
            ankanCount,
            minkanCount,
            melds,
            firstTurn,
            winTile,
            context);
        return ResultNode(result).ToJsonString();
    }

    /// <summary><c>conditionFn(id)(hand, groups)</c> 的同步桥；返回 bool。</summary>
    public bool CheckerConditionFn(string id, string handJson, string groupsJson)
    {
        var hand = ParseTiles(ParseArray(handJson));
        var groups = ParseGroups(groupsJson);
        return _checker.ConditionFn(id ?? string.Empty, hand, groups);
    }

    /// <summary><c>checker.optionalEnabled.add(id)</c>。</summary>
    public string OptionalEnabledAdd(string id)
    {
        SetOptional(id, true);
        return "ok";
    }

    /// <summary><c>checker.optionalEnabled.delete(id)</c>。</summary>
    public string OptionalEnabledRemove(string id)
    {
        SetOptional(id, false);
        return "ok";
    }

    /// <summary>当前启用的可选役（JSON 字符串数组），供 JS 侧初始化 Set。</summary>
    public string OptionalEnabledList()
    {
        var array = new JsonArray();
        foreach (var id in _checker.OptionalEnabled.OrderBy(x => x, StringComparer.Ordinal))
        {
            array.Add(id);
        }

        return array.ToJsonString();
    }

    /// <summary>
    /// <c>listTenpai(checker, hand, melds, opts)</c> 的同步桥：手牌 / 副露 / opts 都是 JSON。
    /// 返回与 tenpai.js 同形状的数组：<c>{tile, success, totalFu, noYakuGap}</c>。
    /// </summary>
    public string ListTenpai(string handJson, string meldsJson, string optionsJson)
    {
        var hand = ParseTiles(ParseArray(handJson));
        var melds = ParseMelds(ParseArray(meldsJson));
        var opts = ParseObject(optionsJson) ?? new JsonObject();
        var options = new TenpaiOptions
        {
            SelfDraw = BoolOrNull(opts, "winSelfDraw") ?? true,
            HasCalled = BoolOrNull(opts, "hasCalled").GetValueOrDefault(),
            AnkanCount = (int)(LongOrNull(opts, "ankanCount") ?? 0),
            MinkanCount = (int)(LongOrNull(opts, "minkanCount") ?? 0),
            FirstTurn = BoolOrNull(opts, "firstTurn").GetValueOrDefault(),
            SeatWind = ParseSeatWind(Str(opts, "seatWind")),
            RoundWind = ParseSeatWind(Str(opts, "roundWind")),
            BaseFu = _yaku.BaseFu,
        };

        var entries = TenpaiCalculator.ListTenpai(_checker, hand, melds, options);
        var array = new JsonArray();
        foreach (var entry in entries)
        {
            array.Add(new JsonObject
            {
                ["tile"] = TileText(entry.Tile),
                ["success"] = entry.Success,
                ["totalFu"] = entry.TotalFu,
                ["noYakuGap"] = entry.NoYakuGap,
            });
        }

        return array.ToJsonString();
    }

    /// <summary><c>getAIDiscardIndex(hand, game)</c> 的同步桥；game 参数在 JS 里只作上下文，算法未用。</summary>
    public int GetAIDiscardIndex(string handJson, double random)
    {
        var hand = ParseTiles(ParseArray(handJson));
        return AiPlayer.ChooseDiscardIndex(hand, random);
    }

    private JsonObject ResultNode(WinResult result)
    {
        var yaku = new JsonArray();
        foreach (var item in result.ActiveYaku)
        {
            yaku.Add(new JsonObject
            {
                ["name"] = item.Name,
                ["fu"] = item.Fu,
                ["id"] = item.Id,
                ["isBonus"] = item.IsBonus,
                ["isHigh"] = item.IsHigh,
                ["kaishoku"] = item.Kaishoku,
                ["doubleYakuman"] = item.DoubleYakuman,
                ["ma"] = item.Name == "赐马",
                ["fixedPoints"] = NumberNode(item.FixedPoints),
                ["pointsBonus"] = NumberNode(item.PointsBonus),
            });
        }

        // UI 只读 success / totalFu / activeYaku；points 是给调试与后续消费方的派生值，不影响判役。
        JsonNode? points = result.SpecialPoints is int special
            ? JsonValue.Create(special)
            : result.TotalFu is int fu
                ? JsonValue.Create(_engine.CalcPoints(fu))
                : null;

        return new JsonObject
        {
            ["success"] = result.Success,
            ["reason"] = result.Reason,
            ["totalFu"] = NumberNode(result.TotalFu),
            ["handType"] = result.HandType?.ToWire(),
            ["activeYaku"] = yaku,
            ["yaku"] = yaku.DeepClone(),
            ["points"] = points,
            ["specialPoints"] = NumberNode(result.SpecialPoints),
            ["sevenHonors"] = result.SevenHonors,
        };
    }

    private static SeatWind ParseSeatWind(string value) => value switch
    {
        "south" => SeatWind.South,
        "west" => SeatWind.West,
        "north" => SeatWind.North,
        _ => SeatWind.East,
    };

    private static string TileText(Tile tile) =>
        tile.Suit == Suit.Honor ? tile.Num + "z" : tile.Num + SuitWire(tile.Suit);

    // ══════════════════════════════════════════════════════════════
    // JSON 解析辅助
    // ══════════════════════════════════════════════════════════════

    private static JsonObject? ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonArray? ParseArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json) as JsonArray;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonNode? ParseNode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>conditionFn 的 groups 参数：<c>[{type, tiles:[...]}]</c>；null / [] 保持原样。</summary>
    private static List<Group>? ParseGroups(string? json)
    {
        var array = ParseArray(json);
        if (array is null)
        {
            return null;
        }

        var groups = new List<Group>();
        foreach (var node in array)
        {
            if (node is not JsonObject obj)
            {
                continue;
            }

            var tiles = ParseTiles(obj["tiles"] as JsonArray);
            if (tiles.Count == 0)
            {
                continue;
            }

            var kind = Str(obj, "type") switch
            {
                "sequence" or "chi" => GroupKind.Sequence,
                "pung" or "pon" or "minkan" or "ankan" or "kakan" => GroupKind.Pung,
                "pair" => GroupKind.Pair,
                _ => GroupKind.Other,
            };
            groups.Add(new Group(kind, tiles));
        }

        return groups;
    }

    private static void MergeInto(JsonObject target, JsonObject source)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value?.DeepClone();
        }
    }

    private static string Str(JsonObject? node, string key) => DataStore.NodeToString(node?[key]);

    private static bool? BoolOrNull(JsonObject? node, string key)
    {
        var value = node?[key];
        return value is null ? null : DataStore.IsTruthy(value);
    }

    private static long? LongOrNull(JsonObject? node, string key)
    {
        var value = node?[key];
        if (value is null)
        {
            return null;
        }

        if (value is JsonValue jsonValue && jsonValue.TryGetValue<double>(out var number))
        {
            return (long)number;
        }

        return DataStore.ToInt(value);
    }

    private static List<Tile> ParseTiles(JsonArray? array)
    {
        var tiles = new List<Tile>();
        if (array is null)
        {
            return tiles;
        }

        foreach (var node in array)
        {
            if (ParseTile(node) is { } tile)
            {
                tiles.Add(tile);
            }
        }

        return tiles;
    }

    private static Tile? ParseTile(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonValue value when value.TryGetValue<string>(out var id):
                return Tile.TryParse(id, out var tile) ? tile : null;
            case JsonObject obj:
            {
                string id = Str(obj, "id");
                if (id.Length > 0 && Tile.TryParse(id, out var byId))
                {
                    return byId;
                }

                string suit = Str(obj, "suit");
                if (suit.Length != 1 || !int.TryParse(Str(obj, "num"), out int num))
                {
                    return null;
                }

                return suit[0] switch
                {
                    'm' => new Tile(Suit.Man, num),
                    'p' => new Tile(Suit.Pin, num),
                    's' => new Tile(Suit.Sou, num),
                    'z' => new Tile(Suit.Honor, num),
                    _ => null,
                };
            }

            default:
                return null;
        }
    }

    private static List<Meld> ParseMelds(JsonNode? node)
    {
        var melds = new List<Meld>();
        if (node is not JsonArray array)
        {
            return melds;
        }

        foreach (var item in array)
        {
            if (item is not JsonObject obj)
            {
                continue;
            }

            var tiles = ParseTiles(obj["tiles"] as JsonArray);
            var type = Str(obj, "type");
            if (type.Length == 0 || tiles.Count == 0)
            {
                continue;
            }

            melds.Add(new Meld(type, tiles));
        }

        return melds;
    }

    private static WinContext? ParseContext(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        return new WinContext
        {
            SeaMoon = BoolOrNull(obj, "seaMoon").GetValueOrDefault(),
            HaiDei = BoolOrNull(obj, "haiDei").GetValueOrDefault(),
            RiverFish = BoolOrNull(obj, "riverFish").GetValueOrDefault(),
            LastTileVisible = (int)(LongOrNull(obj, "lastTileVisible") ?? 0),
            SeatWind = NullableStr(obj, "seatWind"),
            RoundWind = NullableStr(obj, "roundWind"),
            WaterDrop = BoolOrNull(obj, "waterDrop").GetValueOrDefault(),
            RinshanGun = BoolOrNull(obj, "rinshanGun").GetValueOrDefault(),
            RinshanFlower = BoolOrNull(obj, "rinshanFlower").GetValueOrDefault(),
            KanRiichi = BoolOrNull(obj, "kanRiichi").GetValueOrDefault(),
            Renhe = BoolOrNull(obj, "renhe").GetValueOrDefault(),
        };
    }

    private static string? NullableStr(JsonObject node, string key)
    {
        var value = node[key];
        return value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) ? text : null;
    }
}
