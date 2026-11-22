using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tiisu.Core;
using Tiisu.Engine.Ai;

namespace Tiisu.App.Runtime;

public sealed record DiscardGrade(int StepIndex, int Player, string Tile, int Loss, bool Best, bool Approximate, string BestTile);

public sealed record SeatReport(int Player, int Evaluated, int Best, double AverageLoss);

public sealed record ReplayReport(
    IReadOnlyDictionary<int, DiscardGrade> Grades,
    IReadOnlyList<SeatReport> Seats)
{
    public static readonly ReplayReport Empty = new(
        new Dictionary<int, DiscardGrade>(),
        Array.Empty<SeatReport>());
}

/// <summary>
/// 牌谱复盘：对每一步弃牌做「实际向听 vs 最优向听」的评分。
/// 只读日志，不改引擎；吃碰后没有摸牌的弃牌不评分。
/// </summary>
public static class ReplayAnalysis
{
    public static ReplayReport Analyze(ReplayRound round)
    {
        if (round is null || round.Steps.Count == 0)
        {
            return ReplayReport.Empty;
        }

        var grades = new Dictionary<int, DiscardGrade>();
        var evaluated = new int[4];
        var best = new int[4];
        var lossSum = new int[4];

        for (int stepIndex = 0; stepIndex < round.Steps.Count; stepIndex++)
        {
            ReplayStep step = round.Steps[stepIndex];
            if (!string.Equals(step.Action, "discard", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(step.Tile)
                || string.IsNullOrWhiteSpace(step.Hand))
            {
                continue;
            }

            List<Tile> hand13 = ParseHand(step.Hand);
            List<Tile>? tileList = ParseHand(step.Tile);
            if (tileList.Count != 1)
            {
                continue;
            }

            IReadOnlyList<ReplayMeld> melds = ResolveMelds(step);
            int meldCount = melds.Count;
            int kongCount = melds.Count(m => IsKong(m.Type));

            var hand14 = new List<Tile>(hand13) { tileList[0] };
            if (hand14.Count + 3 * meldCount + kongCount != 14)
            {
                // 吃碰后无摸牌的弃牌，或旧日志缺副露数据。
                continue;
            }

            int? chosen = ShantenOf(hand13, meldCount, kongCount);
            if (chosen is null)
            {
                continue;
            }

            int bestShanten = int.MaxValue;
            string bestTile = step.Tile!;
            foreach (Tile candidate in hand14.Distinct())
            {
                var next = new List<Tile>(hand14);
                next.Remove(candidate);
                int? value = ShantenOf(next, meldCount, kongCount);
                if (value is int v && v < bestShanten)
                {
                    bestShanten = v;
                    bestTile = TileNotation.TileToStr(candidate);
                }
            }

            if (bestShanten == int.MaxValue)
            {
                continue;
            }

            int loss = Math.Max(0, chosen.Value - bestShanten);
            bool isBest = loss == 0;
            bool approximate = step.AllMelds is null && meldCount > 0;
            var grade = new DiscardGrade(stepIndex, step.Player, step.Tile!, loss, isBest, approximate, bestTile);
            grades[stepIndex] = grade;

            if (step.Player is >= 0 and < 4)
            {
                evaluated[step.Player]++;
                lossSum[step.Player] += loss;
                if (isBest)
                {
                    best[step.Player]++;
                }
            }
        }

        var seats = new List<SeatReport>(4);
        for (int player = 0; player < 4; player++)
        {
            seats.Add(new SeatReport(
                player,
                evaluated[player],
                best[player],
                evaluated[player] == 0 ? 0 : (double)lossSum[player] / evaluated[player]));
        }

        return new ReplayReport(grades, seats);
    }

    /// <summary>解析紧凑手牌串（<c>123m456p789s1122z</c>）；空格和未知字符忽略。</summary>
    public static List<Tile> ParseHand(string? compact)
    {
        var result = new List<Tile>();
        if (string.IsNullOrWhiteSpace(compact))
        {
            return result;
        }

        var digits = new List<int>(4);
        foreach (char c in compact)
        {
            if (c is >= '1' and <= '9')
            {
                digits.Add(c - '0');
                continue;
            }

            switch (c)
            {
                case 'm':
                    AddSuit(result, digits, Suit.Man);
                    digits.Clear();
                    break;
                case 'p':
                    AddSuit(result, digits, Suit.Pin);
                    digits.Clear();
                    break;
                case 's':
                    AddSuit(result, digits, Suit.Sou);
                    digits.Clear();
                    break;
                case 'z':
                    AddSuit(result, digits, Suit.Honor);
                    digits.Clear();
                    break;
                case ' ':
                case '\t':
                    break;
                default:
                    digits.Clear();
                    break;
            }
        }

        return result;
    }

    private static void AddSuit(List<Tile> target, List<int> digits, Suit suit)
    {
        foreach (int num in digits)
        {
            if (suit == Suit.Honor ? num is >= 1 and <= 7 : num is >= 1 and <= 9)
            {
                target.Add(new Tile(suit, num));
            }
        }
    }

    private static IReadOnlyList<ReplayMeld> ResolveMelds(ReplayStep step)
    {
        if (step.AllMelds is not null
            && step.Player >= 0
            && step.Player < step.AllMelds.Count)
        {
            return step.AllMelds[step.Player];
        }

        return step.Melds ?? Array.Empty<ReplayMeld>();
    }

    private static int? ShantenOf(IReadOnlyList<Tile> hand, int meldCount, int kongCount)
    {
        try
        {
            return Shanten.Calculate(hand, meldCount, kongCount).Shanten;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsKong(string type) =>
        string.Equals(type, "minkan", StringComparison.Ordinal)
        || string.Equals(type, "ankan", StringComparison.Ordinal)
        || string.Equals(type, "kakan", StringComparison.Ordinal);
}
