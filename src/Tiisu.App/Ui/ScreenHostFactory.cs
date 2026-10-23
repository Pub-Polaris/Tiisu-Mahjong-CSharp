using System;
using System.Windows.Forms;
using Tiisu.App.Assets;

namespace Tiisu.App.Ui;

/// <summary>
/// 选择渲染后端：先试 GL（<see cref="GlScreenHost"/>），不可用就回退到原来的 CPU 光栅
/// （<see cref="CpuScreenHost"/>），并在控制台留一行日志说明走了哪条。
/// </summary>
internal static class ScreenHostFactory
{
    public static Control Create(TileAtlas? atlas, bool forceCpu)
    {
        if (forceCpu)
        {
            Console.WriteLine("[render] backend=cpu control=SKControl reason=\"--cpu-render 强制\"");
            return new CpuScreenHost(atlas);
        }

        using (GlHostWindow? probe = GlHostWindow.TryCreate(out string detail, out string? error))
        {
            if (probe is not null)
            {
                Console.WriteLine($"[render] backend=gpu control=SKGLControl renderer=\"{detail}\"");
                return new GlScreenHost(atlas);
            }

            Console.WriteLine($"[render] backend=cpu control=SKControl reason=\"GL 不可用：{error}\"");
        }

        return new CpuScreenHost(atlas);
    }
}
