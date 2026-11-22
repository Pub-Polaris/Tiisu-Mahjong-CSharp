using System;
using System.IO;
using System.Windows.Forms;
using Tiisu.Localization;
using Tiisu.App.Assets;
using Tiisu.App.Runtime;

namespace Tiisu.App.Ui;

/// <summary>主窗口：把铺满的 ScreenHost 放上去，并在窗口层接管键盘（不依赖控件焦点）。</summary>
public sealed class MainForm : Form
{
    private readonly TileAtlas _atlas;
    private readonly LaunchOptions _options;
    private readonly PlayerProfileStore _profiles;
    private readonly bool _forceCpu;
    private Control _host;
    private PlayerProfile _profile;
    private System.Windows.Forms.Timer? _profileSwitchTimer;
    private readonly bool _logKeys = Tiisu.Core.TiisuConfig.Current.LogKeys;
    private bool _fullscreen;
    private System.Drawing.Rectangle _restoreBounds;
    private FormWindowState _restoreState = FormWindowState.Normal;
    private FormBorderStyle _restoreStyle = FormBorderStyle.Sizable;

    public MainForm(TileAtlas atlas, LaunchOptions options)
    {
        _atlas = atlas;
        _options = options ?? new LaunchOptions(false, false, null, null);
        _forceCpu = _options.ForceCpuRender;

        // 档案必须在任何 store 读取前确定：AppData.Current 是各 store 的默认根。
        _profiles = PlayerProfileStore.LoadOrCreate();
        _profile = _profiles.ResolveLaunchProfile(_options.Profile);
        AppData.Use(_profile.Directory);

        Text = L.T("app.ui.window.title");

        // 窗口图标与 exe 图标一致：直接取可执行文件里嵌的那一份（csproj 的 ApplicationIcon），
        // 不在程序集里再存一份。取不到（如无图标的调试产物）就保留 WinForms 默认图标。
        try
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch (ArgumentException)
        {
            // 可执行文件没有图标资源，保持默认
        }

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
        _host = CreateHost(_profile);
        Controls.Add(_host);
        Text = L.T("app.ui.window.title");

        // 启动/激活时把焦点交给画布，否则键盘事件没有落点。
        UiText.LanguageChanged += OnLanguageChanged;
        FormClosed += (_, _) =>
        {
            UiText.LanguageChanged -= OnLanguageChanged;
            _profileSwitchTimer?.Stop();
            _profileSwitchTimer?.Dispose();
            _profileSwitchTimer = null;
        };

        Shown += (_, _) =>
        {
            _host.Focus();
            if (_options.Fullscreen)
            {
                BeginInvoke(ToggleFullscreen);
            }
        };
        Activated += (_, _) => _host.Focus();
    }

    private Control CreateHost(PlayerProfile profile) =>
        ScreenHostFactory.Create(_atlas, _forceCpu, profile, _profiles, SwitchProfile);

    /// <summary>
    /// 档案页请求切换：延迟到本次鼠标点击完全结束后再换 host。
    /// 若在 OnClick 里直接替换控件，同一次点击的 MouseUp 会落到新菜单上，
    /// 可能误触「一局/南风场」直接开局。
    /// </summary>
    private void SwitchProfile(PlayerProfile profile)
    {
        if (profile is null || IsDisposed || !IsHandleCreated || _profileSwitchTimer is not null)
        {
            return;
        }

        var timer = new System.Windows.Forms.Timer { Interval = 90 };
        _profileSwitchTimer = timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            _profileSwitchTimer = null;
            ApplyProfileSwitch(profile);
        };
        timer.Start();
    }

    private void ApplyProfileSwitch(PlayerProfile profile)
    {
        if (IsDisposed)
        {
            return;
        }

        PlayerProfile previous = _profile;
        try
        {
            PlayerProfile active = _profiles.SetActive(profile.Id);
            AppData.Use(active.Directory);
            Control next = CreateHost(active);
            Controls.Add(next);
            Control old = _host;
            _host = next;
            Controls.Remove(old);
            old.Dispose();
            _profile = active;
            _host.Focus();
            Invalidate(true);
        }
        catch (Exception ex)
        {
            try
            {
                _profiles.SetActive(previous.Id);
                AppData.Use(previous.Directory);
            }
            catch
            {
                // 回滚失败也保留旧 host。
            }

            MessageBox.Show(this, ex.Message, L.T("app.ui.profile.switchFailed"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnLanguageChanged()
    {
        Text = L.T("app.ui.window.title");
        Invalidate(true);
    }

    /// <summary>Tab 会被 WinForms 焦点遍历吞掉；显式转给画布，菜单/设置才能用 Tab 切换。</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Tab || keyData == (Keys.Tab | Keys.Shift))
        {
            if (_host is IKeyRouter router && router.RouteKey(Keys.Tab, (keyData & Keys.Shift) == Keys.Shift))
            {
                return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>窗口层接管按键：KeyPreview 保证这里先收到，再转给当前 Screen。</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        try
        {
            RouteKeyCore(e);
        }
        catch (Exception ex)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tiisu-error.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + ex + Environment.NewLine);
            }
            catch
            {
                // 日志失败忽略
            }
        }
    }

/// <summary>F11 全屏：保存当前尺寸/样式，按显示器工作区铺满；再按恢复。</summary>
      private void ToggleFullscreen()
      {
          if (!_fullscreen)
          {
              _restoreBounds = Bounds;
              _restoreState = WindowState;
              _restoreStyle = FormBorderStyle;
              MinimumSize = Size.Empty;
              FormBorderStyle = FormBorderStyle.None;
              WindowState = FormWindowState.Normal;
              Bounds = System.Windows.Forms.Screen.FromControl(this).Bounds;
              _fullscreen = true;
          }
          else
          {
              FormBorderStyle = _restoreStyle;
              Bounds = _restoreBounds;
              WindowState = _restoreState;
              MinimumSize = new Size(640, 640);
              _fullscreen = false;
          }
      }


    /// <summary>IME / 文本输入：KeyPress 的字符转给当前屏的文本框；控制键仍走 KeyDown。</summary>
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar)
            && _host is ICharRouter router
            && router.RouteChar(e.KeyChar))
        {
            e.Handled = true;
            return;
        }

        base.OnKeyPress(e);
    }

    private void RouteKeyCore(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F11 && !e.Shift)
        {
            ToggleFullscreen();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

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
