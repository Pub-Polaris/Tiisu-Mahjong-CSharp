using Tiisu.Core;
using Tiisu.Engine.Deal;
using Tiisu.Engine.Random;

namespace Tiisu.Engine.SelfPlay;

/// <summary>自走参数。</summary>
public sealed class SelfPlayOptions
{
    /// <summary>场次目标（1 / 4 / 8 / 16…；引擎按 <see cref="MahjongEngine.AdvanceRound"/> 自身规则收口）。</summary>
    public int ModeTarget { get; init; } = 2;

    /// <summary>安全上限：正常由引擎终局（含 modeTarget+16 兜底）收口，这里只防死循环。</summary>
    public int MaxHands { get; init; } = 512;

    /// <summary>
    /// 每步动作后的观察回调（自走引擎实例，含刚发生动作的结果）。
    /// 终局动作（和牌加了荣和张、流局）后也会回调一次，届时 <see cref="MahjongEngine.GameOver"/> 为 true。
    /// </summary>
    public Action<MahjongEngine>? Observer { get; init; }
}

/// <summary>
/// 自走驱动：按 Web 侧 <c>engine.js</c> 的全 AI 顺序把一整场打完：
/// 定庄/切牌/洗牌/配牌 → 摸牌 → 自摸判定 → 暗杠/加杠（含岭上补摸与杠后自摸）→ 出牌 →
/// 特殊流局/拔厄 → 多家荣和 → 杠/碰/吃 → <c>nextTurn</c>；局末 <c>advanceRound</c> 后开下一局，直到终局。
/// <para>
/// 随机源：每次 <see cref="AiPlayer.ChooseDiscardIndex"/> 与 <see cref="AiPlayer.DecideMeld"/> 各取**恰好一次**
/// <see cref="IRandomSource.NextDouble"/>（与决策语料“一个决策点一个 rnd”一致）；自摸 / 杠判定不消耗随机值。
/// 洗牌与骰子也走同一随机源，因此同 seed 的整场事件流完全可复现。
/// </para>
/// <para>
/// 回放语料里由语料直接给出、本驱动不重跑的两条路径：
/// 抢杠（<c>checkRobKan</c>）与推牌流局（九种九牌 / 十三不靠——AI 不会主动推牌）。
/// </para>
/// </summary>
public static class SelfPlayDriver
{
    /// <summary>听牌型奖赏 + 0 赋加计项：JS 里这些项带 <c>isBonus</c>，不并入赋位表。</summary>
    private static readonly HashSet<string> BonusYakuNames = new(StringComparer.Ordinal)
    {
        "嵌张", "边张", "单钓", "双碰", "筋和", "特例加计", "倍满加计",
    };

    /// <summary>同 seed 自走一整场。</summary>
    public static SelfPlayResult Run(uint seed, int modeTarget = 2) =>
        Run(seed, new SelfPlayOptions { ModeTarget = modeTarget });

    /// <summary>同 seed 自走一整场（可带观察回调）。</summary>
    public static SelfPlayResult Run(uint seed, SelfPlayOptions options) =>
        Run(new Mulberry32Random(seed), seed, options);

    /// <summary>指定随机源自走（测试可直接控制随机流）。</summary>
    public static SelfPlayResult Run(IRandomSource randomSource, uint seed, SelfPlayOptions options)
    {
        ArgumentNullException.ThrowIfNull(randomSource);
        ArgumentNullException.ThrowIfNull(options);
        if (options.ModeTarget <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.ModeTarget, "ModeTarget 必须为正");
        }

        // 数据只加载一次：200 场自走不至于反复解析 ike.json。
        var yakuData = YakuData.LoadDefault();
        var points = PointsConfig.LoadDefault();
        var checker = new WinChecker(yakuData);

        var engine = new MahjongEngine(points, checker, yakuData, randomSource);
        engine.SetMode(options.ModeTarget);

        var events = new List<GameEvent>();
        int rounds = 0;
        int winRounds = 0;
        int tsumoWins = 0;
        int ronWins = 0;
        int specialFlows = 0;
        int actions = 0;

        // 自摸均摊的余数归自摸者（engine.js splitPayment）：每次自摸会让总分 +0..2。
        // 因此行牌中分数总和守恒，但跨局会因自摸余数累积；这里按每局开局的实际总和校验。
        int expectedScoreSum = engine.InitialPoints * MahjongEngine.PlayerCount;

        while (!engine.MatchOver && engine.HandsPlayed < options.MaxHands)
        {
            rounds++;
            expectedScoreSum = engine.Scores.Sum();

            RoundDeal deal;
            if (rounds == 1)
            {
                // 定庄只在整场第一局掷骰（engine.js 的 _firstDeal）。
                var dealerDice = TileSetFactory.RollDice(randomSource);
                int dealer = TileSetFactory.DetermineDealer(dealerDice);
                deal = RoundDealFactory.Create(dealer, randomSource);
            }
            else
            {
                deal = RoundDealFactory.Create(engine.DealerIndex, randomSource);
            }

            engine.HandNumber++;
            engine.StartNewRound(deal);
            Step(engine, options, expectedScoreSum);

            var outcome = PlayRound(engine, randomSource, checker, events, options, ref actions, expectedScoreSum);

            if (outcome.Kind is RoundKind.Tsumo or RoundKind.Ron)
            {
                winRounds++;
                tsumoWins += outcome.Kind == RoundKind.Tsumo ? 1 : 0;
                ronWins += outcome.Winners.Count;
            }
            else if (outcome.Kind == RoundKind.SpecialFlow)
            {
                specialFlows++;
            }

            events.Add(new GameEvent
            {
                Type = GameEventTypes.RoundEnd,
                Round = engine.HandNumber,
                Player = outcome.Kind is RoundKind.Tsumo or RoundKind.Ron && outcome.Winners.Count == 1
                    ? outcome.Winners[0]
                    : -1,
                Detail = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0}:scores={1}:honba={2}:dealer={3}",
                    outcome.Kind.ToString().ToLowerInvariant(),
                    string.Join(",", engine.Scores),
                    engine.Honba,
                    engine.DealerIndex),
            });

            if (engine.MatchOver || !engine.AdvanceRound())
            {
                break;
            }
        }

        if (!engine.MatchOver)
        {
            throw new InvalidOperationException(
                $"自走未在 {options.MaxHands} 局内终局（handsPlayed={engine.HandsPlayed}, roundWindIdx={engine.RoundWindIdx}）");
        }

        int drawRounds = rounds - winRounds;
        var eventList = events.AsReadOnly();
        return new SelfPlayResult
        {
            Seed = seed,
            ModeTarget = options.ModeTarget,
            Rounds = rounds,
            HandsPlayed = engine.HandsPlayed,
            WinRounds = winRounds,
            TsumoWins = tsumoWins,
            RonWins = ronWins,
            DrawRounds = drawRounds,
            SpecialFlowRounds = specialFlows,
            FinalScores = engine.Scores.ToArray(),
            FinalRankPoints = engine.RankPoints.ToArray(),
            EventHash = SelfPlayResult.ComputeHash(eventList),
            Events = eventList,
        };
    }

    // ─────────────────────────────────────────────────────────────
    // 一局
    // ─────────────────────────────────────────────────────────────

    private enum RoundKind
    {
        Tsumo,
        Ron,
        Flow,
        SpecialFlow,
    }

    private sealed record RoundOutcome(RoundKind Kind, IReadOnlyList<int> Winners, string? Reason = null);

    private static RoundOutcome PlayRound(
        MahjongEngine engine,
        IRandomSource random,
        WinChecker checker,
        List<GameEvent> events,
        SelfPlayOptions options,
        ref int actions,
        int expectedScoreSum)
    {
        while (!engine.GameOver)
        {
            if (++actions > 200_000)
            {
                throw new InvalidOperationException("自走动作数超过 200000，疑似死循环");
            }

            int pi = engine.TurnIndex;
            var player = engine.Players[pi];

            // ── engine.js aiTurn：摸牌 / 庄家首巡 / 副露后免摸 ──
            bool isDealerFirst = player.IsDealer
                                 && player.Hand.Count == 14
                                 && player.LastDraw is null
                                 && !player.SkipDraw;
            if (player.SkipDraw)
            {
                player.SkipDraw = false; // 副露后免摸，直接出牌
            }
            else if (!isDealerFirst)
            {
                var drawn = engine.DrawFromWall(player);
                if (drawn is null)
                {
                    engine.Flow();
                    AddFlow(events, engine, "牌山已空（荒牌流局）");
                    Step(engine, options, expectedScoreSum);
                    return new RoundOutcome(RoundKind.Flow, Array.Empty<int>());
                }

                events.Add(new GameEvent
                {
                    Type = GameEventTypes.Draw,
                    Round = engine.HandNumber,
                    Player = pi,
                    Tile = drawn.Value.Id,
                    Detail = "wall=" + engine.Wall.Count,
                });
                Step(engine, options, expectedScoreSum);
            }
            else
            {
                player.FirstTurn = false;
            }

            // ── 自摸 → 暗杠/加杠（含杠后自摸）──
            var obs = BuildObservation(engine, pi);
            var tsumo = AiPlayer.CheckTsumo(obs, checker);
            if (tsumo.Success)
            {
                return DeclareTsumo(engine, events, tsumo, options, expectedScoreSum);
            }

            var kan = AiPlayer.DecideKan(obs);
            if (kan is not null)
            {
                bool ok = engine.DoKan(player, kan.Tile, kan.Type, engine.PeekRinshanTile());
                if (!ok)
                {
                    if (!engine.GameOver)
                    {
                        throw new InvalidOperationException($"玩家 {pi} 执行 {kan.Type.Wire()} 失败但未触发流局");
                    }

                    AddFlow(events, engine, "杠后牌山耗尽（荒牌流局）");
                    Step(engine, options, expectedScoreSum);
                    return new RoundOutcome(RoundKind.Flow, Array.Empty<int>());
                }

                events.Add(new GameEvent
                {
                    Type = GameEventTypes.Kan,
                    Round = engine.HandNumber,
                    Player = pi,
                    Tile = kan.Tile.Id,
                    Detail = kan.Type.Wire() + ":rinshan=" + (engine.Rinshan.Count > 0 ? "yes" : "wall"),
                });
                Step(engine, options, expectedScoreSum);

                // 杠后自摸再判一次（engine.js 的 r2）
                var afterKanObs = BuildObservation(engine, pi);
                var afterKan = AiPlayer.CheckTsumo(afterKanObs, checker);
                if (afterKan.Success)
                {
                    return DeclareTsumo(engine, events, afterKan, options, expectedScoreSum);
                }
            }

            // ── 出牌（每个 discard 决策恰好消耗一次随机值）──
            int discardIndex = AiPlayer.ChooseDiscardIndex(player.Hand, random.NextDouble());
            if (discardIndex < 0 || discardIndex >= player.Hand.Count)
            {
                throw new InvalidOperationException($"玩家 {pi} 弃牌下标越界: {discardIndex}（手牌 {player.Hand.Count}）");
            }

            var tile = player.Hand[discardIndex];
            var discarded = engine.Discard(player, tile);
            if (discarded is null)
            {
                throw new InvalidOperationException($"玩家 {pi} 打不出 {tile.Id}");
            }

            events.Add(new GameEvent
            {
                Type = GameEventTypes.Discard,
                Round = engine.HandNumber,
                Player = pi,
                Tile = discarded.Value.Id,
            });
            Step(engine, options, expectedScoreSum);

            // ── afterDiscard：特殊流局/拔厄 → 荣和 → 副露 ──
            if (engine.CheckSpecialFlow())
            {
                AddFlow(events, engine, "四风连打");
                Step(engine, options, expectedScoreSum);
                return new RoundOutcome(RoundKind.SpecialFlow, Array.Empty<int>());
            }

            var ronObs = BuildObservation(engine, -1, pi, discarded);
            var ron = AiPlayer.CheckRonDetailed(ronObs, checker);
            if (ron.Count > 0)
            {
                var winners = new List<RonWinner>(ron.Count);
                foreach (var candidate in ron)
                {
                    var winner = engine.Players[candidate.PlayerIndex];
                    winner.Hand.Add(discarded.Value); // JS checkRon：荣和张留在胜者暗手里
                    winner.Hand.Sort();
                    winners.Add(new RonWinner(candidate.PlayerIndex, ToSettlement(candidate.Result)));
                }

                engine.DeclareMultiRon(winners, pi);

                if (ron.Count >= 3)
                {
                    AddFlow(events, engine, "三家和牌（抢杠/多家和）");
                    Step(engine, options, expectedScoreSum);
                    return new RoundOutcome(RoundKind.SpecialFlow, Array.Empty<int>());
                }

                foreach (var winner in engine.LastRonMulti)
                {
                    events.Add(new GameEvent
                    {
                        Type = GameEventTypes.Win,
                        Round = engine.HandNumber,
                        Player = winner.PlayerIndex,
                        Tile = discarded.Value.Id,
                        Detail = string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "ron:from={0}:fu={1}:points={2}",
                            pi,
                            winner.TotalFu,
                            winner.FinalPoints?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"),
                    });
                }

                Step(engine, options, expectedScoreSum);
                return new RoundOutcome(RoundKind.Ron, engine.LastRonMulti.Select(w => w.PlayerIndex).ToArray());
            }

            // ── 杠/碰/吃（每个 meld 决策恰好消耗一次随机值）──
            var meldObs = BuildObservation(engine, -1, pi, discarded);
            var meld = AiPlayer.DecideMeld(meldObs, random.NextDouble());
            if (meld.Applied)
            {
                engine.ApplyMeld(meld.Target, meld.Type, discarded.Value, pi, meld.ChiTrio, engine.PeekRinshanTile());
                events.Add(new GameEvent
                {
                    Type = GameEventTypes.Meld,
                    Round = engine.HandNumber,
                    Player = meld.Target,
                    Tile = discarded.Value.Id,
                    Detail = meld.Type.Wire() + ":from=" + pi,
                });
                Step(engine, options, expectedScoreSum);

                if (engine.GameOver)
                {
                    AddFlow(events, engine, "杠后牌山耗尽（荒牌流局）");
                    Step(engine, options, expectedScoreSum);
                    return new RoundOutcome(RoundKind.Flow, Array.Empty<int>());
                }

                continue;
            }

            engine.NextTurn();
            Step(engine, options, expectedScoreSum);
            if (engine.GameOver)
            {
                AddFlow(events, engine, "牌山已空（荒牌流局）");
                Step(engine, options, expectedScoreSum);
                return new RoundOutcome(RoundKind.Flow, Array.Empty<int>());
            }
        }

        // while 只在 GameOver 后退出；正常每条路径都已 return。
        throw new InvalidOperationException("自走循环意外结束但未产生局末结果");
    }

    private static RoundOutcome DeclareTsumo(
        MahjongEngine engine,
        List<GameEvent> events,
        WinResult result,
        SelfPlayOptions options,
        int expectedScoreSum)
    {
        int winner = engine.TurnIndex;
        var settlement = ToSettlement(result);
        engine.DeclareWin(winner, true, settlement);

        events.Add(new GameEvent
        {
            Type = GameEventTypes.Win,
            Round = engine.HandNumber,
            Player = winner,
            Tile = engine.CurrentDrawn?.Id,
            Detail = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "tsumo:fu={0}:ma={1}:sevenHonors={2}",
                settlement.TotalFu,
                settlement.ResolvedMaFu,
                settlement.SevenHonors ? "yes" : "no"),
        });
        Step(engine, options, expectedScoreSum);
        return new RoundOutcome(RoundKind.Tsumo, new[] { winner });
    }

    private static void AddFlow(List<GameEvent> events, MahjongEngine engine, string reason) =>
        events.Add(new GameEvent
        {
            Type = GameEventTypes.Flow,
            Round = engine.HandNumber,
            Detail = reason,
        });

    // ─────────────────────────────────────────────────────────────
    // 观察 / 结算 / 不变量
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 把引擎状态投影成 AiPlayer 的决策输入（与 AiGoldenTests 的语料重建逐字段一致）。
    /// <paramref name="playerIndex"/> = -1 表示荣和/副露这类引擎级判定。
    /// </summary>
    public static AiPlayerObservation BuildObservation(
        MahjongEngine engine,
        int playerIndex,
        int discPlayer = -1,
        Tile? discarded = null)
    {
        var obs = new AiPlayerObservation
        {
            PlayerIndex = playerIndex,
            TurnIndex = engine.TurnIndex,
            DiscPlayer = discPlayer,
            Discarded = discarded,
            WallCount = engine.Wall.Count,
            RinshanCount = engine.Rinshan.Count,
            RoundWind = engine.RoundWind,
            DealerIndex = engine.DealerIndex,
            Honba = engine.Honba,
            HandNumber = engine.HandNumber,
            Scores = engine.Scores.ToArray(),
            LastDiscarded = engine.LastDiscarded,
            KanThisTurn = engine.KanThisTurn,
        };

        foreach (var player in engine.Players)
        {
            var view = new AiPlayerView
            {
                SeatWind = player.SeatWind,
                IsDealer = player.IsDealer,
                IsHuman = player.IsHuman,
                FirstTurn = player.FirstTurn,
                AnkanCount = player.AnkanCount,
                MinkanCount = player.MinkanCount,
                LastDraw = player.LastDraw,
                SeaMoon = player.SeaMoon,
                HaiDei = player.HaiDei,
                RinshanDrawn = player.RinshanDrawn,
                FirstTurnTenpai = player.FirstTurnTenpai,
                SkipDraw = player.SkipDraw,
            };

            view.Hand.AddRange(player.Hand);
            view.Discards.AddRange(player.Discards);
            foreach (var meld in player.Melds)
            {
                view.Melds.Add(new Meld(meld.Type, meld.Tiles));
            }

            obs.Players.Add(view);
        }

        return obs;
    }

    /// <summary>把 Core 的判役结果转成引擎结算输入（字段口径与回放驱动的 <c>BuildSettlement</c> 一致）。</summary>
    public static WinSettlement ToSettlement(WinResult result)
    {
        var settlement = new WinSettlement
        {
            TotalFu = result.TotalFu ?? 0,
            SpecialPoints = result.SpecialPoints,
            SevenHonors = result.SevenHonors,
            HandType = result.HandType,
        };

        foreach (var yaku in result.ActiveYaku)
        {
            bool isMa = yaku.Name == "赐马";
            settlement.ActiveYaku.Add(new WinYaku
            {
                Name = yaku.Name,
                Fu = yaku.Fu,
                IsBonus = yaku.IsBonus || BonusYakuNames.Contains(yaku.Name),
                IsMa = isMa,
                Kaishoku = yaku.Kaishoku,
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

    /// <summary>自走每步校验：牌总数 136、行牌内分数总和不变、墙长合法、手牌张数合法。</summary>
    private static void Step(MahjongEngine engine, SelfPlayOptions options, int expectedScoreSum)
    {
        if (!engine.GameOver)
        {
            ValidatePlayState(engine, expectedScoreSum);
        }

        options.Observer?.Invoke(engine);
    }

    private static void ValidatePlayState(MahjongEngine engine, int expectedScoreSum)
    {
        int total = engine.Wall.Count + engine.Rinshan.Count;
        for (int i = 0; i < MahjongEngine.PlayerCount; i++)
        {
            var player = engine.Players[i];
            total += player.Hand.Count + player.Discards.Count;
            foreach (var meld in player.Melds)
            {
                // 261024 起：被鸣的牌会从放铳者牌河移除并进副露，这里不再需要扣重复。
                total += meld.Tiles.Count;
            }
        }

        if (total != MahjongEngine.TotalTiles)
        {
            throw new InvalidOperationException(
                $"牌总数不变量被破坏：{total} != {MahjongEngine.TotalTiles}" +
                $"（第 {engine.HandNumber} 局，玩家 {engine.TurnIndex}）");
        }

        int scoreSum = engine.Scores.Sum();
        if (scoreSum != expectedScoreSum)
        {
            throw new InvalidOperationException(
                $"行牌中分数总和被破坏：{scoreSum} != {expectedScoreSum}（第 {engine.HandNumber} 局）");
        }

        if (engine.Wall.Count is < 0 or > MahjongEngine.TotalTiles - MahjongEngine.DeadWallMaCount)
        {
            throw new InvalidOperationException($"牌墙长度越界：{engine.Wall.Count}");
        }

        for (int i = 0; i < MahjongEngine.PlayerCount; i++)
        {
            var player = engine.Players[i];
            int kong = player.KongCount();
            int totalTiles = player.TotalTiles();
            if (totalTiles != 13 + kong && totalTiles != 14 + kong)
            {
                throw new InvalidOperationException(
                    $"玩家 {i} 手牌张数非法：total={totalTiles}, kong={kong}（第 {engine.HandNumber} 局）");
            }
        }
    }
}
