namespace Tiisu.Engine;

/// <summary>
/// 一组副露。与 JS 的 <c>{type, tiles}</c> 对应；<see cref="Tiles"/> 保持加入顺序
/// （吃牌按点数升序，碰/杠 全是同一张牌），语料快照 <c>m[].x</c> 用的就是这个顺序。
/// 加杠时 <see cref="Type"/> 由 pon 改成 kakan 并追加一张，因此这里是可变的 class 而不是 record。
/// </summary>
public sealed class Meld
{
    public Meld(MeldType type, IEnumerable<Tile> tiles)
    {
        Type = type;
        Tiles = tiles.ToList();
    }

    public MeldType Type { get; set; }

    public List<Tile> Tiles { get; }

    public bool IsKong => Tiles.Count == 4;
}

/// <summary>本局弃牌记录（JS 的 <c>roundDiscards</c>：四风连打判定要看"最近 4 张是谁打的"）。</summary>
public sealed record RoundDiscard(int PlayerIndex, Tile Tile);

/// <summary>一种可行的吃牌组合（JS <c>chiOptions</c> 返回的 <c>{trio}</c>）。</summary>
public sealed record ChiOption(IReadOnlyList<Tile> Trio);

/// <summary>回放开局输入：庄家、跳墩张数（骰子和 ×2）、四家配牌。定庄与配牌由语料给出，引擎不掷骰/不洗牌。</summary>
public sealed record RoundDeal(int DealerIndex, int SkipTiles, IReadOnlyList<IReadOnlyList<Tile>> Hands);

/// <summary>一个荣和者。JS 的 <c>{playerIdx, result, totalFu, finalPoints}</c>。</summary>
public sealed class RonWinner
{
    public RonWinner(int playerIndex, WinSettlement result)
    {
        PlayerIndex = playerIndex;
        Result = result;
    }

    public int PlayerIndex { get; }

    public WinSettlement Result { get; }

    /// <summary>含赐马的总赋（JS <c>w.totalFu</c>）。</summary>
    public int TotalFu { get; set; }

    /// <summary>最终支付点数（JS <c>w.finalPoints</c>）。</summary>
    public int? FinalPoints { get; set; }
}
