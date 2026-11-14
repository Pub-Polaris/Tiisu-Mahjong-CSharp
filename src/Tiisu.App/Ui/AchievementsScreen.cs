using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Tiisu.App.Rendering;
using Tiisu.App.Runtime;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>本机成就页：从 StatsSummary 现算，不单独存文件。</summary>
public sealed class AchievementsScreen : Screen
{
    private readonly Action _back;
    private IReadOnlyList<Achievement> _items;
    private RectF _backButton;
    private bool _backHover;

    private static readonly Color Gold = Color.Rgb(242, 201, 76);
    private static readonly Color Text = Color.Rgb(222, 230, 220);
    private static readonly Color Muted = Color.Rgb(150, 168, 158);

    public AchievementsScreen(Action back) : base("achievements")
    {
        _back = back;
        Reload();
    }

    /// <summary>重新读取统计并重算成就；进入成就页时调用。</summary>
    public void Reload() => _items = Achievements.From(StatsSummary.Load(new StatsStore()));

    public override void Draw(ICanvas c, double time)
    {
        c.FillLinearGradient(new RectF(0, 0, Width, Height), 0,
            Color.Rgb(18, 26, 30), Color.Rgb(8, 12, 16));
        c.DrawText(UiText.Tr("成就"), new RectF(0, 32, Width, 50), Gold,
            new TextStyle(36, Bold: true, Center: true));
        c.DrawText(UiText.Tr("已解锁 " + Achievements.UnlockedCount(_items) + " / " + _items.Count),
            new RectF(0, 82, Width, 24), Muted, new TextStyle(15, Center: true));

        for (int i = 0; i < _items.Count; i++)
        {
            Achievement item = _items[i];
            int column = i % 2;
            int row = i / 2;
            var rect = new RectF(100 + column * 540, 126 + row * 100, 500, 84);
            c.FillRoundRect(rect, 12, item.Unlocked ? Color.Rgb(30, 46, 40) : Color.Rgb(22, 30, 28));
            c.StrokeRoundRect(rect, 12,
                item.Unlocked ? Gold : Color.Rgb(66, 82, 74), item.Unlocked ? 1.4f : 1.0f);
            c.DrawText(UiText.Tr(item.Title), new RectF(rect.X + 18, rect.Y + 12, rect.W - 36, 28),
                item.Unlocked ? Color.Rgb(255, 244, 214) : Muted, new TextStyle(19, Bold: true));
            string description = UiText.Tr(item.Description);
            if (!item.Unlocked)
            {
                description += " (" + item.Progress + ")";
            }

            c.DrawText(description, new RectF(rect.X + 18, rect.Y + 44, rect.W - 36, 22),
                item.Unlocked ? Text : Color.Rgb(120, 136, 126), new TextStyle(14));
            if (item.Unlocked)
            {
                c.DrawText(UiText.Tr("已解锁"), new RectF(rect.Right - 100, rect.Y + 12, 82, 22), Gold,
                    new TextStyle(13, Bold: true, Right: true));
            }
        }

        _backButton = new RectF(Width / 2f - 90, 650, 180, 42);
        c.FillRoundRect(_backButton, 10, _backHover ? Color.Rgb(52, 86, 78) : Color.Rgb(40, 58, 48));
        c.StrokeRoundRect(_backButton, 10, _backHover ? Gold : Color.Rgb(196, 168, 96), 1.3f);
        c.DrawText(UiText.Tr("返回战绩"), _backButton, Color.Rgb(242, 230, 190),
            new TextStyle(17, Bold: true, Center: true));
    }

    public override bool OnClick(float x, float y)
    {
        if (_backButton.Contains(x, y))
        {
            _back();
            return true;
        }

        return false;
    }

    public override bool OnPointerMove(float x, float y)
    {
        _backHover = _backButton.Contains(x, y);
        return _backHover;
    }

    public override bool OnKey(Keys key)
    {
        if (key is Keys.Escape or Keys.Enter or Keys.Space)
        {
            _back();
            return true;
        }

        return false;
    }
}
