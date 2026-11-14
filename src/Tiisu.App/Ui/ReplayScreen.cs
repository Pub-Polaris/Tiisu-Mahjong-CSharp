using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using Tiisu.App.Rendering;
using Tiisu.App.Runtime;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>牌谱回看：读取 roundlog_*.jsonl 的最后一局，按手翻页。</summary>
public sealed class ReplayScreen : Screen
{
    private readonly Action _back;
    private readonly IReadOnlyList<ReplayRound> _rounds;
    private ReplayRound? _round;
    private int _roundIndex;
    private int _index;
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

    private static readonly Color Gold = Color.Rgb(242, 201, 76);
    private static readonly Color Text = Color.Rgb(222, 230, 220);
    private static readonly Color Muted = Color.Rgb(150, 168, 158);

    public ReplayScreen(Action back) : base("replay")
    {
        _back = back;
        var loaded = RoundLogReader.LoadLatestRounds();
        _rounds = loaded?.Rounds ?? Array.Empty<ReplayRound>();
        _roundIndex = Math.Max(0, _rounds.Count - 1);
        _round = _rounds.Count > 0 ? _rounds[_roundIndex] : null;
    }

    public override void Draw(ICanvas c, double time)
    {
        c.FillLinearGradient(new RectF(0, 0, Width, Height), 0,
            Color.Rgb(18, 26, 30), Color.Rgb(8, 12, 16));

        c.DrawText("牌谱回看", new RectF(0, 30, Width, 48), Gold,
            new TextStyle(36, Bold: true, Center: true));
        c.DrawText(_round is null
                ? "本机 logs 里还没有牌谱；在设置中开启「记录牌型日志」后打一局。"
                : (_rounds.Count > 1 ? "第 " + (_roundIndex + 1) + "/" + _rounds.Count + " 局 · " : string.Empty)
                  + _round.FileName + " · " + _round.Round + " 局 · " + _round.Honba + " 本场",
            new RectF(0, 78, Width, 24), Muted, new TextStyle(14, Center: true));

        if (_round is null || _round.Steps.Count == 0)
        {
            c.DrawText("没有可回看的牌谱。", new RectF(0, 300, Width, 28), Muted,
                new TextStyle(18, Center: true));
            DrawNavigation(c);
            return;
        }

        var list = new RectF(80, 116, 1120, 390);
        c.FillRoundRect(list, 14, Color.Rgb(24, 36, 32));
        c.StrokeRoundRect(list, 14, Color.Rgb(74, 96, 84), 1.2f);

        int visible = 13;
        int start = Math.Clamp(_index - visible / 2, 0, Math.Max(0, _round.Steps.Count - visible));
        float y = list.Y + 14;
        for (int i = start; i < Math.Min(_round.Steps.Count, start + visible); i++)
        {
            ReplayStep step = _round.Steps[i];
            bool selected = i == _index;
            var row = new RectF(list.X + 14, y, list.W - 28, 27);
            if (selected)
            {
                c.FillRoundRect(row, 6, Color.Rgb(48, 72, 62));
            }

            string line = StepLine(_round, step, i);
            c.DrawText(line, new RectF(row.X + 8, row.Y, row.W - 16, row.H),
                selected ? Color.Rgb(255, 244, 214) : Text,
                new TextStyle(15, Bold: selected));
            y += 28;
        }

        ReplayStep current = _round.Steps[_index];
        var detail = new RectF(80, 522, 1120, 86);
        c.FillRoundRect(detail, 12, Color.Rgb(26, 38, 34));
        c.StrokeRoundRect(detail, 12, Color.Rgb(74, 96, 84), 1.1f);
        string player = RoundLogReader.PlayerName(_round.Headers, current.Player);
        c.DrawText(player + " · " + RoundLogReader.ActionName(current.Action)
                      + (string.IsNullOrEmpty(current.Tile) ? string.Empty : " " + current.Tile)
                      + (string.IsNullOrEmpty(current.Reason) ? string.Empty : " · " + current.Reason),
            new RectF(detail.X + 18, detail.Y + 10, detail.W - 36, 26), Gold,
            new TextStyle(17, Bold: true));
        c.DrawText("手牌：" + (current.Hand ?? "—") + "    余牌：" + current.Wall,
            new RectF(detail.X + 18, detail.Y + 42, detail.W - 36, 24), Text,
            new TextStyle(14));
        c.DrawText(RiversLine(_round, current),
            new RectF(detail.X + 18, detail.Y + 64, detail.W - 36, 20), Muted,
            new TextStyle(12));

        c.DrawText((_index + 1) + " / " + _round.Steps.Count, new RectF(0, 616, Width, 22), Muted,
            new TextStyle(13, Center: true));
        DrawNavigation(c);
    }

    private void DrawNavigation(ICanvas c)
    {
        _prev = new RectF(Width / 2f - 300, 648, 150, 42);
        _backButton = new RectF(Width / 2f - 75, 648, 150, 42);
        _next = new RectF(Width / 2f + 150, 648, 150, 42);
        DrawButton(c, _prev, "上一手", _prevHover, _round is not null && _index > 0);
        DrawButton(c, _backButton, "返回菜单", _backHover, true);
        DrawButton(c, _next, "下一手", _nextHover, _round is not null && _index < _round.Steps.Count - 1);

        _prevRound = new RectF(80, 610, 140, 30);
        _nextRound = new RectF(Width - 220, 610, 140, 30);
        if (_rounds.Count > 1)
        {
            DrawButton(c, _prevRound, "← 上一局", _prevRoundHover, _roundIndex > 0);
            DrawButton(c, _nextRound, "下一局 →", _nextRoundHover, _roundIndex < _rounds.Count - 1);
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

        return parts.Count == 0 ? "牌河：—" : "牌河：" + string.Join(" ｜ ", parts);
    }

    private static void DrawButton(ICanvas c, RectF rect, string label, bool hover, bool enabled)
    {
        var border = !enabled ? Color.Rgb(70, 84, 78) : hover ? Gold : Color.Rgb(196, 168, 96);
        var fill = enabled
            ? hover ? Color.Rgb(52, 86, 78) : Color.Rgb(40, 58, 48)
            : Color.Rgb(28, 36, 32);
        c.FillRoundRect(rect, 10, fill);
        c.StrokeRoundRect(rect, 10, border, 1.3f);
        c.DrawText(label, rect, enabled ? Color.Rgb(242, 230, 190) : Color.Rgb(110, 124, 116),
            new TextStyle(17, Bold: true, Center: true));
    }

    private static string StepLine(ReplayRound round, ReplayStep step, int index)
    {
        string player = RoundLogReader.PlayerName(round.Headers, step.Player);
        string action = RoundLogReader.ActionName(step.Action);
        string tile = string.IsNullOrEmpty(step.Tile) ? string.Empty : " " + step.Tile;
        string wall = step.Wall > 0 ? "（余 " + step.Wall + "）" : string.Empty;
        return (index + 1).ToString(CultureInfo.InvariantCulture) + ". " + player + " " + action + tile + wall;
    }

    private void SetRound(int index)
    {
        _roundIndex = Math.Clamp(index, 0, _rounds.Count - 1);
        _round = _rounds.Count > 0 ? _rounds[_roundIndex] : null;
        _index = 0;
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
        _nextHover = _round is not null && _index < _round.Steps.Count - 1 && _next.Contains(x, y);
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
