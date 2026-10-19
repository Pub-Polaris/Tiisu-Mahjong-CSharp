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
    /// <c>checker.checkWin(...)</c> 的同步桥。入参是 JSON：
    /// <c>{ hand:[...], selfDraw, hasCalled, ankanCount, minkanCount, melds:[...], firstTurn, winTile, ctx }</c>。
    /// </summary>
    public string CheckWin(string requestJson)
    {
        var request = ParseObject(requestJson) ?? new JsonObject();
        var hand = ParseTiles(request["hand"] as JsonArray);
        var melds = ParseMelds(request["melds"]);
        var winTile = ParseTile(request["winTile"]);
        var context = ParseContext(request["ctx"]);

        var result = _checker.CheckWin(
            hand,
            selfDraw: BoolOrNull(request, "selfDraw").GetValueOrDefault(),
            hasCalled: BoolOrNull(request, "hasCalled").GetValueOrDefault(),
            ankanCount: (int)(LongOrNull(request, "ankanCount") ?? 0),
            minkanCount: (int)(LongOrNull(request, "minkanCount") ?? 0),
            melds: melds,
            firstTurn: BoolOrNull(request, "firstTurn").GetValueOrDefault(),
            winTile: winTile,
            context: context);

        return ResultNode(result).ToJsonString();
    }

    private static JsonObject ResultNode(WinResult result)
    {
        var yaku = new JsonArray();
        foreach (var item in result.ActiveYaku)
        {
            yaku.Add(new JsonObject
            {
                ["name"] = item.Name,
                ["fu"] = item.Fu,
                ["isBonus"] = item.IsBonus,
                ["ma"] = item.Name == "赐马",
                ["fixedPoints"] = NumberNode(item.FixedPoints),
                ["pointsBonus"] = NumberNode(item.PointsBonus),
            });
        }

        return new JsonObject
        {
            ["success"] = result.Success,
            ["reason"] = result.Reason,
            ["totalFu"] = NumberNode(result.TotalFu),
            ["handType"] = result.HandType?.ToWire(),
            ["activeYaku"] = yaku,
            ["specialPoints"] = NumberNode(result.SpecialPoints),
            ["sevenHonors"] = result.SevenHonors,
        };
    }

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
