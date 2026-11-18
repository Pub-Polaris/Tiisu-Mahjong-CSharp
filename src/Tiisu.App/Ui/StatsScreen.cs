using System;
using System.Globalization;
using System.Windows.Forms;
using Tiisu.App.Rendering;
using Tiisu.App.Runtime;
using Tiisu.Localization;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>本机战绩：总局数、和牌率、自摸/荣和、流局与四家累计。</summary>
public sealed class StatsScreen : Screen
{
    private readonly Action _back;
    private readonly Action? _openAchievements;
    private StatsSummary _stats;
    private RectF _backButton;
    private RectF _achievementsButton;
    private bool _backHover;
    private bool _achievementsHover;

    private static readonly Color Gold = Color.Rgb(242, 201, 76);
    private static readonly Color Text = Color.Rgb(222, 230, 220);
    private static readonly Color Muted = Color.Rgb(150, 168, 158);

    public StatsScreen(Action back, Action? openAchievements = null) : base("stats")
    {
        _back = back;
        _openAchievements = openAchievements;
        Reload();
    }

    /// <summary>重新读取本机统计；进入战绩页时调用，避免显示启动时缓存。</summary>
    public void Reload() => _stats = StatsSummary.Load(new StatsStore());

    public override void OnLanguageChanged() => Reload();

    public override void Draw(ICanvas c, double time)
    {
        c.FillLinearGradient(new RectF(0, 0, Width, Height), 0,
            Color.Rgb(18, 26, 30), Color.Rgb(8, 12, 16));

        c.DrawText(L.T("app.ui.stats.title"), new RectF(0, 32, Width, 50), Gold,
            new TextStyle(36, Bold: true, Center: true));
        c.DrawText(L.T("app.ui.stats.totalRounds", ("rounds", _stats.Rounds)), new RectF(0, 82, Width, 24), Muted,
            new TextStyle(15, Center: true));

        DrawCard(c, new RectF(100, 126, 250, 90), "app.ui.stats.card.rounds", _stats.Rounds.ToString(CultureInfo.InvariantCulture));
        DrawCard(c, new RectF(370, 126, 250, 90), "app.ui.stats.card.winRate", Percent(_stats.WinRate));
        DrawCard(c, new RectF(640, 126, 250, 90), "app.ui.stats.card.tsumoRon",
            _stats.TsumoWins + " / " + _stats.RonWins);
        DrawCard(c, new RectF(910, 126, 250, 90), "app.ui.stats.card.drawRate", Percent(_stats.DrawRate));

        DrawCard(c, new RectF(100, 238, 250, 90), "app.ui.stats.card.tiisuin", _stats.TiisuinCount.ToString(CultureInfo.InvariantCulture));
        DrawCard(c, new RectF(370, 238, 250, 90), "app.ui.stats.card.witness", _stats.WitnessCount.ToString(CultureInfo.InvariantCulture));
        DrawCard(c, new RectF(640, 238, 250, 90), "app.ui.stats.card.points",
            _stats.CumulativePoints.ToString("+0;-0;0", CultureInfo.InvariantCulture));
        DrawCard(c, new RectF(910, 238, 250, 90), "app.ui.stats.card.specialDraw",
            _stats.SpecialFlowRounds.ToString(CultureInfo.InvariantCulture));

        var table = new RectF(100, 358, 1060, 244);
        c.FillRoundRect(table, 14, Color.Rgb(24, 36, 32));
        c.StrokeRoundRect(table, 14, Color.Rgb(74, 96, 84), 1.2f);

        if (_stats.Players.Count == 0)
        {
            c.DrawText(L.T(_stats.Rounds == 0 ? "app.ui.stats.empty.noRounds" : "app.ui.stats.empty.noPlayers"),
                new RectF(table.X, table.CenterY - 18, table.W, 28), Muted,
                new TextStyle(18, Center: true));
        }
        else
        {
            float x = table.X + 24;
            float y = table.Y + 16;
            c.DrawText(L.T("app.ui.stats.table.player"), new RectF(x, y, 260, 24), Gold, new TextStyle(15, Bold: true));
            c.DrawText(L.T("app.ui.stats.table.wins"), new RectF(x + 280, y, 80, 24), Gold, new TextStyle(15, Bold: true, Right: true));
            c.DrawText(L.T("app.ui.stats.table.tiisuin"), new RectF(x + 370, y, 160, 24), Gold, new TextStyle(15, Bold: true, Right: true));
            c.DrawText(L.T("app.ui.stats.table.witness"), new RectF(x + 540, y, 100, 24), Gold, new TextStyle(15, Bold: true, Right: true));
            c.DrawText(L.T("app.ui.stats.table.points"), new RectF(x + 650, y, 190, 24), Gold, new TextStyle(15, Bold: true, Right: true));

            y += 34;
            foreach (PlayerStats player in _stats.Players)
            {
                if (y > table.Bottom - 28)
                {
                    break;
                }

                c.DrawText(UiText.Tr(player.Name), new RectF(x, y, 260, 24), Text, new TextStyle(16));
                c.DrawText(player.Wins.ToString(CultureInfo.InvariantCulture),
                    new RectF(x + 280, y, 80, 24), Text, new TextStyle(16, Right: true));
                c.DrawText(player.Tiisuin.ToString(CultureInfo.InvariantCulture),
                    new RectF(x + 370, y, 160, 24), Text, new TextStyle(16, Right: true));
                c.DrawText(player.Witness.ToString(CultureInfo.InvariantCulture),
                    new RectF(x + 540, y, 100, 24), Text, new TextStyle(16, Right: true));
                c.DrawText(player.Points.ToString("+0;-0;0", CultureInfo.InvariantCulture),
                    new RectF(x + 650, y, 190, 24), Text, new TextStyle(16, Right: true));
                y += 30;
            }
        }

        _backButton = new RectF(Width / 2f - 190, 628, 180, 44);
        DrawButton(c, _backButton, "app.ui.stats.back", _backHover);
        _achievementsButton = new RectF(Width / 2f + 10, 628, 180, 44);
        DrawButton(c, _achievementsButton, "app.ui.stats.achievements", _achievementsHover);
    }

    private static string Percent(double value) =>
        (value * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static void DrawCard(ICanvas c, RectF rect, string label, string value)
    {
        c.FillRoundRect(rect, 12, Color.Rgb(26, 38, 34));
        c.StrokeRoundRect(rect, 12, Color.Rgb(74, 96, 84), 1.1f);
        c.DrawText(L.T(label), new RectF(rect.X + 16, rect.Y + 12, rect.W - 32, 22), Muted,
            new TextStyle(14));
        c.DrawText(value, new RectF(rect.X + 16, rect.Y + 42, rect.W - 32, 34), Text,
            new TextStyle(24, Bold: true));
    }

    private static void DrawButton(ICanvas c, RectF rect, string label, bool hover)
    {
        c.FillRoundRect(rect, 10, hover ? Color.Rgb(52, 86, 78) : Color.Rgb(40, 58, 48));
        c.StrokeRoundRect(rect, 10, hover ? Gold : Color.Rgb(196, 168, 96), 1.3f);
        c.DrawText(L.T(label), rect, Color.Rgb(242, 230, 190), new TextStyle(17, Bold: true, Center: true));
    }

    public override bool OnClick(float x, float y)
    {
        if (_backButton.Contains(x, y))
        {
            _back();
            return true;
        }

        if (_achievementsButton.Contains(x, y) && _openAchievements is not null)
        {
            _openAchievements();
            return true;
        }

        return false;
    }

    public override bool OnPointerMove(float x, float y)
    {
        _backHover = _backButton.Contains(x, y);
        _achievementsHover = _openAchievements is not null && _achievementsButton.Contains(x, y);
        return _backHover || _achievementsHover;
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
