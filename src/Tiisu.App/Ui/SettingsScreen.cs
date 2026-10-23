using System;
using System.Collections.Generic;
using System.Linq;
using Tiisu.App.Rendering;
using Tiisu.App.Runtime;
using Tiisu.Core;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>
/// 设置屏：单列列表，选中行高亮，值显示在右侧；←→ / 点击切换，改完立即写 <c>settings.json</c>。
/// 与菜单屏同为 1280×720 逻辑画布。
/// </summary>
public sealed class SettingsScreen : Screen
{
    private static readonly int[] ThinkChoices = { 5, 10, 15, 20, 30, 60 };

    /// <summary>权重预设：默认 30/45/20/5，其余几组供 ←→ 循环。</summary>
    private static readonly int[][] WeightPresets =
    {
        new[] { 30, 45, 20, 5 },
        new[] { 50, 35, 15, 0 },
        new[] { 10, 35, 40, 15 },
        new[] { 0, 0, 100, 0 },
        new[] { 0, 0, 0, 100 },
    };

    private const float RowTop = 100f;
    private const float RowStep = 32f;
    private const float RowHeight = 30f;

    /// <summary>人类座位（引擎固定座位 0）；该行只展示「你」，不可锁。</summary>
    private const int HumanSeat = 0;

    /// <summary>座位名（按引擎四家顺序）。</summary>
    private static readonly string[] SeatNames = { "东座", "南座", "西座", "北座" };

    /// <summary>与 Web 设置页同一组可选役（5 项）；中文名取自 ike.json。</summary>
    private static readonly (string Id, string Name)[] OptionalYakuOptions = BuildOptionalOptions();

    private static readonly Color TextColor = Color.Rgb(240, 236, 220);
    private static readonly Color Muted = Color.Rgb(150, 164, 152);
    private static readonly Color Gold = Color.Rgb(196, 168, 96);

    private readonly Settings _settings;
    private readonly Action<Settings> _changed;
    private readonly Action _back;
    private readonly List<Row> _rows = new();

    /// <summary>左上角「← 返回」按钮：键盘没送达时也能靠鼠标离开设置屏。</summary>
    private static readonly RectF BackButton = new(28, 22, 132, 42);

    private bool _backHover;
    private int _selected;
    private int _hover = -1;

    public SettingsScreen(Settings settings, Action<Settings> changed, Action back) : base("settings")
    {
        _settings = settings ?? new Settings();
        _changed = changed ?? (_ => { });
        _back = back ?? (() => { });
        BuildRows();
    }

    public override Color Background => Color.Rgb(10, 16, 20);

    public override void Draw(ICanvas c, double time)
    {
        c.FillLinearGradient(new RectF(0, 0, Width, Height), 0,
            Color.Rgb(20, 28, 34), Color.Rgb(8, 12, 16));

        c.DrawText("设置", new RectF(0, 36, Width, 52), Gold,
            new TextStyle(32, Bold: true, Center: true));

        // 左上角返回按钮
        c.FillRoundRect(BackButton, 10, _backHover ? Color.Rgb(44, 66, 60) : Color.Rgb(24, 34, 30).WithAlpha(200));
        c.StrokeRoundRect(BackButton, 10, Gold, 1.2f);
        c.DrawText("← 返回", BackButton, TextColor, new TextStyle(18, Bold: false, Center: true));

        float panelH = Math.Min(600, 40 + (_rows.Count * RowStep));
        var panel = new RectF(330, 92, 620, panelH);
        c.FillRoundRect(panel, 16, Color.Rgb(16, 24, 22).WithAlpha(232));
        c.StrokeRoundRect(panel, 16, Color.Rgb(60, 78, 70), 1.4f);

        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var rect = new RectF(350, RowTop + (i * RowStep), 580, RowHeight);
            bool selected = i == _selected;
            bool hover = i == _hover;
            var fill = selected ? Color.Rgb(44, 66, 60) : hover ? Color.Rgb(30, 44, 40) : Color.Transparent;
            if (fill.A > 0)
            {
                c.FillRoundRect(rect, 8, fill);
            }

            if (selected)
            {
                c.StrokeRoundRect(rect, 8, Gold, 1.4f);
            }

            c.DrawText(row.Label, new RectF(rect.X + 16, rect.Y, rect.W - 180, rect.H),
                selected ? Color.Rgb(255, 244, 214) : TextColor,
                new TextStyle(17, Bold: selected, Center: false));

            c.DrawText(row.Value(), new RectF(rect.Right - 170, rect.Y, 150, rect.H),
                row.IsOn() ? Gold : Muted,
                new TextStyle(16, Bold: row.IsOn(), Center: false, Right: true));
        }

        c.DrawText("↑↓ 选择 · ←→ 切换 · Esc 返回 · 座位锁定下一场生效", new RectF(0, 684, Width, 28), Muted,
            new TextStyle(16, Center: true));
    }

    public override bool OnPointerMove(float x, float y)
    {
        _backHover = BackButton.Contains(x, y);
        _hover = RowAt(x, y);
        return _backHover || _hover >= 0;
    }

    public override bool OnClick(float x, float y)
    {
        if (BackButton.Contains(x, y))
        {
            _back();
            return true;
        }

        int index = RowAt(x, y);
        if (index < 0)
        {
            return false;
        }

        _selected = index;
        _rows[index].Change(1);
        return true;
    }

    public override bool OnKey(Keys key)
    {
        switch (key)
        {
            case Keys.Escape:
                _back();
                return true;

            case Keys.Up:
                _selected = (_selected - 1 + _rows.Count) % _rows.Count;
                return true;

            case Keys.Down:
                _selected = (_selected + 1) % _rows.Count;
                return true;

            case Keys.Left:
                _rows[_selected].Change(-1);
                return true;

            case Keys.Right:
            case Keys.Enter:
            case Keys.Space:
                _rows[_selected].Change(1);
                return true;

            default:
                return false;
        }
    }

    private int RowAt(float x, float y)
    {
        if (x < 350 || x > 930)
        {
            return -1;
        }

        int index = (int)((y - RowTop) / RowStep);
        if (index < 0 || index >= _rows.Count)
        {
            return -1;
        }

        float top = RowTop + (index * RowStep);
        return y >= top && y <= top + RowHeight ? index : -1;
    }

    private void BuildRows()
    {
        _rows.Add(new Row(
            "长考秒数",
            () => _settings.ThinkSeconds + " 秒",
            () => _settings.ThinkSeconds > 0,
            dir => CycleThink(dir)));

        foreach (var (id, name) in OptionalYakuOptions)
        {
            _rows.Add(new Row(
                name,
                () => IsOptionalOn(id) ? "开" : "关",
                () => IsOptionalOn(id),
                _ => ToggleOptional(id)));
        }

        _rows.Add(new Row("音效", () => OnOff(_settings.SoundEnabled), () => _settings.SoundEnabled, _ =>
        {
            _settings.SoundEnabled = !_settings.SoundEnabled;
            SoundEffects.Enabled = _settings.SoundEnabled;
            if (_settings.SoundEnabled)
            {
                SoundEffects.Play(SoundEffects.Cue.Call);
            }

            Save();
        }));
        _rows.Add(new Row("显示岭上指示牌", () => OnOff(_settings.ShowMa), () => _settings.ShowMa, _ => Toggle(
            () => _settings.ShowMa,
            value => _settings.ShowMa = value)));
        _rows.Add(new Row("记录牌型日志", () => OnOff(_settings.RecordTiles), () => _settings.RecordTiles, _ => Toggle(
            () => _settings.RecordTiles,
            value => _settings.RecordTiles = value)));
        _rows.Add(new Row("牌山查看器入口", () => OnOff(_settings.ShowWallViewer), () => _settings.ShowWallViewer, _ => Toggle(
            () => _settings.ShowWallViewer,
            value => _settings.ShowWallViewer = value)));
        _rows.Add(new Row("调试信息", () => OnOff(_settings.ShowDebug), () => _settings.ShowDebug, _ => Toggle(
            () => _settings.ShowDebug,
            value => _settings.ShowDebug = value)));

        _rows.Add(new Row(
            "AI 难度权重",
            WeightText,
            () => true,
            CycleWeights));
        _rows.Add(new Row(
            "修罗 MCTS",
            () => OnOff(_settings.AiMctsEnabled),
            () => _settings.AiMctsEnabled,
            _ => Toggle(() => _settings.AiMctsEnabled, value => _settings.AiMctsEnabled = value)));

        // 按座位锁定：-1 = 随机（∅），0..3 = 简 / 普 / 难 / 修；人类座位只标注「你」。
        for (int seat = 0; seat < 4; seat++)
        {
            int index = seat;
            bool human = seat == HumanSeat;
            _rows.Add(new Row(
                "锁定 · " + SeatNames[seat] + (human ? "（你）" : string.Empty),
                () => human ? "你" : LockName(SeatLock(index)),
                () => !human && SeatLock(index) >= 0,
                direction =>
                {
                    if (!human)
                    {
                        CycleSeatLock(index, direction);
                    }
                }));
        }
    }

    /// <summary>权重展示：<c>30/45/20/5</c>（简单/普通/困难/修罗）。</summary>
    private string WeightText()
    {
        var weights = _settings.AiWeights;
        return weights is { Length: 4 } ? string.Join("/", weights) : "30/45/20/5";
    }

    /// <summary>锁档名：-1 = 随机，0..3 = 简单 / 普通 / 困难 / 修罗。</summary>
    private static string LockName(int value) => value switch
    {
        0 => "简单",
        1 => "普通",
        2 => "困难",
        3 => "修罗",
        _ => "随机",
    };

    private int SeatLock(int seat)
    {
        var locks = _settings.AiSeatLocks;
        return locks is { Length: 4 } ? locks[seat] : -1;
    }

    /// <summary>←→ 循环：随机 → 简单 → 普通 → 困难 → 修罗 → 随机。</summary>
    private void CycleSeatLock(int seat, int direction)
    {
        if (_settings.AiSeatLocks is not { Length: 4 })
        {
            _settings.AiSeatLocks = new[] { -1, -1, -1, -1 };
        }

        int current = _settings.AiSeatLocks[seat];
        if (current is < -1 or > 3)
        {
            current = -1;
        }

        int next = current + (direction >= 0 ? 1 : -1);
        if (next > 3)
        {
            next = -1;
        }
        else if (next < -1)
        {
            next = 3;
        }

        _settings.AiSeatLocks[seat] = next;
        Save();
    }

    private void CycleWeights(int direction)
    {
        int index = -1;
        var current = _settings.AiWeights;
        if (current is { Length: 4 })
        {
            for (int i = 0; i < WeightPresets.Length; i++)
            {
                if (SameWeights(current, WeightPresets[i]))
                {
                    index = i;
                    break;
                }
            }
        }

        if (index < 0)
        {
            index = direction >= 0 ? 0 : WeightPresets.Length - 1;
        }
        else
        {
            index = (index + direction + WeightPresets.Length) % WeightPresets.Length;
        }

        _settings.AiWeights = (int[])WeightPresets[index].Clone();
        Save();
    }

    private static bool SameWeights(int[] a, int[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    private void CycleThink(int direction)
    {
        int index = Array.IndexOf(ThinkChoices, _settings.ThinkSeconds);
        if (index < 0)
        {
            index = 0;
            for (int i = 0; i < ThinkChoices.Length; i++)
            {
                if (ThinkChoices[i] <= _settings.ThinkSeconds)
                {
                    index = i;
                }
            }
        }
        else
        {
            index = (index + direction + ThinkChoices.Length) % ThinkChoices.Length;
        }

        _settings.ThinkSeconds = ThinkChoices[index];
        Save();
    }

    private bool IsOptionalOn(string id) =>
        _settings.OptionalYaku.Any(x => string.Equals(x, id, StringComparison.Ordinal));

    private void ToggleOptional(string id)
    {
        if (IsOptionalOn(id))
        {
            _settings.OptionalYaku.RemoveAll(x => string.Equals(x, id, StringComparison.Ordinal));
        }
        else
        {
            _settings.OptionalYaku.Add(id);
        }

        Save();
    }

    private void Toggle(Func<bool> read, Action<bool> write)
    {
        write(!read());
        Save();
    }

    private void Save()
    {
        try
        {
            _settings.Save();
        }
        catch
        {
            // 设置写盘失败不应拖死 UI；内存值仍然生效。
        }

        _changed(_settings.Clone());
    }

    private static string OnOff(bool value) => value ? "开" : "关";

    private static (string Id, string Name)[] BuildOptionalOptions()
    {
        try
        {
            var data = YakuData.LoadDefault();
            return data.Yaku
                .Where(y => y.Optional)
                .Select(y => (y.Id, y.Name))
                .ToArray();
        }
        catch
        {
            // 数据读不到时不显示可选役开关（不影响其他设置项）。
            return Array.Empty<(string Id, string Name)>();
        }
    }

    private sealed class Row
    {
        public Row(string label, Func<string> value, Func<bool> isOn, Action<int> change)
        {
            Label = label;
            Value = value;
            IsOn = isOn;
            Change = change;
        }

        public string Label { get; }

        public Func<string> Value { get; }

        public Func<bool> IsOn { get; }

        public Action<int> Change { get; }
    }
}
