namespace Tiisu.Core;

/// <summary>
/// 和牌上下文（对应 JS <c>winCtx</c>）。字段名与 golden 语料中的 ctx 一致。
/// </summary>
public sealed class WinContext
{
    public bool SeaMoon { get; init; }

    public bool HaiDei { get; init; }

    public bool RiverFish { get; init; }

    public int LastTileVisible { get; init; }

    public string? SeatWind { get; init; }

    public string? RoundWind { get; init; }

    public bool WaterDrop { get; init; }

    public bool RinshanGun { get; init; }

    public bool RinshanFlower { get; init; }

    public bool KanRiichi { get; init; }

    public bool Renhe { get; init; }
}

/// <summary>一条成立的役 / 加计项。字段与 golden 的 result.yaku[] 对应。</summary>
public sealed class YakuResult
{
    public required string Name { get; init; }

    public int Fu { get; set; }

    public string? Id { get; init; }

    public bool IsBonus { get; init; }

    public bool IsHigh { get; init; }

    public bool Kaishoku { get; init; }

    public bool DoubleYakuman { get; init; }

    public int? PointsBonus { get; init; }

    /// <summary>固定点数（人和 7000 / 三国将副露 7000）；由 specialPoints 使用。</summary>
    public int? FixedPoints { get; set; }

    public static YakuResult Normal(YakuDefinition definition) => new()
    {
        Name = definition.Name,
        Fu = definition.Fu,
        Id = definition.Id,
        Kaishoku = definition.Kaishoku,
        FixedPoints = definition.FixedPoints,
    };

    public static YakuResult High(YakuDefinition definition) => new()
    {
        Name = definition.Name,
        Fu = definition.Fu,
        Id = definition.Id,
        IsHigh = true,
        DoubleYakuman = definition.DoubleYakuman,
        PointsBonus = definition.PointsBonus,
    };
}

/// <summary>checkWin 的返回值。失败时 TotalFu 可能为 null（not_formed）或带值（no_yaku）。</summary>
public sealed class WinResult
{
    public bool Success { get; init; }

    public string? Reason { get; init; }

    public int? TotalFu { get; init; }

    public HandKind? HandType { get; init; }

    public IReadOnlyList<YakuResult> ActiveYaku { get; init; } = Array.Empty<YakuResult>();

    public int? SpecialPoints { get; init; }

    public bool SevenHonors { get; init; }
}
