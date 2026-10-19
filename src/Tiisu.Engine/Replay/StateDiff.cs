namespace Tiisu.Engine.Replay;

/// <summary>一条状态差异：第几个事件的哪个字段，期望 / 实际各是什么。</summary>
public sealed record StateDiff(int EventIndex, int Game, string EventType, string Field, string Expected, string Actual)
{
    public override string ToString() =>
        $"#{EventIndex} g{Game} {EventType} {Field}: 期望[{Expected}] 实际[{Actual}]";
}

/// <summary>回放结果：处理了多少事件、差在哪。</summary>
public sealed class ReplayReport
{
    private readonly List<StateDiff> _diffs = new();

    public ReplayReport(int eventCount)
    {
        EventCount = eventCount;
    }

    /// <summary>语料里的事件总数。</summary>
    public int EventCount { get; }

    /// <summary>实际处理完的事件数（正常应等于 <see cref="EventCount"/>）。</summary>
    public int Processed { get; private set; }

    public IReadOnlyList<StateDiff> Diffs => _diffs;

    public bool Clean => _diffs.Count == 0;

    public void MarkProcessed() => Processed++;

    public void Add(int eventIndex, ReplayEvent ev, string field, string expected, string actual) =>
        _diffs.Add(new StateDiff(eventIndex, ev.Game, ev.Type, field, expected, actual));

    /// <summary>给测试失败信息用：前 <paramref name="max"/> 条差异逐行列出。</summary>
    public string Describe(int max = 20)
    {
        var lines = new List<string>
        {
            $"回放事件 {EventCount} 个（已处理 {Processed} 个），状态差异 {_diffs.Count} 条。",
        };

        foreach (var diff in _diffs.Take(max))
        {
            lines.Add(diff.ToString());
        }

        if (_diffs.Count > max)
        {
            lines.Add($"……其余 {_diffs.Count - max} 条省略");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
