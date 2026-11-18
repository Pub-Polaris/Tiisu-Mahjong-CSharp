using System.ComponentModel;
using System.Diagnostics;
using Tiisu.DevTools.TaskCenter;
using Tiisu.Localization;

namespace Tiisu.DevTools;

/// <summary>四个分区（美工 / 音效 / 布局 / 牌桌样式）的启动器窗口。</summary>
internal sealed class DevToolsForm : Form
{
    private static readonly (string Label, string Variant, string FileName)[] TableStyles =
    {
        (L.T("tools.devtools.tableStyle.flat"), "flat", "style-flat.png"),
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

        Text = L.T("tools.devtools.title");
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
            Text = L.T("tools.devtools.status.ready"),
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
            Text = L.T("tools.devtools.log.group"),
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
            CheckSteamTaskAsync,
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
        var taskPage = new TabPage(L.T("tools.devtools.tab.taskCenter"));
        taskPage.Controls.Add(taskCenter);
        var oldPage = new TabPage(L.T("tools.devtools.tab.legacy"));
        oldPage.Controls.Add(sections);
        tabs.TabPages.Add(taskPage);
        tabs.TabPages.Add(oldPage);

        layout.Controls.Add(tabs, 0, 0);
        layout.Controls.Add(logBox, 0, 1);
        layout.Controls.Add(statusStrip, 0, 2);
        Controls.Add(layout);

        ResumeLayout(performLayout: true);

        AppendLog(L.T("tools.devtools.startup.banner"));
        foreach (string note in _tools.Notes)
        {
            AppendLog("  " + note);
        }

        AppendLog(L.T("tools.devtools.log.outputDir", ("path", _tools.OutputDir)));
        AppendLog(string.Empty);

        if (_tools.GameExe is null)
        {
            SetStatus(L.T("tools.devtools.status.gameExeMissing"), string.Empty);
        }
    }

    // ── 分区 ───────────────────────────────────────────────────────

    private GroupBox BuildArtSection() => Section(
        L.T("tools.devtools.section.art"),
        CommandButton(L.T("tools.devtools.action.exportUiSvg"), ExportUiSvgAsync),
        CommandButton(L.T("tools.devtools.action.renderActions"), RenderActionsAsync),
        CommandButton(L.T("tools.devtools.action.openOutput"), OpenOutputAsync));

    private GroupBox BuildAudioSection() => Section(
        L.T("tools.devtools.section.audio"),
        CommandButton(L.T("tools.devtools.action.generateAudio"), GenerateAudioAsync),
        CommandButton(L.T("tools.devtools.action.generateSfx"), GenerateSfxAsync),
        CommandButton(L.T("tools.devtools.action.openSounds"), () => OpenDirectoryAsync(_tools.SoundsDir, "sounds")),
        CommandButton(L.T("tools.devtools.action.openVoices"), () => OpenDirectoryAsync(_tools.VoicesDir, "voices")));

    private GroupBox BuildLayoutSection() => Section(
        L.T("tools.devtools.section.layout"),
        CommandButton(L.T("tools.devtools.action.launchGame"), LaunchGameAsync),
        CommandButton(L.T("tools.devtools.action.viewLayout"), ViewLayoutAsync),
        CommandButton(L.T("tools.devtools.action.deleteLayout"), DeleteLayoutAsync));

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
                L.T("tools.devtools.task.tableStyleWithLabel", ("style", style.Label)),
                () => RenderStyleAsync(style));
            row.Controls.Add(button, i, 0);
        }

        return Section(
            L.T("tools.devtools.section.style"),
            row,
            CommandButton(L.T("tools.devtools.action.renderAllStyles"), RenderAllStylesAsync));
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
            (L.T("tools.devtools.action.checkPublish"), CheckPublishAsync),
            (L.T("tools.devtools.action.buildInstaller"), BuildInstallerAsync),
            (L.T("tools.devtools.action.buildRules"), BuildRulesAsync),
            (L.T("tools.devtools.action.copyHashes"), CopyArtifactHashesAsync),
            (L.T("tools.devtools.action.openDist"), OpenDistAsync),
            (L.T("tools.devtools.action.openDesktopSetup"), OpenDesktopSetupAsync),
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

        return Section(L.T("tools.devtools.section.publish"), grid);
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
            AppendLog(L.T("tools.devtools.log.publishMissing"));
            SetStatus(L.T("tools.devtools.status.publishMissing"), string.Empty);
            return;
        }

        await ExecuteAsync(
            L.T("tools.devtools.action.checkPublish"),
            Application.ExecutablePath,
            new[] { "--check-publish", publish },
            _tools.WorkingDirectory,
            revealPath: null);
    }

    private async Task CheckSteamTaskAsync()
    {
        await ExecuteAsync(
            L.T("tools.devtools.action.checkSteam"),
            Application.ExecutablePath,
            new[] { "--check-steam", _tools.WorkingDirectory },
            _tools.WorkingDirectory,
            revealPath: null);
    }

    private async Task BuildInstallerAsync()
    {
        string script = Path.Combine(_tools.WorkingDirectory, "installer", "build_installer.ps1");
        if (!File.Exists(script))
        {
            AppendLog(L.T("tools.devtools.log.installerScriptMissing", ("path", script)));
            SetStatus(L.T("tools.devtools.status.installerScriptMissing"), string.Empty);
            return;
        }

        string desktopSetup = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "TiisuMahjong-Setup.exe");
        await ExecuteAsync(
            L.T("tools.devtools.action.buildInstaller"),
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
            AppendLog(L.T("tools.devtools.log.steamScriptMissing", ("path", script)));
            SetStatus(L.T("tools.devtools.status.steamScriptMissing"), string.Empty);
            return;
        }

        await ExecuteAsync(
            L.T("tools.devtools.action.buildSteamDepot"),
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
            AppendLog(L.T("tools.devtools.log.rulesScriptMissing", ("path", script)));
            SetStatus(L.T("tools.devtools.status.rulesScriptMissing"), string.Empty);
            return;
        }

        await ExecuteAsync(
            L.T("tools.devtools.action.buildRules"),
            "powershell",
            new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script },
            _tools.WorkingDirectory,
            Path.Combine(_tools.WorkingDirectory, "dist"));
    }

    private Task CopyArtifactHashesAsync()
    {
        AppendLog(string.Empty);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] " + L.T("tools.devtools.log.artifactHashes"));
        string dist = Path.Combine(_tools.WorkingDirectory, "dist");
        if (!Directory.Exists(dist))
        {
            AppendLog(L.T("tools.devtools.log.distMissing"));
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

        SetStatus(L.T("tools.devtools.status.hashesWritten"), string.Empty);
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
            AppendLog(L.T("tools.devtools.log.desktopSetupMissing"));
            SetStatus(L.T("tools.devtools.status.desktopSetupMissing"), string.Empty);
            return Task.CompletedTask;
        }

        CommandRunner.OpenInExplorer(setup, selectFile: true);
        SetStatus(L.T("tools.devtools.status.desktopSetupFound"), string.Empty);
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
            L.T("tools.devtools.task.renderScreen", ("screen", screen)),
            Application.ExecutablePath,
            new[] { "--render-screen", outputPath, screen },
            _tools.WorkingDirectory,
            outputPath);
    }

    private async Task RenderRoundEndTaskAsync(string outputPath)
    {
        await ExecuteAsync(
            L.T("tools.devtools.task.renderRoundEnd"),
            Application.ExecutablePath,
            new[] { "--render-roundend", outputPath },
            _tools.WorkingDirectory,
            outputPath);
    }

    private async Task RenderActionsTaskAsync(string outputPath)
    {
        await ExecuteAsync(
            L.T("tools.devtools.task.renderActions"),
            Application.ExecutablePath,
            new[] { "--render-actions", outputPath },
            _tools.WorkingDirectory,
            outputPath);
    }

    private async Task ExportUiSvgTaskAsync(string outputPath)
    {
        await ExecuteAsync(
            L.T("tools.devtools.action.exportUiSvg"),
            Application.ExecutablePath,
            new[] { "--export-ui-svg", outputPath },
            _tools.WorkingDirectory,
            outputPath);
    }

    private async Task RenderTableStyleTaskAsync(string outputPath)
    {
        await ExecuteAsync(
            L.T("tools.devtools.task.renderTableStyle"),
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
            L.T("tools.devtools.task.exportSteamShots"),
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
            L.T("tools.devtools.task.exportSteamCapsules"),
            Application.ExecutablePath,
            new[] { "--export-steam-capsules", dir },
            _tools.WorkingDirectory,
            Path.Combine(dir, "main-616x353.png"));
    }

    private Task OpenRenderOutputAsync()
    {
        OpenDirectory(_renderOutputDir, L.T("tools.devtools.open.label.renderDir"));
        return Task.CompletedTask;
    }


    private async Task ExportSelfplayTaskAsync()
    {
        using var dialog = new SelfPlayDialog(_tools.WorkingDirectory);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            AppendLog(L.T("tools.devtools.log.cancelledSelfplay"));
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
            L.T("tools.devtools.action.exportSelfplay"),
            Application.ExecutablePath,
            args,
            _tools.WorkingDirectory,
            dialog.OutputPath);
    }

    private async Task SmokeNeuralTaskAsync()
    {
        await ExecuteAsync(
            L.T("tools.devtools.action.smokeNeural"),
            Application.ExecutablePath,
            new[] { "--smoke-neural" },
            _tools.WorkingDirectory,
            revealPath: null);
    }

    private async Task Smoke7NeuuTaskAsync()
    {
        await ExecuteAsync(
            L.T("tools.devtools.action.smoke7neuu"),
            Application.ExecutablePath,
            new[] { "--smoke-7neuu" },
            _tools.WorkingDirectory,
            revealPath: null);
    }

    private async Task BenchSnapshotTaskAsync()
    {
        await ExecuteAsync(
            L.T("tools.devtools.action.benchSnapshot"),
            Application.ExecutablePath,
            new[] { "--bench-snapshot", "20000" },
            _tools.WorkingDirectory,
            revealPath: null);
    }

    private Task OpenAiDataAsync()
    {
        Directory.CreateDirectory(_aiOutputDir);
        OpenDirectory(_aiOutputDir, L.T("tools.devtools.open.label.aiData"));
        return Task.CompletedTask;
    }

    private async Task AuditGamesTaskAsync()
    {
        using var dialog = new AuditGamesDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            AppendLog(L.T("tools.devtools.log.cancelledAudit"));
            return;
        }

        await ExecuteAsync(
            L.T("tools.devtools.action.auditGames"),
            Application.ExecutablePath,
            dialog.Arguments(),
            _tools.WorkingDirectory,
            revealPath: null);
    }

    private async Task AuditUiTaskAsync()
    {
        await ExecuteAsync(
            L.T("tools.devtools.action.auditUi"),
            Application.ExecutablePath,
            new[] { "--audit-games", "50", "4", "1", "--jobs", "4", "--ui" },
            _tools.WorkingDirectory,
            revealPath: null);
    }

    private async Task AuditCallTaskAsync()
    {
        await ExecuteAsync(
            L.T("tools.devtools.action.auditCall"),
            Application.ExecutablePath,
            new[] { "--audit-games", "50", "4", "1", "--jobs", "4", "--passive", "--call" },
            _tools.WorkingDirectory,
            revealPath: null);
    }

    private async Task SmokeRunTaskAsync()
    {
        await ExecuteAsync(
            L.T("tools.devtools.action.smokeRun"),
            Application.ExecutablePath,
            new[] { "--smoke-run", "1", "4" },
            _tools.WorkingDirectory,
            revealPath: null);
    }

    private async Task CheckFontTaskAsync()
    {
        await ExecuteAsync(
            L.T("tools.devtools.action.checkFont"),
            Application.ExecutablePath,
            new[] { "--check-font", _tools.WorkingDirectory },
            _tools.WorkingDirectory,
            revealPath: null);
    }

    private async Task AuditUiStringsTaskAsync()
    {
        await ExecuteAsync(
            L.T("tools.devtools.action.auditUiStrings"),
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
            L.T("tools.devtools.action.exportAtlas"),
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
            L.T("tools.devtools.action.exportUiSvg"),
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
            L.T("tools.devtools.action.renderActions"),
            game,
            new[] { "--render-actions", relative },
            _tools.WorkingDirectory,
            Path.Combine(_tools.OutputDir, "btn-colors.png"));
    }

    private Task OpenOutputAsync()
    {
        Directory.CreateDirectory(_tools.OutputDir);
        OpenDirectory(_tools.OutputDir, L.T("tools.devtools.open.label.outputDir"));
        return Task.CompletedTask;
    }

    private async Task GenerateAudioAsync()
    {
        if (RequireAudioGen() is not { } audioGen)
        {
            return;
        }

        int? code = await ExecuteAsync(
            L.T("tools.devtools.action.generateAudio"),
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
            L.T("tools.devtools.action.generateSfx"),
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
        AppendLog($"[{DateTime.Now:HH:mm:ss}] " + L.T("tools.devtools.log.launchGame"));
        AppendLog("  " + game);

        try
        {
            using Process process = CommandRunner.Launch(
                game,
                Path.GetDirectoryName(game) ?? _tools.WorkingDirectory);
            AppendLog(L.T("tools.devtools.log.started", ("pid", process.Id)));
            SetStatus(L.T("tools.devtools.status.gameStarted"), string.Empty);
            MessageBox.Show(
                this,
                L.T("tools.devtools.dialog.layoutEdit.text"),
                L.T("tools.devtools.dialog.layoutEdit.title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            AppendLog(L.T("tools.devtools.log.launchFailed", ("message", ex.Message)));
            SetStatus(L.T("tools.devtools.status.launchFailed", ("message", ex.Message)), string.Empty);
        }

        return Task.CompletedTask;
    }

    private Task ViewLayoutAsync()
    {
        string path = _tools.LayoutFile;
        AppendLog(string.Empty);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] " + L.T("tools.devtools.log.viewLayout"));
        AppendLog("  " + path);

        if (File.Exists(path))
        {
            bool opened = CommandRunner.OpenInExplorer(path, selectFile: true);
            AppendLog(opened ? L.T("tools.devtools.log.explorerLocated") : L.T("tools.devtools.log.openFailed", ("path", path)));
            SetStatus(opened ? L.T("tools.devtools.status.layoutLocated") : L.T("tools.devtools.status.layoutOpenFailed"), string.Empty);
            return Task.CompletedTask;
        }

        AppendLog(L.T("tools.devtools.log.noCustomLayout"));
        SetStatus(L.T("tools.devtools.status.noCustomLayout"), string.Empty);

        string? directory = Path.GetDirectoryName(path);
        if (directory is not null && Directory.Exists(directory))
        {
            CommandRunner.OpenInExplorer(directory, selectFile: false);
        }
        else
        {
            MessageBox.Show(
                this,
                L.T("tools.devtools.dialog.viewLayout.text", ("path", path)),
                L.T("tools.devtools.dialog.viewLayout.title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        return Task.CompletedTask;
    }

    private Task DeleteLayoutAsync()
    {
        string path = _tools.LayoutFile;
        AppendLog(string.Empty);
        AppendLog($"[{DateTime.Now:HH:mm:ss}] " + L.T("tools.devtools.log.deleteLayout"));
        AppendLog("  " + path);

        if (!File.Exists(path))
        {
            AppendLog(L.T("tools.devtools.log.noCustomLayoutDelete"));
            SetStatus(L.T("tools.devtools.status.noCustomLayoutDelete"), string.Empty);
            return Task.CompletedTask;
        }

        DialogResult answer = MessageBox.Show(
            this,
            L.T("tools.devtools.dialog.deleteLayout.text", ("path", path)),
            L.T("tools.devtools.dialog.deleteLayout.title"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes)
        {
            AppendLog(L.T("tools.devtools.log.cancelled"));
            SetStatus(L.T("tools.devtools.status.deleteCancelled"), string.Empty);
            return Task.CompletedTask;
        }

        try
        {
            File.Delete(path);
            AppendLog(L.T("tools.devtools.log.deleted"));
            SetStatus(L.T("tools.devtools.status.deleted"), string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppendLog(L.T("tools.devtools.log.deleteFailed", ("message", ex.Message)));
            SetStatus(L.T("tools.devtools.status.deleteFailed", ("message", ex.Message)), string.Empty);
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
            L.T("tools.devtools.task.tableStyleWithLabel", ("style", style.Label)),
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
                L.T("tools.devtools.task.tableStyleWithLabel", ("style", style.Label)),
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
            ? L.T("tools.devtools.summary.allStylesOk", ("total", TableStyles.Length))
            : L.T("tools.devtools.summary.allStylesPartial", ("ok", TableStyles.Length - failed), ("total", TableStyles.Length), ("failed", failed));
        AppendLog($"  → {summary} · {CommandRunner.FormatElapsed(total.Elapsed)}");
        SetStatus(summary, CommandRunner.FormatElapsed(total.Elapsed));
        OpenDirectory(_tools.OutputDir, L.T("tools.devtools.open.label.outputDir"));
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
            SetStatus(L.T("tools.devtools.status.busy"), string.Empty);
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
            AppendLog(L.T("tools.devtools.log.unhandled", ("message", ex.Message)));
            SetStatus(L.T("tools.devtools.status.taskFailed", ("title", label), ("message", ex.Message)), string.Empty);
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
            AppendLog(L.T("tools.devtools.log.startFailed", ("message", result.StartError)));
            SetStatus(L.T("tools.devtools.status.commandNotStartable", ("title", title)), CommandRunner.FormatElapsed(result.Elapsed));
            MessageBox.Show(
                this,
                result.StartError + "\n\n" + result.CommandLine,
                L.T("tools.devtools.dialog.commandFailed.title", ("title", title)),
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
        string summary = ok ? "exit 0" : L.T("tools.devtools.summary.exitFailed", ("code", result.ExitCode));
        string elapsed = CommandRunner.FormatElapsed(result.Elapsed);
        AppendLog($"  → {summary} · {elapsed}");
        SetStatus(L.T("tools.devtools.status.taskResult", ("title", title), ("summary", summary)), elapsed);

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
            AppendLog(L.T("tools.devtools.log.outputFileMissing", ("path", path)));
            return;
        }

        AppendLog(L.T("tools.devtools.log.output", ("path", path)));
        if (!CommandRunner.OpenInExplorer(path, selectFile: true))
        {
            AppendLog(L.T("tools.devtools.log.cannotLocate"));
        }
    }

    private void OpenDirectory(string path, string label)
    {
        if (!Directory.Exists(path))
        {
            AppendLog(L.T("tools.devtools.log.dirMissing", ("label", label), ("path", path)));
            SetStatus(L.T("tools.devtools.status.dirMissing", ("label", label)), string.Empty);
            return;
        }

        bool opened = CommandRunner.OpenInExplorer(path, selectFile: false);
        AppendLog(opened
            ? L.T("tools.devtools.log.opened", ("label", label), ("path", path))
            : L.T("tools.devtools.log.openFailedLabel", ("label", label), ("path", path)));
        if (opened)
        {
            SetStatus(L.T("tools.devtools.status.opened", ("label", label)), string.Empty);
        }
        else
        {
            SetStatus(L.T("tools.devtools.status.openFailedLabel", ("label", label)), string.Empty);
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

        string message = L.T("tools.devtools.dialog.toolMissing.text", ("name", name), ("build", buildCommand));
        AppendLog("  ! " + message.Replace("\n\n", " ", StringComparison.Ordinal));
        SetStatus(L.T("tools.devtools.status.toolMissing", ("name", name), ("build", buildCommand)), string.Empty);
        MessageBox.Show(this, message, L.T("tools.devtools.dialog.toolMissing.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        text.Length <= limit ? text : L.T("tools.devtools.log.tailTruncated", ("limit", limit)) + text[^limit..];
}
