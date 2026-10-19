using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Tiisu.Shell;

/// <summary>
/// 唯一窗口：一个铺满的 <see cref="WebView2"/>。
/// 页面通过虚拟主机 <c>https://tiisu.local/</c> 加载仓库里的 <c>Web/</c>；
/// <c>/api/*</c> 由 <see cref="ApiRouter"/> 直接应答；引擎通过 host object 同步桥注入。
/// </summary>
internal sealed class MainForm : Form
{
    private const string AppHost = "tiisu.local";

    /// <summary>API 请求的虚拟主机（页面静态资源仍走 <see cref="AppHost"/> 的 folder mapping）。</summary>
    private const string ApiHost = "tiisu-api.local";

    /// <summary>页面 CSS 固定 <c>.app</c> / <c>.table</c> = 1460×900（不改页面尺寸，只整体等比缩放）。</summary>
    private const int PageWidth = 1460;

    private const int PageHeight = 900;

    /// <summary><c>Web/css/game.css</c> 的 <c>body{padding:12px}</c>：牌桌四周留白，缩放时要一起算进去。</summary>
    private const int PagePadding = 12;

    /// <summary>仅 --smoke：把自动演示的 AI 节奏压到最快，保证 15 秒内打完一局。</summary>
    private const string SmokeSpeedScript = """
        (function () {
          try {
            var q = new URLSearchParams(location.search);
            if (q.get('engine') !== 'csharp' || !q.get('auto')) return;
            setInterval(function () {
              try {
                if (window.__game && window.__game.speed > 5) window.__game.speed = 5;
              } catch (e) {}
            }, 100);
          } catch (e) {}
        })();
        """;

    /// <summary>--drive-human：人类模式自动出牌 + 压快 AI 节奏（无头验证人类桥）。</summary>
    private const string DriveHumanScript = """
        (function () {
          try {
            setInterval(function () {
              try {
                if (window.__game && window.__game.speed > 5) window.__game.speed = 5;
              } catch (e) {}
            }, 100);

            setInterval(function () {
              try {
                var g = window.__game;
                if (!g) return;
                if (g._actOpts) { g.passAction(); return; }
                if (g.state === 'human_discard') {
                  var tile = document.querySelector('.tile.clickable');
                  if (tile) tile.click();
                }
              } catch (e) {
                console.error('[drive-human] ' + ((e && e.stack) || e));
              }
            }, 40);
          } catch (e) {}
        })();
        """;

    private readonly ShellOptions _options;
    private readonly string _webDirectory;
    private readonly string _dataDirectory;
    private readonly EngineHost _engineHost = new();
    private readonly ApiRouter _apiRouter;
    private readonly WebView2 _webView;
    private readonly List<string> _consoleLines = new();

    private CoreWebView2DevToolsProtocolEventReceiver? _consoleReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? _exceptionReceiver;

    private int _consoleTotal;
    private int _consoleDropped;
    private bool _failed;
    private bool _closing;
    private bool _shotDone;
    private long _navigationStarted;

    private System.Windows.Forms.Timer? _smokeTimer;
    private System.Windows.Forms.Timer? _shotTimer;

    public MainForm(ShellOptions options, string webDirectory)
    {
        _options = options;
        _webDirectory = webDirectory;

        // 数据根目录：%APPDATA%\TiisuMahjong（首次启动建目录 + 一次性迁移 Web 目录里的旧数据）。
        _dataDirectory = ShellDataDirectory.Resolve(webDirectory, Log);
        _apiRouter = new ApiRouter(_dataDirectory);

        Text = "Tiisu Mahjong · C# Shell";
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Normal;
        ClientSize = new Size(PageWidth, PageHeight);
        MinimumSize = new Size(900, 640);
        BackColor = Color.FromArgb(15, 31, 13);

        _webView = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_webView);
        _webView.SizeChanged += (_, _) => ApplyZoom();
        ResizeEnd += (_, _) => ApplyZoom();

        Load += OnLoad;
        Log($"[shell] Web 目录: {_webDirectory}");
        Log($"[shell] 窗口: 客户区 {PageWidth}×{PageHeight} · 缩放 = min((宽−2×{PagePadding}−滚动条)/{PageWidth}, (高−2×{PagePadding})/{PageHeight})");
        Log("[shell] 引擎: C# (Tiisu.Engine) · 桥: AddHostObjectToScript(\"tiisu\") · API: C# DataStore");
    }

    public int ExitCode { get; private set; }

    private async void OnLoad(object? sender, EventArgs e)
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TiisuMahjong",
                "Shell",
                "WebView2");
            Directory.CreateDirectory(userDataFolder);

            CoreWebView2Environment environment;
            try
            {
                environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            }
            catch (Exception ex)
            {
                Log("[shell] 默认 user-data 目录不可用，改用临时目录: " + ex.Message);
                var fallback = Path.Combine(Path.GetTempPath(), "TiisuShellWebView2", Guid.NewGuid().ToString("N"));
                environment = await CoreWebView2Environment.CreateAsync(null, fallback);
            }

            await _webView.EnsureCoreWebView2Async(environment);
            if (_closing)
            {
                return;
            }

            var core = _webView.CoreWebView2;
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            ApplyZoom();

            core.SetVirtualHostNameToFolderMapping(
                AppHost,
                _webDirectory,
                CoreWebView2HostResourceAccessKind.Allow);
            core.AddHostObjectToScript("tiisu", _engineHost);

            // 注意（WebView2 平台限制）：文档明确 "For URLs with virtual hostnames, using the
            // WebResourceRequested event isn't supported" —— 虚拟主机映射的请求不会触发本事件。
            // 因此页面里的 /api/* 会被 host_adapter.js 改写到 tiisu-api.local（同一个 WebResourceRequested
            // 处理器），保证 API 仍由 C# DataStore 直接应答、不启任何服务器。
            core.AddWebResourceRequestedFilter(
                $"https://{AppHost}/api/*",
                CoreWebView2WebResourceContext.All);
            core.AddWebResourceRequestedFilter(
                $"https://{ApiHost}/api/*",
                CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnWebResourceRequested;
            Log($"[shell] WebResourceRequested 已订阅: https://{AppHost}/api/*, https://{ApiHost}/api/*");
            core.ProcessFailed += OnProcessFailed;
            core.NavigationCompleted += OnNavigationCompleted;

            // 本 SDK 版本没有 CoreWebView2.ConsoleMessageReceived，改用 DevTools 协议的
            // Runtime.consoleAPICalled / Runtime.exceptionThrown 采集控制台消息与 JS 异常。
            await EnableConsoleCaptureAsync(core);

            if (_options.SmokeSeconds > 0)
            {
                await core.AddScriptToExecuteOnDocumentCreatedAsync(SmokeSpeedScript);
            }

            if (_options.DriveHuman)
            {
                await core.AddScriptToExecuteOnDocumentCreatedAsync(DriveHumanScript);
            }

            string url = BuildUrl();
            Log("[shell] 导航: " + url);
            _navigationStarted = Environment.TickCount64;
            core.Navigate(url);
        }
        catch (Exception ex)
        {
            Log("[shell] WebView2 初始化失败: " + ex);
            Fail();
            if (_options.IsHeadless)
            {
                ExitCode = 1;
                FinishAndClose();
            }
            else
            {
                MessageBox.Show(
                    this,
                    "WebView2 初始化失败：\n" + ex.Message,
                    "Tiisu Mahjong",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Close();
            }
        }
    }

    private async Task EnableConsoleCaptureAsync(CoreWebView2 core)
    {
        try
        {
            _consoleReceiver = core.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled");
            _consoleReceiver.DevToolsProtocolEventReceived += OnConsoleApiCalled;
            _exceptionReceiver = core.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown");
            _exceptionReceiver.DevToolsProtocolEventReceived += OnExceptionThrown;
            await core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
        }
        catch (Exception ex)
        {
            Log("[shell] 启用控制台采集失败: " + ex.Message);
        }
    }

    private string BuildUrl()
    {
        var query = new StringBuilder("?engine=csharp&ui=classic");
        if (!_options.NoAuto && (_options.SmokeSeconds > 0 || _options.ShotPath is not null))
        {
            // 自检 / 截图固定跑全自动一局：无弹窗、能看到牌桌在动。
            query.Append("&auto=1&mode=1");
        }

        return $"https://{AppHost}/index.html{query}";
    }

    // ══════════════════════════════════════════════════════════════
    // WebView2 事件
    // ══════════════════════════════════════════════════════════════

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            string uri = e.Request.Uri ?? string.Empty;
            if (uri.Contains("/api/", StringComparison.Ordinal))
            {
                _apiRouter.Handle(_webView.CoreWebView2, e, Log);
            }
        }
        catch (Exception ex)
        {
            Log("[api] 未捕获异常: " + ex);
        }
    }

    private void OnConsoleApiCalled(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        try
        {
            if (JsonNode.Parse(e.ParameterObjectAsJson) is not JsonObject root)
            {
                return;
            }

            string type = TextOf(root["type"]);
            var parts = new List<string>();
            if (root["args"] is JsonArray args)
            {
                foreach (var arg in args)
                {
                    parts.Add(RenderRemoteObject(arg as JsonObject));
                }
            }

            string source = string.Empty;
            if (root["stackTrace"]?["callFrames"] is JsonArray frames
                && frames.Count > 0
                && frames[0] is JsonObject frame)
            {
                source = TextOf(frame["url"]) + ":" + (Number(frame["lineNumber"]) + 1).ToString(CultureInfo.InvariantCulture);
            }

            RecordConsole(type, source, string.Join(" ", parts));
        }
        catch (Exception ex)
        {
            Log("[shell] 解析 console 事件失败: " + ex.Message);
        }
    }

    private void OnExceptionThrown(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        _failed = true;
        _consoleTotal++;
        try
        {
            if (JsonNode.Parse(e.ParameterObjectAsJson) is not JsonObject root
                || root["exceptionDetails"] is not JsonObject details)
            {
                AppendConsoleLine("[console:exception] (无法解析的 exceptionThrown)");
                return;
            }

            string text = TextOf(details["text"]);
            string description = TextOf((details["exception"] as JsonObject)?["description"]);
            string url = TextOf(details["url"]);
            long line = (long)Number(details["lineNumber"]) + 1;
            string message = string.Join(
                " ",
                new[] { text, description }.Where(part => part.Length > 0));
            AppendConsoleLine($"[console:exception] {url}:{line} {message}".TrimEnd());
        }
        catch (Exception ex)
        {
            AppendConsoleLine("[console:exception] (解析失败: " + ex.Message + ")");
        }
    }

    private void RecordConsole(string type, string source, string message)
    {
        _consoleTotal++;
        string level = type switch
        {
            "error" or "assert" => "error",
            "warning" => "warning",
            "info" => "info",
            _ => "log",
        };

        AppendConsoleLine($"[console:{level}] {source} {message}".TrimEnd());
        if (level == "error")
        {
            _failed = true;
        }
    }

    private void AppendConsoleLine(string line)
    {
        if (_consoleLines.Count < 400)
        {
            _consoleLines.Add(line);
            Log(line);
        }
        else if (_consoleDropped++ == 0)
        {
            Log($"[console] 消息过多，后续省略（已收到 {_consoleTotal} 条）");
        }
    }

    private static string RenderRemoteObject(JsonObject? obj)
    {
        if (obj is null)
        {
            return "null";
        }

        if (obj["value"] is JsonValue value)
        {
            if (value.TryGetValue<string>(out string? text))
            {
                return text;
            }

            if (value.TryGetValue<bool>(out bool flag))
            {
                return flag ? "true" : "false";
            }

            if (value.TryGetValue<double>(out double number))
            {
                return number.ToString("0.###", CultureInfo.InvariantCulture);
            }
        }

        string description = TextOf(obj["description"]);
        if (description.Length > 0)
        {
            return description;
        }

        string raw = TextOf(obj["unserializableValue"]);
        if (raw.Length > 0)
        {
            return raw;
        }

        string type = TextOf(obj["type"]);
        return type.Length > 0 ? "<" + type + ">" : "<object>";
    }

    private static string TextOf(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out string? text))
            {
                return text;
            }

            if (value.TryGetValue<double>(out double number))
            {
                return number.ToString("0.###", CultureInfo.InvariantCulture);
            }

            if (value.TryGetValue<bool>(out bool flag))
            {
                return flag ? "true" : "false";
            }
        }

        return string.Empty;
    }

    private static double Number(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<double>(out double result) ? result : 0;

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        _failed = true;
        Log($"[shell] WebView2 进程失败: kind={e.ProcessFailedKind} reason={e.Reason} exit={e.ExitCode} desc={e.ProcessDescription}");
        if (_options.IsHeadless)
        {
            ExitCode = 1;
            FinishAndClose();
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        long elapsed = Environment.TickCount64 - _navigationStarted;
        if (!e.IsSuccess)
        {
            Log($"[shell] 导航失败: {e.WebErrorStatus}（{elapsed}ms）");
            Fail();
            if (_options.IsHeadless)
            {
                ExitCode = 1;
                FinishAndClose();
            }

            return;
        }

        Log($"[shell] 已加载: {_webView.CoreWebView2.Source}（{elapsed}ms）");

        // 页面就绪后补一次适配（此前可能在 CSS 生效前量过，拿到的是视口宽而不是 1460）
        ApplyZoom();

        if (_options.SmokeSeconds > 0)
        {
            _smokeTimer = new System.Windows.Forms.Timer
            {
                Interval = Math.Max(1, _options.SmokeSeconds) * 1000,
            };
            _smokeTimer.Tick += async (_, _) =>
            {
                _smokeTimer?.Stop();
                await FinishSmokeAsync();
            };
            _smokeTimer.Start();
        }

        if (_options.ShotPath is not null)
        {
            _shotTimer = new System.Windows.Forms.Timer { Interval = Math.Max(1, _options.ShotDelayMs) };
            _shotTimer.Tick += async (_, _) =>
            {
                _shotTimer?.Stop();
                await CaptureShotAsync();
            };
            _shotTimer.Start();
        }
    }

    // ══════════════════════════════════════════════════════════════
    // 窗口缩放：页面尺寸固定 1460×900，只调 ZoomFactor 等比适配
    // ══════════════════════════════════════════════════════════════

    private bool _fitting;
    private Size? _pendingFit;

    /// <summary>
    /// 等比适配：按**整页文档尺寸**算缩放，而不是按牌桌 1460×900。
    /// 页面实际是 1460 宽 + body 内边距、高度 = 牌桌 900 + 下方分析条/日志条（约 1096），
    /// 只按牌桌算会把底部工具栏裁到视口外。
    ///
    /// 用 CSS 像素量（<c>getBoundingClientRect</c> / <c>scrollHeight</c>），与 WebView2 的
    /// ZoomFactor 无关，因此**不需要先把缩放归 1**，拖动边框时不会闪。
    /// </summary>
    private void ApplyZoom()
    {
        if (_closing || _webView.CoreWebView2 is null)
        {
            return;
        }

        Size client = _webView.ClientSize;
        if (client.Width <= 0 || client.Height <= 0)
        {
            return;
        }

        // 正在量的时候又来一次 resize：记下来，量完再补跑，避免丢掉最后一次尺寸
        if (_fitting)
        {
            _pendingFit = client;
            return;
        }

        _ = FitToDocumentAsync(client);
    }

    private async Task FitToDocumentAsync(Size client)
    {
        if (_closing || _webView.CoreWebView2 is null)
        {
            return;
        }

        _fitting = true;
        try
        {
            string raw = await _webView.CoreWebView2.ExecuteScriptAsync(
                "(()=>{const t=document.getElementById('table');"
                + "const b=document.body;"
                + "if(!t||!b){return '0,0';}"
                + "const cs=getComputedStyle(b);"
                + "const padX=(parseFloat(cs.paddingLeft)||0)+(parseFloat(cs.paddingRight)||0);"
                + "const r=t.getBoundingClientRect();"
                + "const w=Math.ceil(r.width+padX);"
                + "const h=Math.ceil(Math.max(b.scrollHeight,document.documentElement.scrollHeight));"
                + "return w+','+h;})()");

            string text = raw.Trim().Trim('"');
            string[] parts = text.Split(',');
            if (parts.Length == 2
                && double.TryParse(parts[0], out double docW)
                && double.TryParse(parts[1], out double docH)
                && docW >= 600 && docH >= 400)
            {
                double zoom = Math.Min(client.Width / docW, client.Height / docH);
                zoom = Math.Clamp(zoom, 0.25, 5.0);

                if (Math.Abs(_webView.ZoomFactor - zoom) > 0.001)
                {
                    _webView.ZoomFactor = zoom;
                    Log($"[shell] 视口 {client.Width}×{client.Height} · 文档 {docW:0}×{docH:0} · 缩放 {zoom:0.###}");
                }
            }
            else
            {
                Log("[shell] 量页面尺寸失败: " + raw);
            }
        }
        catch (Exception ex)
        {
            Log("[shell] 设置缩放失败: " + ex.Message);
        }
        finally
        {
            _fitting = false;
        }

        // 量期间尺寸又变过 → 用最新尺寸再跑一次
        if (_pendingFit is { } pending)
        {
            _pendingFit = null;
            ApplyZoom();
            _ = pending;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // 自检 / 截图
    // ══════════════════════════════════════════════════════════════

    private async Task FinishSmokeAsync()
    {
        if (_closing)
        {
            return;
        }

        const string probe = """
            (function () {
              try {
                var g = window.__game || null;
                return JSON.stringify({
                  hasGame: !!g,
                  isHost: !!(g && g.__isHost),
                  state: g ? g.state : null,
                  handNumber: g ? g.handNumber : 0,
                  handsPlayed: g ? g.handsPlayed : 0,
                  matchOver: g ? g.matchOver : false,
                  wall: g && g.wall ? g.wall.length : -1,
                  humanHand: g && g.players && g.players[0] && g.players[0].hand ? g.players[0].hand.length : -1,
                  scores: g ? g.scores : null
                });
              } catch (e) {
                return JSON.stringify({ error: String((e && e.stack) || e) });
              }
            })()
            """;

        string? reportJson = null;
        try
        {
            string raw = await _webView.CoreWebView2.ExecuteScriptAsync(probe);
            var value = JsonNode.Parse(raw) as JsonValue;
            reportJson = value is not null && value.TryGetValue<string>(out string? text) ? text : raw;
        }
        catch (Exception ex)
        {
            Log("[smoke] ExecuteScriptAsync 失败: " + ex.Message);
            Fail();
        }

        JsonObject? report = null;
        if (reportJson is not null)
        {
            try
            {
                report = JsonNode.Parse(reportJson) as JsonObject;
            }
            catch
            {
                // report 保持 null，下面按失败处理。
            }
        }

        bool hasGame = BoolValue(report?["hasGame"]);
        bool isHost = BoolValue(report?["isHost"]);
        double handsPlayed = Number(report?["handsPlayed"]);
        bool matchOver = BoolValue(report?["matchOver"]);
        bool oneRound = handsPlayed >= 1 || matchOver;

        Log("");
        Log($"[smoke] 控制台消息: {_consoleTotal} 条（JS 错误: {(_failed ? "有" : "无")}）");
        Log($"[smoke] 页面报告: {reportJson ?? "(空)"}");
        Log($"[smoke] 检查: hasGame={hasGame} isHost={isHost} handsPlayed={handsPlayed} matchOver={matchOver}");

        bool ok = !_failed && hasGame && isHost && oneRound;
        ExitCode = ok ? 0 : 1;
        Log(ok ? "[smoke] RESULT: PASS" : "[smoke] RESULT: FAIL");
        FinishAndClose();
    }

    private async Task CaptureShotAsync()
    {
        if (_closing || _shotDone || _webView.CoreWebView2 is null)
        {
            return;
        }

        _shotDone = true;
        try
        {
            string path = Path.GetFullPath(_options.ShotPath!);
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 截图前回到页面顶部：牌桌在视口里居中，滚动位置不影响结论，但成品图更直观。
            try
            {
                await _webView.CoreWebView2.ExecuteScriptAsync("window.scrollTo(0, 0)");
                await Task.Delay(250);
            }
            catch
            {
                // 页面还没准备好时忽略，直接截当前视口。
            }

            using var stream = new MemoryStream();
            await _webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            byte[] bytes = stream.ToArray();
            await File.WriteAllBytesAsync(path, bytes);
            Log($"[shot] 已保存 {path}（{bytes.Length} 字节）");
            Log($"[shot] 窗口客户区 {ClientSize.Width}×{ClientSize.Height} · WebView {_webView.ClientSize.Width}×{_webView.ClientSize.Height} · 缩放 {_webView.ZoomFactor:0.###}");
        }
        catch (Exception ex)
        {
            Log("[shot] 截图失败: " + ex);
            Fail();
        }

        if (_options.SmokeSeconds <= 0)
        {
            ExitCode = _failed ? 1 : 0;
            FinishAndClose();
        }
    }

    // ══════════════════════════════════════════════════════════════
    // 收尾
    // ══════════════════════════════════════════════════════════════

    private static bool BoolValue(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<bool>(out bool result) && result;

    private void Fail() => _failed = true;

    private void FinishAndClose()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _smokeTimer?.Stop();
        _shotTimer?.Stop();
        try
        {
            _webView.Dispose();
        }
        catch
        {
            // 已释放 / 未初始化。
        }

        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        _smokeTimer?.Stop();
        _shotTimer?.Stop();

        try
        {
            if (_consoleReceiver is not null)
            {
                _consoleReceiver.DevToolsProtocolEventReceived -= OnConsoleApiCalled;
                _consoleReceiver = null;
            }

            if (_exceptionReceiver is not null)
            {
                _exceptionReceiver.DevToolsProtocolEventReceived -= OnExceptionThrown;
                _exceptionReceiver = null;
            }
        }
        catch
        {
            // CoreWebView2 已释放。
        }

        try
        {
            // 关窗释放 CoreWebView2（WebView2.Dispose 会释放 CoreWebView2 与 host object）。
            _webView.Dispose();
        }
        catch
        {
            // 忽略重复释放。
        }

        base.OnFormClosing(e);
    }

    private void Log(string line)
    {
        if (_options.IsHeadless)
        {
            Console.WriteLine(line);
            Console.Out.Flush();
        }
        else
        {
            System.Diagnostics.Debug.WriteLine(line);
        }
    }
}

