using System;
using System.Windows.Forms;
using SkiaSharp.Views.Desktop;
using Tiisu.App.Assets;

namespace Tiisu.App.Ui;

/// <summary>
/// GPU 宿主：<see cref="SKGLControl"/> 提供 <see cref="GRContext"/> 与 OpenGL 后备缓冲，
/// 每帧把同一个 <see cref="ScreenHostCore.Draw"/> 画到 GL surface 上。
/// 尺寸变化 / 最小化恢复由控件在一次新的 paint 里重建 render target（<see cref="SKGLControl"/> 内部处理）。
/// </summary>
internal sealed class GlScreenHost : SKGLControl
{
    private readonly ScreenHostCore _core;

    public GlScreenHost(TileAtlas? atlas)
    {
        Dock = DockStyle.Fill;
        _core = new ScreenHostCore(atlas, Invalidate, () => FindForm()?.Close());
    }

    protected override void OnPaintSurface(SKPaintGLSurfaceEventArgs e)
    {
        _core.Draw(e.Surface.Canvas, e.Info);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (IsHandleCreated)
        {
            // 尺寸变了：下一次 paint 会按新尺寸重建 GL render target。
            Invalidate();
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && IsHandleCreated)
        {
            // 最小化恢复后补一帧，避免停在旧内容上。
            Invalidate();
        }
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
