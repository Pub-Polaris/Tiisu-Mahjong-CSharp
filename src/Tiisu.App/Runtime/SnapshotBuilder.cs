using System;
using System.Collections.Generic;
using System.Text;
using Tiisu.App.Table;
using Tiisu.Core;
using Tiisu.Engine;

namespace Tiisu.App.Runtime;

/// <summary>
/// 把 <see cref="MahjongEngine"/> 的当前状态投影成 UI 用的不可变 <see cref="TableViewModel"/>。
/// UI 线程只碰这个快照，绝不触碰引擎对象。
/// <para>牌 id 用牌面图集的「数字在前」记号（1m / 5z）；引擎内部的 <see cref="Tile.Id"/> 是 m1 / z5。</para>
/// </summary>
public static class SnapshotBuilder
{
    private static readonly string[] WindNames = { "东", "南", "西", "北" };

    /// <summary>暗杠在对局中翻开的牌位：第 2、4 张（0 基索引）。</summary>
    private static readonly int[] AnkanFaceUpIndices = { 1, 3 };

    public static TableViewModel Build(
        MahjongEngine engine,
        string phase,
        HumanAction actions,
        IReadOnlyList<string>? actionLabels,
        string? message,
        Settings? settings,
        IReadOnlyList<string>? aiLevels = null)
    {
        ArgumentNullException.ThrowIfNull(engine);

        var seats = new List<SeatView>(MahjongEngine.PlayerCount);
        for (int i = 0; i < MahjongEngine.PlayerCount; i++)
        {
            var player = engine.Players[i];
            bool self = player.IsHuman;
            bool reveal = engine.RevealHands || self;

            var hand = new List<TileView>(player.Hand.Count);
            if (reveal)
            {
                foreach (var tile in player.Hand)
                {
                    hand.Add(new TileView(TileNotation.AtlasId(tile)));
                }
            }

            var melds = new List<MeldView>(player.Melds.Count);
            bool afterWin = phase is "round_end" or "match_over" || engine.RevealHands;
            foreach (var meld in player.Melds)
            {
                var tiles = new List<TileView>(meld.Tiles.Count);
                foreach (var tile in meld.Tiles)
                {
                    tiles.Add(new TileView(TileNotation.AtlasId(tile)));
                }

                bool ankan = meld.Type == MeldType.Ankan;
                melds.Add(new MeldView
                {
                    Type = meld.Type.Wire(),
                    Tiles = tiles,

                    // 暗杠：对局中只翻第 2、4 张（索引 1、3）；结算态全部翻开。
                    RevealedTiles = ankan && !afterWin ? AnkanFaceUpIndices : null,
                    RevealedAfterWin = ankan && afterWin,
                });
            }

            var discards = new List<TileView>(player.Discards.Count);
            foreach (var tile in player.Discards)
            {
                discards.Add(new TileView(TileNotation.AtlasId(tile)));
            }

            TileView? lastDiscard = null;
            for (int k = engine.RoundDiscards.Count - 1; k >= 0; k--)
            {
                if (engine.RoundDiscards[k].PlayerIndex == i)
                {
                    lastDiscard = new TileView(TileNotation.AtlasId(engine.RoundDiscards[k].Tile));
                    break;
                }
            }

            seats.Add(new SeatView
            {
                Index = i,
                Name = player.Name,
                Wind = WindNames[(int)player.SeatWind % 4],
                Score = engine.Scores[i],
                IsDealer = player.IsDealer,
                IsHuman = player.IsHuman,
                IsTurn = engine.TurnIndex == i && engine.State is not (EngineState.Idle or EngineState.Ended),
                IsCalled = player.IsCalled,
                Noten = i < engine.FlowTenpai.Count && !engine.FlowTenpai[i],
                HandCount = player.Hand.Count,
                Hand = hand,
                Melds = melds,
                Discards = discards,
                LastDiscard = lastDiscard,
            });
        }

        bool revealHands = engine.RevealHands;
        var rinshan = new List<TileView>(engine.Rinshan.Count);
        foreach (var tile in engine.Rinshan)
        {
            if (tile is not Tile value)
            {
                continue;
            }

            bool show = revealHands || (settings?.ShowMa ?? false);
            rinshan.Add(new TileView(TileNotation.AtlasId(value), Hidden: !show));
        }

        bool hasAnkan = false;
        bool hasKakan = false;
        for (int i = 0; i < MahjongEngine.PlayerCount; i++)
        {
            var player = engine.Players[i];
            if (!player.IsHuman)
            {
                continue;
            }

            hasAnkan = AiPlayer.CanAnkan(player.Hand) is not null;
            if (!hasAnkan)
            {
                var view = new AiPlayerView();
                view.Hand.AddRange(player.Hand);
                foreach (var meld in player.Melds)
                {
                    view.Melds.Add(meld);
                }

                hasKakan = AiPlayer.CanKakan(view) is not null;
            }

            break;
        }

        // 待听牌提示：真听牌就常显（不再限定 human_discard 阶段）。
        var tenpai = new List<string>();
        int selfIndex = -1;
        for (int i = 0; i < MahjongEngine.PlayerCount; i++)
        {
            if (engine.Players[i].IsHuman)
            {
                selfIndex = i;
                break;
            }
        }

        bool selfCalled = false;
        bool selfBanned = false;
        bool selfMisfire = false;
        if (selfIndex >= 0)
        {
            var self = engine.Players[selfIndex];
            selfCalled = self.IsCalled;
            selfBanned = engine.IsWinBanned(self);
            tenpai.AddRange(TenpaiTileIds(engine, self));

            // 【错和可能】：能报听（或已报听），但所有待牌的最高赋都不到起和线。
            if (engine.CanDeclareCall(self) || self.IsCalled)
            {
                int maxFu = MaxWaitFu(engine, self);
                selfMisfire = maxFu > 0 && maxFu < engine.BaseFu;
            }
        }

        return new TableViewModel
        {
            RoundLabel = engine.RoundLabel() + " / " + engine.ModeTarget + "局",
            WallCount = engine.Wall.Count,
            RinshanCount = engine.Rinshan.Count,
            Honba = engine.Honba,

            // 场赏 = 700 × 连庄数，和牌者取走；与连庄奖励同额、不重复加（字段名沿用 Kyotaku）。
            Kyotaku = 700 * engine.Honba,
            TurnIndex = engine.TurnIndex,
            ModeTarget = engine.ModeTarget,
            RevealHands = revealHands,
            MatchOver = engine.MatchOver,
            Rinshan = rinshan,
            Actions = actions,
            HasAnkan = hasAnkan,
            HasKakan = hasKakan,
            ActionLabels = actionLabels ?? Array.Empty<string>(),
            Message = message ?? string.Empty,
            Phase = phase,
            Seats = seats,
            TenpaiTiles = tenpai,
            IsCalled = selfCalled,
            CallWouldMisfire = selfMisfire,
            WinBanned = selfBanned,
            AiLevels = aiLevels ?? Array.Empty<string>(),
        };
    }

    /// <summary>听牌张里最高的赋数（牌形形成才算；用于判断报听后报和是否必然错和）。</summary>
    private static int MaxWaitFu(MahjongEngine engine, Player player)
    {
        int max = 0;
        try
        {
            var hand = engine.TenpaiHandOf(player);
            foreach (var entry in engine.ListTenpaiForHand(
                         hand, player.Melds, player.AnkanCount, player.MinkanCount, player.SeatWind, player.FirstTurn))
            {
                if (entry.Formed)
                {
                    max = Math.Max(max, entry.TotalFu);
                }
            }
        }
        catch
        {
            // 判定失败不影响主流程
        }

        return max;
    }

    /// <summary>
    /// 待牌提示：对「13 张形」手牌列可和牌（14 张形时按摸切口径去掉本次摸到的牌）。
    /// 供快照与报听提示共用。
    /// <para>
    /// 未报听时只显示「真能和的待牌」（赋达起和线）；报听后显示**牌形听牌张**
    /// （与锁手 / 报听可用性同一口径，含赋不足起和线的待牌），否则低赋手报听后提示会是空的。
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> TenpaiTileIds(MahjongEngine engine, Player player)
    {
        var list = new List<string>();
        try
        {
            var hand = engine.TenpaiHandOf(player);
            var entries = engine.ListTenpaiForHand(
                hand, player.Melds, player.AnkanCount, player.MinkanCount, player.SeatWind, player.FirstTurn);
            bool shape = player.IsCalled;
            foreach (var entry in entries)
            {
                if (shape ? entry.TotalFu > 0 : entry.Success)
                {
                    list.Add(TileNotation.AtlasId(entry.Tile));
                }
            }
        }
        catch
        {
            // 听牌提示失败不影响主流程
        }

        return list;
    }
}

/// <summary>
/// 牌面记号（与 Web 侧 <c>notation.js</c> 同口径）：单张 <c>1m</c>、手牌 <c>123m456p789s1122z</c>。
/// 日志与快照共用，保证 ASCII。
/// </summary>
public static class TileNotation
{
    public static string SuitChar(Suit suit) => suit switch
    {
        Suit.Man => "m",
        Suit.Pin => "p",
        Suit.Sou => "s",
        _ => "z",
    };

    /// <summary>单张牌：数字在前（<c>1m</c> / <c>5z</c>），与牌面图集 id 一致。</summary>
    public static string TileToStr(Tile tile) =>
        tile.Num.ToString(System.Globalization.CultureInfo.InvariantCulture) + SuitChar(tile.Suit);

    /// <summary>牌面图集 id（同 <see cref="TileToStr"/>）。</summary>
    public static string AtlasId(Tile tile) => TileToStr(tile);

    /// <summary>手牌 → <c>123m456p789s1122z</c>（按花色分组，组内升序）。</summary>
    public static string HandToStr(IReadOnlyList<Tile> hand)
    {
        if (hand is null || hand.Count == 0)
        {
            return string.Empty;
        }

        var sorted = new List<Tile>(hand);
        sorted.Sort();

        var builder = new StringBuilder(sorted.Count + 4);
        int i = 0;
        while (i < sorted.Count)
        {
            var suit = sorted[i].Suit;
            int j = i;
            while (j < sorted.Count && sorted[j].Suit == suit)
            {
                builder.Append(sorted[j].Num.ToString(System.Globalization.CultureInfo.InvariantCulture));
                j++;
            }

            builder.Append(SuitChar(suit));
            i = j;
        }

        return builder.ToString();
    }
}
