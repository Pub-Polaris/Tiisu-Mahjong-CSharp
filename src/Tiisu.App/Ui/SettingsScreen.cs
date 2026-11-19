using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tiisu.App.Rendering;
using Tiisu.App.Runtime;
using Tiisu.Engine.Ai;
using Tiisu.Localization;
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

    private const float RowTop = 136f;
    private const float TabTop = 84f;
    private const float TabHeight = 30f;

    private enum SettingsGroup { Basic, Rules, Ai }

    /// <summary>五项 AI 风格权重（顺序与设置页滑条一致）。</summary>
    private enum StyleField
    {
        Attack = 0,
        Defense = 1,
        Meld = 2,
        Value = 3,
        Pursuit = 4,
    }

    private static readonly string[] StyleLabelKeys =
    {
        "app.ui.settings.row.aiStyleAttack",
        "app.ui.settings.row.aiStyleDefense",
        "app.ui.settings.row.aiStyleMeld",
        "app.ui.settings.row.aiStyleValue",
        "app.ui.settings.row.aiStylePursuit",
    };

    /// <summary>三个预设的名称 key（顺序与 <see cref="AiStyle.Presets"/> 一致）。</summary>
    private static readonly string[] StylePresetKeys =
    {
        "app.ui.settings.aiStyle.steady",
        "app.ui.settings.aiStyle.balanced",
        "app.ui.settings.aiStyle.pursuit",
    };

    private static readonly (SettingsGroup Group, string Label)[] Groups =
    {
        (SettingsGroup.Basic, "app.ui.settings.group.basic"),
        (SettingsGroup.Rules, "app.ui.settings.group.rules"),
        (SettingsGroup.Ai, "app.ui.settings.group.ai"),
    };
    private const float RowStep = 32f;
    private const float RowHeight = 30f;

    /// <summary>人类座位（引擎固定座位 0）；该行只展示「你」，不可锁。</summary>
    private const int HumanSeat = 0;

    /// <summary>座位名（按引擎四家顺序）。</summary>
    private static readonly string[] SeatNames =
    {
        "app.ui.settings.seat.east",
        "app.ui.settings.seat.south",
        "app.ui.settings.seat.west",
        "app.ui.settings.seat.north",
    };

    /// <summary>与 Web 设置页同一组可选役（5 项）；中文名取自 ike.json。</summary>
    private static readonly (string Id, string Name)[] OptionalYakuOptions = BuildOptionalOptions();

    private static readonly Color TextColor = Color.Rgb(240, 236, 220);
    private static readonly Color Muted = Color.Rgb(150, 164, 152);
    private static readonly Color Gold = Color.Rgb(196, 168, 96);

    private readonly ISoundEffects? _sound;
    private readonly Settings _settings;
    private readonly Action<Settings> _changed;
    private readonly Action _back;
    private readonly List<Row> _rows = new();

    /// <summary>左上角「← 返回」按钮：键盘没送达时也能靠鼠标离开设置屏。</summary>
    private static readonly RectF BackButton = new(28, 22, 132, 42);

    private bool _backHover;
    private int _selected;
    private int _hover = -1;
      private SettingsGroup _group = SettingsGroup.Basic;
      private int _tabHover = -1;
      private Slider? _activeSlider;
      private bool _sliderHit;

      /// <summary>五项风格滑条（按 <see cref="StyleField"/> 下标）；应用预设时同步它们。</summary>
      private readonly Slider?[] _styleSliders = new Slider?[5];

    public SettingsScreen(Settings settings, Action<Settings> changed, Action back,
        ISoundEffects? sound = null) : base("settings")
    {
        _sound = sound;
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

        c.DrawText(L.T("app.ui.settings.title"), new RectF(0, 36, Width, 52), Gold,
            new TextStyle(32, Bold: true, Center: true));

        // 左上角返回按钮
        c.FillRoundRect(BackButton, 10, _backHover ? Color.Rgb(44, 66, 60) : Color.Rgb(24, 34, 30).WithAlpha(200));
        c.StrokeRoundRect(BackButton, 10, Gold, 1.2f);
        c.DrawText(L.T("app.ui.settings.back"), BackButton, TextColor, new TextStyle(18, Bold: false, Center: true));

        for (int t = 0; t < Groups.Length; t++)
        {
            var tab = new RectF(350 + t * 150, TabTop, 138, TabHeight);
            bool active = Groups[t].Group == _group;
            c.FillRoundRect(tab, 8, active ? Color.Rgb(44, 66, 60) : Color.Rgb(22, 32, 28));
            c.StrokeRoundRect(tab, 8, active ? Gold : Color.Rgb(70, 88, 80), 1.2f);
            c.DrawText(L.T(Groups[t].Label), tab, active ? Color.Rgb(255, 244, 214) : Muted,
                new TextStyle(15, Bold: active, Center: true));
        }

        var visible = VisibleRows();
        _selected = Math.Clamp(_selected, 0, Math.Max(0, visible.Count - 1));
        float panelH = 40 + (visible.Count * RowStep);
        var panel = new RectF(330, 118, 620, panelH);
        c.FillRoundRect(panel, 16, Color.Rgb(16, 24, 22).WithAlpha(232));
        c.StrokeRoundRect(panel, 16, Color.Rgb(60, 78, 70), 1.4f);

        for (int i = 0; i < visible.Count; i++)
        {
            var row = visible[i];
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

            c.DrawText(RowLabel(row.Label), new RectF(rect.X + 16, rect.Y, rect.W - 180, rect.H),
                selected ? Color.Rgb(255, 244, 214) : TextColor,
                new TextStyle(17, Bold: selected, Center: false));

              if (row.Slider is { } slider)
              {
                  if (row.ShowSliderValue)
                  {
                      // 风格权重需要看到数值：滑条让出一段右侧文字区。
                      slider.Layout(new RectF(rect.Right - 195, rect.Y + 4, 120, rect.H - 8));
                      slider.Draw(c, selected, hover);
                      c.DrawText(row.Value(), new RectF(rect.Right - 68, rect.Y, 58, rect.H),
                          selected ? Color.Rgb(255, 244, 214) : TextColor,
                          new TextStyle(16, Bold: selected, Center: false, Right: true));
                  }
                  else
                  {
                      slider.Layout(new RectF(rect.Right - 190, rect.Y + 4, 170, rect.H - 8));
                      slider.Draw(c, selected, hover);
                  }
              }
            else
            {
                c.DrawText(row.Value(), new RectF(rect.Right - 170, rect.Y, 150, rect.H),
                    row.IsOn() ? Gold : Muted,
                    new TextStyle(16, Bold: row.IsOn(), Center: false, Right: true));
            }
        }

        c.DrawText(L.T("app.ui.settings.footer"), new RectF(0, 684, Width, 28), Muted,
            new TextStyle(16, Center: true));
    }

    public override bool OnPointerMove(float x, float y)
    {
        if (_activeSlider is { Dragging: true } active)
        {
            _activeSlider = active;
            return active.OnPointerMove(x, y);
        }
        _backHover = BackButton.Contains(x, y);
        _tabHover = TabAt(x, y);
        _hover = RowAt(x, y);
        return _backHover || _tabHover >= 0 || _hover >= 0;
    }

    public override bool OnClick(float x, float y)
    {
        if (BackButton.Contains(x, y))
        {
            _back();
            return true;
        }

        int tab = TabAt(x, y);
        if (tab >= 0)
        {
            _group = Groups[tab].Group;
            _selected = 0;
            return true;
        }

        int index = RowAt(x, y);
        if (index < 0)
        {
            return false;
        }

        var visible = VisibleRows();
        _selected = index;
        visible[index].Change(1);
        return true;
    }

      public override bool OnPointerDown(float x, float y)
      {
          int index = RowAt(x, y);
          var visible = VisibleRows();
          if (index >= 0 && index < visible.Count && visible[index].Slider is { } slider && slider.OnPointerDown(x, y))
          {
              _activeSlider = slider;
              _sliderHit = true;
              _selected = index;
              return true;
          }

          return false;
      }

      public override bool OnPointerUp(float x, float y)
      {
          bool handled = _activeSlider?.Dragging == true;
          _activeSlider?.OnPointerUp();
          _activeSlider = null;
          return handled;
      }

    public override bool OnKey(Keys key)
    {
        switch (key)
        {
            case Keys.Escape:
                _back();
                return true;

            case Keys.Tab:
            case Keys.OemCloseBrackets:
                _group = Groups[(Array.IndexOf(Groups, Groups.First(g => g.Group == _group)) + 1) % Groups.Length].Group;
                _selected = 0;
                return true;

            case Keys.OemOpenBrackets:
                _group = Groups[(Array.IndexOf(Groups, Groups.First(g => g.Group == _group)) - 1 + Groups.Length) % Groups.Length].Group;
                _selected = 0;
                return true;

            case Keys.Up:
            {
                var visible = VisibleRows();
                _selected = (_selected - 1 + visible.Count) % visible.Count;
                return true;
            }

            case Keys.Down:
            {
                var visible = VisibleRows();
                _selected = (_selected + 1) % visible.Count;
                return true;
            }

            case Keys.Left:
            {
                var visible = VisibleRows();
                visible[_selected].Change(-1);
                return true;
            }

            case Keys.Right:
            case Keys.Enter:
            case Keys.Space:
            {
                var visible = VisibleRows();
                visible[_selected].Change(1);
                return true;
            }

            default:
                return false;
        }
    }

      /// <summary>行标签：app.* 走 L.T，其余（可选役中文名等数据）走旧中文键 Tr。</summary>
      private static string RowLabel(string label) =>
          label.StartsWith("app.", StringComparison.Ordinal) ? L.T(label) : UiText.Tr(label);

      private List<Row> VisibleRows() => _rows.Where(r => r.Group == _group).ToList();

      private int TabAt(float x, float y)
      {
          for (int i = 0; i < Groups.Length; i++)
          {
              if (new RectF(350 + i * 150, TabTop, 138, TabHeight).Contains(x, y))
              {
                  return i;
              }
          }

          return -1;
      }


    private int RowAt(float x, float y)
    {
        if (x < 350 || x > 930)
        {
            return -1;
        }

        int index = (int)((y - RowTop) / RowStep);
        // 只命中当前分组实际画出来的行，避免点面板下方空白处索引越界（历史遗留）。
        if (index < 0 || index >= VisibleRows().Count)
        {
            return -1;
        }

        float top = RowTop + (index * RowStep);
        return y >= top && y <= top + RowHeight ? index : -1;
    }

    private void BuildRows()
    {
        _rows.Add(new Row(
            "app.ui.settings.row.think",
            () => L.T("app.ui.settings.seconds", ("value", _settings.ThinkSeconds)),
            () => _settings.ThinkSeconds > 0,
            dir => CycleThink(dir)));

        foreach (var (id, name) in OptionalYakuOptions)
        {
            _rows.Add(new Row(
                name,
                () => OnOff(IsOptionalOn(id)),
                () => IsOptionalOn(id),
                _ => ToggleOptional(id),
                group: SettingsGroup.Rules));
        }

          var bgmSlider = new Slider(0, 100, _settings.BgmVolume,
              v => SetVolumeLive(bgm: true, v), Save);
          var sfxSlider = new Slider(0, 100, _settings.SfxVolume,
              v => SetVolumeLive(bgm: false, v), Save);
          _rows.Add(new Row(
              "app.ui.settings.row.bgm",
              () => _settings.BgmVolume + "%",
              () => _settings.BgmVolume > 0,
              direction => bgmSlider.Step(direction * 10),
              () => _settings.BgmVolume,
              bgmSlider));
          _rows.Add(new Row(
              "app.ui.settings.row.sfx",
              () => _settings.SfxVolume + "%",
              () => _settings.SfxVolume > 0,
              direction => sfxSlider.Step(direction * 10),
              () => _settings.SfxVolume,
              sfxSlider));
        _rows.Add(new Row("app.ui.settings.row.voice", () => OnOff(_settings.VoiceEnabled), () => _settings.VoiceEnabled, _ => Toggle(
            () => _settings.VoiceEnabled,
            value => _settings.VoiceEnabled = value)));
        _rows.Add(new Row("app.ui.settings.row.language", LanguageName,
            () => _settings.Language != "zh-CN", _ => CycleLanguage()));
        _rows.Add(new Row("app.ui.settings.row.textScale", TextScaleName, () => _settings.UiTextScale > 1.01, _ => CycleTextScale()));
        _rows.Add(new Row("app.ui.settings.row.showMa", () => OnOff(_settings.ShowMa), () => _settings.ShowMa, _ => Toggle(
            () => _settings.ShowMa,
            value => _settings.ShowMa = value)));
        _rows.Add(new Row("app.ui.settings.row.recordTiles", () => OnOff(_settings.RecordTiles), () => _settings.RecordTiles, _ => Toggle(
            () => _settings.RecordTiles,
            value => _settings.RecordTiles = value)));
        _rows.Add(new Row("app.ui.settings.row.diceViewer", () => OnOff(_settings.ShowWallViewer), () => _settings.ShowWallViewer, _ => Toggle(
            () => _settings.ShowWallViewer,
            value => _settings.ShowWallViewer = value)));

        // 1102工程：规则集切换（只在新一局生效；ScreenHost 会重置牌桌并回菜单）。
        _rows.Add(new Row(
            "app.ui.settings.row.ruleset",
            () => _settings.Ruleset == Ruleset.TuidaoHu
                ? L.T("app.ui.settings.ruleset.tuidaohu")
                : L.T("app.ui.settings.ruleset.qizi"),
            () => _settings.Ruleset == Ruleset.TuidaoHu,
            _ =>
            {
                _settings.Ruleset = _settings.Ruleset == Ruleset.TuidaoHu ? Ruleset.Qizi : Ruleset.TuidaoHu;
                Save();
            },
            group: SettingsGroup.Rules));

        _rows.Add(new Row(
            "app.ui.settings.row.aiWeights",
            WeightText,
            () => true,
            CycleWeights));
        _rows.Add(new Row(
            "app.ui.settings.row.mcts",
            () => OnOff(_settings.AiMctsEnabled),
            () => _settings.AiMctsEnabled,
            _ => Toggle(() => _settings.AiMctsEnabled, value => _settings.AiMctsEnabled = value)));

        _rows.Add(new Row(
            "app.ui.settings.row.tableDifficulty",
            TableDifficultyName,
            () => TableDifficultyTier() >= 0,
            _ => CycleTableDifficulty()));

        // ── 7Neuu 自动追役原型（A 期）：开关 + 三预设 + 五项风格滑条 ──
        _rows.Add(new Row(
            "app.ui.settings.row.aiPursuit",
            () => OnOff(_settings.AiPursuitEnabled),
            () => _settings.AiPursuitEnabled,
            _ => Toggle(() => _settings.AiPursuitEnabled, value => _settings.AiPursuitEnabled = value),
            group: SettingsGroup.Ai));
        _rows.Add(new Row(
            "app.ui.settings.row.aiStylePreset",
            StylePresetName,
            () => _settings.AiStyle.MatchingPreset() >= 0,
            CycleStylePreset,
            group: SettingsGroup.Ai));
        for (int field = 0; field < StyleLabelKeys.Length; field++)
        {
            AddStyleSlider((StyleField)field, StyleLabelKeys[field]);
        }

        // 按座位锁定：-1 = 随机（∅），0..3 = 简 / 普 / 难 / 修；人类座位只标注「你」。
        for (int seat = 0; seat < 4; seat++)
        {
            int index = seat;
            bool human = seat == HumanSeat;
            _rows.Add(new Row(
                human
                    ? L.T("app.ui.settings.seatLock.lockedYou", ("seat", L.T(SeatNames[seat])))
                    : L.T("app.ui.settings.seatLock.locked", ("seat", L.T(SeatNames[seat]))),
                () => human ? L.T("app.ui.settings.you") : LockName(SeatLock(index)),
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

    /// <summary>全桌统一难度：四家锁定值一致时显示档位；否则为自定义。</summary>
    private int TableDifficultyTier()
    {
        var locks = _settings.AiSeatLocks;
        if (locks is not { Length: 4 })
        {
            return -2;
        }

        int tier = locks[HumanSeat == 0 ? 1 : 0];
        for (int i = 0; i < 4; i++)
        {
            if (i == HumanSeat)
            {
                continue;
            }

            if (locks[i] != tier)
            {
                return -2;
            }
        }

        return tier;
    }

    private string TableDifficultyName() => TableDifficultyTier() switch
    {
        -2 => L.T("app.ui.settings.difficulty.custom"),
        -1 => L.T("app.ui.settings.difficulty.random"),
        0 => L.T("app.ui.settings.difficulty.easy"),
        1 => L.T("app.ui.settings.difficulty.normal"),
        2 => L.T("app.ui.settings.difficulty.hard"),
        3 => L.T("app.ui.settings.difficulty.shura"),
        4 => "7Neuu",
        _ => L.T("app.ui.settings.difficulty.random"),
    };

    private void CycleTableDifficulty()
    {
        int[] tiers = { -1, 0, 1, 2, 3, 4 };
        int current = TableDifficultyTier();
        int index = Array.IndexOf(tiers, current);
        int next = tiers[(index + 1 + tiers.Length) % tiers.Length];
        _settings.AiSeatLocks = new[] { HumanSeat == 0 ? -1 : next, next, next, next };
        if (HumanSeat != 0)
        {
            _settings.AiSeatLocks[HumanSeat] = -1;
        }

        Save();
    }

    /// <summary>权重展示：<c>30/45/20/5</c>（简单/普通/困难/修罗）。</summary>
    private string WeightText()
    {
        var weights = _settings.AiWeights;
        return weights is { Length: 4 } ? string.Join("/", weights) : "30/45/20/5";
    }

    /// <summary>锁档名：-1 = 随机，0..3 = 简单 / 普通 / 困难 / 修罗，4 = 7Neuu。</summary>
    private static string LockName(int value) => value switch
    {
        0 => L.T("app.ui.settings.difficulty.easy"),
        1 => L.T("app.ui.settings.difficulty.normal"),
        2 => L.T("app.ui.settings.difficulty.hard"),
        3 => L.T("app.ui.settings.difficulty.shura"),
        4 => "7Neuu",
        _ => L.T("app.ui.settings.difficulty.random"),
    };

    private int SeatLock(int seat)
    {
        var locks = _settings.AiSeatLocks;
        return locks is { Length: 4 } ? locks[seat] : -1;
    }

    /// <summary>←→ 循环：随机 → 简单 → 普通 → 困难 → 修罗 → 7Neuu → 随机。</summary>
    private void CycleSeatLock(int seat, int direction)
    {
        if (_settings.AiSeatLocks is not { Length: 4 })
        {
            _settings.AiSeatLocks = new[] { -1, -1, -1, -1 };
        }

        _settings.AiSeatLocks[seat] = NextLockValue(_settings.AiSeatLocks[seat], direction);
        Save();
    }

    /// <summary>锁档的循环取值：随机 → 简单 → 普通 → 困难 → 修罗 → 7Neuu → 随机。</summary>
    internal static int NextLockValue(int current, int direction)
    {
        if (current is < -1 or > 4)
        {
            current = -1;
        }

        int next = current + (direction >= 0 ? 1 : -1);
        if (next > 4)
        {
            return -1;
        }

        return next < -1 ? 4 : next;
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

    // ─────────────────────────────────────────────────────────────
    // 7Neuu 自动追役原型：五项风格权重
    // ─────────────────────────────────────────────────────────────

    private double GetStyle(StyleField field) => field switch
    {
        StyleField.Attack => _settings.AiStyle.Attack,
        StyleField.Defense => _settings.AiStyle.Defense,
        StyleField.Meld => _settings.AiStyle.Meld,
        StyleField.Value => _settings.AiStyle.Value,
        _ => _settings.AiStyle.Pursuit,
    };

    private void SetStyle(StyleField field, double value)
    {
        double clamped = Math.Clamp(Math.Round(value, 1), AiStyle.Min, AiStyle.Max);
        _settings.AiStyle = field switch
        {
            StyleField.Attack => _settings.AiStyle with { Attack = clamped },
            StyleField.Defense => _settings.AiStyle with { Defense = clamped },
            StyleField.Meld => _settings.AiStyle with { Meld = clamped },
            StyleField.Value => _settings.AiStyle with { Value = clamped },
            _ => _settings.AiStyle with { Pursuit = clamped },
        };
    }

    /// <summary>0..2 滑条，0.1 一档；拖动时实时改值（不写盘），松手 / 键盘步进才 <see cref="Save"/>。</summary>
    private void AddStyleSlider(StyleField field, string labelKey)
    {
        var slider = new Slider(
            (float)AiStyle.Min,
            (float)AiStyle.Max,
            (float)GetStyle(field),
            value => SetStyle(field, value),
            Save);
        _styleSliders[(int)field] = slider;
        _rows.Add(new Row(
            labelKey,
            () => GetStyle(field).ToString("0.0", CultureInfo.InvariantCulture),
            () => Math.Abs(GetStyle(field) - AiStyle.Neutral) > 1e-6,
            direction => slider.Step(direction * 0.1f),
            () => (int)MathF.Round(slider.Value * 10f),
            slider,
            SettingsGroup.Ai,
            showSliderValue: true));
    }

    private string StylePresetName()
    {
        int index = _settings.AiStyle.MatchingPreset();
        return index >= 0 ? L.T(StylePresetKeys[index]) : L.T("app.ui.settings.aiStyle.custom");
    }

    /// <summary>←→ 在三个预设之间循环；当前是自定义时，→ 落到稳守、← 落到追役。</summary>
    private void CycleStylePreset(int direction)
    {
        int count = AiStyle.Presets.Count;
        int current = _settings.AiStyle.MatchingPreset();
        int next = current < 0
            ? (direction >= 0 ? 0 : count - 1)
            : (current + (direction >= 0 ? 1 : -1) + count) % count;
        ApplyStylePreset(next);
    }

    private void ApplyStylePreset(int index)
    {
        if (index < 0 || index >= AiStyle.Presets.Count)
        {
            return;
        }

        _settings.AiStyle = AiStyle.Presets[index];
        for (int field = 0; field < _styleSliders.Length; field++)
        {
            _styleSliders[field]?.SetValue((float)GetStyle((StyleField)field), notify: false);
        }

        Save();
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

    private string TextScaleName() => _settings.UiTextScale switch
    {
        >= 1.29 => "130%",
        >= 1.14 => "115%",
        _ => "100%",
    };

    private string LanguageName() => _settings.Language switch
    {
        "en-US" => "English",
        "ja-JP" => "日本語",
        _ => "中文",
    };

    private void CycleLanguage()
    {
        _settings.Language = _settings.Language switch
        {
            "zh-CN" => "en-US",
            "en-US" => "ja-JP",
            _ => "zh-CN",
        };
        UiText.SetLanguage(_settings.Language);
        Save();
    }

    private void CycleTextScale()
    {
        double[] choices = { 1.0, 1.15, 1.3 };
        int index = Array.FindIndex(choices, x => Math.Abs(x - _settings.UiTextScale) < 0.01);
        _settings.UiTextScale = choices[(index + 1 + choices.Length) % choices.Length];
        Save();
    }

    private void Toggle(Func<bool> read, Action<bool> write)
    {
        write(!read());
        Save();
    }

    /// <summary>音量滑条：左右/点击按 10 一档，0 = 静音。</summary>
      private void SetVolumeLive(bool bgm, float value)
      {
          int v = Math.Clamp((int)MathF.Round(value), 0, 100);
          if (bgm)
          {
              _settings.BgmVolume = v;
              _sound?.BgmVolume = v;
          }
          else
          {
              _settings.SfxVolume = v;
              _sound?.SfxVolume = v;
              if (v > 0 && v % 10 == 0)
              {
                  _sound?.Play(SoundEffects.Cue.Call);
              }
          }

      }

    private void SetVolume(bool bgm, int direction)
    {
        int step = direction >= 0 ? 10 : -10;
        if (bgm)
        {
            _settings.BgmVolume = Math.Clamp(_settings.BgmVolume + step, 0, 100);
            _sound?.BgmVolume = _settings.BgmVolume;
        }
        else
        {
            _settings.SfxVolume = Math.Clamp(_settings.SfxVolume + step, 0, 100);
            _sound?.SfxVolume = _settings.SfxVolume;
            if (_settings.SfxVolume > 0)
            {
                _sound?.Play(SoundEffects.Cue.Call);
            }
        }

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

    private static string OnOff(bool value) => value
        ? L.T("app.ui.settings.on")
        : L.T("app.ui.settings.off");

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
        public Row(string label, Func<string> value, Func<bool> isOn, Action<int> change,
            Func<int>? percent = null, Slider? slider = null, SettingsGroup group = SettingsGroup.Basic,
            bool showSliderValue = false)
        {
            Label = label;
            Value = value;
            IsOn = isOn;
            Change = change;
            Percent = percent;
            Slider = slider;
            Group = group;
            ShowSliderValue = showSliderValue;
        }

        public string Label { get; }

        public Func<string> Value { get; }

        public Func<bool> IsOn { get; }

        public Action<int> Change { get; }

        /// <summary>非 null 时该行画成 0..100 滑条，不显示文字数值。</summary>
        public Func<int>? Percent { get; }

          /// <summary>非 null 时该行绘制并可拖动独立滑条。</summary>
          public Slider? Slider { get; }

        /// <summary>滑条行是否在右侧额外显示 <see cref="Value"/> 文字（风格权重用）。</summary>
        public bool ShowSliderValue { get; }

      public SettingsGroup Group { get; set; }
    }
}
