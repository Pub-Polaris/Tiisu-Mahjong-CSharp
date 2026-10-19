using Tiisu.Core;

namespace Tiisu.Engine;

/// <summary>
/// 一条成立的役 / 加计项，字段与 JS 的 <c>activeYaku[]</c> 对应。
/// <see cref="IsMa"/> 对应 JS 的 <c>ma: true</c>（赐马）；<see cref="IsBonus"/> 对应 <c>isBonus</c>（听牌型奖赏）。
/// 两者都不并入赋位表，而是按 700 点/个直接相加。
/// </summary>
public sealed class WinYaku
{
    public required string Name { get; init; }

    public int Fu { get; set; }

    public bool IsBonus { get; set; }

    public bool IsMa { get; set; }

    public bool Kaishoku { get; set; }

    public int? PointsBonus { get; set; }

    /// <summary>固定点数役（人和 7000 / 三国将副露 7000）。</summary>
    public int? FixedPoints { get; set; }
}

/// <summary>
/// 和牌结果（JS <c>checkWin</c> 返回的那个 result）。回放模式下由语料的 <c>y</c> / <c>fu</c> 还原；
/// <see cref="TotalFu"/> 是**不含赐马**的赋数（与 JS <c>result.totalFu</c> 一致），赐马单独记在
/// <see cref="ResolvedMaFu"/>（回放时语料已结算；活局由 <c>resolveMa</c> 现算）。
/// </summary>
public sealed class WinSettlement
{
    public int TotalFu { get; set; }

    public int ResolvedMaFu { get; set; }

    public List<WinYaku> ActiveYaku { get; } = new();

    public int? SpecialPoints { get; set; }

    public HandKind? HandType { get; set; }

    public bool SevenHonors { get; set; }
}
