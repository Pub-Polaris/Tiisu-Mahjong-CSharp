namespace Tiisu.Engine;

/// <summary>门风 / 场风。顺序与 JS 的 <c>['east','south','west','north']</c> 一致。</summary>
public enum SeatWind
{
    East = 0,
    South = 1,
    West = 2,
    North = 3,
}

/// <summary>副露类型（语料快照的 <c>m[].t</c>）。</summary>
public enum MeldType
{
    Chi,
    Pon,
    Minkan,
    Ankan,
    Kakan,
}

/// <summary>引擎状态（语料快照的 <c>state</c>）。</summary>
public enum EngineState
{
    Idle,
    Playing,
    AiTurn,
    HumanDraw,
    HumanDiscard,
    HumanRon,
    HumanMeld,
    Ended,
}

/// <summary>枚举 ↔ JS 字符串。语料里的口径都是小写下划线。</summary>
public static class EngineWire
{
    public static string Wire(this SeatWind wind) => wind switch
    {
        SeatWind.East => "east",
        SeatWind.South => "south",
        SeatWind.West => "west",
        _ => "north",
    };

    public static string Wire(this MeldType type) => type switch
    {
        MeldType.Chi => "chi",
        MeldType.Pon => "pon",
        MeldType.Minkan => "minkan",
        MeldType.Ankan => "ankan",
        _ => "kakan",
    };

    public static string Wire(this EngineState state) => state switch
    {
        EngineState.Idle => "idle",
        EngineState.Playing => "playing",
        EngineState.AiTurn => "ai_turn",
        EngineState.HumanDraw => "human_draw",
        EngineState.HumanDiscard => "human_discard",
        EngineState.HumanRon => "human_ron",
        EngineState.HumanMeld => "human_meld",
        _ => "ended",
    };

    public static MeldType ParseMeldType(string? value) => value switch
    {
        "chi" => MeldType.Chi,
        "pon" => MeldType.Pon,
        "minkan" => MeldType.Minkan,
        "ankan" => MeldType.Ankan,
        "kakan" => MeldType.Kakan,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "未知副露类型"),
    };
}
