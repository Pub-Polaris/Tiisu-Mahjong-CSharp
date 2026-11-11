using System;
using System.Collections.Generic;
using Tiisu.Core;

namespace Tiisu.Engine;

/// <summary>
/// Core 判役结果 → 引擎结算输入的唯一转换点（审计 P3-2）。
/// 原先放在 <see cref="SelfPlay.SelfPlayDriver"/>，导致宿主/回放各自复制；现在集中在这里。
/// </summary>
public static class EngineSettlement
{
    /// <summary>听牌型奖赏 + 0 赋加计项：JS 里这些项带 <c>isBonus</c>，不并入赋位表。</summary>
    public static readonly IReadOnlySet<string> BonusYakuNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "嵌张", "边张", "单钓", "双碰", "筋和", "特例加计", "倍满加计",
        };

    /// <summary>把 Core 的判役结果转成引擎结算输入（字段口径与回放驱动的 <c>BuildSettlement</c> 一致）。</summary>
    public static WinSettlement From(WinResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var settlement = new WinSettlement
        {
            TotalFu = result.TotalFu ?? 0,
            SpecialPoints = result.SpecialPoints,
            SevenHonors = result.SevenHonors,
            HandType = result.HandType,
        };

        foreach (var yaku in result.ActiveYaku)
        {
            AddYaku(settlement, yaku.Name, yaku.Fu, yaku.IsBonus || BonusYakuNames.Contains(yaku.Name), isMa: null,
                yaku.Kaishoku, yaku.PointsBonus, yaku.FixedPoints);
        }

        return settlement;
    }

    /// <summary>
    /// 单条役的映射规则（自走与回放共用，避免两份口径漂移）。
    /// isMa / isBonus 传 null 时按名字推断。
    /// </summary>
    public static void AddYaku(
        WinSettlement settlement,
        string name,
        int fu,
        bool? isBonus = null,
        bool? isMa = null,
        bool kaishoku = false,
        int? pointsBonus = null,
        int? fixedPoints = null)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(name);

        bool ma = isMa ?? name == "赐马";
        settlement.ActiveYaku.Add(new WinYaku
        {
            Name = name,
            Fu = fu,
            IsBonus = isBonus ?? BonusYakuNames.Contains(name),
            IsMa = ma,
            Kaishoku = kaishoku,
            PointsBonus = pointsBonus,
            FixedPoints = fixedPoints,
        });

        if (ma)
        {
            settlement.ResolvedMaFu += fu;
        }
    }
}
