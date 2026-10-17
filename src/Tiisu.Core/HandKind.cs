namespace Tiisu.Core;

/// <summary>和牌手牌类型。WireName 与 JS/golden 语料中的字符串一致。</summary>
public enum HandKind
{
    StandardHand,
    SevenPairs,
    HighYaku,
}

public static class HandKindExtensions
{
    public static string ToWire(this HandKind kind) => kind switch
    {
        HandKind.StandardHand => "standard_hand",
        HandKind.SevenPairs => "seven_pairs",
        HandKind.HighYaku => "highyaku",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
