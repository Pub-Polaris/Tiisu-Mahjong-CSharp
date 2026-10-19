using System;
using Tiisu.App.Assets;
using Tiisu.App.Rendering;
using Color = Tiisu.App.Rendering.Color;

namespace Tiisu.App;

/// <summary>
/// 验证用场景：一张牌桌 + 自家手牌 + 四家牌背 + 牌河 + 一个旋转的麻将牌。
/// 全部用 ICanvas 图元绘制（不依赖任何位图资源），用来验证：
/// 中文字体、高 DPI 缩放、动画帧率、鼠标命中测试。
/// </summary>
public sealed class BoardScene
{
    private readonly TileAtlas? _atlas;

    public BoardScene(TileAtlas? atlas = null) => _atlas = atlas;

    public TileAtlas? Atlas => _atlas;

    public const float LogicalWidth = 1280f;
    public const float LogicalHeight = 720f;

    private const float TileW = 54f;
    private const float TileH = 74f;
    private const float TileGap = 4f;
    private const float HandY = 604f;

    private static readonly string[] Hand =
    {
        "1m", "2m", "3m", "5m", "5m", "7m",
        "2p", "3p", "4p",
        "1s", "5s", "9s",
        "7z",
    };

    /// <summary>鼠标在逻辑坐标下命中第几张手牌；-1 表示没命中。</summary>
    public static int HitTestHand(float x, float y)
    {
        float total = Hand.Length * TileW + (Hand.Length - 1) * TileGap;
        float startX = (LogicalWidth - total) / 2f;
        for (int i = 0; i < Hand.Length; i++)
        {
            var r = new RectF(startX + i * (TileW + TileGap), HandY, TileW, TileH);
            if (r.Contains(x, y))
            {
                return i;
            }
        }

        return -1;
    }

    public void Draw(ICanvas c, double time, int hoverIndex)
    {
        DrawBackground(c);
        DrawTable(c);
        DrawOpponents(c);
        DrawDiscards(c);
        DrawCenterPanel(c);
        DrawHand(c, hoverIndex);
        DrawSpinningTile(c, time);
    }

    private void DrawBackground(ICanvas c)
    {
        c.FillLinearGradient(new RectF(0, 0, LogicalWidth, LogicalHeight), 0,
            Color.Rgb(18, 34, 40), Color.Rgb(6, 12, 16));
    }

    private void DrawTable(ICanvas c)
    {
        var table = new RectF(40, 96, 1200, 588);
        c.FillRoundRect(table.Inflate(6), 26, Color.Rgb(0, 0, 0).WithAlpha(120));
        c.FillLinearGradient(table, 22, Color.Rgb(26, 92, 84), Color.Rgb(14, 58, 54));
        c.StrokeRoundRect(table, 22, Color.Rgb(196, 168, 96), 2f);
        c.StrokeRoundRect(table.Inflate(-10), 16, Color.Rgb(255, 255, 255).WithAlpha(18), 1f);
    }

    private void DrawOpponents(ICanvas c)
    {
        // 对家 13 张牌背（横向排）
        float bw = 34, bh = 46, gap = 3;
        float total = 13 * bw + 12 * gap;
        float x0 = (LogicalWidth - total) / 2f;
        for (int i = 0; i < 13; i++)
        {
            DrawTileBack(c, new RectF(x0 + i * (bw + gap), 120, bw, bh));
        }

        // 左右两家牌背（竖向排）
        for (int i = 0; i < 13; i++)
        {
            DrawTileBack(c, new RectF(64, 170 + i * (bw + gap), bh, bw));
            DrawTileBack(c, new RectF(LogicalWidth - 64 - bh, 170 + i * (bw + gap), bh, bw));
        }
    }

    private void DrawTileBack(ICanvas c, RectF r)
    {
        c.FillRoundRect(r, 4, Color.Rgb(0, 0, 0).WithAlpha(90));
        c.FillLinearGradient(r, 4, Color.Rgb(46, 132, 92), Color.Rgb(24, 84, 58));
        c.StrokeRoundRect(r, 4, Color.Rgb(12, 40, 28), 1f);
    }

    private void DrawDiscards(ICanvas c)
    {
        // 中央牌河：四家各两排小牌
        float w = 26, h = 34, gap = 2;
        string[] river =
        {
            "3m", "4m", "8p", "1z", "9s", "2p",
            "5s", "6z", "7m", "1p", "4s", "2z",
        };

        for (int i = 0; i < river.Length; i++)
        {
            int row = i / 6;
            int col = i % 6;
            var r = new RectF(452 + col * (w + gap), 330 + row * (h + gap), w, h);
            DrawTileFace(c, r, river[i], dim: 0.72f);
        }

        for (int i = 0; i < river.Length; i++)
        {
            int row = i / 6;
            int col = i % 6;
            var r = new RectF(700 + col * (w + gap), 330 + row * (h + gap), w, h);
            DrawTileFace(c, r, river[river.Length - 1 - i], dim: 0.72f);
        }
    }

    private void DrawCenterPanel(ICanvas c)
    {
        var panel = new RectF(440, 200, 400, 96);
        c.FillRoundRect(panel, 14, Color.Rgb(0, 0, 0).WithAlpha(110));
        c.StrokeRoundRect(panel, 14, Color.Rgb(196, 168, 96).WithAlpha(150), 1.5f);
        c.DrawText("东一局 · 0 本场", new RectF(panel.X, panel.Y + 8, panel.W, 40),
            Color.Rgb(246, 232, 190), new TextStyle(30, Bold: true, Center: true, Shadow: true));
        c.DrawText("剩余 62 张 · 本场 0 · 供托 0", new RectF(panel.X, panel.Y + 50, panel.W, 30),
            Color.Rgb(210, 230, 220), new TextStyle(18, Center: true));
    }

    private void DrawHand(ICanvas c, int hoverIndex)
    {
        float total = Hand.Length * TileW + (Hand.Length - 1) * TileGap;
        float startX = (LogicalWidth - total) / 2f;

        for (int i = 0; i < Hand.Length; i++)
        {
            float x = startX + i * (TileW + TileGap);
            float y = HandY;
            if (i == hoverIndex)
            {
                y -= 14;
                c.FillRoundRect(new RectF(x - 4, y - 4, TileW + 8, TileH + 8), 10,
                    Color.Rgb(255, 214, 96).WithAlpha(70));
            }

            DrawTileFace(c, new RectF(x, y, TileW, TileH), Hand[i], dim: 1f);
        }
    }

    /// <summary>画一张正面牌：象牙底 + 顶面高光 + 花色文字。</summary>
    private void DrawTileFace(ICanvas c, RectF r, string id, float dim)
    {
        if (_atlas is { IsLoaded: true })
        {
            c.DrawTile(id, r, dim);
            return;
        }

        float a = dim;
        c.FillRoundRect(new RectF(r.X + 1, r.Y + 2, r.W, r.H), 5, Color.Rgb(0, 0, 0).WithAlpha((byte)(110 * a)));
        c.FillLinearGradient(r, 5,
            Color.Rgb((int)(252 * a), (int)(250 * a), (int)(238 * a)),
            Color.Rgb((int)(226 * a), (int)(220 * a), (int)(202 * a)));
        c.StrokeRoundRect(r, 5, Color.Rgb((int)(170 * a), (int)(162 * a), (int)(140 * a)), 1f);

        // 顶面（伪 3D 侧面高光）
        c.FillRoundRect(new RectF(r.X + 2, r.Y + 2, r.W - 4, 6), 3,
            Color.White.WithAlpha((byte)(160 * a)));

        var (glyph, color) = Glyph(id);
        float size = Math.Min(r.W, r.H) * (glyph.Length > 1 ? 0.42f : 0.6f);
        c.DrawText(glyph, new RectF(r.X, r.Y + r.H * 0.12f, r.W, r.H * 0.76f), color.WithAlpha((byte)(255 * a)),
            new TextStyle(size, Bold: true, Center: true));
    }

    private static (string Glyph, Color Color) Glyph(string id)
    {
        string num = id[..^1];
        char suit = id[^1];
        return suit switch
        {
            'm' => (num + "万", Color.Rgb(180, 32, 48)),
            'p' => (num + "筒", Color.Rgb(32, 96, 176)),
            's' => (num + "条", Color.Rgb(24, 128, 72)),
            _ => (num + "字", Color.Rgb(120, 60, 140)),
        };
    }

    private void DrawSpinningTile(ICanvas c, double time)
    {
        // 用横向缩放模拟绕 Y 轴旋转（验证动画帧率），并在下方给一个旋转的圆环
        float phase = (float)(time * 1.2) % 1f;
        float scaleX = MathF.Abs(MathF.Cos(phase * MathF.PI * 2f));
        const float w = 84f, h = 118f;
        float cx = LogicalWidth / 2f;
        float cy = 176f;

        c.Save();
        c.Translate(cx, cy);
        c.RotateDegrees((float)(time * 40));
        c.FillCircle(0, 0, 52, Color.Rgb(255, 214, 96).WithAlpha(50));
        c.Restore();

        float drawW = MathF.Max(w * scaleX, 10f);
        DrawTileFace(c, new RectF(cx - drawW / 2f, cy - h / 2f, drawW, h), "5z", dim: 1f);
    }
}
