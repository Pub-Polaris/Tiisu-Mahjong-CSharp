using System;
using System.Globalization;
using System.Windows.Forms;
using Tiisu.App.Rendering;
using Tiisu.App.Runtime;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>本机战绩：总局数、和牌率、自摸/荣和、流局与四家累计。</summary>
public sealed class StatsScreen : Screen
{
    private readonly Action _back;
    private readonly StatsSummary _stats;
    private RectF _backButton;
    private bool _backHover;

    private static readonly Color Gold = Color.Rgb(242, 201, 76);
    private static readonly Color Text = Color.Rgb(222, 230, 220);
    private static readonly Color Muted = Color.Rgb(150, 168, 158);

    public StatsScreen(Action back) : base("stats")
    {
        _back = back;
        _stats = StatsSummary.Load(new StatsStore());
    }

    public override void Draw(ICanvas c, double time)
    {
        c.FillLinearGradient(new RectF(0, 0, Width, Height), 0,
            Color.Rgb(18, 26, 30), Color.Rgb(8, 12, 16));

        c.DrawText("战绩统计", new RectF(0, 32, Width, 50), Gold,
            new TextStyle(36, Bold: true, Center: true));
        c.DrawText("本机累计 · " + _stats.Rounds + " 个小局", new RectF(0, 82, Width, 24), Muted,
            new TextStyle(15, Center: true));

        DrawCard(c, new RectF(100, 126, 250, 90), "总局数", _stats.Rounds.ToString(CultureInfo.InvariantCulture));
        DrawCard(c, new RectF(370, 126, 250, 90), "和牌率", Percent(_stats.WinRate));
        DrawCard(c, new RectF(640, 126, 250, 90), "自摸 / 荣和",
            _stats.TsumoWins + " / " + _stats.RonWins);
        DrawCard(c, new RectF(910, 126, 250, 90), "流局率", Percent(_stats.DrawRate));

        DrawCard(c, new RectF(100, 238, 250, 90), "大七星", _stats.TiisuinCount.ToString(CultureInfo.InvariantCulture));
        DrawCard(c, new RectF(370, 238, 250, 90), "见证", _stats.WitnessCount.ToString(CultureInfo.InvariantCulture));
        DrawCard(c, new RectF(640, 238, 250, 90), "累计点数",
            _stats.CumulativePoints.ToString("+0;-0;0", CultureInfo.InvariantCulture));
        DrawCard(c, new RectF(910, 238, 250, 90), "特殊流局",
            _stats.SpecialFlowRounds.ToString(CultureInfo.InvariantCulture));

        var table = new RectF(100, 358, 1060, 244);
        c.FillRoundRect(table, 14, Color.Rgb(24, 36, 32));
        c.StrokeRoundRect(table, 14, Color.Rgb(74, 96, 84), 1.2f);

        if (_stats.Players.Count == 0)
        {
            c.DrawText(_stats.Rounds == 0 ? "还没有对局记录，先打一局。" : "还没有玩家统计。",
                new RectF(table.X, table.CenterY - 18, table.W, 28), Muted,
                new TextStyle(18, Center: true));
        }
        else
        {
            float x = table.X + 24;
            float y = table.Y + 16;
            c.DrawText("玩家", new RectF(x, y, 390, 24), Gold, new TextStyle(15, Bold: true));
            c.DrawText("和牌", new RectF(x + 410, y, 100, 24), Gold, new TextStyle(15, Bold: true, Right: true));
            c.DrawText("大七星", new RectF(x + 540, y, 110, 24), Gold, new TextStyle(15, Bold: true, Right: true));
            c.DrawText("见证", new RectF(x + 680, y, 110, 24), Gold, new TextStyle(15, Bold: true, Right: true));
            c.DrawText("累计点数", new RectF(x + 820, y, 190, 24), Gold, new TextStyle(15, Bold: true, Right: true));

            y += 34;
            foreach (PlayerStats player in _stats.Players)
            {
                if (y > table.Bottom - 28)
                {
                    break;
                }

                c.DrawText(player.Name, new RectF(x, y, 390, 24), Text, new TextStyle(16));
                c.DrawText(player.Wins.ToString(CultureInfo.InvariantCulture),
                    new RectF(x + 410, y, 100, 24), Text, new TextStyle(16, Right: true));
                c.DrawText(player.Tiisuin.ToString(CultureInfo.InvariantCulture),
                    new RectF(x + 540, y, 110, 24), Text, new TextStyle(16, Right: true));
                c.DrawText(player.Witness.ToString(CultureInfo.InvariantCulture),
                    new RectF(x + 680, y, 110, 24), Text, new TextStyle(16, Right: true));
                c.DrawText(player.Points.ToString("+0;-0;0", CultureInfo.InvariantCulture),
                    new RectF(x + 820, y, 190, 24), Text, new TextStyle(16, Right: true));
                y += 30;
            }
        }

        _backButton = new RectF(Width / 2f - 90, 628, 180, 44);
        DrawButton(c, _backButton, "返回菜单", _backHover);
    }

    private static string Percent(double value) =>
        (value * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static void DrawCard(ICanvas c, RectF rect, string label, string value)
    {
        c.FillRoundRect(rect, 12, Color.Rgb(26, 38, 34));
        c.StrokeRoundRect(rect, 12, Color.Rgb(74, 96, 84), 1.1f);
        c.DrawText(label, new RectF(rect.X + 16, rect.Y + 12, rect.W - 32, 22), Muted,
            new TextStyle(14));
        c.DrawText(value, new RectF(rect.X + 16, rect.Y + 42, rect.W - 32, 34), Text,
            new TextStyle(24, Bold: true));
    }

    private static void DrawButton(ICanvas c, RectF rect, string label, bool hover)
    {
        c.FillRoundRect(rect, 10, hover ? Color.Rgb(52, 86, 78) : Color.Rgb(40, 58, 48));
        c.StrokeRoundRect(rect, 10, hover ? Gold : Color.Rgb(196, 168, 96), 1.3f);
        c.DrawText(label, rect, Color.Rgb(242, 230, 190), new TextStyle(17, Bold: true, Center: true));
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
