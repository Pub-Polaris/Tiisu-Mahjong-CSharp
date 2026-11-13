using System;
using System.Collections.Generic;
using Tiisu.Core;

namespace Tiisu.Engine.SelfPlay;

/// <summary>
/// 手牌结构/牌型倾向特征（12 维）。只做结构统计，不判偶发役（如海底、抢杠、胡绝张）。
/// 供自博弈特征与运行时策略网共用，保证 Python/C# 布局一致。
/// </summary>
public static class HandPatternFeatures
{
    public const int Count = 12;

    public static float[] Build(IReadOnlyList<Tile> hand)
    {
        var counts = new int[34];
        foreach (var tile in hand)
        {
            counts[tile.Index]++;
        }

        int pairs = 0, triplets = 0, sequences = 0;
        for (int i = 0; i < 34; i++)
        {
            if (counts[i] >= 2) pairs++;
            if (counts[i] >= 3) triplets++;
        }

        // 连续顺子潜力：同花色 n,n+1,n+2 至少各 1 张，按最小张数计。
        for (int suit = 0; suit < 3; suit++)
        {
            for (int n = 0; n < 7; n++)
            {
                int a = suit * 9 + n;
                int min = Math.Min(counts[a], Math.Min(counts[a + 1], counts[a + 2]));
                sequences += Math.Min(min, 1);
            }
        }

        int[] suitCount = new int[3];
        int honors = 0, terminals = 0;
        for (int i = 0; i < 34; i++)
        {
            if (i < 27) suitCount[i / 9] += counts[i];
            else honors += counts[i];
            if (i < 27 && (i % 9 == 0 || i % 9 == 8)) terminals += counts[i];
        }

        int total = Math.Max(1, hand.Count);
        int maxSuit = Math.Max(suitCount[0], Math.Max(suitCount[1], suitCount[2]));
        int suitsPresent = (suitCount[0] > 0 ? 1 : 0) + (suitCount[1] > 0 ? 1 : 0) + (suitCount[2] > 0 ? 1 : 0);

        // 三色同顺潜力：同一数字在三种花色都能组成顺子的数量。
        int sanshokuDoujun = 0;
        for (int n = 0; n < 7; n++)
        {
            int suits = 0;
            for (int s = 0; s < 3; s++)
            {
                int a = s * 9 + n;
                if (counts[a] > 0 && counts[a + 1] > 0 && counts[a + 2] > 0) suits++;
            }

            if (suits == 3) sanshokuDoujun++;
        }

        // 三色同刻潜力：同一数字在三种花色都有刻子。
        int sanshokuDoukou = 0;
        for (int n = 0; n < 9; n++)
        {
            int suits = 0;
            for (int s = 0; s < 3; s++)
            {
                if (counts[s * 9 + n] >= 3) suits++;
            }

            if (suits == 3) sanshokuDoukou++;
        }

        // 五门齐：万/筒/索/风/箭五类都有。
        bool hasWind = false, hasDragon = false;
        for (int i = 27; i < 31; i++) hasWind |= counts[i] > 0;
        for (int i = 31; i < 34; i++) hasDragon |= counts[i] > 0;
        int fiveGates = suitsPresent + (hasWind ? 1 : 0) + (hasDragon ? 1 : 0);

        int yakuhaiPairs = 0;
        for (int i = 27; i < 34; i++)
        {
            if (counts[i] >= 2) yakuhaiPairs++;
        }

        return new[]
        {
            Math.Min(sequences, 8) / 8f,
            Math.Min(triplets, 4) / 4f,
            Math.Min(pairs, 7) / 7f,
            Math.Min(pairs + triplets, 7) / 7f,             // 对对胡倾向
            maxSuit / (float)total,                          // 清一色/混一色倾向
            suitsPresent / 3f,
            honors / (float)total,
            terminals / (float)total,
            Math.Min(sanshokuDoujun, 3) / 3f,                // 三色同顺
            Math.Min(sanshokuDoukou, 3) / 3f,                // 三色同刻
            fiveGates / 5f,                                  // 五门齐
            Math.Min(yakuhaiPairs, 4) / 4f,
        };
    }
}
