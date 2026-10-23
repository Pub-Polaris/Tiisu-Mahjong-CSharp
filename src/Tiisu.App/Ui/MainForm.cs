using Tiisu.App.Assets;

namespace Tiisu.App.Ui;

/// <summary>主窗口：只做一件事 —— 把一个铺满的 ScreenHost 丢上去。</summary>
public sealed class MainForm : Form
{
    private readonly TileAtlas _atlas;

    public MainForm(TileAtlas atlas, bool forceCpuRender = false)
    {
        _atlas = atlas;

        Text = "七子麻将 · Tiisu Mahjong";

        // 牌桌是 1:1 方形画布，窗口默认也做成方的，否则上下/左右会留大片黑边。
        // 菜单屏仍是 1280×720，切过去时会在方形窗口里上下留边（可接受）。
        // 注意：Screen 在本命名空间里是界面基类，取显示器要用全名
        var work = System.Windows.Forms.Screen.FromPoint(Cursor.Position).WorkingArea;
        int side = Math.Min(1000, Math.Min(work.Width, work.Height) - 80);
        side = Math.Max(side, 640);

        ClientSize = new Size(side, side);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 640);
        BackColor = Color.Black;
        KeyPreview = true;

        // 退出程序只保留窗口关闭按钮；ESC 由各 Screen 处理成「返回」。
        // GPU（SKGLControl + GRContext）优先，不可用时工厂内部自动回退 CPU（SKControl）。
        Controls.Add(ScreenHostFactory.Create(atlas, forceCpuRender));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _atlas.Dispose();
        }

        base.Dispose(disposing);
    }
}
