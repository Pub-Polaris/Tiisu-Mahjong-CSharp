namespace Tiisu.Engine.Random;

/// <summary>
/// 牌山工具：136 张标准牌、Fisher–Yates 洗牌、掷骰、定庄、切牌（跳墩 / 岭上 / 王堆 / 活牌山）。
/// 规则口径与 Web 侧 <c>tiles.js</c> 的 <c>createDeck</c> / <c>shuffle</c> 和 <c>engine.js</c> 的
/// <c>rollDice</c> / <c>determineDealer</c> / <c>setupWallAndCut</c> 一致。
/// </summary>
public static class TileSetFactory
{
    public const int DeckSize = 136;

    /// <summary>岭上（马）张数。</summary>
    public const int DeadWallMaCount = 4;

    /// <summary>配牌张数：3 圈 × 4 家 × 4 张 + 每家 1 张 + 庄家 1 张 = 53。</summary>
    public const int DealTileCount = 53;

    /// <summary>
    /// <c>createDeck()</c>：万/饼/索 1-9 ×4，字牌 东南西北白发中 ×4，共 136。
    /// 顺序即 JS 的 push 顺序（万 → 饼 → 索 → 字）。
    /// </summary>
    public static List<Tile> CreateDeck()
    {
        var deck = new List<Tile>(DeckSize);
        for (int suit = 0; suit < 3; suit++)
        {
            for (int num = 1; num <= 9; num++)
            {
                for (int copy = 0; copy < 4; copy++)
                {
                    deck.Add(new Tile((Suit)suit, num));
                }
            }
        }

        for (int num = 1; num <= 7; num++)
        {
            for (int copy = 0; copy < 4; copy++)
            {
                deck.Add(new Tile(Suit.Honor, num));
            }
        }

        return deck;
    }

    /// <summary><c>shuffle(arr)</c>：i 从 length−1 递减到 1，j = floor(rnd × (i+1))，交换。</summary>
    public static void Shuffle(IList<Tile> deck, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(random);

        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = (int)Math.Floor(random.NextDouble() * (i + 1));
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
    }

    /// <summary><c>rollDice()</c>：两颗骰子，各 1..6，返回顺序为 [骰1, 骰2]。</summary>
    public static int[] RollDice(IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return new[]
        {
            1 + (int)Math.Floor(random.NextDouble() * 6),
            1 + (int)Math.Floor(random.NextDouble() * 6),
        };
    }

    /// <summary>
    /// <c>determineDealer(dice)</c>：点数和 &lt;7 顺时针、=7 掷骰者本人、&gt;7 逆时针；掷骰者固定为 0 号。
    /// 返回庄家序号（0..3）。
    /// </summary>
    public static int DetermineDealer(IReadOnlyList<int> dice)
    {
        if (dice.Count != 2)
        {
            throw new ArgumentException("定庄必须是两颗骰子", nameof(dice));
        }

        int sum = dice[0] + dice[1];
        const int roller = 0;
        if (sum == 7)
        {
            return roller;
        }

        return sum < 7
            ? (roller + sum) % MahjongEngine.PlayerCount
            : ((roller - (sum % MahjongEngine.PlayerCount)) + MahjongEngine.PlayerCount) % MahjongEngine.PlayerCount;
    }

    /// <summary>
    /// 牌山切分结果。字段顺序/含义照 <c>setupWallAndCut</c>：
    /// <see cref="Rinshan"/> 是从庄家侧开始的摸取顺序（shift）；<see cref="WangDui"/> 是王堆；
    /// <see cref="Wall"/> 是「活牌山 → 王堆」再反转后的数组（pop 从末尾取，故先摸活牌山）。
    /// </summary>
    public sealed record WallLayout(
        IReadOnlyList<Tile> Rinshan,
        IReadOnlyList<Tile> WangDui,
        IReadOnlyList<Tile> Wall);

    /// <summary>
    /// 由洗牌后的完整牌序切出岭上 / 王堆 / 活牌山。<paramref name="skipTiles"/> = 2 × 骰子和。
    /// 跳墩区 = 牌序前 skipTiles 张：靠近活牌山的 4 张为岭上，其余为王堆。
    /// </summary>
    public static WallLayout Cut(IReadOnlyList<Tile> deck, int skipTiles)
    {
        ArgumentNullException.ThrowIfNull(deck);
        if (deck.Count != DeckSize)
        {
            throw new ArgumentException($"牌序必须是 {DeckSize} 张，实际 {deck.Count}", nameof(deck));
        }

        int skip = Math.Clamp(skipTiles, 0, DeckSize);
        int wangCount = Math.Max(0, skip - DeadWallMaCount);

        var rinshan = new List<Tile>(DeadWallMaCount);
        // JS: dead.slice(dead.length - maCount).reverse()；shift() 取庄家侧那张。
        for (int i = 0; i < DeadWallMaCount; i++)
        {
            int index = skip - 1 - i;
            if (index >= 0)
            {
                rinshan.Add(deck[index]);
            }
        }

        var wang = new List<Tile>(wangCount);
        for (int i = 0; i < wangCount; i++)
        {
            wang.Add(deck[i]);
        }

        // drawOrder = live ++ wangDui；wall = reverse(drawOrder)，pop 即先摸活牌山。
        var wall = new List<Tile>(DeckSize - DeadWallMaCount);
        for (int i = wangCount - 1; i >= 0; i--)
        {
            wall.Add(wang[i]);
        }

        for (int i = DeckSize - 1; i >= skip; i--)
        {
            wall.Add(deck[i]);
        }

        return new WallLayout(rinshan, wang, wall);
    }

    /// <summary>
    /// <c>deal()</c>：从 <paramref name="wall"/> 的末尾 pop 53 张（3 圈 ×4 家 ×4 + 每家 1 + 庄家 1），
    /// 返回四家配牌（已按手牌顺序排序）。<paramref name="wall"/> 会被原地修改（剩余即开局牌山）。
    /// </summary>
    public static List<IReadOnlyList<Tile>> DealFromWall(IList<Tile> wall, int dealerIndex)
    {
        ArgumentNullException.ThrowIfNull(wall);
        if (dealerIndex is < 0 or >= MahjongEngine.PlayerCount)
        {
            throw new ArgumentOutOfRangeException(nameof(dealerIndex));
        }

        if (wall.Count < DealTileCount)
        {
            throw new InvalidOperationException($"牌山不足配牌张数：{wall.Count} < {DealTileCount}");
        }

        var hands = new List<List<Tile>>(MahjongEngine.PlayerCount);
        for (int i = 0; i < MahjongEngine.PlayerCount; i++)
        {
            hands.Add(new List<Tile>(14));
        }

        for (int round = 0; round < 3; round++)
        {
            for (int pi = 0; pi < MahjongEngine.PlayerCount; pi++)
            {
                for (int i = 0; i < 4; i++)
                {
                    hands[pi].Add(Pop(wall));
                }
            }
        }

        for (int pi = 0; pi < MahjongEngine.PlayerCount; pi++)
        {
            hands[pi].Add(Pop(wall));
        }

        hands[dealerIndex].Add(Pop(wall));

        var result = new List<IReadOnlyList<Tile>>(MahjongEngine.PlayerCount);
        foreach (var hand in hands)
        {
            hand.Sort();
            result.Add(hand);
        }

        return result;
    }

    private static Tile Pop(IList<Tile> wall)
    {
        var tile = wall[^1];
        wall.RemoveAt(wall.Count - 1);
        return tile;
    }
}
