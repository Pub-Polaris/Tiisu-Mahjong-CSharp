using System;
using System.Windows.Forms;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Tiisu.App.Ui;

/// <summary>
/// 屏幕外的 GL 上下文探针：在屏幕外开一个不可见的小窗口 + <see cref="SKGLControl"/>，
/// 强制走一次真实绘制。成功说明「显存 / 驱动 / OpenGL」这条链路可用，失败则返回原因。
/// 启动时的自动回退判断和 <c>--bench-gl</c> 的离屏 GPU surface 都靠它。
/// </summary>
internal sealed class GlHostWindow : IDisposable
{
    private readonly Form _form;
    private readonly SKGLControl _control;

    private GlHostWindow(Form form, SKGLControl control)
    {
        _form = form;
        _control = control;
    }

    public SKGLControl Control => _control;

    public GRContext? Context => _control.GRContext;

    /// <summary>
    /// 尝试建立隐藏 GL 窗口并完成一次绘制。
    /// 成功返回实例（<paramref name="detail"/> 形如 <c>Intel(R) Arc(TM) B580 Graphics · OpenGL 3.3.0</c>）；
    /// 失败返回 null，原因写到 <paramref name="error"/>。
    /// </summary>
    public static GlHostWindow? TryCreate(out string detail, out string? error, int size = 320)
    {
        detail = string.Empty;
        error = null;
        Form? form = null;

        try
        {
            form = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new System.Drawing.Point(-4000, -4000),
                Size = new System.Drawing.Size(size, size),
                Opacity = 0,
            };

            var control = new SKGLControl { Dock = DockStyle.Fill };
            int paints = 0;
            control.PaintSurface += (_, e) =>
            {
                paints++;
                e.Surface.Canvas.Clear(SKColors.Black);
            };

            form.Controls.Add(control);
            form.Show();

            // 不跑消息循环也能用：Refresh() 会同步发 WM_PAINT，DoEvents 兜住构造期的消息。
            control.Refresh();
            Application.DoEvents();
            control.Refresh();

            if (paints == 0)
            {
                error = "隐藏窗口没有完成 GL 绘制";
                form.Dispose();
                return null;
            }

            if (control.GRContext is null)
            {
                error = "SKGLControl 未能创建 GRContext";
                form.Dispose();
                return null;
            }

            control.MakeCurrent();
            string renderer = GlString(StringName.Renderer) ?? "unknown";
            string version = GlString(StringName.Version) ?? "?";
            detail = $"{renderer} · OpenGL {version}";
            return new GlHostWindow(form, control);
        }
        catch (Exception ex)
        {
            error = ex.GetBaseException().Message;
            try
            {
                form?.Dispose();
            }
            catch
            {
                // 探测失败时清理再失败也不再掩盖原始原因。
            }

            return null;
        }
    }

    private static string? GlString(StringName name)
    {
        try
        {
            return GL.GetString(name);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _control.Dispose();
        _form.Dispose();
    }
}
