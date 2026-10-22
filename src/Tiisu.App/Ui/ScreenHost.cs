using System;
using System.Diagnostics;
using System.IO;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Tiisu.App.Assets;
using Tiisu.App.Rendering;
using Tiisu.App.Runtime;

namespace Tiisu.App.Ui;

/// <summary>
/// 屏幕宿主：整个窗口唯一的控件。
/// 负责：DPI/letterbox、把输入换算到逻辑坐标、在两屏之间切换（带淡入淡出），
/// 以及持有唯一的 <see cref="GameSession"/>（后台引擎线程）。
/// </summary>
public sealed class ScreenHost : SKControl
{
    private readonly TileAtlas? _atlas;
    private readonly GameSession _session;
    private readonly Settings _settings;
    private readonly TableScreen _table;
    private readonly MenuScreen _menu;
    private readonly SettingsScreen _settingsScreen;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer _frameTimer;

    private Screen _current;
    private Screen? _previous;
    private double _transition;      // 0 → 1
    private const double TransitionSeconds = 0.15;

    private float _scale = 1f;
    private float _offsetX;
    private float _offsetY;
    private float _canvasW = Screen.Width;
    private float _canvasH = Screen.Height;

    public ScreenHost(TileAtlas? atlas)
    {
        _atlas = atlas;
        Dock = DockStyle.Fill;

        // 会话必须在 UI 线程构造：它捕获当前 SynchronizationContext 把事件回投到 UI。
        _settings = Settings.Load();
        SoundEffects.Enabled = _settings.SoundEnabled;
        _session = new GameSession(_settings);
        var menuLayout = UiLayout.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "layout", "menu.json"));
        _menu = new MenuScreen(menuLayout, OnMenuAction);
        _settingsScreen = new SettingsScreen(_settings, OnSettingsChanged, () => SwitchTo(_menu));
        _table = new TableScreen(_session, atlas, () => SwitchTo(_menu));

        _current = _menu;

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
            _session.Dispose();
        }

        base.Dispose(disposing);
    }

    private bool OnMenuAction(string action)
    {
        if (action.StartsWith("mode:", StringComparison.Ordinal)
            && int.TryParse(action.Substring("mode:".Length), out int mode))
        {
            _session.Post(GameCommand.StartMatch(mode));
            SwitchTo(_table);
            return true;
        }

        switch (action)
        {
            case "resume":
                SwitchTo(_table);
                _session.PostResume();
                return true;

            case "next":
                SwitchTo(_table);
                _session.Post(GameCommand.NextRound());
                return true;

            case "settings":
                SwitchTo(_settingsScreen);
                return true;

            default:
                return false;
        }
    }

    /// <summary>设置屏每次改动后：热更新到引擎线程（可选役 / recordTiles / 长考秒数）。</summary>
    private void OnSettingsChanged(Settings settings) => _session.Post(GameCommand.ApplySettings(settings));

    private void SwitchTo(Screen next)
    {
        bool lobby = next is MenuScreen or SettingsScreen;
        if (lobby)
        {
            SoundEffects.PlayBgm();
        }
        else
        {
            SoundEffects.StopBgm();
        }
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
        var bg = _current.Background;
        canvas.Clear(new SKColor(bg.R, bg.G, bg.B, bg.A));

        _canvasW = _current.CanvasWidth;
        _canvasH = _current.CanvasHeight;

        _scale = MathF.Min(info.Width / _canvasW, info.Height / _canvasH);
        _offsetX = (info.Width - _canvasW * _scale) / 2f;
        _offsetY = (info.Height - _canvasH * _scale) / 2f;

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
                // 两屏画布尺寸不同时直接切，避免旧屏按新尺寸被拉伸
                bool sameSize = MathF.Abs(_previous.CanvasWidth - _canvasW) < 0.01f
                                && MathF.Abs(_previous.CanvasHeight - _canvasH) < 0.01f;
                if (sameSize)
                {
                    _previous.Draw(ctx, t);
                    float k = (float)_transition;
                    ctx.FillRect(new RectF(0, 0, _canvasW, _canvasH),
                        Rendering.Color.Black.WithAlpha((byte)(255 * (1 - k))));
                }
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

        if (e.KeyCode == Keys.M
            && (ReferenceEquals(_current, _menu) || ReferenceEquals(_current, _table)))
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
