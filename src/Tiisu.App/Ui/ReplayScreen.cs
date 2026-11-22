using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Tiisu.App.Rendering;
using Tiisu.App.Runtime;
using Tiisu.App.Table;
using Tiisu.Localization;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>
/// 牌谱回看：牌桌回放 + 步进 + 每打向听评分。
/// 读取当前档案目录下的 roundlog_*.jsonl 最后一局。
/// </summary>
public sealed class ReplayScreen : Screen
{
    private const float TableSize = 660f;
    private const float TableScale = TableSize / 1400f;

    private readonly Action _back;
    private readonly IReadOnlyList<ReplayRound>? _injectedRounds;
    private readonly TableSceneSkia _scene = new();
    private readonly TableLayoutSpec _tableLayout = new();

    private IReadOnlyList<ReplayRound> _rounds = Array.Empty<ReplayRound>();
    private ReplayRound? _round;
    private ReplayReport _report = ReplayReport.Empty;
    private int _viewerSeat;
    private int _roundIndex;
    private int _index;
    private TableViewModel? _table;
    private int _tableStep = -1;
    private int _tableRound = -1;

    private RectF _prev;
    private RectF _next;
    private RectF _prevRound;
    private RectF _nextRound;
    private RectF _backButton;
    private bool _prevHover;
    private bool _nextHover;
    private bool _prevRoundHover;
    private bool _nextRoundHover;
    private bool _backHover;
    private readonly List<(int Step, RectF Rect)> _stepRects = new();

    private static readonly Color Gold = Color.Rgb(242, 201, 76);
    private static readonly Color Text = Color.Rgb(222, 230, 220);
    private static readonly Color Muted = Color.Rgb(150, 168, 158);
    private static readonly Color Panel = Color.Rgb(18, 30, 34);
    private static readonly Color PanelBorder = Color.Rgb(42, 62, 62);
    private static readonly Color Good = Color.Rgb(126, 196, 132);
    private static readonly Color Warn = Color.Rgb(222, 176, 96);
    private static readonly Color Bad = Color.Rgb(208, 120, 110);

    public ReplayScreen(Action back, IReadOnlyList<ReplayRound>? rounds = null) : base("replay")
    {
        _back = back;
        _injectedRounds = rounds;
        YogaTableLayout.Apply(_tableLayout);
        CustomLayoutStore.Load(_tableLayout);
        Reload();
    }

    /// <summary>重新读取最新牌谱；进入回放页时调用，避免显示启动时缓存。</summary>
    public void Reload()
    {
        if (_injectedRounds is not null)
        {
            _rounds = _injectedRounds;
        }
        else
        {
            var loaded = RoundLogReader.LoadLatestRounds();
            _rounds = loaded?.Rounds ?? Array.Empty<ReplayRound>();
        }

        _roundIndex = Math.Max(0, _rounds.Count - 1);
        _round = _rounds.Count > 0 ? _rounds[_roundIndex] : null;
        _index = 0;
        RebuildReport();
        _table = null;
    }

    public override void OnLanguageChanged() => Reload();

    /// <summary>DevTools / 测试用：直接跳到某一步。</summary>
    public void ShowStep(int index)
    {
        if (_round is not null && _round.Steps.Count > 0)
        {
            _index = Math.Clamp(index, 0, _round.Steps.Count - 1);
            _table = null;
        }
    }

    public override void Draw(ICanvas c, double time)
    {
        c.FillLinearGradient(new RectF(0, 0, Width, Height), 0,
            Color.Rgb(18, 26, 30), Color.Rgb(8, 12, 16));

        c.DrawText(L.T("app.ui.replay.title"), new RectF(24, 14, 300, 36), Gold,
            new TextStyle(24, Bold: true, Center: false));
        c.DrawText(_round is null
                ? L.T("app.ui.replay.emptyLog")
                : (_rounds.Count > 1
                        ? L.T("app.ui.replay.roundPrefix", ("current", _roundIndex + 1), ("total", _rounds.Count))
                        : string.Empty)
                  + L.T("app.ui.replay.roundMeta", ("file", _round.FileName), ("round", _round.Round), ("honba", _round.Honba)),
            new RectF(230, 18, 700, 26), Muted, new TextStyle(13, Center: false));

        if (_round is null || _round.Steps.Count == 0)
        {
            c.DrawText(L.T("app.ui.replay.noLogHint"), new RectF(0, 300, Width, 28), Muted,
                new TextStyle(18, Center: true));
            DrawNavigation(c);
            return;
        }

        DrawTable(c, time);
        DrawSidePanel(c);
        DrawNavigation(c);
    }

    private void DrawTable(ICanvas c, double time)
    {
        var zone = new RectF(16, 56, TableSize, TableSize);
        c.FillRoundRect(zone, 14, Color.Rgb(12, 20, 22));
        c.StrokeRoundRect(zone, 14, PanelBorder, 1.2f);

        TableViewModel vm = TableForCurrentStep();
        c.Save();
        c.ClipRect(zone);
        c.Translate(zone.X, zone.Y);
        c.Scale(TableScale);
        _scene.Draw(c, vm, _tableLayout, -1, time);
        c.Restore();
    }

    private void DrawSidePanel(ICanvas c)
    {
        var list = new RectF(688, 56, 576, 292);
        c.FillRoundRect(list, 12, Panel);
        c.StrokeRoundRect(list, 12, PanelBorder, 1.1f);

        _stepRects.Clear();
        var steps = _round!.Steps;
        int visible = 10;
        int start = Math.Clamp(_index - visible / 2, 0, Math.Max(0, steps.Count - visible));
        float y = list.Y + 8;
        for (int i = start; i < Math.Min(steps.Count, start + visible); i++)
        {
            ReplayStep step = steps[i];
            bool selected = i == _index;
            var row = new RectF(list.X + 8, y, list.W - 16, 27);
            if (selected)
            {
                c.FillRoundRect(row, 6, Color.Rgb(44, 68, 58));
            }

            _stepRects.Add((i, row));
            string line = StepLine(_round, step, i);
            c.DrawText(line, new RectF(row.X + 8, row.Y, row.W - 100, row.H),
                selected ? Color.Rgb(255, 244, 214) : Text,
                new TextStyle(14, Bold: selected));
            if (_report.Grades.TryGetValue(i, out DiscardGrade? grade))
            {
                string badge = grade.Best ? L.T("app.ui.replay.best") : "-" + grade.Loss.ToString(CultureInfo.InvariantCulture);
                Color color = grade.Best ? Good : grade.Loss == 1 ? Warn : Bad;
                c.DrawText(badge, new RectF(row.Right - 78, row.Y, 70, row.H), color,
                    new TextStyle(13, Bold: grade.Best, Center: false, Right: true));
            }

            y += 28;
        }

        ReplayStep current = steps[_index];
        string bestHint = string.Empty;
        if (_report.Grades.TryGetValue(_index, out DiscardGrade? currentGrade) && !currentGrade.Best)
        {
            bestHint = " · " + L.T("app.ui.replay.bestDiscard", ("tile", currentGrade.BestTile));
        }

        var detail = new RectF(688, 356, 576, 92);
        c.FillRoundRect(detail, 12, Panel);
        c.StrokeRoundRect(detail, 12, PanelBorder, 1.1f);
        string player = RoundLogReader.PlayerName(_round.Headers, current.Player);
        c.DrawText(player + " · " + RoundLogReader.ActionName(current.Action)
                      + (string.IsNullOrEmpty(current.Tile) ? string.Empty : " " + current.Tile)
                      + (string.IsNullOrEmpty(current.Reason) ? string.Empty : " · " + current.Reason)
                      + bestHint,
            new RectF(detail.X + 14, detail.Y + 8, detail.W - 28, 24), Gold,
            new TextStyle(15, Bold: true));
        c.DrawText(L.T("app.ui.replay.hand") + (current.Hand ?? "—") + "    " +
                   L.T("app.ui.replay.wall") + current.Wall,
            new RectF(detail.X + 14, detail.Y + 34, detail.W - 28, 22), Text,
            new TextStyle(12));
        c.DrawText(RiversLine(_round, current),
            new RectF(detail.X + 14, detail.Y + 58, detail.W - 28, 22), Muted,
            new TextStyle(11));

        DrawSummary(c);
        c.DrawText((_index + 1) + " / " + steps.Count, new RectF(688, 632, 576, 20), Muted,
            new TextStyle(12, Center: true));
    }

    private void DrawSummary(ICanvas c)
    {
        var box = new RectF(688, 456, 576, 168);
        c.FillRoundRect(box, 12, Panel);
        c.StrokeRoundRect(box, 12, PanelBorder, 1.1f);
        c.DrawText(L.T("app.ui.replay.summary"), new RectF(box.X + 14, box.Y + 8, box.W - 28, 22), Gold,
            new TextStyle(14, Bold: true, Center: false));

        float y = box.Y + 34;
        for (int seat = 0; seat < _report.Seats.Count && seat < 4; seat++)
        {
            SeatReport report = _report.Seats[seat];
            string name = RoundLogReader.PlayerName(_round!.Headers, seat);
            bool isViewer = seat == _viewerSeat;
            c.DrawText(name, new RectF(box.X + 16, y, 180, 24), isViewer ? Gold : Text,
                new TextStyle(13, Bold: isViewer, Center: false));
            string ratio = report.Evaluated == 0
                ? "—"
                : report.Best.ToString(CultureInfo.InvariantCulture) + "/" + report.Evaluated.ToString(CultureInfo.InvariantCulture);
            c.DrawText(ratio, new RectF(box.X + 200, y, 90, 24), Text,
                new TextStyle(13, Center: false, Right: true));
            string avg = report.Evaluated == 0
                ? "—"
                : report.AverageLoss.ToString("0.0", CultureInfo.InvariantCulture);
            c.DrawText(avg, new RectF(box.X + 300, y, 90, 24),
                report.Evaluated == 0 ? Muted : report.AverageLoss <= 0.25 ? Good : report.AverageLoss <= 0.75 ? Warn : Bad,
                new TextStyle(13, Center: false, Right: true));
            y += 28;
        }

        c.DrawText(L.T("app.ui.replay.summaryLegend"), new RectF(box.X + 400, box.Y + 34, box.W - 416, 80), Muted,
            new TextStyle(11, Center: false));
    }

    private TableViewModel TableForCurrentStep()
    {
        if (_table is not null && _tableStep == _index && _tableRound == _roundIndex)
        {
            return _table;
        }

        _table = ReplayTableModel.Build(_round!, _round!.Steps[_index], _viewerSeat);
        _tableStep = _index;
        _tableRound = _roundIndex;
        return _table;
    }

    private void RebuildReport()
    {
        _report = _round is null ? ReplayReport.Empty : ReplayAnalysis.Analyze(_round);
        _viewerSeat = 0;
        if (_round is not null)
        {
            ReplayHeader? human = _round.Headers.FirstOrDefault(h => h.Human);
            if (human is not null)
            {
                _viewerSeat = Math.Clamp(human.Player, 0, 3);
            }
        }
    }

    private void DrawNavigation(ICanvas c)
    {
        _prev = new RectF(700, 664, 150, 38);
        _backButton = new RectF(865, 664, 150, 38);
        _next = new RectF(1030, 664, 150, 38);
        DrawButton(c, _prev, "app.ui.replay.prev", _prevHover, _round is not null && _index > 0);
        DrawButton(c, _backButton, "app.ui.replay.back", _backHover, true);
        DrawButton(c, _next, "app.ui.replay.next", _nextHover, _round is not null && _index < _round.Steps.Count - 1);

        _prevRound = new RectF(1000, 16, 110, 32);
        _nextRound = new RectF(1120, 16, 110, 32);
        if (_rounds.Count > 1)
        {
            DrawButton(c, _prevRound, "app.ui.replay.prevRound", _prevRoundHover, _roundIndex > 0);
            DrawButton(c, _nextRound, "app.ui.replay.nextRound", _nextRoundHover, _roundIndex < _rounds.Count - 1);
        }
    }

    private static string RiversLine(ReplayRound round, ReplayStep step)
    {
        var parts = new List<string>();
        for (int i = 0; i < step.Rivers.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(step.Rivers[i]))
            {
                parts.Add(RoundLogReader.PlayerName(round.Headers, i) + " " + step.Rivers[i]);
            }
        }

        return parts.Count == 0 ? L.T("app.ui.replay.rivers") + "—" : L.T("app.ui.replay.rivers") + string.Join(" ｜ ", parts);
    }

    private static void DrawButton(ICanvas c, RectF rect, string label, bool hover, bool enabled)
    {
        var border = !enabled ? Color.Rgb(70, 84, 78) : hover ? Gold : Color.Rgb(196, 168, 96);
        var fill = enabled
            ? hover ? Color.Rgb(52, 86, 78) : Color.Rgb(40, 58, 48)
            : Color.Rgb(28, 36, 32);
        c.FillRoundRect(rect, 10, fill);
        c.StrokeRoundRect(rect, 10, border, 1.3f);
        c.DrawText(L.T(label), rect, enabled ? Color.Rgb(242, 230, 190) : Color.Rgb(110, 124, 116),
            new TextStyle(15, Bold: true, Center: true));
    }

    private static string StepLine(ReplayRound round, ReplayStep step, int index)
    {
        string player = RoundLogReader.PlayerName(round.Headers, step.Player);
        string action = RoundLogReader.ActionName(step.Action);
        string tile = string.IsNullOrEmpty(step.Tile) ? string.Empty : " " + step.Tile;
        return (index + 1).ToString(CultureInfo.InvariantCulture) + ". " + player + " " + action + tile;
    }

    private void SetRound(int index)
    {
        _roundIndex = Math.Clamp(index, 0, _rounds.Count - 1);
        _round = _rounds.Count > 0 ? _rounds[_roundIndex] : null;
        _index = 0;
        RebuildReport();
        _table = null;
    }

    public override bool OnClick(float x, float y)
    {
        if (_backButton.Contains(x, y))
        {
            _back();
            return true;
        }

        if (_round is null)
        {
            return false;
        }

        if (_rounds.Count > 1 && _prevRound.Contains(x, y) && _roundIndex > 0)
        {
            SetRound(_roundIndex - 1);
            return true;
        }

        if (_rounds.Count > 1 && _nextRound.Contains(x, y) && _roundIndex < _rounds.Count - 1)
        {
            SetRound(_roundIndex + 1);
            return true;
        }

        foreach (var (step, rect) in _stepRects)
        {
            if (rect.Contains(x, y))
            {
                _index = step;
                return true;
            }
        }

        if (_prev.Contains(x, y) && _index > 0)
        {
            _index--;
            return true;
        }

        if (_next.Contains(x, y) && _index < _round.Steps.Count - 1)
        {
            _index++;
            return true;
        }

        return false;
    }

    public override bool OnPointerMove(float x, float y)
    {
        _prevHover = _round is not null && _index > 0 && _prev.Contains(x, y);
        _nextHover = _round is not null && _round.Steps.Count > 0 && _index < _round.Steps.Count - 1 && _next.Contains(x, y);
        _prevRoundHover = _rounds.Count > 1 && _roundIndex > 0 && _prevRound.Contains(x, y);
        _nextRoundHover = _rounds.Count > 1 && _roundIndex < _rounds.Count - 1 && _nextRound.Contains(x, y);
        _backHover = _backButton.Contains(x, y);
        return _prevHover || _nextHover || _prevRoundHover || _nextRoundHover || _backHover;
    }

    public override bool OnKey(Keys key)
    {
        switch (key)
        {
            case Keys.Escape:
                _back();
                return true;
            case Keys.Left:
            case Keys.Up:
                if (_round is not null && _index > 0)
                {
                    _index--;
                }

                return true;
            case Keys.Right:
            case Keys.Down:
                if (_round is not null && _index < _round.Steps.Count - 1)
                {
                    _index++;
                }

                return true;
            case Keys.PageUp:
                if (_rounds.Count > 1 && _roundIndex > 0)
                {
                    SetRound(_roundIndex - 1);
                }

                return true;
            case Keys.PageDown:
                if (_rounds.Count > 1 && _roundIndex < _rounds.Count - 1)
                {
                    SetRound(_roundIndex + 1);
                }

                return true;
            case Keys.Home:
                _index = 0;
                return true;
            case Keys.End:
                if (_round is not null)
                {
                    _index = _round.Steps.Count - 1;
                }

                return true;
            default:
                return false;
        }
    }
}
