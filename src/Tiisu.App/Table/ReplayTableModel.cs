using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tiisu.App.Table;
using Tiisu.Core;
using Tiisu.Localization;

namespace Tiisu.App.Runtime;

/// <summary>把一条牌谱步骤投影成牌桌渲染用的 <see cref="TableViewModel"/>。</summary>
public static class ReplayTableModel
{
    private static readonly string[] WindNames = { "东", "南", "西", "北" };

    public static TableViewModel Build(ReplayRound round, ReplayStep step, int viewerSeat)
    {
        viewerSeat = Math.Clamp(viewerSeat, 0, 3);
        int actor = step.Turn ?? step.Player;
        var seats = new List<SeatView>(4);
        for (int display = 0; display < 4; display++)
        {
            int absolute = (viewerSeat + display) % 4;
            ReplayHeader? header = round.Headers.FirstOrDefault(h => h.Player == absolute);
            string handText = HandText(step, absolute);
            var hand = new List<TileView>();
            foreach (Tile tile in ReplayAnalysis.ParseHand(handText))
            {
                hand.Add(new TileView(TileNotation.AtlasId(tile)));
            }

            var melds = new List<MeldView>();
            foreach (ReplayMeld meld in MeldsFor(step, absolute))
            {
                melds.Add(new MeldView
                {
                    Type = meld.Type,
                    Tiles = meld.Tiles.Select(id => new TileView(id)).ToArray(),
                    ClaimedIndex = meld.ClaimedIndex,
                });
            }

            var discards = new List<TileView>();
            if (step.Rivers.Count > absolute)
            {
                foreach (string id in step.Rivers[absolute].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    discards.Add(new TileView(id));
                }
            }

            int handCount = hand.Count;
            if (step.HandCounts is { } counts && absolute < counts.Count)
            {
                handCount = counts[absolute];
            }
            else if (handCount == 0 && absolute != step.Player)
            {
                handCount = 13;
            }

            seats.Add(new SeatView
            {
                Index = display,
                AbsoluteIndex = absolute,
                Name = header is null
                    ? L.T("app.ui.replay.playerFallback", ("index", absolute))
                    : string.IsNullOrWhiteSpace(header.DisplayName) ? header.Name : header.DisplayName!,
                Wind = header?.Wind ?? WindNames[absolute],
                Score = step.Scores is { } scores && absolute < scores.Count ? scores[absolute] : 25000,
                IsDealer = header?.Dealer ?? false,
                IsHuman = header?.Human ?? absolute == 0,
                IsTurn = absolute == actor,
                HandCount = handCount,
                Hand = hand,
                Melds = melds,
                Discards = discards,
                LastDiscard = discards.Count > 0 ? discards[^1] : null,
            });
        }

        int turn = (actor - viewerSeat + 4) % 4;
        return new TableViewModel
        {
            RoundLabel = L.T("app.ui.replay.roundLabel",
                ("wind", WindNames[Math.Clamp(round.RoundWind, 0, 3)]),
                ("round", round.Round)),
            WallCount = step.Wall,
            RinshanCount = 0,
            Honba = round.Honba,
            TurnIndex = turn,
            ModeTarget = 1,
            RevealHands = true,
            MatchOver = false,
            Seats = seats,
            Actions = HumanAction.None,
            Phase = "round_end",
            SelfSeat = 0,
            Message = string.Empty,
        };
    }

    private static string HandText(ReplayStep step, int absolute)
    {
        if (step.AllHands is { } hands && absolute < hands.Count)
        {
            string? text = hands[absolute];
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text!;
            }
        }

        return absolute == step.Player ? step.Hand ?? string.Empty : string.Empty;
    }

    private static IReadOnlyList<ReplayMeld> MeldsFor(ReplayStep step, int absolute)
    {
        if (step.AllMelds is { } melds && absolute < melds.Count)
        {
            return melds[absolute];
        }

        return absolute == step.Player ? step.Melds ?? Array.Empty<ReplayMeld>() : Array.Empty<ReplayMeld>();
    }
}
