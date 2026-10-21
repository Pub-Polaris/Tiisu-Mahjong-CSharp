namespace Tiisu.App.Runtime;

/// <summary>UI → 引擎线程的命令种类。与 Web 侧 <c>game.js</c> / <c>engine.js</c> 的人类操作一一对应。</summary>
public enum GameCommandKind
{
    StartMatch,
    Discard,
    Chi,
    Pon,
    KanAn,
    KanMinkan,
    KanKakan,
    Tsumo,
    Ron,
    Pass,
    Flow,
    NextRound,
    ResetAll,
    ApplySettings,
    Resume,
}

/// <summary>
/// 一条对局命令。只由 UI 线程投递，由 <see cref="GameSession"/> 的后台线程逐条消费；
/// 命令里不携带任何引擎对象，序号 / 组合下标全部是值类型。
/// </summary>
public sealed record GameCommand(
    GameCommandKind Kind,
    int Index = -1,
    int Option = 0,
    int Mode = 0,
    string? Text = null,
    Settings? Settings = null,
    SaveState? Save = null)
{
    public static GameCommand StartMatch(int mode) => new(GameCommandKind.StartMatch, Mode: mode);

    public static GameCommand Discard(int index) => new(GameCommandKind.Discard, Index: index);

    public static GameCommand Chi(int option) => new(GameCommandKind.Chi, Option: option);

    public static GameCommand Pon() => new(GameCommandKind.Pon);

    public static GameCommand KanAn() => new(GameCommandKind.KanAn);

    public static GameCommand KanMinkan() => new(GameCommandKind.KanMinkan);

    public static GameCommand KanKakan() => new(GameCommandKind.KanKakan);

    public static GameCommand Tsumo() => new(GameCommandKind.Tsumo);

    public static GameCommand Ron() => new(GameCommandKind.Ron);

    public static GameCommand Pass() => new(GameCommandKind.Pass);

    public static GameCommand Flow(string? reason = null) => new(GameCommandKind.Flow, Text: reason);

    public static GameCommand NextRound() => new(GameCommandKind.NextRound);

    public static GameCommand ResetAll() => new(GameCommandKind.ResetAll);

    public static GameCommand ApplySettings(Settings settings) =>
        new(GameCommandKind.ApplySettings, Settings: settings);

    public static GameCommand Resume(SaveState save) => new(GameCommandKind.Resume, Save: save);
}
