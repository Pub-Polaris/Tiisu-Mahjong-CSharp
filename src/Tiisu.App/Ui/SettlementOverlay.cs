using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tiisu.App.Rendering;
using Tiisu.App.Runtime;
using Tiisu.App.Table;
using Tiisu.Engine;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App.Ui;

/// <summary>
/// 局末结算覆盖层：深色遮罩 + 圆角面板，1200×1200 画布居中。
/// 数据全部来自 <see cref="RoundResult"/>（明细由引擎结算后现算）与最终快照，不碰引擎。
/// </summary>
public sealed class SettlementOverlay
{
    public const float CanvasW = 1200f;
    public const float CanvasH = 1200f;

    private const float PanelX = 436f;
    private const float PanelY = 46f;
    private const float PanelW = 748f;
    private const float Left = 472f;
    private const float Right = 1148f;

    /// <summary>左侧牌桌缩略图：1200×1200 场景按比例缩到 396×396（小窗口由宿主整体缩放）。</summary>
    private const float ThumbX = 18f;
    private const float ThumbSize = 396f;
    private const float ThumbPad = 3f;

    private static readonly Color Gold = Color.Rgb(214, 180, 104);
    private static readonly Color TextColor = Color.Rgb(232, 226, 206);
    private static readonly Color Muted = Color.Rgb(150, 164, 152);
    private static readonly Color PanelFill = Color.Rgb(16, 24, 22).WithAlpha(246);
    private static readonly Color Positive = Color.Rgb(150, 220, 160);
    private static readonly Color Negative = Color.Rgb(230, 140, 130);

    /// <summary>
    /// 画一屏结算。调用方负责只在本局已结束时调用。
    /// <paramref name="thumbnail"/> 负责在左侧缩略图区域内画牌桌（面板让出位置，不遮文字）。
    /// </summary>
    public void Draw(ICanvas c, RoundResult result, TableViewModel vm, Action<ICanvas, RectF>? thumbnail = null)
    {
        bool matchOver = vm.Phase == "match_over" || result.MatchOver;
        var details = result.Details ?? Array.Empty<SettlementDetail>();

        float contentEnd = details.Count switch
        {
            0 => 166f + 106f,
            1 => 166f + MeasureDetailBlock(details[0], 12),
            _ => 166f + Math.Max(MeasureDetailBlock(details[0], 10), MeasureDetailBlock(details[1], 10)),
        };
        float scoreY = contentEnd + 20f;
        float panelBottom = scoreY + 220f;
        float panelH = panelBottom - PanelY;

        c.FillRect(new RectF(0, 0, CanvasW, CanvasH), Color.Black.WithAlpha(190));

        // 面板垂直居中；内容坐标固定，整体平移。
        float dy = ((CanvasH - panelH) / 2f) - PanelY;
        if (PanelY + dy < 36f)
        {
            dy = 36f - PanelY;
        }

        // 左侧牌桌缩略图：高度跟着面板走，面板太矮时按比例缩。
        float thumbSize = Math.Min(ThumbSize, Math.Max(160f, panelH - 32f));
        float thumbY = PanelY + dy + ((panelH - thumbSize) / 2f);
        var thumb = new RectF(ThumbX, thumbY, thumbSize, thumbSize);
        c.FillRoundRect(thumb, 10, Color.Rgb(12, 18, 16));
        if (thumbnail is not null)
        {
            var inner = thumb.Inflate(-ThumbPad);
            c.Save();
            c.ClipRect(inner);
            thumbnail(c, inner);
            c.Restore();
        }

        c.StrokeRoundRect(thumb, 10, Gold, 1.5f);

        c.Save();
        c.Translate(0, dy);

        c.FillRoundRect(new RectF(PanelX, PanelY, PanelW, panelH), 18, PanelFill);
        c.StrokeRoundRect(new RectF(PanelX, PanelY, PanelW, panelH), 18, Gold, 2f);

        string title = matchOver ? "终局" : "本局结算";
        c.DrawText(title, new RectF(Left, 68, Right - Left, 44), Gold,
            new TextStyle(28, Bold: true, Center: false));

        string meta = "第 " + result.Round + " 局 · " + OutcomeLabel(result);
        c.DrawText(meta, new RectF(Left, 116, Right - Left, 28), Muted,
            new TextStyle(17, Center: false));

        if (details.Count == 0)
        {
            DrawNoDetail(c, result, 166f);
        }
        else if (details.Count == 1)
        {
            DrawDetailBlock(c, result, details[0], vm, Left, Right - Left, 166f, 12);
        }
        else
        {
            float gap = 26f;
            float width = (Right - Left - gap) / 2f;
            DrawDetailBlock(c, result, details[0], vm, Left, width, 166f, 10);
            DrawDetailBlock(c, result, details[1], vm, Left + width + gap, width, 166f, 10);
        }

        if (matchOver)
        {
            DrawRanking(c, vm, scoreY);
        }
        else
        {
            DrawScoreChanges(c, result, vm, scoreY);
        }

        string footer = matchOver ? "R / Enter 回菜单" : "Enter 下一局 · Esc 回菜单";
        c.DrawText(footer, new RectF(Left, panelBottom - 54f, Right - Left, 32), Muted,
            new TextStyle(17, Center: true));
        c.Restore();
    }

    /// <summary>与 <see cref="DrawDetailBlock"/> 的实际推进保持一致（用于先算面板高度）。</summary>
    private static float MeasureDetailBlock(SettlementDetail detail, int maxYakuRows)
    {
        int rows = detail.Yaku.Count;
        int shown = Math.Min(rows, maxYakuRows);
        float height = 386f + (shown * 26f);
        if (rows > shown)
        {
            height += 24f;
        }

        return height;
    }

    private static float DrawDetailBlock(
        ICanvas c,
        RoundResult result,
        SettlementDetail detail,
        TableViewModel vm,
        float x,
        float width,
        float y,
        int maxYakuRows)
    {
        float right = x + width;
        string who = SeatName(vm, detail.WinnerIndex);
        var seat = vm.Seats.FirstOrDefault(s => s.Index == detail.WinnerIndex);
        if (seat is { IsDealer: true })
        {
            who += "（庄）";
        }

        string how = detail.SelfDraw ? "自摸" : "荣和";
        if (!detail.SelfDraw && detail.PayerIndex is int payer)
        {
            how += " · 放铳者 " + SeatName(vm, payer);
        }

        c.DrawText(who + " · " + how, new RectF(x, y, width, 30), Gold,
            new TextStyle(19, Bold: true, Center: false));
        y += 34f;

        c.DrawText("役种", new RectF(x, y, width - 78, 22), Muted, new TextStyle(14, Center: false));
        c.DrawText("赋", new RectF(right - 78, y, 78, 22), Muted,
            new TextStyle(14, Center: false, Right: true));
        y += 24f;

        int rows = detail.Yaku.Count;
        int shown = Math.Min(rows, maxYakuRows);
        for (int i = 0; i < shown; i++)
        {
            var yaku = detail.Yaku[i];
            string label = yaku.Name + FlagSuffix(yaku.Flag);
            var color = yaku.Flag is SettlementYakuFlag.Ma or SettlementYakuFlag.Tableless
                ? Color.Rgb(206, 190, 150)
                : TextColor;
            c.DrawText(label, new RectF(x + 6, y, width - 90, 24), color,
                new TextStyle(15, Center: false));
            c.DrawText(yaku.Fu.ToString(CultureInfo.InvariantCulture),
                new RectF(right - 78, y, 78, 24), TextColor,
                new TextStyle(15, Center: false, Right: true));
            y += 26f;
        }

        if (rows > shown)
        {
            c.DrawText("… 另有 " + (rows - shown) + " 项", new RectF(x + 6, y, width - 90, 22), Muted,
                new TextStyle(13, Center: false));
            y += 24f;
        }

        y += 8f;
        c.FillRect(new RectF(x, y, width, 1), Color.Rgb(74, 92, 82));
        y += 12f;

        y = SummaryRow(c, x, width, y, "总赋（含赐马）", detail.TotalFu.ToString(CultureInfo.InvariantCulture));
        y = SummaryRow(c, x, width, y, "查表赋", detail.TableSafeFu.ToString(CultureInfo.InvariantCulture));
        bool fixedPoints = detail.Yaku.Any(yaku => yaku.Flag == SettlementYakuFlag.FixedPoints);
        y = SummaryRow(c, x, width, y, fixedPoints ? "固定点数（子家）" : "查表点数（子家）",
            detail.BasePoints.ToString(CultureInfo.InvariantCulture));
        y = SummaryRow(c, x, width, y, "表外赋 ×700", detail.TablelessBonus.ToString(CultureInfo.InvariantCulture));
        y = SummaryRow(c, x, width, y, "点数加项", detail.PointsBonus.ToString(CultureInfo.InvariantCulture));
        y = SummaryRow(c, x, width, y, "子家点数", detail.SubPoints.ToString(CultureInfo.InvariantCulture));
        string dealerValue = detail.DealerMultiplierApplied
            ? MahjongEngine.DealerBonus(detail.SubPoints).ToString(CultureInfo.InvariantCulture)
            : "—";
        y = SummaryRow(c, x, width, y, "庄家 ×1.5", dealerValue);
        y = SummaryRow(c, x, width, y, "本场 +700×" + detail.HonbaCount, detail.HonbaBonus.ToString(CultureInfo.InvariantCulture));
        y = FinalRow(c, x, width, y, "最终点数", detail.FinalPoints.ToString(CultureInfo.InvariantCulture));

        var paymentBox = new RectF(x, y + 4f, width, 38f);
        c.FillRoundRect(paymentBox, 9, Color.Rgb(44, 62, 50).WithAlpha(235));
        c.StrokeRoundRect(paymentBox, 9, Gold, 1.2f);
        c.DrawText(detail.PaymentText, paymentBox, Gold, new TextStyle(17, Bold: true, Center: true));
        y += 50f;

        return y;
    }

    private static float DrawNoDetail(ICanvas c, RoundResult result, float y)
    {
        if (result.Kind == "tiisuin")
        {
            c.DrawText("大七星", new RectF(Left, y + 6, Right - Left, 46), Gold,
                new TextStyle(34, Bold: true, Center: true));
            c.DrawText("七种字牌七对子 · 整场结束", new RectF(Left, y + 56, Right - Left, 30), TextColor,
                new TextStyle(19, Center: true));
            return y + 104f;
        }

        c.DrawText(DrawReasonText(result.DrawReason), new RectF(Left, y + 6, Right - Left, 46), Gold,
            new TextStyle(32, Bold: true, Center: true));
        c.DrawText("流局 · 不结算点数", new RectF(Left, y + 58, Right - Left, 28), Muted,
            new TextStyle(17, Center: true));
        return y + 106f;
    }

    private static void DrawScoreChanges(ICanvas c, RoundResult result, TableViewModel vm, float y)
    {
        c.DrawText("分数变化", new RectF(Left, y, Right - Left, 26), Muted,
            new TextStyle(15, Center: false));
        y += 30f;

        for (int i = 0; i < vm.Seats.Count && i < result.Scores.Length; i++)
        {
            var seat = vm.Seats[i];
            int after = result.Scores[i];
            int before = result.ScoresBefore is { } scoresBefore && i < scoresBefore.Count ? scoresBefore[i] : after;
            string transition = before.ToString(CultureInfo.InvariantCulture) + " → " +
                                after.ToString(CultureInfo.InvariantCulture);
            DrawScoreRow(c, seat, transition, after - before, y);
            y += 30f;
        }
    }

    private static void DrawRanking(ICanvas c, TableViewModel vm, float y)
    {
        c.DrawText("最终排名", new RectF(Left, y, Right - Left, 26), Muted,
            new TextStyle(15, Center: false));
        y += 30f;

        var ranked = vm.Seats.OrderByDescending(s => s.Score).ToList();
        for (int i = 0; i < ranked.Count; i++)
        {
            var seat = ranked[i];
            string name = (i + 1) + ". " + seat.Name + (seat.IsDealer ? "（庄）" : string.Empty);
            var color = i == 0 ? Gold : TextColor;
            c.DrawText(name, new RectF(Left + 10, y, 360, 28), color,
                new TextStyle(i == 0 ? 18 : 17, Bold: i == 0, Center: false));
            c.DrawText(seat.Score.ToString(CultureInfo.InvariantCulture),
                new RectF(Right - 190, y, 180, 28), color,
                new TextStyle(i == 0 ? 18 : 17, Bold: i == 0, Center: false, Right: true));
            y += 30f;
        }
    }

    private static void DrawScoreRow(ICanvas c, SeatView seat, string transition, int delta, float y)
    {
        c.DrawText(seat.Name, new RectF(Left + 10, y, 300, 26), TextColor,
            new TextStyle(16, Center: false));

        c.DrawText(transition, new RectF(Left + 320, y, 400, 26), TextColor,
            new TextStyle(16, Center: false));

        string deltaText = delta > 0 ? "+" + delta : delta < 0 ? delta.ToString(CultureInfo.InvariantCulture) : "0";
        var color = delta > 0 ? Positive : delta < 0 ? Negative : Muted;
        c.DrawText(deltaText, new RectF(Right - 190, y, 180, 26), color,
            new TextStyle(16, Bold: true, Center: false, Right: true));
    }

    private static float SummaryRow(ICanvas c, float x, float width, float y, string label, string value)
    {
        float right = x + width;
        c.DrawText(label, new RectF(x + 6, y, width - 128, 26), Muted,
            new TextStyle(15, Center: false));
        c.DrawText(value, new RectF(right - 122, y, 116, 26), TextColor,
            new TextStyle(15, Center: false, Right: true));
        return y + 28f;
    }

    private static float FinalRow(ICanvas c, float x, float width, float y, string label, string value)
    {
        float right = x + width;
        c.DrawText(label, new RectF(x + 6, y, width - 160, 30), Gold,
            new TextStyle(17, Bold: true, Center: false));
        c.DrawText(value, new RectF(right - 200, y, 194, 30), Gold,
            new TextStyle(19, Bold: true, Center: false, Right: true));
        return y + 34f;
    }

    private static string FlagSuffix(SettlementYakuFlag flag) => flag switch
    {
        SettlementYakuFlag.Optional => "（可选）",
        SettlementYakuFlag.Tableless => "（表外）",
        SettlementYakuFlag.Ma => "（赐马）",
        SettlementYakuFlag.FixedPoints => "（固定点数）",
        _ => string.Empty,
    };

    private static string SeatName(TableViewModel vm, int index)
    {
        var seat = vm.Seats.FirstOrDefault(s => s.Index == index);
        return seat?.Name ?? "玩家" + index;
    }

    private static string OutcomeLabel(RoundResult result) => result.Kind switch
    {
        "tsumo" => "自摸",
        "ron" => "荣和",
        "tiisuin" => "大七星",
        "special_flow" => "流局",
        _ => "流局",
    };

    private static string DrawReasonText(string? code) => code switch
    {
        "exhaustive" => "荒牌流局",
        "four_wind_repeat" => "四风连打",
        "nine_orphans" => "九种九牌",
        "thirteen_unrelated" => "十三不靠",
        "triple_ron" => "三家和牌",
        "triple_ron_chankan" => "三家和牌（抢杠）",
        "special" => "特殊流局",
        null or "" => "流局",
        _ => code,
    };
}
