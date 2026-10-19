using Tiisu.App.Assets;

namespace Tiisu.App.Ui;

/// <summary>主窗口：只做一件事 —— 把一个铺满的 ScreenHost 丢上去。</summary>
public sealed class MainForm : Form
{
    private readonly TileAtlas _atlas;

    public MainForm(TileAtlas atlas)
    {
        _atlas = atlas;

        Text = "七子麻将 · Tiisu Mahjong";
        ClientSize = new Size(1280, 720);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 540);
        BackColor = Color.Black;
        KeyPreview = true;

        Controls.Add(new ScreenHost(atlas));

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape && e.Handled == false)
            {
                Close();
            }
        };
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
