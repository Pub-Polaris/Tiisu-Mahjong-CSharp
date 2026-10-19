using System;
using System.Diagnostics;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Tiisu.App.Assets;
using Tiisu.App.Rendering;

namespace Tiisu.App;

/// <summary>
/// 整个窗口只有这一个控件：铺满、自绘。
/// DPI 缩放由 SKControl 处理，内部按 1280×720 逻辑坐标绘制并等比居中（letterbox）。
/// </summary>
public sealed class BoardControl : SKControl
{
    private readonly BoardScene _scene;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer _frameTimer;
    private int _hoverIndex = -1;
    private float _scale = 1f;
    private float _offsetX;
    private float _offsetY;

    public BoardControl(TileAtlas? atlas)
    {
        _scene = new BoardScene(atlas);
        Dock = DockStyle.Fill;
        _frameTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60fps
        _frameTimer.Tick += (_, _) => Invalidate();
        _frameTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _frameTimer.Stop();
            _frameTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;
        canvas.Clear(SKColors.Black);

        _scale = MathF.Min(info.Width / BoardScene.LogicalWidth, info.Height / BoardScene.LogicalHeight);
        _offsetX = (info.Width - BoardScene.LogicalWidth * _scale) / 2f;
        _offsetY = (info.Height - BoardScene.LogicalHeight * _scale) / 2f;

        canvas.Save();
        canvas.Translate(_offsetX, _offsetY);
        canvas.Scale(_scale);

        using var ctx = new SkiaCanvas(canvas, _scene.Atlas);
        _scene.Draw(ctx, _clock.Elapsed.TotalSeconds, _hoverIndex);

        canvas.Restore();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = ToLogicalHit(e.X, e.Y);
        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        Cursor = Cursors.Default;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int index = ToLogicalHit(e.X, e.Y);
        if (index >= 0)
        {
            Text = "点击了手牌 #" + index; // 仅用于确认输入链路
        }
    }

    private int ToLogicalHit(int px, int py)
    {
        if (_scale <= 0)
        {
            return -1;
        }

        float lx = (px - _offsetX) / _scale;
        float ly = (py - _offsetY) / _scale;
        return BoardScene.HitTestHand(lx, ly);
    }
}
