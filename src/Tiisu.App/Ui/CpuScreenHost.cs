using System;
using System.Windows.Forms;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Tiisu.App.Assets;

namespace Tiisu.App.Ui;

/// <summary>CPU 光栅宿主（原路径）：<see cref="SKControl"/> 的 software surface。</summary>
internal sealed class CpuScreenHost : SKControl, IKeyRouter
{
    private readonly ScreenHostCore _core;

    public CpuScreenHost(TileAtlas? atlas)
    {
        Dock = DockStyle.Fill;
        _core = new ScreenHostCore(atlas, Invalidate, () => FindForm()?.Close());
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        _core.Draw(e.Surface.Canvas, e.Info);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = _core.PointerMove(e.X, e.Y);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Cursor = Cursors.Default;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        _core.Click(e.X, e.Y);
    }

    /// <summary>窗口层转发：即使控件没焦点也能响应按键。</summary>
    public bool RouteKey(Keys key, bool shift) => _core.KeyDown(key, shift);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _core.PointerDown(e.X, e.Y);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
        {
            _core.PointerUp(e.X, e.Y);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _core.Wheel(e.X, e.Y, e.Delta, (ModifierKeys & Keys.Shift) == Keys.Shift);
    }

    /// <summary>Tab / F2 / 方向键默认会被 WinForms 当导航键吞掉，这里声明为输入键。</summary>
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) switch
    {
        Keys.Tab or Keys.F2 or Keys.Left or Keys.Right or Keys.Up or Keys.Down => true,
        _ => base.IsInputKey(keyData),
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_core.KeyDown(e.KeyCode, e.Shift))
        {
            e.Handled = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _core.Dispose();
        }

        base.Dispose(disposing);
    }
}
