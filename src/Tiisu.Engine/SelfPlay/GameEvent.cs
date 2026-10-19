namespace Tiisu.Engine.SelfPlay;

/// <summary>事件类型常量（与自走记录口径一致，全部小写）。</summary>
public static class GameEventTypes
{
    public const string Draw = "draw";
    public const string Discard = "discard";
    public const string Meld = "meld";
    public const string Kan = "kan";
    public const string Win = "win";
    public const string Flow = "flow";
    public const string RoundEnd = "round_end";
}

/// <summary>
/// 自走事件流的一条记录。<see cref="GameEventTypes"/> 覆盖
/// draw / discard / meld / kan / win / flow / round_end。
/// <para>
/// <see cref="Round"/> 是本局局号（引擎 <c>HandNumber</c>，从 1 开始）；
/// <see cref="Player"/> 是动作发起者（-1 表示引擎级 / 多家事件）；
/// <see cref="Tile"/> 是牌 id；<see cref="Detail"/> 是补充信息（副露类型、和牌方式、流局理由等）。
/// </para>
/// <see cref="Canonical"/> 生成确定性文本行，<see cref="SelfPlayResult.EventHash"/> 对整串做 SHA256。
/// </summary>
public sealed record GameEvent
{
    public required string Type { get; init; }

    public int Round { get; init; }

    public int Player { get; init; } = -1;

    public string? Tile { get; init; }

    public string? Detail { get; init; }

    /// <summary>规范化的单行文本（哈希输入）。字段用 <c>|</c> 分隔，空值写 <c>-</c>。</summary>
    public string Canonical() => string.Join(
        "|",
        Type,
        Round.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Player.ToString(System.Globalization.CultureInfo.InvariantCulture),
        string.IsNullOrEmpty(Tile) ? "-" : Tile,
        string.IsNullOrEmpty(Detail) ? "-" : Detail);

    public override string ToString() => Canonical();
}
