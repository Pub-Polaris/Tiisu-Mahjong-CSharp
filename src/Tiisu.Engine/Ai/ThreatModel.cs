namespace Tiisu.Engine.Ai;

/// <summary>
/// 威胁模型（B 期）的只读输入。字段只允许来自**公开信息**：
/// 四家牌河、四家副露 / 杠、剩余牌数、分数、本场、自家座位。
/// 不包含别家手牌，也不包含牌山顺序。
/// </summary>
/// <param name="AllDiscards">四家牌河（含自家），顺序无关。</param>
/// <param name="AllMelds">四家副露（含自家），按座位顺序 0..3；可空列表。</param>
/// <param name="SeatsRemainingTiles">各家自摸剩余张数（通常都等于 <paramref name="WallCount"/>）。</param>
/// <param name="WallCount">活牌山剩余张数。</param>
/// <param name="Scores">四家分数。</param>
/// <param name="Honba">本场数。</param>
/// <param name="SelfIndex">自家座位号 0..3。</param>
public sealed record ThreatInput(
    IReadOnlyList<IReadOnlyList<Tile>> AllDiscards,
    IReadOnlyList<IReadOnlyList<Meld>> AllMelds,
    IReadOnlyList<int> SeatsRemainingTiles,
    int WallCount,
    int[] Scores,
    int Honba,
    int SelfIndex)
{
    /// <summary>
    /// 自家暗手（可选）。只用于「壁」计数（自己手里的同牌也算可见），
    /// 不读取别家手牌。缺省时壁只按牌河 + 副露计。
    /// </summary>
    public IReadOnlyList<Tile>? SelfHand { get; init; }
}

/// <summary>
/// 防守威胁模型（B 期）：把公开信息折算成「这张牌打出去有多危险」（0..1）
/// 与「当前局面有多紧张」（0..1）。
///
/// <para>
/// 危险度从低到高的顺序是：现物 &lt; 壁 &lt; 筋 &lt; 无信息的幺九 &lt; 无信息的中张 &lt; 未现字牌；
/// 再叠加对手副露数、残局、本场 / 分数落后等场况。所有结果 clamp 到 [0, 1]。
/// </para>
/// <para>
/// 纯函数、无随机源：同一输入永远得到同一输出，因此不破坏「同 seed 同决策」。
/// </para>
/// </summary>
public static class ThreatModel
{
    private const int StandardKindCount = 34;

    /// <summary>现物的基础危险度（完全安全是不可能的：仍有形式上的风险）。</summary>
    private const double GenbutsuDanger = 0.02;

    /// <summary>每张牌的放铳危险度（0..1，越大越危险）。</summary>
    public static double Danger(Tile tile, ThreatInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        int visible = CountVisible(tile, input);
        if (IsInAnyRiver(tile, input.AllDiscards))
        {
            // 现物：任意一家牌河出现过 → 最低危险。不再叠加场况，避免「现物也危险」的自相矛盾。
            return GenbutsuDanger;
        }

        double danger = BaseDanger(tile, visible);

        if (IsSuji(tile, input.AllDiscards))
        {
            danger *= 0.5;
        }

        if (visible >= 4)
        {
            // 壁：四张全部可见（牌河 + 副露 + 自家手牌）→ 不可能再被这张牌放铳。
            danger *= 0.2;
        }

        danger += OpenMeldPressure(input);
        danger += LateGamePressure(input.WallCount);
        danger += ContextPressure(input);

        return Clamp01(danger);
    }

    /// <summary>
    /// 0..1：四家整体「有人可能听牌」的紧张度。供失误模型 / 思考深度复用。
    /// 由对手副露、全牌种平均危险度、残局程度、分数落后线性合成。
    /// </summary>
    public static double Tension(ThreatInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        double meldTension = Clamp01(OpponentMeldCount(input) / 6.0);
        double boardTension = AverageDanger(input);
        double lateTension = Clamp01(1.0 - input.WallCount / 70.0);
        double behindTension = ScoreBehind(input);

        return Clamp01(
            meldTension * 0.40
            + boardTension * 0.25
            + lateTension * 0.20
            + behindTension * 0.15);
    }

    // ─────────────────────────────────────────────────────────────
    // 基础危险度
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 无信息时的基础危险：中张 3–7 最高，幺九最低，字牌最高（字牌只能靠现物 / 壁排除）。
    /// 同牌可见张数越多越安全。
    /// </summary>
    private static double BaseDanger(Tile tile, int visible)
    {
        if (tile.IsHonor)
        {
            return Math.Max(0.12, 0.72 - 0.15 * visible);
        }

        double baseDanger = tile.Num switch
        {
            1 or 9 => 0.32,
            2 or 8 => 0.44,
            _ => 0.60,
        };

        return Math.Max(0.10, baseDanger - 0.05 * visible);
    }

    /// <summary>
    /// 筋：同花色相隔 3 的牌在任意牌河出现（如 2 万 / 8 万 之于 5 万）。
    /// 只做「相隔三」的近似，不额外推断对手手牌；中间张的完整安全逻辑留给后续迭代。
    /// </summary>
    private static bool IsSuji(Tile tile, IReadOnlyList<IReadOnlyList<Tile>> rivers)
    {
        if (tile.IsHonor)
        {
            return false;
        }

        return PartnerDiscarded(tile, tile.Num - 3, rivers)
            || PartnerDiscarded(tile, tile.Num + 3, rivers);
    }

    private static bool PartnerDiscarded(
        Tile tile,
        int partnerNum,
        IReadOnlyList<IReadOnlyList<Tile>> rivers)
    {
        if (partnerNum is < 1 or > 9)
        {
            return false;
        }

        return IsInAnyRiver(new Tile(tile.Suit, partnerNum), rivers);
    }

    private static bool IsInAnyRiver(Tile tile, IReadOnlyList<IReadOnlyList<Tile>> rivers)
    {
        foreach (var river in rivers)
        {
            foreach (var discarded in river)
            {
                if (discarded == tile)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>可见张数：四家牌河 + 四家副露 / 杠 + 自家手牌（可选）。</summary>
    private static int CountVisible(Tile tile, ThreatInput input)
    {
        int visible = 0;

        foreach (var river in input.AllDiscards)
        {
            foreach (var discarded in river)
            {
                if (discarded == tile)
                {
                    visible++;
                }
            }
        }

        foreach (var melds in input.AllMelds)
        {
            foreach (var meld in melds)
            {
                foreach (var meldTile in meld.Tiles)
                {
                    if (meldTile == tile)
                    {
                        visible++;
                    }
                }
            }
        }

        if (input.SelfHand is not null)
        {
            foreach (var handTile in input.SelfHand)
            {
                if (handTile == tile)
                {
                    visible++;
                }
            }
        }

        return visible;
    }

    // ─────────────────────────────────────────────────────────────
    // 场况
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 对手副露压力：别家 2 组以上副露 → 整体上浮，3 组 → 更高。
    /// 只看副露组数（公开信息），不推断具体听牌形。
    /// </summary>
    private static double OpenMeldPressure(ThreatInput input)
    {
        double pressure = 0.0;
        for (int seat = 0; seat < input.AllMelds.Count; seat++)
        {
            if (seat == input.SelfIndex)
            {
                continue;
            }

            int count = input.AllMelds[seat].Count;
            if (count >= 3)
            {
                pressure += 0.16;
            }
            else if (count >= 2)
            {
                pressure += 0.09;
            }
        }

        return pressure;
    }

    private static int OpponentMeldCount(ThreatInput input)
    {
        int total = 0;
        for (int seat = 0; seat < input.AllMelds.Count; seat++)
        {
            if (seat == input.SelfIndex)
            {
                continue;
            }

            total += Math.Min(4, input.AllMelds[seat].Count);
        }

        return total;
    }

    /// <summary>残局逐级上浮：30 / 15 / 8 张三个台阶。</summary>
    private static double LateGamePressure(int wallCount)
    {
        if (wallCount <= 8)
        {
            return 0.24;
        }

        if (wallCount <= 15)
        {
            return 0.18;
        }

        if (wallCount <= 30)
        {
            return 0.10;
        }

        return 0.0;
    }

    /// <summary>本场高 / 大比分落后时略上浮（象征性权重，不主导决策）。</summary>
    private static double ContextPressure(ThreatInput input)
    {
        double pressure = 0.0;

        if (input.Honba >= 3)
        {
            pressure += 0.05;
        }
        else if (input.Honba > 0)
        {
            pressure += 0.01 * input.Honba;
        }

        double behind = ScoreBehind(input);
        if (behind >= 1.0)
        {
            pressure += 0.03;
        }
        else if (behind > 0)
        {
            pressure += 0.015;
        }

        return pressure;
    }

    /// <summary>分数落后度 0..1（落后 20000 点视为拉满）。</summary>
    private static double ScoreBehind(ThreatInput input)
    {
        if (input.Scores is not { Length: >= 4 } || input.SelfIndex is < 0 or > 3)
        {
            return 0.0;
        }

        int own = input.Scores[input.SelfIndex];
        int leader = input.Scores[0];
        for (int i = 1; i < 4; i++)
        {
            if (input.Scores[i] > leader)
            {
                leader = input.Scores[i];
            }
        }

        if (leader <= own)
        {
            return 0.0;
        }

        return Clamp01((leader - own) / 20000.0);
    }

    /// <summary>全 34 种牌的平均危险度，作为「牌桌整体危险」的代理量。</summary>
    private static double AverageDanger(ThreatInput input)
    {
        double total = 0.0;
        for (int i = 0; i < StandardKindCount; i++)
        {
            total += Danger(Tile.FromIndex(i), input);
        }

        return total / StandardKindCount;
    }

    private static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : value;
}
