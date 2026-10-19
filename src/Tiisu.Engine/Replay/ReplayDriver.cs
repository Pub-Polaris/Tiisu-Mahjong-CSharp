using System.Globalization;
using Tiisu.Core;

namespace Tiisu.Engine.Replay;

/// <summary>
/// 回放驱动：按 <c>g</c> 分成若干场，逐事件执行「同一次动作」，每处理完一个事件就与语料快照逐字段比对。
///
/// 三条约定：
/// <list type="number">
/// <item>每个 <c>g</c> 是一场新比赛（语料里每场的分数都回到 49000，<c>hp=0</c>），因此每场新建引擎 + SetMode。</item>
/// <item>摸到的牌 / 配牌由语料给出，引擎不洗牌不掷骰；墙、岭上、王堆只负责长度。</item>
/// <item>明杠 / 杠的岭上补摸牌语料没单独记事件，从「该事件快照的暗手」与「动作应删掉的牌」之差推出（差值必然是单张 +1）。</item>
/// </list>
/// </summary>
public static class ReplayDriver
{
    /// <summary>听牌型奖赏 + 0 赋加计项：JS 里这些项带 <c>isBonus</c>，不并入赋位表。</summary>
    private static readonly HashSet<string> BonusYakuNames = new(StringComparer.Ordinal)
    {
        "嵌张", "边张", "单钓", "双碰", "筋和", "特例加计", "倍满加计",
    };

    public static ReplayReport Run(ReplayCorpus corpus)
    {
        var report = new ReplayReport(corpus.Events.Count);
        MahjongEngine? engine = null;
        int currentGame = int.MinValue;
        int index = -1;

        foreach (var ev in corpus.Events)
        {
            index++;

            if (ev.Game != currentGame)
            {
                currentGame = ev.Game;
                engine = new MahjongEngine();
                engine.SetMode(corpus.Hands > 0 ? corpus.Hands : 1);
            }

            if (engine is null)
            {
                throw new InvalidOperationException("语料缺少 round_start");
            }

            switch (ev.Type)
            {
                case "round_start":
                    engine.HandNumber++; // JS：advanceRound 成功后 handNumber++，再 startNewRound
                    engine.StartNewRound(new RoundDeal(
                        ev.Snapshot.Dealer,
                        ev.Snapshot.Wang + MahjongEngine.DeadWallMaCount,
                        ParseHands(ev.Snapshot)));
                    break;

                case "draw":
                {
                    var player = GetPlayer(engine, ev.Player);
                    int wallBefore = engine.Wall.Count;
                    var tile = ParseTile(ev.Tile);
                    if (engine.Draw(player, tile) is null)
                    {
                        report.Add(index, ev, "draw", "牌山未空", "牌山已空");
                        break;
                    }

                    // 语料另外记了摸牌前的墙数、海上月/海底月标志；顺手校验，防止墙数悄悄漂移。
                    if (ev.WallBefore is int expectedBefore && expectedBefore != wallBefore)
                    {
                        report.Add(index, ev, "bw", Format(expectedBefore), Format(wallBefore));
                    }

                    if (ev.SeaMoon is bool expectedSea && expectedSea != player.SeaMoon)
                    {
                        report.Add(index, ev, "sea", Format(expectedSea), Format(player.SeaMoon));
                    }

                    if (ev.HaiDei is bool expectedHai && expectedHai != player.HaiDei)
                    {
                        report.Add(index, ev, "hai", Format(expectedHai), Format(player.HaiDei));
                    }

                    break;
                }

                case "discard":
                {
                    var player = GetPlayer(engine, ev.Player);
                    var tile = ParseTile(ev.Tile);
                    if (engine.Discard(player, tile) is null)
                    {
                        report.Add(index, ev, "discard", $"手里有 {tile.Id}", "找不到该牌");
                    }

                    break;
                }

                case "turn":
                    engine.NextTurn();
                    break;

                case "meld":
                    HandleMeld(report, index, engine, ev);
                    break;

                case "kan":
                    HandleKan(report, index, engine, ev);
                    break;

                case "win":
                    HandleWin(engine, ev);
                    break;

                case "multi_ron":
                    HandleMultiRon(report, index, engine, ev);
                    break;

                case "flow":
                    HandleFlow(engine, ev);
                    break;

                case "game_end":
                    engine.AdvanceRound();
                    break;

                default:
                    report.Add(index, ev, "e", "已知事件类型", ev.Type);
                    break;
            }

            Compare(report, index, engine, ev);
            report.MarkProcessed();

            // JS：discard() 返回后才进 afterDiscard（特殊流局/拔厄优先于荣和）。
            // 出牌快照拍在这之前，所以这里放在快照比对之后。
            if (ev.Type == "discard")
            {
                engine.AfterDiscard();
            }
        }

        return report;
    }

    private static void HandleMeld(ReplayReport report, int index, MahjongEngine engine, ReplayEvent ev)
    {
        int playerIndex = GetPlayerIndex(ev.Player);
        var type = EngineWire.ParseMeldType(ev.MeldType);
        var tile = ParseTile(ev.Tile);
        int from = ev.From ?? engine.TurnIndex;
        var trio = ev.Trio?.Select(ParseTile).ToList();

        Tile? rinshanTile = null;
        if (type == MeldType.Minkan)
        {
            // applyMeld 内会删掉手里 3 张同牌再补摸岭上；补摸牌由快照反推
            rinshanTile = InferRinshanTile(report, index, engine, ev, playerIndex, tile, 3);
        }

        engine.ApplyMeld(playerIndex, type, tile, from, trio, rinshanTile);
    }

    private static void HandleKan(ReplayReport report, int index, MahjongEngine engine, ReplayEvent ev)
    {
        int playerIndex = GetPlayerIndex(ev.Player);
        var tile = ParseTile(ev.KanTile);
        var type = ev.KanType == "ankan" ? MeldType.Ankan : MeldType.Kakan;
        int removeCount = type == MeldType.Ankan ? 4 : 1;
        var rinshanTile = InferRinshanTile(report, index, engine, ev, playerIndex, tile, removeCount);
        engine.DoKan(engine.Players[playerIndex], tile, type, rinshanTile);
    }

    private static void HandleWin(MahjongEngine engine, ReplayEvent ev)
    {
        int playerIndex = GetPlayerIndex(ev.Player);
        bool selfDraw = ev.SelfDraw ?? false;
        var settlement = BuildSettlement(ev.Fu ?? 0, ev.Yaku, ev.SpecialPoints);

        if (!selfDraw && engine.LastDiscarded is Tile ronTile)
        {
            // JS checkRon：和牌张留在胜者暗手里
            engine.Players[playerIndex].Hand.Add(ronTile);
            TileUtil.SortHand(engine.Players[playerIndex].Hand);
        }

        engine.DeclareWin(playerIndex, selfDraw, settlement, ev.From);
    }

    private static void HandleMultiRon(ReplayReport report, int index, MahjongEngine engine, ReplayEvent ev)
    {
        int from = ev.From ?? engine.TurnIndex;
        var recorded = ev.Winners ?? new List<ReplayWinner>();
        var winners = new List<RonWinner>(recorded.Count);
        foreach (var winner in recorded)
        {
            winners.Add(new RonWinner(winner.PlayerIndex, BuildSettlement(winner.Result)));
        }

        if (engine.LastDiscarded is Tile ronTile)
        {
            foreach (var winner in winners)
            {
                engine.Players[winner.PlayerIndex].Hand.Add(ronTile);
                TileUtil.SortHand(engine.Players[winner.PlayerIndex].Hand);
            }
        }
        else
        {
            report.Add(index, ev, "multi_ron.ronTile", "存在放铳牌", "不存在");
        }

        engine.DeclareMultiRon(winners, from);

        // 交叉校验：引擎按 y / fu 重算的赋数与点数，必须与语料记录的一致（不以语料点数直接入账）
        for (int i = 0; i < recorded.Count && i < winners.Count; i++)
        {
            if (recorded[i].TotalFu is int expectedFu && winners[i].TotalFu != expectedFu)
            {
                report.Add(index, ev, $"w[{i}].totalFu", Format(expectedFu), Format(winners[i].TotalFu));
            }

            if (recorded[i].FinalPoints is int expectedPoints && winners[i].FinalPoints != expectedPoints)
            {
                report.Add(index, ev, $"w[{i}].finalPoints", Format(expectedPoints), Format(winners[i].FinalPoints));
            }
        }
    }

    private static void HandleFlow(MahjongEngine engine, ReplayEvent ev)
    {
        var why = ev.Why ?? string.Empty;
        if (ev.Player is int playerIndex)
        {
            engine.DeclarePushFlow(why, playerIndex);
        }
        else if (why.Contains("四风连打", StringComparison.Ordinal) || why.Contains("三家和牌", StringComparison.Ordinal))
        {
            engine.DeclareSpecialFlow(why);
        }
        else
        {
            engine.Flow();
        }
    }

    /// <summary>
    /// 从快照反推岭上补摸牌：动作会先删掉 <paramref name="removeCount"/> 张 <paramref name="removeTile"/>，
    /// 之后手里多出来的那张就是补摸牌。正常情形差值恰好是「单张 +1」。
    /// </summary>
    private static Tile InferRinshanTile(
        ReplayReport report,
        int index,
        MahjongEngine engine,
        ReplayEvent ev,
        int playerIndex,
        Tile removeTile,
        int removeCount)
    {
        var expected = TileUtil.Counts(engine.Players[playerIndex].Hand);
        expected[removeTile.Index] -= removeCount;

        var actual = new int[TileUtil.Count];
        if (playerIndex < ev.Snapshot.Players.Count)
        {
            foreach (var id in ev.Snapshot.Players[playerIndex].Hand)
            {
                if (Tile.TryParse(id, out var tile))
                {
                    actual[tile.Index]++;
                }
            }
        }

        int found = -1;
        bool ok = true;
        for (int i = 0; i < TileUtil.Count; i++)
        {
            int diff = actual[i] - expected[i];
            if (diff == 1 && found < 0)
            {
                found = i;
            }
            else if (diff != 0)
            {
                ok = false;
            }
        }

        if (!ok || found < 0)
        {
            report.Add(index, ev, $"p{playerIndex}.rinshan", "岭上补摸牌可由快照推出", "快照暗手差不是单张 +1");
            return new Tile(Suit.Honor, 9); // 占位，让后续差异继续暴露而不是中断回放
        }

        return Tile.FromIndex(found);
    }

    private static List<IReadOnlyList<Tile>> ParseHands(ReplaySnapshot snapshot)
    {
        var hands = new List<IReadOnlyList<Tile>>(MahjongEngine.PlayerCount);
        foreach (var player in snapshot.Players)
        {
            hands.Add(player.Hand.Select(ParseTile).ToList());
        }

        return hands;
    }

    private static WinSettlement BuildSettlement(int fu, List<string>? yakuStrings, int? specialPoints)
    {
        var settlement = new WinSettlement { TotalFu = fu, SpecialPoints = specialPoints };
        if (yakuStrings is null)
        {
            return settlement;
        }

        foreach (var entry in yakuStrings)
        {
            int separator = entry.LastIndexOf(':');
            string name = separator < 0 ? entry : entry[..separator];
            int value = 0;
            if (separator >= 0)
            {
                int.TryParse(entry[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            }

            bool isMa = name == "赐马";
            settlement.ActiveYaku.Add(new WinYaku
            {
                Name = name,
                Fu = value,
                IsBonus = BonusYakuNames.Contains(name),
                IsMa = isMa,
            });

            if (isMa)
            {
                settlement.ResolvedMaFu += value;
            }
        }

        return settlement;
    }

    private static WinSettlement BuildSettlement(ReplayWinResult result)
    {
        var settlement = new WinSettlement
        {
            TotalFu = result.TotalFu ?? 0,
            SpecialPoints = result.SpecialPoints,
            SevenHonors = result.SevenHonors,
            HandType = ParseHandKind(result.HandType),
        };

        foreach (var yaku in result.ActiveYaku)
        {
            bool isMa = yaku.Ma ?? yaku.Name == "赐马";
            settlement.ActiveYaku.Add(new WinYaku
            {
                Name = yaku.Name,
                Fu = yaku.Fu,
                IsBonus = yaku.IsBonus ?? BonusYakuNames.Contains(yaku.Name),
                IsMa = isMa,
                Kaishoku = yaku.Kaishoku ?? false,
                PointsBonus = yaku.PointsBonus,
                FixedPoints = yaku.FixedPoints,
            });

            if (isMa)
            {
                settlement.ResolvedMaFu += yaku.Fu;
            }
        }

        return settlement;
    }

    private static HandKind? ParseHandKind(string? handType) => handType switch
    {
        "standard_hand" => HandKind.StandardHand,
        "seven_pairs" => HandKind.SevenPairs,
        "highyaku" => HandKind.HighYaku,
        _ => null,
    };

    private static void Compare(ReplayReport report, int index, MahjongEngine engine, ReplayEvent ev)
    {
        var snapshot = ev.Snapshot;
        if (snapshot.Players.Count != MahjongEngine.PlayerCount || snapshot.Scores.Count != MahjongEngine.PlayerCount)
        {
            report.Add(index, ev, "s", "四家快照", $"p={snapshot.Players.Count} scores={snapshot.Scores.Count}");
            return;
        }

        Diff(report, index, ev, "rw", snapshot.RoundWindIdx, engine.RoundWindIdx);
        Diff(report, index, ev, "dealer", snapshot.Dealer, engine.DealerIndex);
        Diff(report, index, ev, "hn", snapshot.HandNumber, engine.HandNumber);
        Diff(report, index, ev, "hp", snapshot.HandsPlayed, engine.HandsPlayed);
        Diff(report, index, ev, "turn", snapshot.Turn, engine.TurnIndex);
        Diff(report, index, ev, "wall", snapshot.Wall, engine.Wall.Count);
        Diff(report, index, ev, "rinshan", snapshot.Rinshan, engine.Rinshan.Count);
        Diff(report, index, ev, "wang", snapshot.Wang, engine.WangDui.Count);
        Diff(report, index, ev, "honba", snapshot.Honba, engine.Honba);
        for (int i = 0; i < MahjongEngine.PlayerCount; i++)
        {
            Diff(report, index, ev, $"scores[{i}]", snapshot.Scores[i], engine.Scores[i]);
        }

        Diff(report, index, ev, "state", snapshot.State, engine.State.Wire());

        for (int i = 0; i < MahjongEngine.PlayerCount; i++)
        {
            var expected = snapshot.Players[i];
            var actual = engine.Players[i];
            Diff(report, index, ev, $"p{i}.h", CanonicalHand(expected.Hand), CanonicalHand(actual.Hand.Select(t => t.Id)));
            Diff(report, index, ev, $"p{i}.m", FormatSnapshotMelds(expected.Melds), FormatMelds(actual.Melds));
            Diff(report, index, ev, $"p{i}.d", string.Join(" ", expected.Discards), string.Join(" ", actual.Discards.Select(t => t.Id)));
            Diff(report, index, ev, $"p{i}.sw", expected.SeatWind, actual.SeatWind.Wire());
            Diff(report, index, ev, $"p{i}.dl", expected.Dealer, actual.IsDealer);
        }
    }

    private static void Diff(ReplayReport report, int index, ReplayEvent ev, string field, object? expected, object? actual)
    {
        string expectedText = Format(expected);
        string actualText = Format(actual);
        if (!string.Equals(expectedText, actualText, StringComparison.Ordinal))
        {
            report.Add(index, ev, field, expectedText, actualText);
        }
    }

    private static string Format(object? value) => value switch
    {
        null => "<null>",
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string CanonicalHand(IEnumerable<string> ids)
    {
        var tiles = new List<Tile>();
        foreach (var id in ids)
        {
            if (!Tile.TryParse(id, out var tile))
            {
                return string.Join(" ", ids.OrderBy(x => x, StringComparer.Ordinal));
            }

            tiles.Add(tile);
        }

        tiles.Sort();
        return string.Join(" ", tiles.Select(t => t.Id));
    }

    private static string CanonicalHand(IEnumerable<Tile> tiles)
    {
        var sorted = tiles.ToList();
        sorted.Sort();
        return string.Join(" ", sorted.Select(t => t.Id));
    }

    private static string FormatMelds(IEnumerable<Meld> melds) =>
        string.Join(" | ", melds.Select(m => $"{m.Type.Wire()}({string.Join(",", m.Tiles.Select(t => t.Id))})"));

    private static string FormatSnapshotMelds(IEnumerable<ReplayMeldSnapshot> melds) =>
        string.Join(" | ", melds.Select(m => $"{m.Type}({string.Join(",", m.Tiles)})"));

    private static Player GetPlayer(MahjongEngine engine, int? playerIndex) =>
        engine.Players[GetPlayerIndex(playerIndex)];

    private static int GetPlayerIndex(int? playerIndex) =>
        playerIndex is int index && index is >= 0 and < MahjongEngine.PlayerCount
            ? index
            : throw new InvalidOperationException($"非法玩家序号: {playerIndex}");

    private static Tile ParseTile(string? id) =>
        Tile.TryParse(id, out var tile) ? tile : throw new InvalidOperationException($"非法牌 id: {id}");
}
