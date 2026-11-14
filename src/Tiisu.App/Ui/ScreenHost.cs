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
    private Tiisu.Core.Ruleset _appliedRuleset;
    private readonly Settings _settings;
    private readonly SoundEffects _sound = new();
    private readonly TableScreen _table;
    private readonly MenuScreen _menu;
    private readonly SettingsScreen _settingsScreen;
    private readonly RulesScreen _rules;
    private readonly LicensesScreen _licenses;
    private readonly TutorialScreen _tutorial;
    private Screen? _settingsReturn;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer _frameTimer;
    private readonly Action _close;

    private Screen _current;
    private Screen? _previous;
    private double _transition;      // 0 → 1
    private double _lastUpdate = -1;
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
        _appliedRuleset = _settings.Ruleset;
        _sound.BgmVolume = _settings.BgmVolume;
        _sound.SfxVolume = _settings.SfxVolume;
        _session = new GameSession(_settings);
        var menuLayout = UiLayout.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "layout", "menu.json"));
        _licenses = new LicensesScreen(() => SwitchTo(_menu), OpenMarkdown);
        _tutorial = new TutorialScreen(() => SwitchTo(_menu), () =>
        {
            _session.Post(GameCommand.StartMatch(1));
            SwitchTo(_table);
        }, () => SwitchTo(_rules));
        _menu = new MenuScreen(menuLayout, OnMenuAction, HandleLink);
        _rules = new RulesScreen(() => SwitchTo(_menu));
        _settingsScreen = new SettingsScreen(_settings, OnSettingsChanged, () =>
        {
            var back = _settingsReturn ?? _menu;
            _settingsReturn = null;
            SwitchTo(back);
        });
        _table = new TableScreen(_session, atlas, () => SwitchTo(_menu), () =>
        {
            _settingsReturn = _table;
            SwitchTo(_settingsScreen);
        }, _settings.TableRender, _settings);

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
            // 「继续上局」已整体停用：菜单没有入口，这里也不再有 resume 分支。

            case "next":
                SwitchTo(_table);
                _session.Post(GameCommand.NextRound());
                return true;

            case "settings":
                SwitchTo(_settingsScreen);
                return true;

            case "rules":
                SwitchTo(_rules);
                return true;

            case "tutorial":
                SwitchTo(_tutorial);
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
    private void OpenMarkdown(string file)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "docs", file);
        string text;
        try
        {
            text = File.Exists(path) ? File.ReadAllText(path) : "未找到文档：" + file;
        }
        catch (Exception ex)
        {
            text = "读取失败：" + ex.Message;
        }

        string title = file switch
        {
            "REFERENCES.md" => "参考与合规说明",
            "ASSET-PROVENANCE.md" => "素材来源",
            "ML-PROVENANCE.md" => "ML 权重来源",
            "SELFPLAY_TRAINING.md" => "自博弈训练",
            "RELEASE_COMPLIANCE.md" => "发布合规清单",
            "THIRD-PARTY-NOTICES.md" => "第三方声明",
            _ => Path.GetFileNameWithoutExtension(file),
        };
        SwitchTo(new MarkdownScreen(title, text, () => SwitchTo(_licenses)));
    }

    /// <summary>菜单链接：app:licenses 在游戏内打开许可页，其余交给浏览器。</summary>
    private void HandleLink(string url)
    {
        if (string.Equals(url, "app:licenses", StringComparison.Ordinal))
        {
            SwitchTo(_licenses);
            return;
        }

        OpenUrl(url);
    }

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
    private void OnSettingsChanged(Settings settings)
    {
        _sound.BgmVolume = settings.BgmVolume;
        _sound.SfxVolume = settings.SfxVolume;
        bool rulesetChanged = _appliedRuleset != settings.Ruleset;
        _appliedRuleset = settings.Ruleset;
        _session.Post(GameCommand.ApplySettings(settings));

        // 规则集只在新一局生效：切了就重置牌桌并回菜单，避免打到一半规则突变。
        if (rulesetChanged)
        {
            _session.Post(GameCommand.ResetAll());
            SwitchTo(_menu);
        }
    }

    private void SwitchTo(Screen next)
    {
        bool lobby = next is MenuScreen or SettingsScreen or RulesScreen or LicensesScreen or TutorialScreen or MarkdownScreen;
        if (lobby)
        {
            _sound.PlayBgm();
        }
        else
        {
            _sound.StopBgm();
        }
        if (ReferenceEquals(next, _current))
        {
            return;
        }

        _previous = _current;
        _current = next;
        // 直接切屏：旧实现只画「旧屏 + 黑幕」，静态画面不再重绘 → 切到主菜单整屏黑。
        _transition = 1;

        if (Tiisu.Core.TiisuConfig.Current.LogKeys)
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

        // 每帧推进当前屏（长考倒计时、按钮动画等依赖 Update 的 dt）。
        double dt = _lastUpdate < 0 ? 0 : t - _lastUpdate;
        _lastUpdate = t;
        if (dt > 0.5)
        {
            dt = 0.5;
        }

        _current.Update(dt);
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
        // F2 在任何屏都能进布局编辑器：不在牌桌时先切到牌桌，再进入编辑态（退出时回菜单）；
        // 已经在牌桌时交给牌桌屏自己处理，才能识别「从菜单进来、退出要回去」。
        if (key == Keys.F2 && !shift)
        {
            if (ReferenceEquals(_current, _table))
            {
                _current.OnKey(key, shift);
            }
            else
            {
                SwitchTo(_table);
                _table.OpenEditorForLayout();
            }

            return true;
        }

        // M 只在局内生效（对局 → 菜单）；主菜单屏不再用快捷键跳转。
        if (key == Keys.M && !shift && ReferenceEquals(_current, _table))
        {
            SwitchTo(_menu);
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
