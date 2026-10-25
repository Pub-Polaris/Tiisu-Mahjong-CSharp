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

    /// <summary>被吃/碰/明杠的那张牌在 <see cref="Tiles"/> 里的下标（-1 = 暗杠或加杠，没有「取牌」）。</summary>
    public int ClaimedIndex { get; set; } = -1;

    public bool IsKong => Tiles.Count == 4;
}

/// <summary>本局弃牌记录（JS 的 <c>roundDiscards</c>：四风连打判定要看"最近 4 张是谁打的"）。</summary>
public sealed record RoundDiscard(int PlayerIndex, Tile Tile);

/// <summary>一种可行的吃牌组合（JS <c>chiOptions</c> 返回的 <c>{trio}</c>）。</summary>
public sealed record ChiOption(IReadOnlyList<Tile> Trio);

/// <summary>
/// 开局输入：庄家、跳墩张数（骰子和 ×2）、四家配牌。
/// <para>
/// <see cref="WallOrder"/> 为空 = 回放模式：牌墙/岭上/王堆只按长度放 null 占位，摸到的牌由语料给出。
/// 非空 = 自走模式：它是洗牌后的**完整 136 张牌序（setup 顺序，即 <c>createDeck()</c> 洗牌后的数组）**；
/// 引擎据此切出岭上 4 张、王堆（skip−4）与活牌山，并反转成 pop 顺序。自走时配牌也由这份牌序的前 53 张
/// （从活牌山末尾 pop）发出，所以 <see cref="Hands"/> 必须与之一致（<c>RoundDealFactory</c> 保证）。
/// </para>
/// </summary>
public sealed record RoundDeal(
    int DealerIndex,
    int SkipTiles,
    IReadOnlyList<IReadOnlyList<Tile>> Hands,
    IReadOnlyList<Tile>? WallOrder = null);

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
