using System.ComponentModel;
using System.Diagnostics;
using Tiisu.DevTools.TaskCenter;

namespace Tiisu.DevTools;

/// <summary>四个分区（美工 / 音效 / 布局 / 牌桌样式）的启动器窗口。</summary>
internal sealed class DevToolsForm : Form
{
    private static readonly (string Label, string Variant, string FileName)[] TableStyles =
    {
        ("平面", "flat", "style-flat.png"),
    };

    private readonly Toolchain _tools;
    private readonly TextBox _log;
    private readonly ToolStripStatusLabel _status;
    private readonly ToolStripStatusLabel _timing;
    private bool _busy;
    private string _renderOutputDir = string.Empty;
    private string _aiOutputDir = string.Empty;
    private string? _lastAtlasPath;

    public DevToolsForm(Toolchain tools)
    {
        _tools = tools;
        _renderOutputDir = tools.OutputDir;
        _aiOutputDir = Path.Combine(tools.WorkingDirectory, "artifacts", "selfplay");

        SuspendLayout();

        Text = "Tiisu 开发者工具";
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        ClientSize = new Size(980, 720);
        MinimumSize = new Size(880, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(10, 10, 10, 6);

        _log = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point),
        };

        _status = new ToolStripStatusLabel
        {
            Text = "就绪",
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _timing = new ToolStripStatusLabel
        {
            Text = string.Empty,
            AutoSize = true,
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        var sections = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0),
        };
        sections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        sections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        sections.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        sections.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        sections.Controls.Add(BuildArtSection(), 0, 0);
        sections.Controls.Add(BuildAudioSection(), 1, 0);
        sections.Controls.Add(BuildLayoutSection(), 0, 1);
        sections.Controls.Add(BuildStyleSection(), 1, 1);
          var publish = BuildPublishSection();
          sections.Controls.Add(publish, 0, 2);
          sections.SetColumnSpan(publish, 2);

        var logBox = new GroupBox
        {
            Text = "运行日志",
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 6, 10, 8),
            Margin = new Padding(4),
        };
        logBox.Controls.Add(_log);

        var statusStrip = new StatusStrip
        {
            Dock = DockStyle.Fill,
            SizingGrip = false,
            AutoSize = false,
        };
        statusStrip.Items.Add(_status);
        statusStrip.Items.Add(new ToolStripSeparator());
        statusStrip.Items.Add(_timing);

        var publishTasks = DevTaskRegistry.Publish(new PublishTaskActions(
            CheckPublishAsync,
            BuildInstallerAsync,
            BuildSteamDepotAsync,
            BuildRulesAsync,
            CopyArtifactHashesAsync,
            OpenDistAsync,
            OpenDesktopSetupAsync));
        var renderTasks = DevTaskRegistry.Render(new RenderTaskActions(
            () => _renderOutputDir,
            RenderScreenTaskAsync,
            RenderRoundEndTaskAsync,
            RenderActionsTaskAsync,
            ExportUiSvgTaskAsync,
            RenderTableStyleTaskAsync,
            ExportSteamTaskAsync,
            () => Path.Combine(_tools.WorkingDirectory, "artifacts", "steam", "01-table.png"),
            ExportSteamCapsulesTaskAsync,
            () => Path.Combine(_tools.WorkingDirectory, "artifacts", "steam", "capsules", "main-616x353.png"),
            OpenRenderOutputAsync));
        var aiTasks = DevTaskRegistry.Ai(new AiTaskActions(
            ExportSelfplayTaskAsync,
            SmokeNeuralTaskAsync,
            Smoke7NeuuTaskAsync,
            BenchSnapshotTaskAsync,
            OpenAiDataAsync));
        var auditTasks = DevTaskRegistry.Audit(new AuditTaskActions(
            AuditGamesTaskAsync,
            AuditUiTaskAsync,
            AuditCallTaskAsync,
            SmokeRunTaskAsync));
        var resourceTasks = DevTaskRegistry.Resource(new ResourceTaskActions(
            CheckFontTaskAsync,
            AuditUiStringsTaskAsync,
            ExportAtlasTaskAsync,
            () => _lastAtlasPath,
            OpenAssetsAsync,
            OpenDocsAsync));
        var allTasks = publishTasks.Concat(renderTasks).Concat(aiTasks).Concat(auditTasks).Concat(resourceTasks).ToArray();

        var taskCenter = new TaskCenterView(
            allTasks,
            _renderOutputDir,
            path => _renderOutputDir = path,
            AppendLog,
            (text, timing) => SetStatus(text, timing),
            task => RunGuardedAsync(task.Title, task.Run));

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var taskPage = new TabPage("任务中心");
        taskPage.Controls.Add(taskCenter);
        var oldPage = new TabPage("旧版工具");
        oldPage.Controls.Add(sections);
        tabs.TabPages.Add(taskPage);
        tabs.TabPages.Add(oldPage);

        layout.Controls.Add(tabs, 0, 0);
        layout.Controls.Add(logBox, 0, 1);
        layout.Controls.Add(statusStrip, 0, 2);
        Controls.Add(layout);

        ResumeLayout(performLayout: true);

        AppendLog("Tiisu 开发者工具 —— 美工 / 音效 / 布局 / 牌桌样式");
        foreach (string note in _tools.Notes)
        {
            AppendLog("  " + note);
        }

        AppendLog("  输出目录：" + _tools.OutputDir);
        AppendLog(string.Empty);

        if (_tools.GameExe is null)
        {
            SetStatus("未找到 Tiisu.Mahjong.exe：先 dotnet build src/Tiisu.App -c Release", string.Empty);
        }
    }

    // ── 分区 ───────────────────────────────────────────────────────

    private GroupBox BuildArtSection() => Section(
        "美工",
        CommandButton("导出合并 SVG", ExportUiSvgAsync),
        CommandButton("按钮三态图", RenderActionsAsync),
        CommandButton("打开输出目录", OpenOutputAsync));

    private GroupBox BuildAudioSection() => Section(
        "音效",
        CommandButton("生成音效+语音", GenerateAudioAsync),
        CommandButton("只生成音效", GenerateSfxAsync),
        CommandButton("打开 sounds 目录", () => OpenDirectoryAsync(_tools.SoundsDir, "sounds")),
        CommandButton("打开 voices 目录", () => OpenDirectoryAsync(_tools.VoicesDir, "voices")));

    private GroupBox BuildLayoutSection() => Section(
        "布局",
        CommandButton("启动游戏（布局编辑）", LaunchGameAsync),
        CommandButton("查看当前自定义布局", ViewLayoutAsync),
        CommandButton("删除自定义布局（恢复默认）", DeleteLayoutAsync));

    private GroupBox BuildStyleSection()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = TableStyles.Length,
            RowCount = 1,
            Margin = new Padding(0),
        };
        for (int i = 0; i < TableStyles.Length; i++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / TableStyles.Length));
            var style = TableStyles[i];
            var button = new Button
            {
                Text = style.Label,
                Dock = DockStyle.Fill,
                Margin = new Padding(2),
                UseVisualStyleBackColor = true,
            };
            button.Click += async (_, _) => await RunGuardedAsync(
                "牌桌样式 " + style.Label,
                () => RenderStyleAsync(style));
            row.Controls.Add(button, i, 0);
        }

        return Section(
            "牌桌样式",
            row,
            CommandButton("全部生成并打开目录", RenderAllStylesAsync));
    }

    private GroupBox BuildPublishSection()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Height = 84,
            Margin = new Padding(0),
        };
        for (int c = 0; c < 3; c++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        }

        for (int r = 0; r < 2; r++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        }

        var buttons = new (string Text, Func<Task> Action)[]
        {
            ("发布自检", CheckPublishAsync),
            ("生成安装包", BuildInstallerAsync),
            ("生成规则库", BuildRulesAsync),
            ("复制产物哈希", CopyArtifactHashesAsync),
            ("打开 dist", OpenDistAsync),
            ("打开桌面安装包", OpenDesktopSetupAsync),
        };

        for (int i = 0; i < buttons.Length; i++)
        {
            var button = new Button
            {
                Text = buttons[i].Text,
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 3, 2, 3),
                UseVisualStyleBackColor = true,
            };
            button.Click += async (_, _) => await RunGuardedAsync(buttons[i].Text, buttons[i].Action);
            grid.Controls.Add(button, i % 3, i / 3);
        }

        return Section("发布 / 合规", grid);
    }


    private static GroupBox Section(string title, params Control[] rows)
    {
        var box = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 6, 10, 8),
            Margin = new Padding(4),
        };

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = rows.Length + 1,
            Margin = new Padding(0),
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        foreach (Control row in rows)
        {
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, row is Button ? 38 : row is TableLayoutPanel grid ? 42 * Math.Max(1, grid.RowCount) : 42));
            row.Dock = DockStyle.Fill;
            row.Margin = new Padding(2, 3, 2, 3);
            content.Controls.Add(row);
        }

        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        box.Controls.Add(content);
        return box;
    }

    private Button CommandButton(string text, Func<Task> action)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            UseVisualStyleBackColor = true,
        };
        button.Click += async (_, _) => await RunGuardedAsync(text, action);
        return button;
    }

    private async Task CheckPublishAsync()
    {
        string publish = Path.Combine(_tools.WorkingDirectory, "dist", "publish");
        if (!Directory.Exists(publish))
        {
            AppendLog("  ! 还没有 dist/publish：先生成安装包");
            SetStatus("还没有 dist/publish", string.Empty);
            return;
        }

        await ExecuteAsync(
            "发布自检",
            Application.ExecutablePath,
            new[] { "--check-publish", publish },
            _tools.WorkingDirectory,
            revealPath: null);
    }

    private async Task BuildInstallerAsync()
    {
        string script = Path.Combine(_tools.WorkingDirectory, "installer", "build_installer.ps1");
        if (!File.Exists(script))
        {
            AppendLog("  ! 找不到安装包脚本：" + script);
            SetStatus("找不到 installer/build_installer.ps1", string.Empty);
            return;
        }

        string desktopSetup = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "TiisuMahjong-Setup.exe");
        await ExecuteAsync(
            "生成安装包",
            "powershell",
            new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-CopyToDesktop" },
            _tools.WorkingDirectory,
            File.Exists(desktopSetup) ? desktopSetup : null);
    }

    private async Task BuildSteamDepotAsync()
    {
        string script = Path.Combine(_tools.WorkingDirectory, "tools", "steam_package.ps1");
        if (!File.Exists(script))
        {
            AppendLog("  ! 找不到 Steam 打包脚本：" + script);
            SetStatus("找不到 tools/steam_package.ps1", string.Empty);
            return;
        }

        await ExecuteAsync(
            "Steam Depot",
            "powershell",
            new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script },
            _tools.WorkingDirectory,
            Path.Combine(_tools.WorkingDirectory, "dist", "steam", "depot-manifest.json"));
    }

    private async Task BuildRulesAsync()
    {
        string script = Path.Combine(_tools.WorkingDirectory, "tools", "build_rules_lib.ps1");
        if (!File.Exists(script))
        {
            AppendLog("  ! 找不到规则库脚本：" + script);
            SetStatus("找不到 tools/build_rules_lib.ps1", string.Empty);
            return;
        }

        await ExecuteAsync(
            "生成规则库",
            "powershell",
            new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script },
            _tools.WorkingDirectory,
            Path.Combine(_tools.WorkingDirectory, "dist"));
    }

    private Task CopyArtifactHashesAsync()
    {
        AppendLog(string.Empty);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 产物 SHA256");
        string dist = Path.Combine(_tools.WorkingDirectory, "dist");
        if (!Directory.Exists(dist))
        {
            AppendLog("  ! dist 不存在，先生成安装包 / 规则库");
            return Task.CompletedTask;
        }

        foreach (string file in Directory.EnumerateFiles(dist, "*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var stream = File.OpenRead(file);
                string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
                AppendLog("  " + Path.GetFileName(file) + "  " + hash);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppendLog("  ! " + Path.GetFileName(file) + "：" + ex.Message);
            }
        }

        SetStatus("已把 dist 产物哈希写入日志", string.Empty);
        return Task.CompletedTask;
    }

    private Task OpenDistAsync()
    {
        OpenDirectory(Path.Combine(_tools.WorkingDirectory, "dist"), "dist");
        return Task.CompletedTask;
    }

    private Task OpenDesktopSetupAsync()
    {
        string setup = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "TiisuMahjong-Setup.exe");
        if (!File.Exists(setup))
        {
            AppendLog("  ! 桌面没有 TiisuMahjong-Setup.exe，先生成安装包");
            SetStatus("桌面没有安装包", string.Empty);
            return Task.CompletedTask;
        }

        CommandRunner.OpenInExplorer(setup, selectFile: true);
        SetStatus("已定位桌面安装包", string.Empty);
        return Task.CompletedTask;
    }


    private string RenderPath(string file)
    {
        Directory.CreateDirectory(_renderOutputDir);
        return Path.Combine(_renderOutputDir, file);
    }

    private async Task RenderScreenTaskAsync(string screen, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? _renderOutputDir);
        await ExecuteAsync(
            "出图：" + screen,
            Application.ExecutablePath,
            new[] { "--render-screen", outputPath, screen },
            _tools.WorkingDirectory,
            outputPath);
    }

    private async Task RenderRoundEndTaskAsync(string outputPath)
    {
        await ExecuteAsync(
            "出图：结算页",
            Application.ExecutablePath,
            new[] { "--render-roundend", outputPath },
            _tools.WorkingDirectory,
            outputPath);
    }

    private async Task RenderActionsTaskAsync(string outputPath)
    {
        await ExecuteAsync(
            "出图：按钮三态",
            Application.ExecutablePath,
            new[] { "--render-actions", outputPath },
            _tools.WorkingDirectory,
            outputPath);
    }

    private async Task ExportUiSvgTaskAsync(string outputPath)
    {
        await ExecuteAsync(
            "导出合并 SVG",
            Application.ExecutablePath,
            new[] { "--export-ui-svg", outputPath },
            _tools.WorkingDirectory,
            outputPath);
    }

    private async Task RenderTableStyleTaskAsync(string outputPath)
    {
        await ExecuteAsync(
            "出图：牌桌样式",
            Application.ExecutablePath,
            StyleArguments(outputPath, "flat"),
            _tools.WorkingDirectory,
            outputPath);
    }

    private async Task ExportSteamTaskAsync()
    {
        string dir = Path.Combine(_tools.WorkingDirectory, "artifacts", "steam");
        Directory.CreateDirectory(dir);
        await ExecuteAsync(
            "Steam 商店截图",
            Application.ExecutablePath,
            new[] { "--export-steam-shots", dir },
            _tools.WorkingDirectory,
            Path.Combine(dir, "01-table.png"));
    }

    private async Task ExportSteamCapsulesTaskAsync()
    {
        string dir = Path.Combine(_tools.WorkingDirectory, "artifacts", "steam", "capsules");
        Directory.CreateDirectory(dir);
        await ExecuteAsync(
            "Steam 胶囊图",
            Application.ExecutablePath,
            new[] { "--export-steam-capsules", dir },
            _tools.WorkingDirectory,
            Path.Combine(dir, "main-616x353.png"));
    }

    private Task OpenRenderOutputAsync()
    {
        OpenDirectory(_renderOutputDir, "出图目录");
        return Task.CompletedTask;
    }


    private async Task ExportSelfplayTaskAsync()
    {
        using var dialog = new SelfPlayDialog(_tools.WorkingDirectory);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            AppendLog("  已取消：导出自博弈数据");
            return;
        }

        string? directory = Path.GetDirectoryName(dialog.OutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            _aiOutputDir = directory;
        }

        var args = new List<string>
        {
            "--export-selfplay",
            dialog.OutputPath,
            "--games",
            dialog.Games.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--jobs",
            dialog.Jobs.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--sample-every",
            dialog.SampleEvery.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--explore",
            dialog.Explore.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        };
        await ExecuteAsync(
            "导出自博弈数据",
            Application.ExecutablePath,
            args,
            _tools.WorkingDirectory,
            dialog.OutputPath);
    }

    private async Task SmokeNeuralTaskAsync()
    {
        await ExecuteAsync(
            "验证 7Neuu 权重",
            Application.ExecutablePath,
            new[] { "--smoke-neural" },
            _tools.WorkingDirectory,
            revealPath: null);
    }

    private async Task Smoke7NeuuTaskAsync()
    {
        await ExecuteAsync(
            "验证 7Neuu 决策",
            Application.ExecutablePath,
            new[] { "--smoke-7neuu" },
            _tools.WorkingDirectory,
            revealPath: null);
    }

    private async Task BenchSnapshotTaskAsync()
    {
        await ExecuteAsync(
            "UI 快照性能",
            Application.ExecutablePath,
            new[] { "--bench-snapshot", "20000" },
            _tools.WorkingDirectory,
            revealPath: null);
    }

    private Task OpenAiDataAsync()
    {
        Directory.CreateDirectory(_aiOutputDir);
        OpenDirectory(_aiOutputDir, "自博弈数据");
        return Task.CompletedTask;
    }

    private async Task AuditGamesTaskAsync()
    {
        using var dialog = new AuditGamesDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            AppendLog("  已取消：批量审局");
            return;
        }

        await ExecuteAsync(
            "批量审局",
            Application.ExecutablePath,
            dialog.Arguments(),
            _tools.WorkingDirectory,
            revealPath: null);
    }

    private async Task AuditUiTaskAsync()
    {
        await ExecuteAsync(
            "UI 不变量审计",
            Application.ExecutablePath,
            new[] { "--audit-games", "50", "4", "1", "--jobs", "4", "--ui" },
            _tools.WorkingDirectory,
            revealPath: null);
    }

    private async Task AuditCallTaskAsync()
    {
        await ExecuteAsync(
            "报听分支审计",
            Application.ExecutablePath,
            new[] { "--audit-games", "50", "4", "1", "--jobs", "4", "--passive", "--call" },
            _tools.WorkingDirectory,
            revealPath: null);
    }

    private async Task SmokeRunTaskAsync()
    {
        await ExecuteAsync(
            "整场冒烟",
            Application.ExecutablePath,
            new[] { "--smoke-run", "1", "4" },
            _tools.WorkingDirectory,
            revealPath: null);
    }

    private async Task CheckFontTaskAsync()
    {
        await ExecuteAsync(
            "字体缺字检查",
            Application.ExecutablePath,
            new[] { "--check-font", _tools.WorkingDirectory },
            _tools.WorkingDirectory,
            revealPath: null);
    }

    private async Task AuditUiStringsTaskAsync()
    {
        await ExecuteAsync(
            "盘点 UI 文案",
            Application.ExecutablePath,
            new[] { "--audit-ui-strings", _tools.WorkingDirectory },
            _tools.WorkingDirectory,
            revealPath: null);
    }

    private async Task ExportAtlasTaskAsync()
    {
        Directory.CreateDirectory(_tools.OutputDir);
        string path = Path.Combine(_tools.OutputDir, "ui-atlas.png");
        _lastAtlasPath = path;
        await ExecuteAsync(
            "导出 UI 图集",
            Application.ExecutablePath,
            new[] { "--export-ui-atlas", _tools.OutputDir },
            _tools.WorkingDirectory,
            path);
    }

    private Task OpenAssetsAsync()
    {
        OpenDirectory(_tools.AssetsDir, "Assets");
        return Task.CompletedTask;
    }

    private Task OpenDocsAsync()
    {
        OpenDirectory(Path.Combine(_tools.WorkingDirectory, "docs"), "docs");
        return Task.CompletedTask;
    }

    // ── 命令 ───────────────────────────────────────────────────────

    private async Task ExportUiSvgAsync()
    {
        if (RequireGame() is not { } game)
        {
            return;
        }

        Directory.CreateDirectory(_tools.OutputDir);
        const string relative = "artifacts/uitools/ui-merged.svg";
        await ExecuteAsync(
            "导出合并 SVG",
            game,
            new[] { "--export-ui-svg", relative },
            _tools.WorkingDirectory,
            Path.Combine(_tools.OutputDir, "ui-merged.svg"));
    }

    private async Task RenderActionsAsync()
    {
        if (RequireGame() is not { } game)
        {
            return;
        }

        Directory.CreateDirectory(_tools.OutputDir);
        const string relative = "artifacts/uitools/btn-colors.png";
        await ExecuteAsync(
            "按钮三态图",
            game,
            new[] { "--render-actions", relative },
            _tools.WorkingDirectory,
            Path.Combine(_tools.OutputDir, "btn-colors.png"));
    }

    private Task OpenOutputAsync()
    {
        Directory.CreateDirectory(_tools.OutputDir);
        OpenDirectory(_tools.OutputDir, "输出目录");
        return Task.CompletedTask;
    }

    private async Task GenerateAudioAsync()
    {
        if (RequireAudioGen() is not { } audioGen)
        {
            return;
        }

        int? code = await ExecuteAsync(
            "生成音效+语音",
            audioGen,
            Array.Empty<string>(),
            _tools.WorkingDirectory,
            revealPath: null);

        if (code == 0)
        {
            OpenDirectory(_tools.AssetsDir, "Assets");
        }
    }

    private async Task GenerateSfxAsync()
    {
        if (RequireAudioGen() is not { } audioGen)
        {
            return;
        }

        int? code = await ExecuteAsync(
            "只生成音效",
            audioGen,
            new[] { "--sfx" },
            _tools.WorkingDirectory,
            revealPath: null);

        if (code == 0)
        {
            OpenDirectory(_tools.SoundsDir, "sounds");
        }
    }

    private Task LaunchGameAsync()
    {
        if (RequireGame() is not { } game)
        {
            return Task.CompletedTask;
        }

        AppendLog(string.Empty);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 启动游戏（布局编辑）");
        AppendLog("  " + game);

        try
        {
            using Process process = CommandRunner.Launch(
                game,
                Path.GetDirectoryName(game) ?? _tools.WorkingDirectory);
            AppendLog($"  已启动（PID {process.Id}）");
            SetStatus("游戏已启动 · 进入牌桌按 F2 编辑布局", string.Empty);
            MessageBox.Show(
                this,
                "已启动 Tiisu.Mahjong.exe。\n\n进入牌桌按 F2 进入布局编辑（S 保存，R 两次确认恢复默认）。",
                "布局编辑",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            AppendLog("  ! 启动失败：" + ex.Message);
            SetStatus("启动游戏失败：" + ex.Message, string.Empty);
        }

        return Task.CompletedTask;
    }

    private Task ViewLayoutAsync()
    {
        string path = _tools.LayoutFile;
        AppendLog(string.Empty);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 查看当前自定义布局");
        AppendLog("  " + path);

        if (File.Exists(path))
        {
            bool opened = CommandRunner.OpenInExplorer(path, selectFile: true);
            AppendLog(opened ? "  已在资源管理器中定位" : "  ! 打开失败：" + path);
            SetStatus(opened ? "已定位 layout-custom.json" : "打开布局文件失败", string.Empty);
            return Task.CompletedTask;
        }

        AppendLog("  ! 还没有自定义布局，游戏当前使用默认布局");
        SetStatus("还没有自定义布局（当前为默认布局）", string.Empty);

        string? directory = Path.GetDirectoryName(path);
        if (directory is not null && Directory.Exists(directory))
        {
            CommandRunner.OpenInExplorer(directory, selectFile: false);
        }
        else
        {
            MessageBox.Show(
                this,
                "还没有自定义布局，游戏当前使用默认布局。\n\n进入游戏牌桌按 F2 编辑，按 S 保存后会生成：\n" + path,
                "查看自定义布局",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        return Task.CompletedTask;
    }

    private Task DeleteLayoutAsync()
    {
        string path = _tools.LayoutFile;
        AppendLog(string.Empty);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] 删除自定义布局");
        AppendLog("  " + path);

        if (!File.Exists(path))
        {
            AppendLog("  ! 没有自定义布局，已经是默认布局");
            SetStatus("没有自定义布局，已是默认布局", string.Empty);
            return Task.CompletedTask;
        }

        DialogResult answer = MessageBox.Show(
            this,
            "删除后游戏恢复默认布局，不能撤销。\n\n" + path,
            "删除自定义布局",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes)
        {
            AppendLog("  已取消");
            SetStatus("已取消删除", string.Empty);
            return Task.CompletedTask;
        }

        try
        {
            File.Delete(path);
            AppendLog("  已删除，下次启动游戏使用默认布局");
            SetStatus("已删除自定义布局（恢复默认）", string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppendLog("  ! 删除失败：" + ex.Message);
            SetStatus("删除失败：" + ex.Message, string.Empty);
        }

        return Task.CompletedTask;
    }

    private async Task RenderStyleAsync((string Label, string Variant, string FileName) style)
    {
        if (RequireGame() is not { } game)
        {
            return;
        }

        Directory.CreateDirectory(_tools.OutputDir);
        await ExecuteAsync(
            "牌桌样式 " + style.Label,
            game,
            StyleArguments(style.FileName, style.Variant),
            _tools.WorkingDirectory,
            Path.Combine(_tools.OutputDir, style.FileName));
    }

    private async Task RenderAllStylesAsync()
    {
        if (RequireGame() is not { } game)
        {
            return;
        }

        Directory.CreateDirectory(_tools.OutputDir);
        var total = Stopwatch.StartNew();
        int failed = 0;

        foreach (var style in TableStyles)
        {
            int? code = await ExecuteAsync(
                "牌桌样式 " + style.Label,
                game,
                StyleArguments(style.FileName, style.Variant),
                _tools.WorkingDirectory,
                Path.Combine(_tools.OutputDir, style.FileName));
            if (code != 0)
            {
                failed++;
            }
        }

        total.Stop();
        string summary = failed == 0
            ? $"全部生成：{TableStyles.Length}/{TableStyles.Length} 成功"
            : $"全部生成：{TableStyles.Length - failed}/{TableStyles.Length} 成功，{failed} 个失败";
        AppendLog($"  → {summary} · {CommandRunner.FormatElapsed(total.Elapsed)}");
        SetStatus(summary, CommandRunner.FormatElapsed(total.Elapsed));
        OpenDirectory(_tools.OutputDir, "输出目录");
    }

    private static string[] StyleArguments(string fileName, string variant) => new[]
    {
        "--render-25d",
        "artifacts/uitools/" + fileName,
        "--variant",
        variant,
    };

    // ── 执行 / 状态 ────────────────────────────────────────────────

    private async Task RunGuardedAsync(string label, Func<Task> action)
    {
        if (_busy)
        {
            SetStatus("上一条命令还在执行，请稍候", string.Empty);
            return;
        }

        _busy = true;
        UseWaitCursor = true;
        SetStatus(label + " …", string.Empty);

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            AppendLog("  ! 未处理的异常：" + ex.Message);
            SetStatus(label + " 失败：" + ex.Message, string.Empty);
        }
        finally
        {
            _busy = false;
            UseWaitCursor = false;
        }
    }

    private async Task<int?> ExecuteAsync(
        string title,
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string? revealPath)
    {
        AppendLog(string.Empty);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] {title}");
        AppendLog("  " + CommandRunner.FormatCommandLine(executable, arguments));

        CommandResult result = await CommandRunner.RunAsync(executable, arguments, workingDirectory);

        if (!result.Started)
        {
            AppendLog("  ! 启动失败：" + result.StartError);
            SetStatus($"{title}：命令无法启动", CommandRunner.FormatElapsed(result.Elapsed));
            MessageBox.Show(
                this,
                result.StartError + "\n\n" + result.CommandLine,
                title + " —— 命令无法启动",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return null;
        }

        string output = Tail(result.Output, 1600);
        if (output.Length > 0)
        {
            AppendLog("  " + output.Replace("\r\n", "\n  ", StringComparison.Ordinal).Replace("\n", Environment.NewLine + "  ", StringComparison.Ordinal));
        }

        bool ok = result.ExitCode == 0;
        string summary = ok ? "exit 0" : $"exit {result.ExitCode}（失败）";
        string elapsed = CommandRunner.FormatElapsed(result.Elapsed);
        AppendLog($"  → {summary} · {elapsed}");
        SetStatus($"{title}：{summary}", elapsed);

        if (ok && revealPath is not null)
        {
            RevealFile(revealPath);
        }

        return result.ExitCode;
    }

    private void RevealFile(string path)
    {
        if (!File.Exists(path))
        {
            AppendLog("  ! 命令成功但没找到输出文件：" + path);
            return;
        }

        AppendLog("  输出：" + path);
        if (!CommandRunner.OpenInExplorer(path, selectFile: true))
        {
            AppendLog("  ! 无法在资源管理器中定位该文件");
        }
    }

    private void OpenDirectory(string path, string label)
    {
        if (!Directory.Exists(path))
        {
            AppendLog("  ! " + label + " 目录不存在：" + path);
            SetStatus(label + " 目录不存在，先生成一次", string.Empty);
            return;
        }

        bool opened = CommandRunner.OpenInExplorer(path, selectFile: false);
        AppendLog((opened ? "  已打开 " : "  ! 打开失败 ") + label + "：" + path);
        if (opened)
        {
            SetStatus("已打开 " + label, string.Empty);
        }
        else
        {
            SetStatus("打开 " + label + " 失败", string.Empty);
        }
    }

    private Task OpenDirectoryAsync(string path, string label)
    {
        OpenDirectory(path, label);
        return Task.CompletedTask;
    }

    private string? RequireGame() => RequireTool(_tools.GameExe, "Tiisu.Mahjong.exe", "dotnet build src/Tiisu.App -c Release");

    private string? RequireAudioGen() => RequireTool(_tools.AudioGenExe, "Tiisu.AudioGen.exe", "dotnet build tools/Tiisu.AudioGen -c Release");

    private string? RequireTool(string? executable, string name, string buildCommand)
    {
        if (executable is not null)
        {
            return executable;
        }

        string message = $"找不到 {name}。\n\n先构建：{buildCommand}\n或在 config.toml 的 [dev] root 指定仓库根目录。";
        AppendLog("  ! " + message.Replace("\n\n", " ", StringComparison.Ordinal));
        SetStatus("找不到 " + name + "：先 " + buildCommand, string.Empty);
        MessageBox.Show(this, message, "找不到命令", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return null;
    }

    private void SetStatus(string text, string timing)
    {
        _status.Text = text;
        _timing.Text = timing;
    }

    private void AppendLog(string line)
    {
        if (_log.IsDisposed)
        {
            return;
        }

        _log.AppendText(line + Environment.NewLine);
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private static string Tail(string text, int limit) =>
        text.Length <= limit ? text : "…（仅显示末尾 " + limit + " 字符）" + text[^limit..];
}
