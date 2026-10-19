using System;
using System.Diagnostics;
using System.IO;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Tiisu.App.Assets;
using Tiisu.App.Rendering;

namespace Tiisu.App.Ui;

/// <summary>
/// 屏幕宿主：整个窗口唯一的控件。
/// 负责：DPI/letterbox、把输入换算到逻辑坐标、在两屏之间切换（带淡入淡出）。
/// </summary>
public sealed class ScreenHost : SKControl
{
    private readonly TileAtlas? _atlas;
    private readonly TableScreen _table;
    private readonly MenuScreen _menu;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer _frameTimer;

    private Screen _current;
    private Screen? _previous;
    private double _transition;      // 0 → 1
    private const double TransitionSeconds = 0.15;

    private float _scale = 1f;
    private float _offsetX;
    private float _offsetY;

    public ScreenHost(TileAtlas? atlas)
    {
        _atlas = atlas;
        Dock = DockStyle.Fill;

        _table = new TableScreen(atlas);
        var menuLayout = UiLayout.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "layout", "menu.json"));
        _menu = new MenuScreen(menuLayout, OnMenuAction);

        _current = _table;

        _frameTimer = new System.Windows.Forms.Timer { Interval = 16 };
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

    private bool OnMenuAction(string action)
    {
        switch (action)
        {
            case "resume":
                SwitchTo(_table);
                return true;
            case "quit":
                FindForm()?.Close();
                return true;
            default:
                return false;
        }
    }

    private void SwitchTo(Screen next)
    {
        if (ReferenceEquals(next, _current))
        {
            return;
        }

        _previous = _current;
        _current = next;
        _transition = 0;
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;
        canvas.Clear(SKColors.Black);

        _scale = MathF.Min(info.Width / Screen.Width, info.Height / Screen.Height);
        _offsetX = (info.Width - Screen.Width * _scale) / 2f;
        _offsetY = (info.Height - Screen.Height * _scale) / 2f;

        double t = _clock.Elapsed.TotalSeconds;
        if (_transition < 1)
        {
            _transition = Math.Min(1, _transition + 0.016 / TransitionSeconds);
        }

        canvas.Save();
        canvas.Translate(_offsetX, _offsetY);
        canvas.Scale(_scale);

        using (var ctx = new SkiaCanvas(canvas, _atlas))
        {
            if (_previous is not null && _transition < 1)
            {
                _previous.Draw(ctx, t);
                // 简单淡入：新屏用 alpha 叠一层黑
                float k = (float)_transition;
                ctx.FillRect(new RectF(0, 0, Screen.Width, Screen.Height),
                    Rendering.Color.Black.WithAlpha((byte)(255 * (1 - k))));
            }
            else
            {
                _previous = null;
            }

            _current.Draw(ctx, t);
        }

        canvas.Restore();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var (lx, ly) = ToLogical(e.X, e.Y);
        Cursor = _current.OnPointerMove(lx, ly) ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Cursor = Cursors.Default;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var (lx, ly) = ToLogical(e.X, e.Y);
        _current.OnClick(lx, ly);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape && ReferenceEquals(_current, _menu))
        {
            SwitchTo(_table);
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.M)
        {
            SwitchTo(ReferenceEquals(_current, _menu) ? _table : _menu);
            e.Handled = true;
            return;
        }

        _current.OnKey(e.KeyCode);
    }

    private (float X, float Y) ToLogical(int px, int py)
    {
        if (_scale <= 0)
        {
            return (-1, -1);
        }

        return ((px - _offsetX) / _scale, (py - _offsetY) / _scale);
    }
}
