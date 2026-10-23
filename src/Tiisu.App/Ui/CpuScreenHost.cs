using System;
using System.Windows.Forms;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Tiisu.App.Assets;

namespace Tiisu.App.Ui;

/// <summary>CPU 光栅宿主（原路径）：<see cref="SKControl"/> 的 software surface。</summary>
internal sealed class CpuScreenHost : SKControl
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_core.KeyDown(e.KeyCode))
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
