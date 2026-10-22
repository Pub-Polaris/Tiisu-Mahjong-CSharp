using Tiisu.Engine.Random;

namespace Tiisu.Engine.Ai;

/// <summary>
/// 一局四家 AI 的难度分配。
///
/// <para>
/// 默认权重 <b>30 / 45 / 20 / 5</b>（Simple / Normal / Hard / Shura）；
/// 房主可改比例，或用 <c>lockSeat[4]</c> 按座位锁定（-1 = 跟随随机，0..3 = 固定档）。
/// 人类座位不参与分配（<see cref="HumanSeats"/> 标记，对应档位仅占位）。
/// </para>
/// <para>
/// 随机源用注入 seed 的 mulberry32，同一 seed 得到同一分配。
/// </para>
/// </summary>
public sealed class AiLineup
{
    public static readonly int[] DefaultWeights = { 30, 45, 20, 5 };

    private readonly AiLevel[] _levels;

    private AiLineup(AiLevel[] levels, bool[] humanSeats, int[] weights)
    {
        _levels = levels;
        HumanSeats = humanSeats;
        Weights = weights;
    }

    /// <summary>四家档位（人类座位忽略）。</summary>
    public IReadOnlyList<AiLevel> Levels => _levels;

    /// <summary>四家是否人类座位。</summary>
    public IReadOnlyList<bool> HumanSeats { get; }

    /// <summary>本次实际采用的权重（已做合法性回退）。</summary>
    public IReadOnlyList<int> Weights { get; }

    public AiLevel this[int seat] => _levels[seat];

    /// <summary>按默认权重与给定 seed 分配。</summary>
    public static AiLineup FromWeights(int[] weights, uint seed) =>
        FromWeights(weights, seed, null, null);

    /// <summary>
    /// 按权重 / 锁座 / 人类座位分配四家档位。
    /// </summary>
    /// <param name="weights">四档权重，顺序 Simple/Normal/Hard/Shura；非法时回退默认值。</param>
    /// <param name="seed">随机种子（同一 seed 同一结果）。</param>
    /// <param name="lockSeat">按座位锁定；-1 = 跟随随机，0..3 = 固定档。null = 全部随机。</param>
    /// <param name="humanSeats">人类座位；null = 全是 AI。</param>
    public static AiLineup FromWeights(
        int[] weights,
        uint seed,
        int[]? lockSeat,
        IReadOnlyList<bool>? humanSeats = null)
    {
        int[] effective = NormalizeWeights(weights);
        bool[] humans = NormalizeHumans(humanSeats);
        int[] locks = NormalizeLocks(lockSeat);

        var levels = new AiLevel[4];
        var randomSeats = new List<int>(4);
        for (int seat = 0; seat < 4; seat++)
        {
            if (humans[seat])
            {
                levels[seat] = AiLevel.Simple;
                continue;
            }

            if (locks[seat] is >= 0 and <= 3)
            {
                levels[seat] = (AiLevel)locks[seat];
                continue;
            }

            randomSeats.Add(seat);
        }

        var rng = new Mulberry32Random(seed);
        int totalWeight = effective[0] + effective[1] + effective[2] + effective[3];
        foreach (int seat in randomSeats)
        {
            levels[seat] = Pick(rng.NextDouble(), effective, totalWeight);
        }

        return new AiLineup(levels, humans, effective);
    }

    private static AiLevel Pick(double roll, int[] weights, int totalWeight)
    {
        double target = roll * totalWeight;
        int cumulative = 0;
        for (int i = 0; i < 4; i++)
        {
            cumulative += weights[i];
            if (target < cumulative)
            {
                return (AiLevel)i;
            }
        }

        return AiLevel.Shura;
    }

    private static int[] NormalizeWeights(int[]? weights)
    {
        if (weights is null || weights.Length != 4)
        {
            return (int[])DefaultWeights.Clone();
        }

        var copy = new int[4];
        int sum = 0;
        for (int i = 0; i < 4; i++)
        {
            if (weights[i] < 0)
            {
                return (int[])DefaultWeights.Clone();
            }

            copy[i] = weights[i];
            sum += weights[i];
        }

        return sum > 0 ? copy : (int[])DefaultWeights.Clone();
    }

    private static bool[] NormalizeHumans(IReadOnlyList<bool>? humanSeats)
    {
        var humans = new bool[4];
        if (humanSeats is null)
        {
            return humans;
        }

        for (int i = 0; i < 4 && i < humanSeats.Count; i++)
        {
            humans[i] = humanSeats[i];
        }

        return humans;
    }

    private static int[] NormalizeLocks(int[]? lockSeat)
    {
        var locks = new[] { -1, -1, -1, -1 };
        if (lockSeat is null)
        {
            return locks;
        }

        for (int i = 0; i < 4 && i < lockSeat.Length; i++)
        {
            locks[i] = lockSeat[i] is >= 0 and <= 3 ? lockSeat[i] : -1;
        }

        return locks;
    }
}
