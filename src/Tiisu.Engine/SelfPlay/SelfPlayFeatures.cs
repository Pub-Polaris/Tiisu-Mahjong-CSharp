using System;
using System.Collections.Generic;
using Tiisu.Core;

namespace Tiisu.Engine.SelfPlay;

/// <summary>
/// 自博弈训练特征：除牌山外的全部场面信息。
/// 布局固定，Python 训练脚本必须逐项一致。
/// <para>
/// 0..33    自家手牌计数
/// 34..169  四家牌河计数（每张 / 4）
/// 170..305 四家副露牌计数（每张 / 4）
/// 306..309 四家副露组数（/ 4）
/// 310..313 四家点数（/ 10000）
/// 314..317 四家点差（相对平均，/ 10000）
/// 318      本场（/ 8）
/// 319..322 场风 one-hot（东/南/西/北）
/// 323..326 座次 one-hot（东/南/西/北）
/// 327..338 牌型结构特征（见 HandPatternFeatures）
/// </para>
/// </summary>
public static class SelfPlayFeatures
{
    public const int Version = 4;
    public const int Count = 327 + HandPatternFeatures.Count;

    public static float[] Build(MahjongEngine engine, int playerIndex)
    {
        var x = new float[Count];
        var me = engine.Players[playerIndex];

        // 0..33 手牌
        foreach (var tile in me.Hand)
        {
            x[tile.Index] += 1f;
        }

        // 34..169 四家牌河
        for (int seat = 0; seat < MahjongEngine.PlayerCount; seat++)
        {
            foreach (var tile in engine.Players[seat].Discards)
            {
                x[34 + seat * 34 + tile.Index] += 0.25f;
            }
        }

        // 170..305 四家副露牌；306..309 副露组数
        for (int seat = 0; seat < MahjongEngine.PlayerCount; seat++)
        {
            var melds = engine.Players[seat].Melds;
            x[306 + seat] = Math.Min(melds.Count, 4) / 4f;
            foreach (var meld in melds)
            {
                foreach (var tile in meld.Tiles)
                {
                    x[170 + seat * 34 + tile.Index] += 0.25f;
                }
            }
        }

        // 310..317 点数与点差
        double avg = 0;
        for (int i = 0; i < MahjongEngine.PlayerCount; i++)
        {
            avg += engine.Scores[i];
        }

        avg /= MahjongEngine.PlayerCount;
        for (int seat = 0; seat < MahjongEngine.PlayerCount; seat++)
        {
            x[310 + seat] = (float)(engine.Scores[seat] / 10000.0);
            x[314 + seat] = (float)((engine.Scores[seat] - avg) / 10000.0);
        }

        // 318 本场
        x[318] = Math.Min(engine.Honba, 8) / 8f;

        // 319..322 场风
        x[319 + (int)engine.RoundWind] = 1f;

        // 323..326 座次
        x[323 + (int)me.SeatWind] = 1f;

        // 327..338 牌型结构特征
        var pattern = HandPatternFeatures.Build(me.Hand);
        Array.Copy(pattern, 0, x, 327, pattern.Length);

        return x;
    }
}
