using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using SkiaSharp;
using Tiisu.App.Assets;
using Tiisu.App.Rendering;
using Tiisu.App.Runtime;

namespace Tiisu.App.Ui;

/// <summary>
/// 屏幕宿主的共享状态：唯一的 <see cref="GameSession"/>、屏幕切换、DPI/letterbox 变换与输入换算。
/// 两个宿主控件（<see cref="GlScreenHost"/> / <see cref="CpuScreenHost"/>）只负责提供 surface，
/// 绘制代码（<see cref="ICanvas"/> / <see cref="SkiaCanvas"/>）与这里完全无关。
/// </summary>
/// <summary>窗口层按键路由：没有焦点时也能把按键送到当前的 Screen。</summary>
internal interface IKeyRouter
{
    bool RouteKey(Keys key, bool shift);
}

internal sealed class ScreenHostCore : IDisposable
{
    private readonly TileAtlas? _atlas;
    private readonly GameSession _session;
    private readonly Settings _settings;
    private readonly TableScreen _table;
    private readonly MenuScreen _menu;
    private readonly SettingsScreen _settingsScreen;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer _frameTimer;
    private readonly Action _close;

    private Screen _current;
    private Screen? _previous;
    private double _transition;      // 0 → 1
    private const double TransitionSeconds = 0.15;

    private float _scale = 1f;
    private float _offsetX;
    private float _offsetY;
    private float _canvasW = Screen.Width;
    private float _canvasH = Screen.Height;

    private bool _disposed;

    public ScreenHostCore(TileAtlas? atlas, Action invalidate, Action close)
    {
        _atlas = atlas;
        _close = close ?? (() => { });

        // 会话必须在 UI 线程构造：它捕获当前 SynchronizationContext 把事件回投到 UI。
        _settings = Settings.Load();
        SoundEffects.Enabled = _settings.SoundEnabled;
        _session = new GameSession(_settings);
        var menuLayout = UiLayout.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "layout", "menu.json"));
        _menu = new MenuScreen(menuLayout, OnMenuAction, OpenUrl);
        _settingsScreen = new SettingsScreen(_settings, OnSettingsChanged, () => SwitchTo(_menu));
        _table = new TableScreen(_session, atlas, () => SwitchTo(_menu));

        _current = _menu;

        _frameTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _frameTimer.Tick += (_, _) => invalidate();
        _frameTimer.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _frameTimer.Stop();
        _frameTimer.Dispose();
        _session.Dispose();
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

            case "quit":
                // 菜单「退出」= 关主窗口（退出程序）；ESC 仍只做返回。
                _close();
                return true;

            default:
                return false;
        }
    }

    /// <summary>用系统默认浏览器打开外链（署名 / 许可证 / 仓库）；失败只静默，不影响游戏。</summary>
    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // 没有默认浏览器 / 系统拒绝：忽略
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

        if (Environment.GetEnvironmentVariable("TIISU_LOG_KEYS") == "1")
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "tiisu-keys.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} screen->{next.Id}{Environment.NewLine}");
            }
            catch
            {
                // 调试日志失败忽略
            }
        }
    }

    /// <summary>一帧：CPU 的 <see cref="SKCanvas"/> 与 GPU 的 <see cref="SKCanvas"/> 走同一条路径。</summary>
    public void Draw(SKCanvas canvas, SKImageInfo info)
    {
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

    public Cursor PointerMove(int x, int y)
    {
        var (lx, ly) = ToLogical(x, y);
        return _current.OnPointerMove(lx, ly) ? Cursors.Hand : Cursors.Default;
    }

    public void Click(int x, int y)
    {
        var (lx, ly) = ToLogical(x, y);
        _current.OnClick(lx, ly);
    }

    public void PointerDown(int x, int y)
    {
        var (lx, ly) = ToLogical(x, y);
        _current.OnPointerDown(lx, ly);
    }

    public void PointerUp(int x, int y)
    {
        var (lx, ly) = ToLogical(x, y);
        _current.OnPointerUp(lx, ly);
    }

    public void Wheel(int x, int y, int delta, bool shift)
    {
        var (lx, ly) = ToLogical(x, y);
        _current.OnWheel(lx, ly, delta, shift);
    }

    /// <summary>返回 true 表示已消费（与原来的 e.Handled 等价）。</summary>
    public bool KeyDown(Keys key, bool shift = false)
    {
        if (key == Keys.M
            && !shift
            && (ReferenceEquals(_current, _menu) || ReferenceEquals(_current, _table)))
        {
            SwitchTo(ReferenceEquals(_current, _menu) ? _table : _menu);
            return true;
        }

        _current.OnKey(key, shift);
        return false;
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
