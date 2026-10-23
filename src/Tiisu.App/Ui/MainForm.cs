using System;
using System.IO;
using System.Windows.Forms;
using Tiisu.App.Assets;

namespace Tiisu.App.Ui;

/// <summary>主窗口：把铺满的 ScreenHost 放上去，并在窗口层接管键盘（不依赖控件焦点）。</summary>
public sealed class MainForm : Form
{
    private readonly TileAtlas _atlas;
    private readonly Control _host;
    private readonly bool _logKeys = Environment.GetEnvironmentVariable("TIISU_LOG_KEYS") == "1";

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

        // 退出程序：菜单「退出」按钮 / 窗口关闭按钮；ESC 由各 Screen 处理成「返回」。
        // GPU（SKGLControl + GRContext）优先，不可用时工厂内部自动回退 CPU（SKControl）。
        _host = ScreenHostFactory.Create(atlas, forceCpuRender);
        Controls.Add(_host);

        // 启动/激活时把焦点交给画布，否则键盘事件没有落点。
        Shown += (_, _) => _host.Focus();
        Activated += (_, _) => _host.Focus();
    }

    /// <summary>窗口层接管按键：KeyPreview 保证这里先收到，再转给当前 Screen。</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (_logKeys)
        {
            Log($"key={e.KeyCode} shift={e.Shift}");
        }

        if (_host is IKeyRouter router && router.RouteKey(e.KeyCode, e.Shift))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private static void Log(string line)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "tiisu-keys.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch
        {
            // 调试日志失败不影响游戏
        }
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
