using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using SkiaSharp;
using Tiisu.App.Assets;
using Tiisu.App.Rendering;
using Tiisu.App.Runtime;
using Tiisu.Localization;
using Tiisu.Net;

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

/// <summary>窗口层文本输入路由：IME 提交的中文/日文等字符不走 KeyDown。</summary>
internal interface ICharRouter
{
    bool RouteChar(char c);
}

internal sealed class ScreenHostCore : IDisposable
{
    private readonly TileAtlas? _atlas;
    private readonly GameSession _session;
    private Tiisu.Core.Ruleset _appliedRuleset;
    private readonly Settings _settings;
    private float _uiTextScale = 1f;
    private readonly SoundEffects _sound = new();
    private readonly ISteamService _steam = new NullSteamService();
    private readonly SteamAchievementSync _steamAchievements;
    private bool _steamMatchSynced;
    private string _appliedLanguage;
    private TableScreen _table;
    private MultiplayerScreen? _multiplayer;
    private HostEngine? _networkHost;
    private PeerClient? _peerClient;
    private NetworkClientSession? _networkClient;
    private Task? _networkStopTask;
    private readonly MenuScreen _menu;
    private readonly SettingsScreen _settingsScreen;
    private readonly RulesScreen _rules;
    private readonly LicensesScreen _licenses;
    private readonly TutorialScreen _tutorial;
    private readonly StatsScreen _stats;
    private readonly AchievementsScreen _achievements;
    private readonly ReplayScreen _replay;
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
        string? launchLanguage = Environment.GetEnvironmentVariable("TIISU_LANG");
        if (!string.IsNullOrWhiteSpace(launchLanguage))
        {
            _settings.Language = UiText.Normalize(launchLanguage);
        }
        _appliedLanguage = _settings.Language;
        _appliedRuleset = _settings.Ruleset;
        _sound.BgmVolume = _settings.BgmVolume;
        _sound.SfxVolume = _settings.SfxVolume;
        _sound.VoiceEnabled = _settings.VoiceEnabled;
        _uiTextScale = (float)_settings.UiTextScale;
        UiText.SetLanguage(_settings.Language);
        _session = new GameSession(_settings);
        _steamAchievements = new SteamAchievementSync(_steam, new SteamAchievementStore());
        _session.UiEvent += OnSteamSyncEvent;
        var menuLayout = UiLayout.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "layout", "menu.json"));
        _licenses = new LicensesScreen(() => SwitchTo(_menu), OpenMarkdown);
        _tutorial = new TutorialScreen(() => SwitchTo(_menu), () =>
        {
            _session.Post(GameCommand.StartMatch(1));
            SwitchTo(_table);
        }, () => SwitchTo(_rules));
        _achievements = new AchievementsScreen(() => SwitchTo(_stats));
        _stats = new StatsScreen(() => SwitchTo(_menu), () =>
        {
            _achievements.Reload();
            SwitchTo(_achievements);
        });
        _replay = new ReplayScreen(() => SwitchTo(_menu));
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
        _multiplayer = new MultiplayerScreen(
            _settings.EffectivePlayerName,
            CreateNetworkHostAsync,
            JoinRoomAsync,
            LeaveNetworkToMenuAsync,
            ShowHostTable,
            ShowClientTable,
            SavePlayerName);

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
        try
        {
            Task close = CloseNetworkSessionAsync();
            if (!close.Wait(TimeSpan.FromSeconds(8)))
            {
                // 端口释放超时不能阻止程序退出；下次启动会重新绑定。
            }
        }
        catch (AggregateException)
        {
            // 关闭失败不能阻止退出。
        }

        _multiplayer?.Dispose();
        _session.UiEvent -= OnSteamSyncEvent;
        _steam.Shutdown();
        _frameTimer.Stop();
        _frameTimer.Dispose();
        _session.Dispose();
    }

    private async Task<HostEngine> CreateNetworkHostAsync(
        string transport, string zone, int table, int mode, bool aiFill)
    {
        await CloseNetworkSessionAsync();
        string name = _settings.EffectivePlayerName;
        var room = RoomInfo.Create(name + "的七子房", name,
            NetworkConstants.DefaultPort, BuildInfo.Version,
            transport: transport, zone: zone, table: table, mode: mode, aiFill: aiFill);
        _networkHost = new HostEngine(_settings, room, transport: transport);
        _networkHost.CheatDetected += reason =>
        {
            _multiplayer?.SetStatus("发现作弊者，本局取消：" + reason);
        };
        return _networkHost;
    }

    private async Task<(PeerClient Client, NetworkClientSession Session)> JoinRoomAsync(RoomInfo room)
    {
        await CloseNetworkSessionAsync();
        string name = _settings.EffectivePlayerName;
        string host = string.IsNullOrWhiteSpace(room.HostAddress) ? "127.0.0.1" : room.HostAddress;
        PeerClient client = await PeerClient.ConnectAsync(
            host, name, BuildInfo.Version, room.Port, room.Transport);
        _peerClient = client;
        _networkClient = new NetworkClientSession(client);
        return (client, _networkClient);
    }

    private void SavePlayerName(string name)
    {
        _settings.PlayerName = Settings.NormalizePlayerName(name);
        _settings.Save();
    }

    private async Task LeaveNetworkToMenuAsync()
    {
        try
        {
            await CloseNetworkSessionAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("CloseNetworkSession failed: " + ex);
        }
        finally
        {
            SwitchTo(_menu);
        }
    }

    /// <summary>关闭当前联机会话并等待 17700 的 TCP/UDP 监听真正释放。</summary>
    private async Task CloseNetworkSessionAsync()
    {
        HostEngine? host = _networkHost;
        NetworkClientSession? client = _networkClient;
        PeerClient? peer = _peerClient;
        _networkHost = null;
        _networkClient = null;
        _peerClient = null;

        var tasks = new List<Task>();
        if (host is not null)
        {
            tasks.Add(host.StopAsync());
        }

        if (client is not null)
        {
            tasks.Add(client.CloseAsync().AsTask());
        }
        else if (peer is not null)
        {
            tasks.Add(peer.DisposeAsync().AsTask());
        }

        if (tasks.Count > 0)
        {
            Task stop = Task.WhenAll(tasks);
            _networkStopTask = stop;
            await stop.ConfigureAwait(false);
        }

        _multiplayer?.ResetRoom();
    }

    private void ShowHostTable(HostEngine host)
    {
        ShowNetworkTable(host);
    }

    private void ShowClientTable(PeerClient peer, NetworkClientSession session)
    {
        ShowNetworkTable(session);
    }

    private void ShowNetworkTable(ITableSession session)
    {
        _table = new TableScreen(session, _atlas, () => _ = LeaveNetworkToMenuAsync(), () =>
        {
            _settingsReturn = _table;
            SwitchTo(_settingsScreen);
        }, _settings.TableRender, _settings);
        SwitchTo(_table);
    }

    private void OnSteamSyncEvent(UiEvent uiEvent)
    {
        if (uiEvent is not StateChangedEvent changed)
        {
            return;
        }

        if (changed.Snapshot.Phase != "match_over")
        {
            _steamMatchSynced = false;
            return;
        }

        if (_steamMatchSynced)
        {
            return;
        }

        _steamMatchSynced = true;
        try
        {
            _steamAchievements.Report(Achievements.From(StatsSummary.Load(new StatsStore())));
        }
        catch
        {
            // 成就上报失败不能影响结算/下一局。
        }
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

            case "multiplayer":
                if (_multiplayer is not null)
                {
                    SwitchTo(_multiplayer);
                }

                return true;

            case "settings":
                SwitchTo(_settingsScreen);
                return true;

            case "rules":
                SwitchTo(_rules);
                return true;

            case "stats":
                _stats.Reload();
                SwitchTo(_stats);
                return true;

            case "replay":
                _replay.Reload();
                SwitchTo(_replay);
                return true;

            case "tutorial":
                SwitchTo(_tutorial);
                return true;

            case "yaku":
                OpenGuide("YAKU.md");
                return true;

            case "guide":
                OpenGuide("STRATEGY.md");
                return true;

            case "quit":
                // 菜单「退出」= 关主窗口（退出程序）；ESC 仍只做返回。
                _close();
                return true;

            default:
                return false;
        }
    }

    /// <summary>打开许可/来源类 Markdown；返回许可页。</summary>
    private void OpenMarkdown(string file) =>
        OpenDocument(file, () => SwitchTo(_licenses));

    /// <summary>打开游戏内指南；返回主菜单。</summary>
    private void OpenGuide(string file) =>
        OpenDocument(file, () => SwitchTo(_menu));

    private void OpenDocument(string file, Action back)
    {
        string basePath = Path.Combine(AppContext.BaseDirectory, "Assets", "docs");
        string path = UiText.IsEnglish ? Path.Combine(basePath, "en", file)
            : UiText.IsJapanese ? Path.Combine(basePath, "ja", file)
            : Path.Combine(basePath, file);
        if (!File.Exists(path))
        {
            path = Path.Combine(basePath, file);
        }

        string text;
        try
        {
            text = File.Exists(path) ? File.ReadAllText(path) : L.T("app.ui.docs.notFound", ("file", file));
        }
        catch (Exception ex)
        {
            text = L.T("app.ui.docs.readFailed", ("message", ex.Message));
        }

        string title = file switch
        {
            "REFERENCES.md" => L.T("app.ui.docs.title.references"),
            "ASSET-PROVENANCE.md" => L.T("app.ui.docs.title.assets"),
            "ML-PROVENANCE.md" => L.T("app.ui.docs.title.ml"),
            "SELFPLAY_TRAINING.md" => L.T("app.ui.docs.title.selfplay"),
            "RELEASE_COMPLIANCE.md" => L.T("app.ui.docs.title.release"),
            "THIRD-PARTY-NOTICES.md" => L.T("app.ui.docs.title.thirdParty"),
            "YAKU.md" => L.T("app.ui.docs.title.yaku"),
            "STRATEGY.md" => L.T("app.ui.docs.title.strategy"),
            _ => Path.GetFileNameWithoutExtension(file),
        };
        SwitchTo(new MarkdownScreen(title, text, back));
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
        bool languageChanged = !string.Equals(_appliedLanguage, settings.Language, StringComparison.Ordinal);
        _appliedLanguage = settings.Language;
        _sound.BgmVolume = settings.BgmVolume;
        _sound.SfxVolume = settings.SfxVolume;
        _sound.VoiceEnabled = settings.VoiceEnabled;
        _uiTextScale = (float)settings.UiTextScale;
        UiText.SetLanguage(settings.Language);
        if (languageChanged)
        {
            // 语言切换必须让所有有缓存的屏幕重新取当前语言资源。
            _menu.OnLanguageChanged();
            _settingsScreen.OnLanguageChanged();
            _rules.OnLanguageChanged();
            _licenses.OnLanguageChanged();
            _tutorial.OnLanguageChanged();
            _stats.OnLanguageChanged();
            _achievements.OnLanguageChanged();
            _replay.OnLanguageChanged();
            _table.OnLanguageChanged();
            _current?.OnLanguageChanged();
        }
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
        bool lobby = next is MenuScreen or SettingsScreen or RulesScreen or LicensesScreen or TutorialScreen or StatsScreen or AchievementsScreen or ReplayScreen or MarkdownScreen;
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

        using (var ctx = new SkiaCanvas(canvas, _atlas) { TextScale = _current is TableScreen or MarkdownScreen ? 1f : _uiTextScale })
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

        return _current.OnKey(key, shift);
    }

    public bool TextInput(string text)
    {
        return text.Length > 0 && _current.OnTextInput(text);
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
