using System.Collections.Generic;

namespace Tiisu.App.Runtime;

/// <summary>结算面板里一条役 / 加计项的标记（决定颜色与后缀）。</summary>
public enum SettlementYakuFlag
{
    /// <summary>普通役。</summary>
    Normal,

    /// <summary>可选役（settings.json 的 optionalYaku 打开时才成立）。</summary>
    Optional,

    /// <summary>表外赋（听牌型奖赏，500 点档）：不进赋位表，每赋另计 700。</summary>
    Tableless,

    /// <summary>赐马：同样不进表，每赋另计 700。</summary>
    Ma,

    /// <summary>固定点数役（人和 / 三国将副露）：直接取固定点数。</summary>
    FixedPoints,
}

/// <summary>
/// 一局的结算明细（UI 用）。所有数值都在引擎完成结算后用引擎公开 API 现算，
/// 口径与 <see cref="Tiisu.Engine.MahjongEngine.DeclareWin"/> / <c>DeclareMultiRon</c> 完全一致。
/// </summary>
public sealed record SettlementDetail
{
    /// <summary>和牌者（玩家下标）。</summary>
    public int WinnerIndex { get; init; }

    /// <summary>自摸（true）/ 荣和（false）。</summary>
    public bool SelfDraw { get; init; }

    /// <summary>荣和放铳者；自摸 / 流局为 null。</summary>
    public int? PayerIndex { get; init; }

    /// <summary>役种列表（名称 / 赋数 / 标记）。</summary>
    public IReadOnlyList<(string Name, int Fu, SettlementYakuFlag Flag)> Yaku { get; init; } =
        System.Array.Empty<(string, int, SettlementYakuFlag)>();

    /// <summary>含赐马的总赋。</summary>
    public int TotalFu { get; init; }

    /// <summary>赐马赋（含在 <see cref="TotalFu"/> 里）。</summary>
    public int MaFu { get; init; }

    /// <summary>查表用的赋数（总赋 − 表外赋）。</summary>
    public int TableSafeFu { get; init; }

    /// <summary>查表基数（固定点数役 / 特殊点数役时为对应值）。</summary>
    public int BasePoints { get; init; }

    /// <summary>表外赋 × 700（固定点数役不叠加，为 0）。</summary>
    public int TablelessBonus { get; init; }

    /// <summary>点数加项（如南北通 +7000）。</summary>
    public int PointsBonus { get; init; }

    /// <summary>子家点数（庄家加成之前）。</summary>
    public int SubPoints { get; init; }

    /// <summary>是否套用了庄家 ×1.5。</summary>
    public bool DealerMultiplierApplied { get; init; }

    /// <summary>本场数（结算时、加本场之前的计数）。</summary>
    public int HonbaCount { get; init; }

    /// <summary>本场加项（700 × N）。</summary>
    public int HonbaBonus { get; init; }

    /// <summary>最终点数（支付总额）。</summary>
    public int FinalPoints { get; init; }

    /// <summary>自摸时每家应付（荣和时 0）。</summary>
    public int PayEach { get; init; }

    /// <summary>自摸余数（归自摸者）。</summary>
    public int PayRemainder { get; init; }

    /// <summary>支付行文案，例：<c>自摸 · 每家 12000 + 余数 0</c> / <c>荣和 · 放铳者付 24000</c>。</summary>
    public string PaymentText { get; init; } = string.Empty;
}
