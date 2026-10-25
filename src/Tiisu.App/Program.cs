using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using SkiaSharp;
using Tiisu.App.Assets;
using Tiisu.App.Rendering;
using Tiisu.App.Runtime;
using Tiisu.App.Ui;
using Tiisu.App.Table;
using Tiisu.App.Svg;
using Tiisu.Engine;
using Tiisu.Engine.Ai;
using Tiisu.Engine.Random;
using GameTile = Tiisu.Core.Tile;
using GameSuit = Tiisu.Core.Suit;
using Color = Tiisu.App.Rendering.Color;
using UiScreen = Tiisu.App.Ui.Screen;

namespace Tiisu.App;

internal static class Program
{
    private static readonly string AssetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");

    [STAThread]
    private static void Main(string[] args)
    {
        TryUseUtf8Console();

        // 无窗口自检（不建窗口、不初始化 WinForms）
        switch (args.Length >= 1 ? args[0] : string.Empty)
        {
            case "--version":
                // 构建时由 Tiisu.App.csproj 从 git 生成；无 git 时为 dev。
                Console.WriteLine(BuildInfo.Version);
                return;
            case "--smoke-run":
                Environment.ExitCode = SmokeRun(args);
                return;
            case "--smoke-ai":
                Environment.ExitCode = SmokeAi(args);
                return;
            case "--smoke-ai-call":
                Environment.ExitCode = SmokeAiCall(args);
                return;
            case "--smoke-save":
                Environment.ExitCode = SmokeSave(args);
                return;
            case "--smoke-resume":
                Environment.ExitCode = SmokeResume(args);
                return;
            case "--smoke-session":
                Environment.ExitCode = SmokeSession(args);
                return;
            case "--smoke-pointer":
                Environment.ExitCode = SmokePointer(args);
                return;
            case "--check-voices":
                Environment.ExitCode = CheckVoices();
                return;
            case "--smoke-escape":
                Environment.ExitCode = SmokeEscape();
                return;
            case "--smoke-credits":
                Environment.ExitCode = SmokeCredits();
                return;
            case "--smoke-layout-editor":
                Environment.ExitCode = SmokeLayoutEditor();
                return;
            case "--smoke-call":
                Environment.ExitCode = SmokeCall();
                return;
            case "--smoke-call-flow":
                Environment.ExitCode = SmokeCallFlow(args);
                return;
            case "--play-real":
                Environment.ExitCode = PlayReal(args);
                return;
            case "--smoke-think":
                Environment.ExitCode = SmokeThink();
                return;
            case "--smoke-wall":
                Environment.ExitCode = SmokeWall();
                return;
        }

        // 编辑态出图：--render-layout-edit <png>
        if (args.Length >= 2 && args[0] == "--render-layout-edit")
        {
            RenderLayoutEdit(args[1]);
            return;
        }

        // 四家手牌明示出图：--render-hands <png> [seed] [mode]
        if (args.Length >= 2 && args[0] == "--render-hands")
        {
            RenderHandsToPng(args);
            return;
        }

        // 无窗口验证：--render <png> [time] [table|menu]
        if (args.Length >= 2 && args[0] == "--render")
        {
            double time = args.Length >= 3 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 0.6;
            string screen = args.Length >= 4 ? args[3] : "table";
            RenderToPng(args[1], time, screen);
            return;
        }

        // 带编号的布局图（看图说话用）：--render-annotated <png>
        if (args.Length >= 2 && args[0] == "--render-annotated")
        {
            RenderAnnotated(args[1]);
            return;
        }

        // 导出可编辑 SVG：--export-svg <dir>
        if (args.Length >= 2 && args[0] == "--export-svg")
        {
            ExportSvg(args[1]);
            return;
        }

        // 合并 UI SVG（牌桌 + 四家 + 五个语义动作按钮 + HUD）：--export-ui-svg <file>
        if (args.Length >= 2 && args[0] == "--export-ui-svg")
        {
            ExportUiSvg(args[1]);
            return;
        }

        // Skia 牌桌渲染（布局验证）：--render-table <png>
        if (args.Length >= 2 && args[0] == "--render-table")
        {
            RenderTableToPng(args[1], args.Length >= 3 && args[2] == "debug");
            return;
        }

        // 真实会话快照出图（自检：后台引擎 → 快照 → Skia 绘制）：--render-session <png> [seed] [mode]
        if (args.Length >= 2 && args[0] == "--render-session")
        {
            RenderSessionToPng(args);
            return;
        }

        // 局末结算屏出图：--render-roundend <png> [seed] [mode]
        if (args.Length >= 2 && args[0] == "--render-roundend")
        {
            Environment.ExitCode = RenderRoundEndToPng(args);
            return;
        }

        // 音效自检：--export-sounds <dir>
        if (args.Length >= 2 && args[0] == "--export-sounds")
        {
            foreach (var (cue, file, bytes) in SoundEffects.ExportWavs(args[1]))
            {
                Console.WriteLine($"  {cue,-9} {bytes,6} B  {file}");
            }

            return;
        }

        // 报听三态出图：--render-call <png 基名>（_callable / _disabled / _noten 三张）
        if (args.Length >= 2 && args[0] == "--render-call")
        {
            RenderCallToPng(args[1]);
            return;
        }

        // 动作按钮条出图：--render-actions <png>
        if (args.Length >= 2 && args[0] == "--render-actions")
        {
            RenderActionsToPng(args[1]);
            return;
        }

        // 设置屏出图：--render-settings <png>
        if (args.Length >= 2 && args[0] == "--render-settings")
        {
            RenderSettingsToPng(args[1]);
            return;
        }

        // 界面截图（与游戏内同一条 Screen 路径）：--render-screen <png> [table|menu]
        if (args.Length >= 2 && args[0] == "--render-screen")
        {
            RenderScreenToPng(args[1], args.Length >= 3 ? args[2] : "table");
            return;
        }

        // 暗杠展示自检：--render-ankan <png 基名 | 目录> → 出「对局中 / 和牌后」两张图
        if (args.Length >= 2 && args[0] == "--render-ankan")
        {
            RenderAnkanToPng(args[1]);
            return;
        }

        // Yoga 布局预览：--render-yoga <png> [画布边长] [副露组数 0..4]
        if (args.Length >= 2 && args[0] == "--render-yoga")
        {
            float canvasY = args.Length >= 3 ? float.Parse(args[2], CultureInfo.InvariantCulture) : 1200f;
            int meldsY = args.Length >= 4 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 1;
            RenderYogaToPng(args[1], canvasY, meldsY);
            return;
        }

        // 2.5D 观感出图：--render-25d <png> --variant mixed|flat|light|medium
        if (args.Length >= 2 && args[0] == "--render-25d")
        {
            Render25DToPng(args);
            return;
        }

        // 帧率基准：--bench <frames>（CPU 光栅，离屏 surface）
        if (args.Length >= 1 && args[0] == "--bench")
        {
            int frames = args.Length >= 2 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 300;
            Bench(frames);
            return;
        }

        // GPU 帧率基准：--bench-gl [frames]（隐藏 GL 窗口 → 离屏 GPU surface；无 GPU 时跳过）
        if (args.Length >= 1 && args[0] == "--bench-gl")
        {
            int frames = args.Length >= 2 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 300;
            Environment.ExitCode = BenchGl(frames);
            return;
        }

        ApplicationConfiguration.Initialize();
        bool forceCpuRender = args.Contains("--cpu-render", StringComparer.Ordinal);
        var atlas = new TileAtlas(AssetsDir);   // 所有权交给 MainForm
        Application.Run(new MainForm(atlas, forceCpuRender));
    }

    // ── 无窗口自检 ────────────────────────────────────────────────

    /// <summary>控制台按 UTF-8 输出（重定向到管道时 .NET 默认就是 UTF-8，这里显式统一）。</summary>
    private static void TryUseUtf8Console()
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (IOException)
        {
            // 无控制台（纯重定向 / GUI 宿主）时忽略。
        }
    }

    /// <summary>
    /// <c>--smoke-run &lt;seed&gt; [mode] [--record]</c>：无窗口跑一整场（人类 AutoPilot：摸什么打什么 / 能和则和），
    /// 打印局数、和牌/流局、四家分数、事件流 SHA256。
    /// </summary>
    private static int SmokeRun(string[] args)
    {
        TryUseUtf8Console();
        if (args.Length < 2
            || !uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint seed))
        {
            Console.Error.WriteLine("用法：--smoke-run <seed> [mode] [--record]");
            return 2;
        }

        int mode = 4;
        if (args.Length >= 3 && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMode))
        {
            mode = parsedMode;
        }

        var settings = LoadSmokeSettings(args);
        var log = new RoundLogWriter { Enabled = settings.RecordTiles };
        var runtime = new MatchRuntime(settings, new Mulberry32Random(seed), _ => { },
            saveStore: null, stats: new StatsStore(), roundLog: log, matchSeed: seed)
        {
            AutoPilot = true,
        };

        runtime.StartMatch(mode);
        int guard = 0;
        while (runtime.Phase != MatchPhase.MatchOver && ++guard < 256)
        {
            if (runtime.Phase == MatchPhase.RoundEnded)
            {
                runtime.NextRound();
            }
            else
            {
                break;
            }
        }

        if (runtime.Phase != MatchPhase.MatchOver)
        {
            Console.Error.WriteLine($"未在 {guard} 次推进内终局（phase={runtime.Phase}）");
            return 1;
        }

        Console.WriteLine($"smoke-run seed={seed} mode={mode}");
        PrintSmoke(runtime);
        if (settings.RecordTiles)
        {
            Console.WriteLine("  roundlog: " + log.FilePath());
        }

        return 0;
    }

    /// <summary>
    /// <c>--smoke-ai &lt;seed&gt; [mode]</c>：整场自走（人类座位也是 AutoPilot），
    /// 打印四家 AI 档位、弃牌决策次数、是否完成、每步平均耗时。
    /// </summary>
    private static int SmokeAi(string[] args)
    {
        TryUseUtf8Console();
        if (args.Length < 2
            || !uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint seed))
        {
            Console.Error.WriteLine("用法：--smoke-ai <seed> [mode]");
            return 2;
        }

        int mode = 4;
        if (args.Length >= 3 && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMode))
        {
            mode = parsedMode;
        }

        var settings = LoadSmokeSettings(args);
        var runtime = new MatchRuntime(settings, new Mulberry32Random(seed), _ => { },
            saveStore: null, stats: null, roundLog: null, matchSeed: seed)
        {
            AutoPilot = true,
        };

        var clock = Stopwatch.StartNew();
        runtime.StartMatch(mode);
        int guard = 0;
        while (runtime.Phase != MatchPhase.MatchOver && ++guard < 256)
        {
            if (runtime.Phase == MatchPhase.RoundEnded)
            {
                runtime.NextRound();
            }
            else
            {
                break;
            }
        }

        clock.Stop();
        bool completed = runtime.Phase == MatchPhase.MatchOver;
        var names = runtime.Engine.Players.Select(p => p.Name).ToArray();

        Console.WriteLine($"smoke-ai seed={seed} mode={mode} matchSeed={runtime.MatchSeed}");
        Console.WriteLine("  权重: " + string.Join("/", settings.AiWeights) + "  修罗MCTS: " + (settings.AiMctsEnabled ? "开" : "关"));
        Console.WriteLine("  四家等级: " + string.Join("  ", names.Select((name, i) =>
            name + "=" + runtime.AiLevels[i])));
        Console.WriteLine("  弃牌决策: " + runtime.AiDiscardDecisions + " 次（" + string.Join(" ", names.Select((name, i) =>
            name + "=" + runtime.AiDiscardDecisionsBySeat[i])) + "）");
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "  完成: {0}  rounds={1} handsPlayed={2}  总耗时={3:F0} ms  平均每步={4:F3} ms",
            completed ? "是" : "否",
            runtime.Rounds,
            runtime.Engine.HandsPlayed,
            clock.Elapsed.TotalMilliseconds,
            runtime.AiDecisionAverageMilliseconds));
        Console.WriteLine("  EventHash: " + runtime.EventHash);

        if (!completed)
        {
            Console.Error.WriteLine($"smoke-ai 未终局（phase={runtime.Phase}）");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// <c>--smoke-ai-call &lt;seed&gt; [mode]</c>：四家全 AI 打一整场（人类座位也交给 AI），
    /// 统计四家报听（叫胡）次数。
    /// </summary>
    private static int SmokeAiCall(string[] args)
    {
        TryUseUtf8Console();
        if (args.Length < 2
            || !uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint seed))
        {
            Console.Error.WriteLine("用法：--smoke-ai-call <seed> [mode]");
            return 2;
        }

        int mode = 4;
        if (args.Length >= 3 && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMode))
        {
            mode = parsedMode;
        }

        var settings = LoadSmokeSettings(args);
        var runtime = new MatchRuntime(settings, new Mulberry32Random(seed), _ => { },
            saveStore: null, stats: null, roundLog: null, matchSeed: seed)
        {
            AutoPilot = true,
            AllSeatsAi = true,
        };

        var clock = Stopwatch.StartNew();
        runtime.StartMatch(mode);
        int guard = 0;
        while (runtime.Phase != MatchPhase.MatchOver && ++guard < 256)
        {
            if (runtime.Phase == MatchPhase.RoundEnded)
            {
                runtime.NextRound();
            }
            else
            {
                break;
            }
        }

        clock.Stop();
        bool completed = runtime.Phase == MatchPhase.MatchOver;
        var names = runtime.Engine.Players.Select(p => p.Name).ToArray();
        var calls = runtime.AiCallDeclarationsBySeat;

        Console.WriteLine($"smoke-ai-call seed={seed} mode={mode} matchSeed={runtime.MatchSeed}");
        Console.WriteLine("  四家等级: " + string.Join("  ", names.Select((name, i) =>
            name + "=" + runtime.AiLevels[i])));
        Console.WriteLine("  报听次数: 总=" + runtime.AiCallDeclarations + "（" + string.Join(" ", names.Select((name, i) =>
            name + "=" + calls[i])) + "）");
        Console.WriteLine("  弃牌决策: " + runtime.AiDiscardDecisions + " 次");
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "  完成: {0}  rounds={1} handsPlayed={2}  总耗时={3:F0} ms",
            completed ? "是" : "否",
            runtime.Rounds,
            runtime.Engine.HandsPlayed,
            clock.Elapsed.TotalMilliseconds));
        Console.WriteLine("  EventHash: " + runtime.EventHash);

        if (!completed)
        {
            Console.Error.WriteLine($"smoke-ai-call 未终局（phase={runtime.Phase}）");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// <c>--smoke-save</c>：验证对局存档已停用 —— 把 <see cref="SaveStore"/> 接进运行时打完整场，
    /// <c>state.json</c> 必须不新建、不改写。<see cref="SaveStore"/> 的字段格式保留，但 <c>Enabled</c> 默认 false。
    /// </summary>
    private static int SmokeSave(string[] args)
    {
        TryUseUtf8Console();
        var settings = LoadSmokeSettings(args);
        using var save = new SaveStore();
        string statePath = save.StatePath;
        bool existedBefore = File.Exists(statePath);
        byte[]? before = existedBefore ? File.ReadAllBytes(statePath) : null;

        var runtime = new MatchRuntime(settings, new Mulberry32Random(1), _ => { },
            save, new StatsStore(), new RoundLogWriter { Enabled = settings.RecordTiles })
        {
            AutoPilot = true,
        };

        runtime.StartMatch(4);
        int guard = 0;
        while (runtime.Phase != MatchPhase.MatchOver && ++guard < 256)
        {
            if (runtime.Phase == MatchPhase.RoundEnded)
            {
                runtime.NextRound();
            }
            else
            {
                break;
            }
        }

        save.Flush();
        bool existedAfter = File.Exists(statePath);
        byte[]? after = existedAfter ? File.ReadAllBytes(statePath) : null;
        bool unchanged = before is null
            ? after is null
            : after is not null && before.AsSpan().SequenceEqual(after);
        bool completed = runtime.Phase == MatchPhase.MatchOver;
        bool ok = !save.Enabled && completed && existedAfter == existedBefore && unchanged;

        Console.WriteLine("smoke-save（验证存档已停用）");
        Console.WriteLine($"  SaveStore.Enabled={save.Enabled}  打完 {runtime.Rounds} 局: {completed}");
        Console.WriteLine($"  存档路径: {statePath}");
        Console.WriteLine($"  文件存在: {existedBefore} → {existedAfter}  内容: {(unchanged ? "未变" : "已变")}");
        Console.WriteLine($"  结论: {(ok ? "未写盘" : "写盘了（停用失效）")}");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// <c>--smoke-resume</c>：验证「恢复上局」已整体停用 —— 菜单没有 resume 入口、
    /// <see cref="GameSession"/> 没有 PostResume、<see cref="SaveStore.Load"/> 读不出任何存档。
    /// </summary>
    private static int SmokeResume(string[] args)
    {
        TryUseUtf8Console();
        var layout = UiLayout.Load(Path.Combine(AssetsDir, "layout", "menu.json"));
        bool hasMenuEntry = layout.Elements.Any(el =>
            string.Equals(el.Action, "resume", StringComparison.Ordinal));
        bool hasPostResume = typeof(GameSession).GetMethod("PostResume") is not null;
        using var save = new SaveStore();
        bool loadBlocked = save.Load() is null;
        bool ok = !hasMenuEntry && !hasPostResume && loadBlocked;

        Console.WriteLine("smoke-resume（验证恢复已停用）");
        Console.WriteLine($"  菜单 action=resume 入口: {(hasMenuEntry ? "有" : "无")}");
        Console.WriteLine($"  GameSession.PostResume: {(hasPostResume ? "存在" : "不存在")}");
        Console.WriteLine($"  SaveStore.Load（{save.StatePath}）: {(loadBlocked ? "null（不读档）" : "读出存档")}");
        Console.WriteLine($"  结论: {(ok ? "无恢复入口" : "恢复入口仍在")}");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// <c>--smoke-session [seed] [mode]</c>：走 <see cref="GameSession"/> 的命令通道（与 UI 点击同一个入口）
    /// 打完整场，验证「后台线程 + 快照 + 事件」这条链路。
    /// </summary>
    private static int SmokeSession(string[] args)
    {
        TryUseUtf8Console();
        uint seed = 1;
        if (args.Length >= 2 && !uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
        {
            Console.Error.WriteLine("seed 无效");
            return 2;
        }

        int mode = 4;
        if (args.Length >= 3 && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMode))
        {
            mode = parsedMode;
        }

        var settings = LoadSmokeSettings(args);
        int events = 0;
        using var session = new GameSession(settings, random: new Mulberry32Random(seed), aiStepDelayMilliseconds: 0);
        session.UiEvent += _ => Interlocked.Increment(ref events);

        session.Post(GameCommand.StartMatch(mode));

        int actions = 0;
        TableViewModel? last = null;
        int idle = 0;
        while (idle < 20_000)
        {
            var vm = session.LatestSnapshot;
            if (ReferenceEquals(vm, last))
            {
                idle++;
                Thread.Sleep(1);
                continue;
            }

            idle = 0;
            last = vm;
            if (vm.Phase == "match_over")
            {
                break;
            }

            GameCommand? command = vm.Phase switch
            {
                "round_end" => GameCommand.NextRound(),
                "human_ron" => GameCommand.Ron(),
                "human_meld" => (vm.Actions & HumanAction.Pon) != 0
                    ? GameCommand.Pon()
                    : (vm.Actions & HumanAction.Chi) != 0 ? GameCommand.Chi(0) : GameCommand.Pass(),
                "human_discard" => (vm.Actions & HumanAction.Tsumo) != 0
                    ? GameCommand.Tsumo()
                    : vm.HasAnkan ? GameCommand.KanAn()
                    : vm.HasKakan ? GameCommand.KanKakan()
                    : GameCommand.Discard(Math.Max(0, vm.Seats[0].Hand.Count - 1)),
                _ => null,
            };

            if (command is not null)
            {
                session.Post(command);
                actions++;
            }
        }

        var final = session.LatestSnapshot;
        bool ok = final.Phase == "match_over" && final.Seats.Count == MahjongEngine.PlayerCount;
        Console.WriteLine($"smoke-session seed={seed} mode={mode}");
        Console.WriteLine($"  终局快照: phase={final.Phase} roundLabel={final.RoundLabel} 人类命令={actions} 事件={events}");
        Console.WriteLine("  四家分数: " + string.Join("  ", final.Seats.Select(seat =>
            seat.Name + "=" + seat.Score.ToString("+0;-0;0", CultureInfo.InvariantCulture))));
        Console.WriteLine("  LastError: " + (session.LastError ?? "-"));

        if (!ok)
        {
            Console.Error.WriteLine("会话未正常终局");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// <c>--smoke-pointer [seed] [mode]</c>：用 <see cref="TableScreen"/> 的输入层（OnClick 命中的手牌矩形 /
    /// OnKey 快捷键）打完整场，验证「点手牌出牌 + H/P/C/K/Space/N」这条 UI 入口。
    /// </summary>
    private static int SmokePointer(string[] args)
    {
        TryUseUtf8Console();
        uint seed = 1;
        if (args.Length >= 2 && !uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
        {
            seed = 1;
        }

        int mode = 4;
        if (args.Length >= 3 && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMode))
        {
            mode = parsedMode;
        }

        var settings = LoadSmokeSettings(args);
        using var session = new GameSession(settings, random: new Mulberry32Random(seed), aiStepDelayMilliseconds: 0);
        using var atlas = new TileAtlas(AssetsDir);
        var screen = new TableScreen(session, atlas);

        // 与 TableScreen 内部同一份布局参数（1200×1200 + Mixed 安全区 + Yoga）。
        var layout = new TableLayoutSpec { CanvasW = 1200f, CanvasH = 1200f };
        layout.ApplyMixedSafeArea();
        YogaTableLayout.Apply(layout);

        session.Post(GameCommand.StartMatch(mode));

        int actions = 0;
        int discards = 0;
        TableViewModel? last = null;
        int idle = 0;
        while (idle < 30_000)
        {
            var vm = session.LatestSnapshot;
            if (ReferenceEquals(vm, last))
            {
                idle++;
                Thread.Sleep(1);
                continue;
            }

            idle = 0;
            last = vm;
            if (vm.Phase == "match_over")
            {
                break;
            }

            int handBefore = vm.Seats[0].Hand.Count;
            switch (vm.Phase)
            {
                case "round_end":
                    screen.OnKey(Keys.N);
                    actions++;
                    break;

                case "human_ron":
                    screen.OnKey(Keys.H);
                    actions++;
                    break;

                case "human_meld":
                    screen.OnKey(Keys.Space);
                    actions++;
                    break;

                case "human_discard":
                    if ((vm.Actions & HumanAction.Tsumo) != 0)
                    {
                        screen.OnKey(Keys.H);
                    }
                    else
                    {
                        // 取手牌最后一张的矩形中心 —— 与鼠标点击走同一个 HitTest。
                        var rects = TableSceneSkia.HandTileRects(layout, vm.Seats[0], 0);
                        int index = Math.Max(0, handBefore - 1);
                        if (index < rects.Length && screen.OnClick(rects[index].CenterX, rects[index].CenterY))
                        {
                            discards++;
                        }
                    }

                    actions++;
                    break;

                default:
                    break;
            }
        }

        var final = session.LatestSnapshot;
        bool ok = final.Phase == "match_over";
        Console.WriteLine($"smoke-pointer seed={seed} mode={mode}");
        Console.WriteLine($"  终局快照: phase={final.Phase} roundLabel={final.RoundLabel} 输入动作={actions} 点手牌出牌={discards}");
        Console.WriteLine("  四家分数: " + string.Join("  ", final.Seats.Select(seat =>
            seat.Name + "=" + seat.Score.ToString("+0;-0;0", CultureInfo.InvariantCulture))));
        Console.WriteLine("  LastError: " + (session.LastError ?? "-"));

        if (!ok)
        {
            Console.Error.WriteLine("输入层自检未正常终局");
            return 1;
        }

        return 0;
    }

    /// <summary><c>--check-voices</c>：打印「副露类型 → 语音名」映射表，供人眼核对。</summary>
    private static int CheckVoices()
    {
        TryUseUtf8Console();
        Console.WriteLine("副露类型 → 语音名");
        foreach (string type in TableScreen.MeldTypes)
        {
            Console.WriteLine($"  {type,-8} → {TableScreen.VoiceForMeld(type)}");
        }

        return 0;
    }

    /// <summary>
    /// <c>--smoke-escape</c>：无窗口验证 ESC 语义 —— 牌桌先「过」/先关结算弹窗、再回菜单；
    /// 菜单不做事；设置屏回菜单。程序退出只留窗口关闭按钮。
    /// </summary>
    private static int SmokeEscape()
    {
        TryUseUtf8Console();

        var commands = new List<GameCommand>();
        bool back = false;
        var meld = new TableScreen(() => TableDemoModel.CreateWithActions(), commands.Add, () => back = true);
        bool meldHandled = meld.OnKey(Keys.Escape);
        string meldCommand = commands.Count == 1 ? commands[0].Kind.ToString() : "none";

        commands.Clear();
        back = false;
        var roundEnd = new TableScreen(() => TableDemoModel.CreateWithAnkan(settled: true), commands.Add, () => back = true);
        var result = new RoundResult(1, "flow", Array.Empty<int>(), null,
            new[] { 25000, 25000, 25000, 25000 }, 0, 0, false);
        roundEnd.ShowSettlement(result);
        bool dismissHandled = roundEnd.OnKey(Keys.Escape);
        bool backAfterDismiss = back;
        bool menuHandled = roundEnd.OnKey(Keys.Escape);
        bool backAfterSecond = back;

        back = false;
        string clicked = string.Empty;
        var menuLayout = UiLayout.Load(Path.Combine(AssetsDir, "layout", "menu.json"));
        var menu = new MenuScreen(menuLayout, action =>
        {
            clicked = action;
            return true;
        });
        bool lobbyHandled = menu.OnKey(Keys.Escape);
        bool menuEscQuit = clicked.Length > 0;
        var quitButton = menuLayout.Elements.First(el => el.Id == "btnQuit");
        bool quitHandled = menu.OnClick(quitButton.Rect.CenterX, quitButton.Rect.CenterY);

        bool settingsBack = false;
        var settings = new SettingsScreen(Settings.Load(), _ => { }, () => settingsBack = true);
        bool settingsHandled = settings.OnKey(Keys.Escape);

        bool ok = meldHandled && meldCommand == "Pass" && !backAfterDismiss && dismissHandled
                  && menuHandled && backAfterSecond && lobbyHandled && !menuEscQuit
                  && quitHandled && clicked == "quit"
                  && settingsHandled && settingsBack;

        Console.WriteLine("smoke-escape");
        Console.WriteLine($"  牌桌 ESC（有「过」）: handled={meldHandled} command={meldCommand}");
        Console.WriteLine($"  牌桌 ESC（结算弹窗）: handled={dismissHandled} 弹窗关闭={!backAfterDismiss}");
        Console.WriteLine($"  牌桌 ESC（弹窗已关）: handled={menuHandled} 回菜单={backAfterSecond}");
        Console.WriteLine($"  菜单 ESC: handled={lobbyHandled} 动作=\"{(menuEscQuit ? clicked : "-")}\"（不退出、不做事）");
        Console.WriteLine($"  菜单「退出」按钮: handled={quitHandled} action=\"{clicked}\"");
        Console.WriteLine($"  设置 ESC: handled={settingsHandled} 回菜单={settingsBack}");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// <c>--smoke-credits</c>：无窗口验证菜单底部署名 —— 每个可点片段的矩形中心点下去
    /// 是否打开对应 URL，以及 ESC / 「退出」按钮行为是否仍与原来一致（不真的开浏览器）。
    /// </summary>
    private static int SmokeCredits()
    {
        TryUseUtf8Console();
        var layout = UiLayout.Load(Path.Combine(AssetsDir, "layout", "menu.json"));
        var opened = new List<string>();
        string clickedAction = string.Empty;
        var menu = new MenuScreen(layout, action =>
        {
            clickedAction = action;
            return true;
        }, opened.Add);

        var credits = layout.Elements.First(el => el.Id == "credits");
        var rects = MenuScreen.LinkRects(credits);
        var expected = new (string Needle, string Url)[]
        {
            ("Parisian", "https://incompetech.com/music/royalty-free/"),
            ("CC BY 4.0", "https://creativecommons.org/licenses/by/4.0/"),
            ("开源信息", "https://github.com/Pub-Polaris/Tiisu-Mahjong-CSharp"),
        };

        bool ok = rects.Count == credits.Links!.Count;
        Console.WriteLine("smoke-credits");
        Console.WriteLine($"  署名行: \"{credits.Text}\"  y={credits.Y:F0}  fontSize={credits.FontSize:F0}");
        foreach (var (needle, url) in expected)
        {
            var hit = rects.FirstOrDefault(r => r.Link.Url == url && r.Link.Text.Contains(needle, StringComparison.Ordinal));
            if (hit.Link is null)
            {
                Console.WriteLine($"  [缺失] {needle} → {url}");
                ok = false;
                continue;
            }

            int before = opened.Count;
            bool overLink = menu.OnPointerMove(hit.Rect.CenterX, hit.Rect.CenterY);
            bool clicked = menu.OnClick(hit.Rect.CenterX, hit.Rect.CenterY);
            bool openedRight = opened.Count == before + 1 && opened[^1] == url;
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0,-12} x={1:F0}..{2:F0} y={3:F0}..{4:F0}  hover={5} click={6} url={7}",
                needle, hit.Rect.Left, hit.Rect.Right, hit.Rect.Top, hit.Rect.Bottom, overLink, clicked, openedRight));
            ok &= overLink && clicked && openedRight;
        }

        bool emptyUrlIgnored = rects.FirstOrDefault(r => r.Link.Url.Length == 0).Link is not null;
        bool escapeHandled = menu.OnKey(Keys.Escape);
        bool noActionOnEsc = clickedAction.Length == 0;
        var quit = layout.Elements.First(el => el.Id == "btnQuit");
        bool quitHandled = menu.OnClick(quit.Rect.CenterX, quit.Rect.CenterY);
        bool quitAction = clickedAction == "quit";
        bool overlap = rects.Any(r => layout.Elements.Any(el =>
            el.Type == "button" && el.Rect.Contains(r.Rect.CenterX, r.Rect.CenterY)));

        Console.WriteLine($"  分隔符（空 URL）保留: {emptyUrlIgnored}");
        Console.WriteLine($"  ESC: handled={escapeHandled} 无动作={noActionOnEsc}");
        Console.WriteLine($"  「退出」按钮: handled={quitHandled} action=\"{clickedAction}\"");
        Console.WriteLine($"  与按钮重叠: {overlap}");

        ok &= emptyUrlIgnored && escapeHandled && noActionOnEsc && quitHandled && quitAction && !overlap;
        Console.WriteLine(ok ? "  OK" : "  FAIL");
        return ok ? 0 : 1;
    }

    private static Settings LoadSmokeSettings(string[] args)
    {
        var settings = Settings.Load();
        if (args.Contains("--record", StringComparer.Ordinal))
        {
            settings.RecordTiles = true;
        }

        return settings;
    }

    private static void PrintSmoke(MatchRuntime runtime)
    {
        var names = runtime.Engine.Players.Select(p => p.Name).ToArray();
        var scores = runtime.FinalScores;
        int winRounds = runtime.TsumoWins + runtime.RonWins;

        Console.WriteLine($"  局数: rounds={runtime.Rounds} handsPlayed={runtime.Engine.HandsPlayed} mode={runtime.ModeTarget}");
        Console.WriteLine(
            $"  和牌/流局: 和牌 {winRounds} 局（自摸 {runtime.TsumoWins} / 荣和 {runtime.RonWins}）· 流局 {runtime.DrawRounds} 局（特殊 {runtime.SpecialFlowRounds}）");
        Console.WriteLine("  四家分数: " + string.Join("  ", names.Select((name, i) =>
            name + "=" + scores[i].ToString("+0;-0;0", CultureInfo.InvariantCulture))));
        Console.WriteLine("  EventHash: " + runtime.EventHash);
    }

    private static UiScreen CreateScreen(string name, TileAtlas? atlas) => name switch
    {
        "menu" => new MenuScreen(
            UiLayout.Load(Path.Combine(AssetsDir, "layout", "menu.json")),
            _ => false),
        "settings" => new SettingsScreen(Settings.Load(), _ => { }, () => { }),
        _ => new TableScreen(atlas),
    };

    private static void RenderToPng(string path, double time, string screenName)
    {
        using var atlas = new TileAtlas(AssetsDir);
        var info = new SKImageInfo((int)UiScreen.Width, (int)UiScreen.Height);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        var screen = CreateScreen(screenName, atlas);
        using (var ctx = new SkiaCanvas(canvas, atlas))
        {
            screen.Update(time);
            screen.Draw(ctx, time);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        Console.WriteLine($"rendered {screenName} -> {path}  (atlas loaded: {atlas.IsLoaded})");
    }

    /// <summary>
    /// <c>--render-hands &lt;png&gt; [seed] [mode]</c>：打到中盘后出图，四家手牌全部明示；
    /// 左上角用系统字体（Microsoft YaHei UI）标注系统时间、当前局目与余牌，方便复盘 AI 打牌。
    /// </summary>
    private static void RenderHandsToPng(string[] args)
    {
        TryUseUtf8Console();
        string path = args[1];
        uint seed = 1;
        if (args.Length >= 3 && !uint.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
        {
            seed = 1;
        }

        int mode = 4;
        if (args.Length >= 4 && !int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out mode))
        {
            mode = 4;
        }

        var runtime = new MatchRuntime(new Settings(), new Mulberry32Random(seed), _ => { },
            saveStore: null, stats: null, roundLog: null, matchSeed: seed);
        runtime.StartMatch(mode);

        int humanDiscards = 0;
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(20))
        {
            var phase = runtime.Phase;
            if (phase is MatchPhase.MatchOver or MatchPhase.RoundEnded)
            {
                break;
            }

            if (phase == MatchPhase.HumanDiscard)
            {
                if (humanDiscards >= 24)
                {
                    break;
                }

                int pick = BestShantenDiscardIndex(runtime.Engine.Players[runtime.HumanIndex]);
                if (pick < 0)
                {
                    break;
                }

                runtime.Execute(GameCommand.Discard(pick));
                humanDiscards++;
                continue;
            }

            if (phase is MatchPhase.HumanRon or MatchPhase.HumanMeld)
            {
                runtime.Execute(GameCommand.Pass());
                continue;
            }

            Thread.Sleep(1);
        }

        var vm = runtime.Snapshot;
        using var atlas = new TileAtlas(AssetsDir);
        var screen = new TableScreen(() => runtime.Snapshot, null, null, atlas);
        using var surface = SKSurface.Create(new SKImageInfo((int)screen.CanvasWidth, (int)screen.CanvasHeight));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        using (var ctx = new SkiaCanvas(canvas, atlas))
        {
            screen.Update(0.6);
            screen.Draw(ctx, 0.6);
        }

        // 左上角信息：系统字体（不走游戏内字体），显示系统时间 / 局目 / 余牌 + 四家手牌。
        SKTypeface typeface = SKTypeface.FromFamilyName("Microsoft YaHei UI") ?? SKTypeface.Default;
        using var titleFont = new SKFont(typeface, 22);
        using var bodyFont = new SKFont(typeface, 17);
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(255, 240, 200) };
        using var muted = new SKPaint { IsAntialias = true, Color = new SKColor(200, 214, 202) };
        using var panel = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 170) };

        var hands = new List<string>();
        for (int i = 0; i < MahjongEngine.PlayerCount; i++)
        {
            var player = runtime.Engine.Players[i];
            var seat = vm.Seats.FirstOrDefault(s => s.Index == i);
            string wind = seat?.Wind ?? player.Name;
            string tiles = string.Join(" ", player.Hand
                .OrderBy(t => (int)t.Suit)
                .ThenBy(t => t.Num)
                .Select(t => t.Id));
            hands.Add($"{wind}家（{player.Hand.Count}）：{tiles}");
        }

        float boxH = 60f + (hands.Count * 30f) + 10f;
        canvas.DrawRoundRect(new SKRect(16, 16, 1184, 16 + boxH), 10, 10, panel);
        canvas.DrawText($"系统时间 {DateTime.Now:yyyy-MM-dd HH:mm:ss}", 30, 46, SKTextAlign.Left, titleFont, paint);
        canvas.DrawText($"第 {vm.RoundLabel} · 余 {vm.WallCount} 张 · seed={seed} mode={mode}",
            30, 74, SKTextAlign.Left, bodyFont, muted);
        float y = 104;
        foreach (string line in hands)
        {
            canvas.DrawText(line, 30, y, SKTextAlign.Left, bodyFont, paint);
            y += 30f;
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        Console.WriteLine($"rendered hands -> {path}  {vm.RoundLabel} 余{vm.WallCount} 人类弃牌={humanDiscards} 阶段={runtime.Phase}");
        foreach (string line in hands)
        {
            Console.WriteLine("  " + line);
        }
    }

    /// <summary>
    /// <c>--render-session &lt;png&gt; [seed] [mode]</c>：开一个真实 <see cref="GameSession"/>，
    /// 等到人类出牌阶段再按牌桌屏的绘制路径出图（自检后台引擎 → 快照 → Skia）。
    /// </summary>
    private static void RenderSessionToPng(string[] args)
    {
        TryUseUtf8Console();
        string path = args[1];
        uint seed = 1;
        if (args.Length >= 3 && !uint.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
        {
            seed = 1;
        }

        int mode = 4;
        if (args.Length >= 4 && int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMode))
        {
            mode = parsedMode;
        }

        using var session = new GameSession(Settings.Load(), random: new Mulberry32Random(seed), aiStepDelayMilliseconds: 0);
        session.Post(GameCommand.StartMatch(mode));

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var vm = session.LatestSnapshot;
            if (vm.Phase is "human_discard" or "human_ron" or "human_meld")
            {
                break;
            }

            Thread.Sleep(2);
        }

        using var atlas = new TileAtlas(AssetsDir);
        var screen = new TableScreen(session, atlas);
        using var surface = SKSurface.Create(new SKImageInfo((int)screen.CanvasWidth, (int)screen.CanvasHeight));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);
        using (var ctx = new SkiaCanvas(canvas, atlas))
        {
            screen.Update(0.6);
            screen.Draw(ctx, 0.6);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());

        var snapshot = session.LatestSnapshot;
        Console.WriteLine($"rendered session -> {path}  phase={snapshot.Phase} round={snapshot.RoundLabel} " +
                          $"actions={snapshot.Actions} hand={snapshot.Seats[0].HandCount}");
    }

    /// <summary>
    /// <c>--render-roundend &lt;png&gt; [seed] [mode]</c>：无窗口跑到第一次局末（人类 AutoPilot），
    /// 再按牌桌屏 + 结算覆盖层出图。明细打印到控制台供对数。
    /// <para><c>--double</c>：双和演示（验证「第 1/2 位 + 下一家」）；<c>--flow</c>：荒牌流局演示。</para>
    /// </summary>
    private static int RenderRoundEndToPng(string[] args)
    {
        TryUseUtf8Console();
        string path = args[1];
        bool doubleWin = args.Contains("--double", StringComparer.Ordinal);
        bool flow = args.Contains("--flow", StringComparer.Ordinal);

        if (args.Contains("--ankan", StringComparer.Ordinal))
        {
            RenderAnkanSettlement(path, doubleWin);
            Console.WriteLine($"rendered roundend(ankan demo) -> {path}");
            return 0;
        }

        if (flow)
        {
            int flowPage = 1;
            int flowPageAt = Array.IndexOf(args, "--page");
            if (flowPageAt >= 0 && flowPageAt + 1 < args.Length)
            {
                int.TryParse(args[flowPageAt + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out flowPage);
            }

            RenderFlowSettlement(path, Math.Max(1, flowPage));
            Console.WriteLine($"rendered roundend(flow demo) -> {path}  page={Math.Max(1, flowPage)}");
            return 0;
        }

        if (doubleWin)
        {
            // --page 2：点「下一家」翻到第 2 位（同时验证按钮可点）。
            int page = 1;
            int pageAt = Array.IndexOf(args, "--page");
            if (pageAt >= 0 && pageAt + 1 < args.Length)
            {
                int.TryParse(args[pageAt + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out page);
            }

            int reached = RenderDoubleSettlement(path, Math.Max(1, page));
            Console.WriteLine($"rendered roundend(double demo) -> {path}  page={reached}");
            return 0;
        }

        // 默认 seed=2：第一局是单家和牌（seed=1 是荒牌流局，交给 --flow 演示）。
        uint seed = 2;
        if (args.Length >= 3 && !uint.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
        {
            seed = 2;
        }

        int mode = 1;
        if (args.Length >= 4 && !int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out mode))
        {
            mode = 1;
        }

        RoundResult? captured = null;
        var runtime = new MatchRuntime(
            new Settings(),
            new Mulberry32Random(seed),
            uiEvent =>
            {
                if (uiEvent is RoundEndEvent end)
                {
                    captured = end.Result;
                }
            })
        {
            AutoPilot = true,
        };

        runtime.StartMatch(mode);
        if (captured is null)
        {
            Console.Error.WriteLine($"没有在第一次局末停下（phase={runtime.Phase}）");
            return 1;
        }

        using var atlas = new TileAtlas(AssetsDir);
        var screen = new TableScreen(() => runtime.Snapshot, null);
        screen.ShowSettlement(captured);
        RenderScreen(path, screen, atlas);

        Console.WriteLine($"rendered roundend -> {path}  seed={seed} mode={mode} phase={runtime.Phase}");
        Console.WriteLine($"  结果: kind={captured.Kind} winners=[{string.Join(",", captured.Winners)}] " +
                          $"draw={captured.DrawReason ?? "-"} matchOver={captured.MatchOver}");
        Console.WriteLine("  分数: " + string.Join("  ", captured.Scores.Select((value, i) =>
            runtime.Engine.Players[i].Name + "=" + value.ToString("+0;-0;0", CultureInfo.InvariantCulture))));
        var details = captured.Details ?? Array.Empty<SettlementDetail>();
        Console.WriteLine($"  明细: {details.Count} 条");
        foreach (var detail in details)
        {
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "    {0}: {1} totalFu={2} tableSafe={3} base={4} tableless={5} bonus={6} sub={7} dealer={8} honba={9} final={10} pay=\"{11}\"",
                runtime.Engine.Players[detail.WinnerIndex].Name,
                detail.SelfDraw ? "自摸" : "荣和",
                detail.TotalFu,
                detail.TableSafeFu,
                detail.BasePoints,
                detail.TablelessBonus,
                detail.PointsBonus,
                detail.SubPoints,
                detail.DealerMultiplierApplied,
                detail.HonbaCount,
                detail.FinalPoints,
                detail.PaymentText));
            Console.WriteLine("      役: " + string.Join(" / ", detail.Yaku.Select(y =>
                y.Name + y.Fu + "(" + y.Flag + ")")));
        }

        return 0;
    }

    /// <summary>
    /// <c>--render-roundend &lt;png&gt; --ankan</c>：用暗杠演示快照 + 合成结算结果出图，
    /// 专门核对「结算屏左侧缩略图里暗杠已翻开 + 30% 遮罩」。
    /// </summary>
    private static void RenderAnkanSettlement(string path, bool doubleWin = false)
    {
        TryUseUtf8Console();
        var detail = new SettlementDetail
        {
            WinnerIndex = 0,
            SelfDraw = true,
            Yaku = new (string Name, int Fu, SettlementYakuFlag Flag)[]
            {
                ("七对子", 25, SettlementYakuFlag.Normal),
                ("门清", 10, SettlementYakuFlag.Normal),
                ("自摸", 2, SettlementYakuFlag.Normal),
            },
            TotalFu = 37,
            TableSafeFu = 37,
            BasePoints = 8000,
            SubPoints = 24000,
            HonbaCount = 3,
            HonbaBonus = 2100,
            FinalPoints = 24000,
            PayEach = 8000,
            PaymentText = "自摸 · 每家 8000 + 余数 0",
        };

        var details = new List<SettlementDetail> { detail };
        if (doubleWin)
        {
            details.Add(new SettlementDetail
            {
                WinnerIndex = 2,
                SelfDraw = false,
                PayerIndex = 1,
                Yaku = new (string Name, int Fu, SettlementYakuFlag Flag)[]
                {
                    ("清一色", 40, SettlementYakuFlag.Normal),
                    ("门清", 10, SettlementYakuFlag.Normal),
                },
                TotalFu = 50,
                TableSafeFu = 50,
                BasePoints = 8000,
                SubPoints = 12000,
                DealerMultiplierApplied = true,
                HonbaCount = 3,
                HonbaBonus = 2100,
                FinalPoints = 18000,
                PaymentText = "荣和 · 放铳者付 18000",
            });
        }

        var result = new RoundResult(
            1,
            "tsumo",
            doubleWin ? new[] { 0, 2 } : new[] { 0 },
            null,
            doubleWin ? new[] { 73000, 31200, 62800, 41000 } : new[] { 73000, 31200, 50800, 41000 },
            3,
            0,
            false,
            details,
            null,
            new[] { 49000, 39200, 58800, 49000 });

        using var atlas = new TileAtlas(AssetsDir);
        var screen = new TableScreen(() => TableDemoModel.CreateWithAnkan(settled: true), null, null, atlas);
        screen.ShowSettlement(result);
        RenderScreen(path, screen, atlas);
    }

    /// <summary>
    /// <c>--render-roundend &lt;png&gt; --double [--page 2]</c>：双和演示 —— 两家和牌详情可翻页，
    /// 验证「第 1/2 位 + 下一家」与两家不同的手牌 / 役种。page&gt;1 时走真实点击翻页。
    /// </summary>
    private static int RenderDoubleSettlement(string path, int page)
    {
        TryUseUtf8Console();
        var result = BuildDoubleSettlementResult();

        using var atlas = new TileAtlas(AssetsDir);
        var screen = new TableScreen(TableDemoModel.CreateForSettlement, null, null, atlas);
        screen.ShowSettlement(result);

        if (page > 1)
        {
            // 先画一帧让按钮矩形有值，再按按钮中心真实点击（同时验证「下一家」可点）。
            DrawScreenOnce(screen, atlas);
            for (int i = 1; i < page; i++)
            {
                var button = screen.SettlementOverlay.NextButtonBounds;
                if (button.W <= 0f)
                {
                    break;
                }

                screen.OnClick(button.CenterX, button.CenterY);
            }
        }

        RenderScreen(path, screen, atlas);
        return screen.SettlementOverlay.CurrentIndex(result) + 1;
    }

    /// <summary>双和演示用的固定结算结果（西家荣和南家的放铳）。</summary>
    private static RoundResult BuildDoubleSettlementResult()
    {
        var details = new List<SettlementDetail>
        {
            new()
            {
                WinnerIndex = 0,
                SelfDraw = true,
                Yaku = new (string Name, int Fu, SettlementYakuFlag Flag)[]
                {
                    ("门清", 10, SettlementYakuFlag.Normal),
                    ("平和", 1, SettlementYakuFlag.Normal),
                    ("自摸", 2, SettlementYakuFlag.Normal),
                    ("单钓", 1, SettlementYakuFlag.Tableless),
                    ("赐马", 1, SettlementYakuFlag.Ma),
                },
                TotalFu = 15,
                MaFu = 1,
                FinalPoints = 12000,
                PayEach = 4000,
                PaymentText = "自摸 · 每家 4000",
            },
            new()
            {
                WinnerIndex = 2,
                SelfDraw = false,
                PayerIndex = 1,
                Yaku = new (string Name, int Fu, SettlementYakuFlag Flag)[]
                {
                    ("混一色", 20, SettlementYakuFlag.Normal),
                    ("门清", 10, SettlementYakuFlag.Normal),
                },
                TotalFu = 30,
                FinalPoints = 8000,
                PaymentText = "荣和 · 放铳者付 8000",
            },
        };

        return new RoundResult(
            2,
            "ron",
            new[] { 0, 2 },
            null,
            new[] { 60300, 19900, 46100, 23700 },
            1,
            0,
            false,
            details,
            null,
            new[] { 48300, 31900, 42100, 27700 });
    }

    /// <summary>画一帧但不落盘（自检里让结算按钮先算好命中矩形）。</summary>
    private static void DrawScreenOnce(UiScreen screen, TileAtlas? atlas)
    {
        var info = new SKImageInfo((int)screen.CanvasWidth, (int)screen.CanvasHeight);
        using var surface = SKSurface.Create(info);
        using var ctx = new SkiaCanvas(surface.Canvas, atlas);
        screen.Update(0.2);
        screen.Draw(ctx, 0.2);
    }

    /// <summary><c>--render-roundend &lt;png&gt; --flow</c>：荒牌流局演示（不列役，只给四家分数变化）。</summary>
    private static void RenderFlowSettlement(string path, int page = 1)
    {
        TryUseUtf8Console();
        var result = new RoundResult(
            2,
            "flow",
            Array.Empty<int>(),
            null,
            new[] { 48300, 31900, 42100, 27700 },
            1,
            0,
            false,
            Array.Empty<SettlementDetail>(),
            "exhaustive",
            new[] { 48300, 31900, 42100, 27700 },
            new[] { "南家 未听返杠 −2100", "北家 鸣牌后无役听牌 −1400" });

        using var atlas = new TileAtlas(AssetsDir);
        var screen = new TableScreen(TableDemoModel.CreateWithFlow, null, null, atlas);
        screen.ShowSettlement(result);
        if (page >= 2)
        {
            screen.OnKey(Keys.Right);
        }

        RenderScreen(path, screen, atlas);
    }

    /// <summary><c>--render-settings &lt;png&gt;</c>：设置屏出图（不写 settings.json）。</summary>
    private static void RenderSettingsToPng(string path)
    {
        TryUseUtf8Console();
        using var atlas = new TileAtlas(AssetsDir);
        var screen = new SettingsScreen(Settings.Load(), _ => { }, () => { });
        RenderScreen(path, screen, atlas);
        Console.WriteLine($"rendered settings -> {path}  {screen.CanvasWidth:F0}x{screen.CanvasHeight:F0}");
    }

    private static void RenderScreen(string path, UiScreen screen, TileAtlas? atlas)
    {
        var info = new SKImageInfo((int)screen.CanvasWidth, (int)screen.CanvasHeight);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        using (var ctx = new SkiaCanvas(canvas, atlas))
        {
            screen.Update(0.6);
            screen.Draw(ctx, 0.6);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    /// <summary>按屏自己的逻辑画布尺寸出图（与游戏内走同一条 Screen 路径）。</summary>
    private static void RenderScreenToPng(string path, string screenName)
    {
        using var atlas = new TileAtlas(AssetsDir);
        var screen = CreateScreen(screenName, atlas);

        var info = new SKImageInfo((int)screen.CanvasWidth, (int)screen.CanvasHeight);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        using (var ctx = new SkiaCanvas(canvas, atlas))
        {
            screen.Update(0.6);
            screen.Draw(ctx, 0.6);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        Console.WriteLine($"rendered screen '{screenName}' -> {path}  " +
                          $"{screen.CanvasWidth:F0}x{screen.CanvasHeight:F0} (atlas: {atlas.IsLoaded})");
    }

    private static void Bench(int frames)
    {
        using var atlas = new TileAtlas(AssetsDir);
        var table = new TableScreen(atlas);
        var info = new SKImageInfo((int)table.CanvasWidth, (int)table.CanvasHeight);
        using var surface = SKSurface.Create(info);

        for (int i = 0; i < 10; i++)
        {
            using var warm = new SkiaCanvas(surface.Canvas, atlas);
            table.Draw(warm, i * 0.016);
        }

        var times = new double[frames];
        var total = Stopwatch.StartNew();
        for (int i = 0; i < frames; i++)
        {
            var one = Stopwatch.StartNew();
            using var ctx = new SkiaCanvas(surface.Canvas, atlas);
            table.Draw(ctx, i * 0.016);
            one.Stop();
            times[i] = one.Elapsed.TotalMilliseconds;
        }

        total.Stop();
        Array.Sort(times);
        double avg = total.Elapsed.TotalMilliseconds / frames;
        int idx = Math.Clamp((int)(frames * 0.95) - 1, 0, frames - 1);
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "bench: {0} frames, avg {1:F2} ms ({2:F1} fps), p95 {3:F2} ms, atlas={4}",
            frames, avg, 1000.0 / avg, times[idx], atlas.IsLoaded));
    }

    /// <summary>
    /// <c>--bench-gl [frames]</c>：隐藏 GL 窗口拿到 <see cref="GRContext"/>，在离屏 GPU surface 上跑同样帧数。
    /// 无 GPU / 驱动不可用时打印「GL 不可用，跳过」并返回 0（不阻塞 CI / 无显卡机器）。
    /// </summary>
    private static int BenchGl(int frames)
    {
        TryUseUtf8Console();
        frames = Math.Max(1, frames);

        using GlHostWindow? host = GlHostWindow.TryCreate(out string renderer, out string? error);
        var gr = host?.Context;
        if (host is null || gr is null)
        {
            Console.WriteLine($"GL 不可用，跳过：{error ?? "未知原因"}");
            return 0;
        }

        using var atlas = new TileAtlas(AssetsDir);
        var table = new TableScreen(atlas);
        var info = new SKImageInfo((int)table.CanvasWidth, (int)table.CanvasHeight);
        using var surface = SKSurface.Create(gr, true, info);
        if (surface is null)
        {
            Console.WriteLine("GL 不可用，跳过：离屏 GPU surface 创建失败");
            return 0;
        }

        for (int i = 0; i < 10; i++)
        {
            using var warm = new SkiaCanvas(surface.Canvas, atlas);
            table.Draw(warm, i * 0.016);
        }

        gr.Submit(true);
        gr.CheckAsyncWorkCompletion();

        var times = new double[frames];
        var total = Stopwatch.StartNew();
        for (int i = 0; i < frames; i++)
        {
            var one = Stopwatch.StartNew();
            using var ctx = new SkiaCanvas(surface.Canvas, atlas);
            table.Draw(ctx, i * 0.016);
            surface.Canvas.Flush();
            gr.Submit(true);
            gr.CheckAsyncWorkCompletion();
            one.Stop();
            times[i] = one.Elapsed.TotalMilliseconds;
        }

        total.Stop();
        Array.Sort(times);
        double avg = total.Elapsed.TotalMilliseconds / frames;
        int idx = Math.Clamp((int)(frames * 0.95) - 1, 0, frames - 1);
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "bench-gl: {0} frames, avg {1:F2} ms ({2:F1} fps), p95 {3:F2} ms, renderer={4}, atlas={5}",
            frames, avg, 1000.0 / avg, times[idx], renderer, atlas.IsLoaded));
        return 0;
    }

    /// <summary>
    /// <c>--render-25d &lt;png&gt; --variant mixed|flat|light|medium</c>：按指定观感出 1200×1200 牌桌图。
    /// 与游戏内同一条 TableScreen 绘制路径（含手牌、牌河、动作条、HUD），便于对照可读性。
    /// </summary>
    private static void Render25DToPng(string[] args)
    {
        string path = args[1];
        var style = TableRenderStyle.Mixed;
        for (int i = 2; i + 1 < args.Length; i++)
        {
            if (!string.Equals(args[i], "--variant", StringComparison.Ordinal))
            {
                continue;
            }

            style = args[i + 1].ToLowerInvariant() switch
            {
                "mixed" or "c" => TableRenderStyle.Mixed,
                "flat" or "2d" => TableRenderStyle.Flat,
                "medium" or "medium25d" => TableRenderStyle.Medium25D,
                "light" or "light25d" => TableRenderStyle.Light25D,
                var other => throw new ArgumentException($"未知 --variant：{other}（mixed | flat | light | medium）"),
            };
        }

        using var atlas = new TileAtlas(AssetsDir);
        var screen = new TableScreen(atlas, style);
        RenderScreen(path, screen, atlas);
        Console.WriteLine($"rendered 25d variant={style} -> {path}");
    }

    private static void RenderTableToPng(string path, bool debug = false)
    {
        using var atlas = new TileAtlas(AssetsDir);
        var layout = NewLayout();

        var info = new SKImageInfo((int)layout.CanvasW, (int)layout.CanvasH);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        using (var ctx = new SkiaCanvas(canvas, atlas))
        {
            new TableSceneSkia().Draw(ctx, TableDemoModel.Create(1), layout);
            if (debug)
            {
                DrawDebugRegions(ctx, layout);
            }
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        Console.WriteLine("rendered table -> " + path);
    }

    /// <summary>1200×1200 的新布局（Yoga 计算，Mixed 档安全区）。</summary>
    private static TableLayoutSpec NewLayout()
    {
        var layout = new TableLayoutSpec { CanvasW = 1200f, CanvasH = 1200f };
        layout.ApplyMixedSafeArea();
        YogaTableLayout.Apply(layout);
        return layout;
    }

    private static void DrawDebugRegions(ICanvas c, TableLayoutSpec l)
    {
        var red = Tiisu.App.Rendering.Color.Rgb(255, 72, 72);
        foreach (var (id, note, r) in SvgExporter.Regions(l))
        {
            c.StrokeRoundRect(r, 2, red, 1.5f);
            c.DrawText($"{id} · {note}",
                new Tiisu.App.Rendering.RectF(r.X + 4, r.Y + 2, 280, 16), red,
                new Tiisu.App.Rendering.TextStyle(11, Bold: true, Center: false));
        }
    }

    private static void RenderYogaToPng(string path, float canvas, int selfMelds)
    {
        using var atlas = new TileAtlas(AssetsDir);

        var layout = new TableLayoutSpec { CanvasW = canvas, CanvasH = canvas };
        layout.ApplyMixedSafeArea();
        YogaTableLayout.Apply(layout);

        var info = new SKImageInfo((int)layout.CanvasW, (int)layout.CanvasH);
        using var surface = SKSurface.Create(info);
        var canvasSk = surface.Canvas;
        canvasSk.Clear(SKColors.Black);

        var vm = TableDemoModel.Create(selfMelds);
        using (var ctx = new SkiaCanvas(canvasSk, atlas))
        {
            new TableSceneSkia().Draw(ctx, vm, layout);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());

        Console.WriteLine($"rendered yoga -> {path}  canvas={canvas:F0}");
        Console.WriteLine($"  plate = {layout.PlateRect.X:F1},{layout.PlateRect.Y:F1} {layout.PlateRect.W:F0}x{layout.PlateRect.H:F0}");
        Console.WriteLine($"  self  = {layout.HandRects[0].X:F1},{layout.HandRects[0].Y:F1} {layout.HandRects[0].W:F0}x{layout.HandRects[0].H:F0}");
    }

    private static void ExportSvg(string outDir)
    {
        Directory.CreateDirectory(outDir);
        string tilesDir = Path.Combine(FindWebDir(), "mj_tiles");

        var layout = NewLayout();
        var vm = TableDemoModel.Create(1);
        var exporter = new SvgExporter(tilesDir, layout);

        string main = Path.Combine(outDir, "table-generated.svg");
        File.WriteAllText(main, exporter.Build(vm), new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outDir, "table-regions.svg"),
            SvgExporter.BuildRegions(layout), new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outDir, "layout.json"),
            SvgExporter.BuildLayoutJson(layout), new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outDir, "layout.md"),
            SvgExporter.BuildLayoutMarkdown(layout), new System.Text.UTF8Encoding(false));

        Console.WriteLine($"exported svg -> {main}（内嵌牌面 {exporter.TileCount} 张）/ table-regions.svg / layout.json / layout.md");
    }

    /// <summary>
    /// <c>--export-ui-svg &lt;file&gt;</c>：合并导出一张 1200×1200 SVG ——
    /// 牌桌底板 + 四家手牌/副露/牌河 + 五个语义色动作按钮 + HUD。
    /// 每个图层带 <c>id</c>/<c>data-name</c>，便于在 Illustrator 里定位修改。
    /// </summary>
    private static void ExportUiSvg(string path)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tilesDir = Path.Combine(FindWebDir(), "mj_tiles");
        var layout = NewLayout();
        var vm = TableDemoModel.CreateForUiExport();
        var exporter = new SvgExporter(tilesDir, layout);

        File.WriteAllText(path, exporter.BuildUi(vm), new System.Text.UTF8Encoding(false));

        var bar = new ActionBar();
        bar.Build(vm, layout);
        var info = new FileInfo(path);
        Console.WriteLine($"exported merged ui svg -> {path}");
        Console.WriteLine($"  size = {info.Length} B   canvas = {layout.CanvasW:0}×{layout.CanvasH:0}   tiles = {exporter.TileCount}   buttons = {bar.Buttons.Count}");
        Console.WriteLine("  ids  = " + string.Join(", ", bar.Buttons.Select(b => "btn-" + ActionBar.SemanticId(b.Kind))));
    }

    /// <summary>从 exe 目录向上找仓库里的 Web 目录（mj_tiles 来源）。</summary>
    private static string FindWebDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir.FullName, "Web", "mj_tiles");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(dir.FullName, "Web");
            }

            dir = dir.Parent;
        }

        return "Web";
    }

    private static void RenderAnnotated(string path)
    {
        using var atlas = new TileAtlas(AssetsDir);
        var layout = NewLayout();
        var regions = SvgExporter.Regions(layout).ToArray();

        const int legendH = 120;
        var info = new SKImageInfo((int)layout.CanvasW, (int)(layout.CanvasH + legendH));
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(18, 26, 22));

        using (var ctx = new SkiaCanvas(canvas, atlas))
        {
            new TableSceneSkia().Draw(ctx, TableDemoModel.Create(1), layout);
        }

        DrawRegionNumbers(canvas, regions);
        DrawLegend(canvas, (int)layout.CanvasW, (int)layout.CanvasH, legendH, regions);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        Console.WriteLine("rendered annotated -> " + path);
    }

    /// <summary>在每个区域左上角画大号编号。</summary>
    private static void DrawRegionNumbers(SKCanvas canvas, (string Id, string Note, Tiisu.App.Rendering.RectF Rect)[] regions)
    {
        using var box = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, Color = new SKColor(255, 72, 72) };
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(200, 32, 32) };
        using var typeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, 20f);
        using var text = new SKPaint { IsAntialias = true, Color = SKColors.White };

        for (int i = 0; i < regions.Length; i++)
        {
            var r = regions[i].Rect;
            canvas.DrawRect(new SKRect(r.X, r.Y, r.Right, r.Bottom), box);
            var badge = new SKRect(r.X, r.Y, r.X + 30, r.Y + 30);
            canvas.DrawRect(badge, fill);
            canvas.DrawText((i + 1).ToString(CultureInfo.InvariantCulture), r.X + 8, r.Y + 22,
                SKTextAlign.Left, font, text);
        }
    }

    /// <summary>底部图例：编号 -> 区域名。</summary>
    private static void DrawLegend(SKCanvas canvas, int width, int top, int height,
        (string Id, string Note, Tiisu.App.Rendering.RectF Rect)[] regions)
    {
        using var bg = new SKPaint { IsAntialias = true, Color = new SKColor(12, 18, 15) };
        canvas.DrawRect(new SKRect(0, top, width, top + height), bg);

        using var typeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, 16f);
        using var head = new SKPaint { IsAntialias = true, Color = new SKColor(255, 214, 120) };
        using var body = new SKPaint { IsAntialias = true, Color = new SKColor(228, 236, 228) };

        var line = string.Join("   ",
            regions.Select((r, i) => $"{i + 1} {r.Note}"));
        canvas.DrawText(line, 16, top + 26, SKTextAlign.Left, font, body);
        canvas.DrawText("直接说编号和怎么改就行，比如「3 应该在 2 的右边、和 2 贴住」「6 太靠下，往上挪」",
            16, top + 58, SKTextAlign.Left, font, head);
        canvas.DrawText("（改完我出图，你再指；不需要你填表或改文件）",
            16, top + 88, SKTextAlign.Left, font, body);
    }

    /// <summary>
    /// <c>--render-ankan &lt;png 基名 | 目录&gt;</c>：暗杠展示自检。
    /// 生成两张图：<c>-play.png</c>（对局中，只翻第 2、4 张）与 <c>-win.png</c>（和牌后，全翻 + 30% 黑遮罩）。
    /// </summary>
    private static void RenderAnkanToPng(string target)
    {
        string basePath = target;
        if (!target.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(target);
            basePath = Path.Combine(target, "ankan.png");
        }
        else
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(basePath));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        string stem = basePath.Substring(0, basePath.Length - 4);
        string playPath = stem + "-play.png";
        string winPath = stem + "-win.png";

        RenderAnkanFrame(playPath, settled: false);
        RenderAnkanFrame(winPath, settled: true);

        Console.WriteLine("rendered ankan -> " + playPath);
        Console.WriteLine("rendered ankan -> " + winPath);
    }

    private static void RenderAnkanFrame(string path, bool settled)
    {
        using var atlas = new TileAtlas(AssetsDir);
        var layout = NewLayout();
        var vm = TableDemoModel.CreateWithAnkan(settled);

        var info = new SKImageInfo((int)layout.CanvasW, (int)layout.CanvasH);
        using var surface = SKSurface.Create(info);
        var canvasSk = surface.Canvas;
        canvasSk.Clear(new SKColor(20, 30, 24));

        using (var ctx = new SkiaCanvas(canvasSk, atlas))
        {
            new TableSceneSkia().Draw(ctx, vm, layout);

            string caption = settled
                ? "和牌后：暗杠整组翻开 + 30% 黑色遮罩"
                : "对局中：暗杠只翻第 2、4 张（其余牌背）";
            var box = new RectF(layout.CanvasW / 2f - 250, 22, 500, 36);
            ctx.FillRoundRect(box, 10, Tiisu.App.Rendering.Color.Rgb(0, 0, 0).WithAlpha(160));
            ctx.DrawText(caption, box, Tiisu.App.Rendering.Color.Rgb(255, 228, 120), new TextStyle(18, Bold: true, Center: true));
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    /// <summary>
    /// <c>--render-actions &lt;png&gt;</c>：动作按钮语义色调色板 ——
    /// 五个动作 × 可用 / 悬停 / 灰显三态，与游戏内共用 <see cref="ActionBar.DrawButton"/>。
    /// </summary>
    private static void RenderActionsToPng(string path)
    {
        var info = new SKImageInfo(1200, 1200);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(16, 24, 19));

        using (var ctx = new SkiaCanvas(canvas, atlas: null))
        {
            ctx.FillLinearGradient(new RectF(0, 0, 1200, 1200), 0,
                Color.Rgb(24, 38, 30), Color.Rgb(13, 19, 15));

            ctx.DrawText("动作按钮 · 语义色", new RectF(0, 58, 1200, 40),
                Color.Rgb(236, 242, 232), new TextStyle(30, Bold: true));
            ctx.DrawText("可用（深底 + 亮边） / 悬停（更亮） / 灰显（去饱和 + 灰字）",
                new RectF(0, 104, 1200, 24), Color.Rgb(150, 166, 154), new TextStyle(16));

            var kinds = new[] { ActionBar.Kind.Chi, ActionBar.Kind.Pon, ActionBar.Kind.Kan,
                ActionBar.Kind.DeclareCall, ActionBar.Kind.Tsumo };
            var labels = new[] { "吃", "碰", "杠", "报听", "和牌" };
            var states = new[] { "可用", "悬停", "灰显" };

            const float bw = 176f, bh = 64f, gap = 20f;
            float totalW = kinds.Length * bw + (kinds.Length - 1) * gap;
            float x0 = (1200f - totalW) / 2f + 40f;
            float y0 = 300f, rowGap = 118f;

            for (int col = 0; col < kinds.Length; col++)
            {
                var accent = ActionBar.Accent(kinds[col]);
                float cx = x0 + col * (bw + gap);
                ctx.DrawText(labels[col], new RectF(cx, 196, bw, 30), accent,
                    new TextStyle(22, Bold: true));
                ctx.DrawText($"#{accent.R:X2}{accent.G:X2}{accent.B:X2}",
                    new RectF(cx, 228, bw, 20), Color.Rgb(126, 140, 130), new TextStyle(14));
            }

            for (int row = 0; row < states.Length; row++)
            {
                float y = y0 + row * rowGap;
                ctx.DrawText(states[row], new RectF(36, y + bh / 2f - 12, 90, 24),
                    row == 2 ? Color.Rgb(140, 148, 142) : Color.Rgb(214, 226, 214),
                    new TextStyle(18, Bold: true, Center: false));

                for (int col = 0; col < kinds.Length; col++)
                {
                    var rect = new RectF(x0 + col * (bw + gap), y, bw, bh);
                    ActionBar.DrawButton(ctx, kinds[col], labels[col], rect,
                        enabled: row != 2, hover: row == 1);
                }
            }

            // 关键说明：和牌已由红改黄。
            var note = new RectF(0, 724, 1200, 30);
            ctx.DrawText("和牌 / 荣和 / 自摸统一用黄色（原红色已废弃）；过 / 流局为中性灰绿。",
                note, Color.Rgb(196, 208, 196), new TextStyle(16));

            // 底部图例：色块 + 名称 + 十六进制，便于对照实现。
            float ly = 806f;
            ctx.DrawText("语义色表", new RectF(0, ly, 1200, 24),
                Color.Rgb(236, 242, 232), new TextStyle(18, Bold: true));
            for (int i = 0; i < kinds.Length; i++)
            {
                var accent = ActionBar.Accent(kinds[i]);
                float bx = 120 + i * 200;
                ctx.FillRoundRect(new RectF(bx, ly + 44, 34, 26), 6, accent);
                ctx.DrawText($"{labels[i]}  #{accent.R:X2}{accent.G:X2}{accent.B:X2}",
                    new RectF(bx + 44, ly + 44, 150, 26), Color.Rgb(210, 222, 210),
                    new TextStyle(15, Center: false));
            }
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        Console.WriteLine($"rendered action palette -> {path}  (5 kinds x 3 states)");
    }

    /// <summary>
    /// <c>--render-call &lt;png 基名&gt;</c>：报听三态出图 ——
    /// <c>_callable</c>（真听牌，按钮可点 + 待牌提示）/ <c>_disabled</c>（不听牌，按钮灰显）/
    /// <c>_noten</c>（荒牌流局：四家翻牌 + 未听牌灰显 + 已报听标记）。
    /// </summary>
    private static void RenderCallToPng(string basePath)
    {
        using var atlas = new TileAtlas(AssetsDir);
        var layout = new TableLayoutSpec { CanvasW = 1200f, CanvasH = 1200f };
        layout.ApplyMixedSafeArea();
        YogaTableLayout.Apply(layout);

        RenderCallShot(basePath + "_callable.png", TableDemoModel.CreateWithCall(true), layout, atlas);
        RenderCallShot(basePath + "_disabled.png", TableDemoModel.CreateWithCall(false), layout, atlas);
        RenderCallShot(basePath + "_noten.png", TableDemoModel.CreateWithFlow(), layout, atlas);
    }

    private static void RenderCallShot(string path, TableViewModel vm, TableLayoutSpec layout, TileAtlas atlas)
    {
        var info = new SKImageInfo((int)layout.CanvasW, (int)layout.CanvasH);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(new SKColor(20, 30, 24));

        using (var ctx = new SkiaCanvas(surface.Canvas, atlas))
        {
            new TableSceneSkia().Draw(ctx, vm, layout, hoverHand: -1);

            var bar = new ActionBar();
            bar.Build(vm, layout);
            bar.DrawTenpai(ctx, vm, layout);
            bar.Draw(ctx);

            // 与牌桌屏同文案的 HUD：顶部消息 + 右上角状态。
            if (!string.IsNullOrEmpty(vm.Message))
            {
                var box = new RectF(layout.CanvasW / 2f - 330, 18, 660, 38);
                ctx.FillRoundRect(box, 10, Color.Rgb(0, 0, 0).WithAlpha(150));
                ctx.DrawText(vm.Message, box, Color.Rgb(255, 228, 120), new TextStyle(17, Bold: true));
            }

            string status = vm.IsCalled ? "已报听" : vm.WinBanned ? "本局禁和" : string.Empty;
            if (status.Length > 0)
            {
                var box = new RectF(layout.CanvasW - 190, 18, 166, 34);
                ctx.FillRoundRect(box, 10, Color.Rgb(0, 0, 0).WithAlpha(150));
                ctx.DrawText(status, box, Color.Rgb(255, 226, 140), new TextStyle(16, Bold: true));
            }
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        Console.WriteLine($"rendered call -> {path}  phase={vm.Phase} called={vm.IsCalled} actions={vm.Actions}");
    }

    /// <summary>
    /// <c>--smoke-call</c>：无窗口走一遍报听机制（宣言 / 锁手拒绝 / 吃碰被拒 / 暗杠例外 /
    /// 普通错和 / 重罚错和 / 禁和标记 / 流局报听者算听牌）。
    /// </summary>
    private static int SmokeCall()
    {
        TryUseUtf8Console();
        int failures = 0;

        void Check(string name, bool ok, string detail)
        {
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name.PadRight(26) + detail);
            if (!ok)
            {
                failures++;
            }
        }

        static List<GameTile> Hand(string spec)
        {
            var tiles = new List<GameTile>();
            var numbers = new List<int>();
            foreach (char ch in spec)
            {
                if (ch >= '0' && ch <= '9')
                {
                    numbers.Add(ch - '0');
                    continue;
                }

                GameSuit suit = ch switch
                {
                    'm' => GameSuit.Man,
                    'p' => GameSuit.Pin,
                    's' => GameSuit.Sou,
                    _ => GameSuit.Honor,
                };
                foreach (int n in numbers)
                {
                    tiles.Add(new GameTile(suit, n));
                }

                numbers.Clear();
            }

            return tiles;
        }

        MahjongEngine NewRound(string selfHand, int dealer, out List<GameTile> hand)
        {
            var engine = new MahjongEngine();
            hand = Hand(selfHand);
            var hands = new List<IReadOnlyList<GameTile>>
            {
                hand,
                Hand("1234m6789p12345s"),
                Hand("6789m1234p6789s1z"),
                Hand("2345m6789p2345s1z"),
            };
            engine.StartNewRound(new RoundDeal(dealer, 0, hands));
            return engine;
        }

        // ── 1. 报听宣言 / 非听牌被拒 ──────────────────────────────
        var engine = NewRound("1111m23m456m789m5z", 0, out var selfHand);
        var player = engine.Players[0];
        Check("13 张手牌听牌", engine.IsTenpai(player), "hand=1111m23m456m789m5z");
        Check("报听宣言", engine.DeclareCall(player) && player.IsCalled,
            "waitMask=" + player.CalledWaitMask.ToString("x"));
        Check("重复宣言被拒", !engine.DeclareCall(player), "");
        var junkEngine = NewRound("1234m6789p12345s", 0, out _);
        Check("非听牌不能报", !junkEngine.DeclareCall(junkEngine.Players[0]), "hand=1234m6789p12345s");

        // ── 2. 锁手：打掉会改听牌张的牌被拒 ──────────────────────
        engine.Draw(player, new GameTile(GameSuit.Pin, 9));
        string? breakReason = null;
        GameTile? breakTile = null;
        foreach (var tile in player.Hand)
        {
            if (!engine.CallAllowsDiscard(player, tile, out string? reason))
            {
                if (reason is not null && reason.Contains("听牌张", StringComparison.Ordinal))
                {
                    breakTile = tile;
                    breakReason = reason;
                    break;
                }
            }
        }

        Check("锁手拒绝改听牌张", breakTile is not null, breakTile is null ? "未找到破坏听牌形的打牌" : "tile=" + breakTile.Value.Id + " · " + breakReason);
        if (breakTile is not null)
        {
            int before = player.Hand.Count;
            var blocked = engine.Discard(player, breakTile.Value);
            Check("越界弃牌被引擎拦下", blocked is null && player.Hand.Count == before, "hand=" + before);
        }

        // ── 3. 吃碰被拒 ─────────────────────────────────────────
        bool ponDenied = !engine.CallAllowsMeld(player, MeldType.Pon, out string? ponReason);
        bool chiDenied = !engine.CallAllowsMeld(player, MeldType.Chi, out _);
        bool minkanDenied = !engine.CallAllowsMeld(player, MeldType.Minkan, out _);
        bool kakanDenied = !engine.CallAllowsMeld(player, MeldType.Kakan, out _);
        Check("禁吃碰明杠加杠", ponDenied && chiDenied && minkanDenied && kakanDenied, ponReason ?? string.Empty);

        bool meldThrew = false;
        try
        {
            engine.ApplyMeld(0, MeldType.Pon, new GameTile(GameSuit.Man, 1), 1);
        }
        catch (InvalidOperationException)
        {
            meldThrew = true;
        }

        Check("引擎硬拒绝副露", meldThrew, "ApplyMeld → InvalidOperationException");

        // ── 4. 暗杠例外 ─────────────────────────────────────────
        var kanEngine = NewRound("1111m23m456m789m5z", 0, out _);
        var kanPlayer = kanEngine.Players[0];
        bool kanDeclared = kanEngine.DeclareCall(kanPlayer);

        // 摸 4m：暗杠 1m 后 23m 由 4m 补成 234m，听牌张仍是 5z（形不变 → 允许）。
        kanEngine.Draw(kanPlayer, new GameTile(GameSuit.Man, 4));
        bool ankanAllowed = kanEngine.CallAllowsAnkan(kanPlayer, new GameTile(GameSuit.Man, 1), out string? ankanYes);
        bool ankanOther = kanEngine.CallAllowsAnkan(kanPlayer, new GameTile(GameSuit.Man, 2), out string? ankanNo);
        Check("暗杠例外（形不变）", kanDeclared && ankanAllowed, ankanYes ?? "ankan 1m allowed");
        Check("非四张暗杠被拒", !ankanOther, ankanNo ?? string.Empty);

        var kakanEngine = NewRound("1111m23m456m789m5z", 0, out _);
        var kakanPlayer = kakanEngine.Players[0];
        kakanEngine.DeclareCall(kakanPlayer);
        kakanEngine.Draw(kakanPlayer, new GameTile(GameSuit.Pin, 9));
        kakanPlayer.AddMeld(MeldType.Pon, new[] { new GameTile(GameSuit.Honor, 5), new GameTile(GameSuit.Honor, 5), new GameTile(GameSuit.Honor, 5) });
        Check("加杠被拒", !kakanEngine.DoKan(kakanPlayer, new GameTile(GameSuit.Honor, 5), MeldType.Kakan), "报听后加杠");

        // ── 5. 普通错和（Z = 赋 × 700）──────────────────────────
        var scoresBefore = engine.Scores.ToArray();
        var penalty = engine.ApplyCallMisfire(0, 2);
        bool minorOk = penalty is not null && !penalty.Severe && penalty.Z == 1400
                       && engine.Scores[0] == scoresBefore[0] - 4200
                       && engine.Scores[1] == scoresBefore[1] + 1400
                       && engine.Scores[2] == scoresBefore[2] + 1400
                       && engine.Scores[3] == scoresBefore[3] + 1400;
        Check("普通错和 Z=赋×700", minorOk,
            penalty is null ? "null" : $"fu={penalty.Fu} z={penalty.Z} scores={string.Join(",", engine.Scores)}");
        Check("错和解除报听", !player.IsCalled, "isCalled=false");
        Check("本巡禁和标记", engine.IsWinBanned(player), "isWinBanned=true");

        // ── 6. 重罚错和（Z > 2100）──────────────────────────────
        var severeEngine = NewRound("1111m23m456m789m5z", 1, out _);
        var severePlayer = severeEngine.Players[0];
        severeEngine.DeclareCall(severePlayer);
        var severeScores = severeEngine.Scores.ToArray();
        var severe = severeEngine.ApplyCallMisfire(0, 4);
        bool severeOk = severe is not null && severe.Severe
                        && severeEngine.Scores[1] == severeScores[1] + 14000
                        && severeEngine.Scores[2] == severeScores[2] + 7000
                        && severeEngine.Scores[3] == severeScores[3] + 7000
                        && severeEngine.Scores[0] == severeScores[0] - 28000;
        Check("重罚错和（子家违规）", severeOk,
            severe is null ? "null" : $"z={severe.Z} paid={string.Join(",", severe.Paid)} scores={string.Join(",", severeEngine.Scores)}");
        Check("重罚：本局+下局禁和",
            severeEngine.IsWinBanned(severePlayer) && severeEngine.IsWinBannedNextHand(0), "hand+nextHand=true");

        // ── 7. 流局：已报听者自动算听牌 ─────────────────────────
        var flowEngine = NewRound("1111m23m456m789m5z", 0, out _);
        var flowPlayer = flowEngine.Players[0];
        flowEngine.DeclareCall(flowPlayer);
        flowEngine.Players[3].IsCalled = true; // 非听牌但已报听（直接置位验证「自动算听牌」）
        flowEngine.Flow();
        Check("流局报听者算听牌",
            flowEngine.FlowTenpai.Count == 4 && flowEngine.FlowTenpai[0] && flowEngine.FlowTenpai[3],
            "tenpai=" + string.Join(",", flowEngine.FlowTenpai));
        Check("流局未听牌家灰显依据",
            !flowEngine.FlowTenpai[1] && !flowEngine.FlowTenpai[2],
            "seat1/2 noten");
        Check("流局翻牌", flowEngine.RevealHands, "revealHands=true");

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "smoke-call: all checks passed" : "smoke-call: " + failures + " failed");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// <c>--play-real &lt;seed&gt; [mode] [games]</c>：像真人一样打牌（无窗口）。
    /// 人类座位会：正常出牌、能报听时报听、荣和窗口随机「和」或「过」、副露窗口随机「过」。
    /// 每一步都校验手牌张数与牌局推进，卡住或牌数异常立即报错退出。
    /// </summary>
    private static int PlayReal(string[] args)
    {
        TryUseUtf8Console();
        if (args.Length < 2
            || !uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint seed))
        {
            Console.Error.WriteLine("用法：--play-real <seed> [mode] [games]");
            return 2;
        }

        int mode = 4;
        if (args.Length >= 3 && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int m))
        {
            mode = m;
        }

        int games = 3;
        if (args.Length >= 4 && int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int g))
        {
            games = g;
        }

        int failures = 0;
        for (int game = 0; game < games; game++)
        {
            uint gameSeed = seed + (uint)game;
            var rng = new Mulberry32Random(gameSeed);
            var runtime = new MatchRuntime(new Settings(), new Mulberry32Random(gameSeed), _ => { },
                saveStore: null, stats: null, roundLog: null, matchSeed: gameSeed);
            runtime.StartMatch(mode);

            var clock = Stopwatch.StartNew();
            int steps = 0;
            int ronWindows = 0;
            int ronPasses = 0;
            int callDeclares = 0;
            int misfireHints = 0;
            string stall = string.Empty;
            int lastTurn = -1;
            MatchPhase lastPhase = MatchPhase.Idle;
            int sameState = 0;

            while (clock.Elapsed < TimeSpan.FromSeconds(45))
            {
                if (++steps > 200_000)
                {
                    stall = "动作数超过 200000";
                    break;
                }

                var phase = runtime.Phase;
                if (phase == MatchPhase.MatchOver)
                {
                    break;
                }

                if (phase == MatchPhase.RoundEnded)
                {
                    runtime.NextRound();
                    continue;
                }

                // 死循环检测：阶段 + 回合 + 人类手牌张数 连续不变。
                var player = runtime.HumanIndex >= 0 ? runtime.Engine.Players[runtime.HumanIndex] : null;
                int handCount = player?.Hand.Count ?? -1;
                if (phase == lastPhase && runtime.Engine.TurnIndex == lastTurn)
                {
                    if (++sameState > 400)
                    {
                        stall = $"卡在 {phase}（回合={runtime.Engine.TurnIndex}，手牌={handCount}，动作={runtime.Snapshot.Actions}）";
                        break;
                    }
                }
                else
                {
                    sameState = 0;
                    lastPhase = phase;
                    lastTurn = runtime.Engine.TurnIndex;
                    if (Environment.GetEnvironmentVariable("TIISU_PLAY_TRACE") == "1")
                    {
                        Console.WriteLine($"    [{steps}] phase={phase} turn={runtime.Engine.TurnIndex} hand={handCount}"
                            + $" called={player?.IsCalled} wall={runtime.Engine.Wall.Count}");
                    }
                }

                switch (phase)
                {
                    case MatchPhase.HumanRon:
                    {
                        ronWindows++;
                        int beforeCount = handCount;
                        bool pass = rng.NextDouble() < 0.5;
                        runtime.Execute(pass ? GameCommand.Pass() : GameCommand.Ron());
                        if (pass && player is not null)
                        {
                            ronPasses++;
                            int meldTiles = player.Melds.Sum(m => m.Tiles.Count);
                            int concealed = 13 - meldTiles + player.KongCount();
                            // 过牌后牌局可能已经推进到人类自己摸牌，所以允许 13（13 张形）/14（摸牌后）两种；
                            // 关键是不能少牌（小相公）也不能多出两张以上。
                            if (player.Hand.Count < concealed || player.Hand.Count > concealed + 1)
                            {
                                stall = $"过牌后手牌张数异常：{beforeCount} -> {player.Hand.Count}"
                                        + $"（应在 {concealed}..{concealed + 1}）";
                            }
                        }

                        break;
                    }

                    case MatchPhase.HumanMeld:
                        runtime.Execute(GameCommand.Pass());
                        break;

                    case MatchPhase.HumanDiscard:
                    {
                        var vm = runtime.Snapshot;
                        if ((vm.Actions & HumanAction.Tsumo) != 0)
                        {
                            runtime.Execute(GameCommand.Tsumo());
                            break;
                        }

                        if ((vm.Actions & HumanAction.DeclareCall) != 0 && rng.NextDouble() < 0.4)
                        {
                            if (vm.CallWouldMisfire)
                            {
                                misfireHints++;
                            }

                            int idx = TenpaiDiscardIndex(runtime.Engine, player!);
                            if (idx >= 0 && idx < player!.Hand.Count)
                            {
                                runtime.Execute(GameCommand.DeclareCall(idx));
                                callDeclares++;
                                break;
                            }
                        }

                        int pick = player!.IsCalled
                            ? LegalCalledDiscardIndex(runtime.Engine, player)
                            : BestShantenDiscardIndex(player);
                        if (pick < 0 || pick >= player.Hand.Count)
                        {
                            stall = "找不到可打的牌（手牌=" + player.Hand.Count + "："
                                    + string.Join(" ", player.Hand.Select(t => t.Id))
                                    + "，报听=" + player.IsCalled + "）";
                            break;
                        }

                        runtime.Execute(GameCommand.Discard(pick));
                        break;
                    }

                    default:
                        break;
                }

                if (stall.Length > 0)
                {
                    break;
                }
            }

            clock.Stop();
            bool completed = runtime.Phase == MatchPhase.MatchOver;
            if (stall.Length == 0 && !completed)
            {
                stall = "45 秒内未终局（阶段=" + runtime.Phase + "）";
            }

            Console.WriteLine($"play-real game={game + 1}/{games} seed={gameSeed} mode={mode} "
                + $"耗时={clock.Elapsed.TotalSeconds:F1}s 步数={steps} 荣和窗口={ronWindows} 过牌={ronPasses} "
                + $"报听={callDeclares} 错和提示={misfireHints} 阶段={runtime.Phase} 结果={(stall.Length == 0 ? "OK" : "FAIL " + stall)}");

            if (stall.Length > 0)
            {
                failures++;
            }
        }

        Console.WriteLine(failures == 0 ? "play-real: all games passed" : $"play-real: {failures} game(s) failed");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// <c>--smoke-wall</c>：按住右下角「牌山」按钮时，牌桌中心区域必须出现查看器面板（像素级判定）。
    /// </summary>
    private static int SmokeWall()
    {
        TryUseUtf8Console();
        var settings = new Settings { ShowWallViewer = true };
        using var atlas = new TileAtlas(AssetsDir);
        var screen = new TableScreen(() => TableDemoModel.Create(1), null, null, atlas,
            TableRenderStyle.Mixed, settings);

        SKBitmap Render(bool held)
        {
            if (held)
            {
                screen.OnPointerDown(screen.CanvasWidth - 98, screen.CanvasHeight - 84);
            }

            using var surface = SKSurface.Create(new SKImageInfo((int)screen.CanvasWidth, (int)screen.CanvasHeight));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Black);
            using (var ctx = new SkiaCanvas(canvas, atlas))
            {
                screen.Update(0.6);
                screen.Draw(ctx, 0.6);
            }

            if (held)
            {
                screen.OnPointerUp(screen.CanvasWidth - 98, screen.CanvasHeight - 84);
            }

            using var image = surface.Snapshot();
            var bitmap = new SKBitmap(image.Width, image.Height);
            image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0);
            return bitmap;
        }

        using var normal = Render(false);
        using var held = Render(true);

        int changed = 0;
        int total = 0;
        for (int y = 120; y < held.Height - 160; y += 4)
        {
            for (int x = 140; x < held.Width - 140; x += 4)
            {
                total++;
                var a = normal.GetPixel(x, y);
                var b = held.GetPixel(x, y);
                if (Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue) > 12)
                {
                    changed++;
                }
            }
        }

        double ratio = total == 0 ? 0 : (double)changed / total;
        bool passed = ratio > 0.25;
        Console.WriteLine($"smoke-wall: 面板区域像素变化={ratio:P1} {(passed ? "PASS" : "FAIL")}");
        return passed ? 0 : 1;
    }

    /// <summary>
    /// <c>--smoke-think</c>：长考 2 秒后应自动摸切并离开人类出牌阶段。
    /// </summary>
    private static int SmokeThink()
    {
        TryUseUtf8Console();
        var settings = new Settings { ThinkSeconds = 2 };
        var runtime = new MatchRuntime(settings, new Mulberry32Random(5), _ => { },
            saveStore: null, stats: null, roundLog: null, matchSeed: 5);
        using var atlas = new TileAtlas(AssetsDir);
        var screen = new TableScreen(() => runtime.Snapshot, runtime.Execute, null, atlas,
            TableRenderStyle.Mixed, settings);
        runtime.StartMatch(4);

        var clock = Stopwatch.StartNew();
        bool sawHuman = false;
        int before = 0;
        bool lastActed = false;
        while (clock.Elapsed < TimeSpan.FromSeconds(20))
        {
            var phase = runtime.Phase;
            if (phase is MatchPhase.MatchOver or MatchPhase.RoundEnded)
            {
                break;
            }

            if (phase == MatchPhase.HumanDiscard)
            {
                sawHuman = true;
                before = runtime.Snapshot.Seats[0].Hand.Count;
                int wallBefore = runtime.Engine.Wall.Count;
                bool acted = false;
                var wait = Stopwatch.StartNew();
                while (wait.Elapsed < TimeSpan.FromSeconds(8))
                {
                    screen.Update(0.016);
                    Thread.Sleep(30);
                    if (runtime.Engine.Wall.Count != wallBefore || runtime.Snapshot.Seats[0].Hand.Count != before)
                    {
                        acted = true;
                        break;
                    }
                }

                lastActed = acted;
                break;
            }

            if (phase is MatchPhase.HumanMeld or MatchPhase.HumanRon)
            {
                runtime.Execute(GameCommand.Pass());
                continue;
            }

            Thread.Sleep(5);
        }

        bool passed = sawHuman && lastActed;
        Console.WriteLine($"smoke-think: 人类出牌={sawHuman} 手牌={before} 自动动作={lastActed} 结束阶段={runtime.Phase} "
            + (passed ? "PASS" : "FAIL"));
        return passed ? 0 : 1;
    }

    /// <summary>
    /// <c>--smoke-call-flow &lt;seed&gt; [mode]</c>：报听真实流程回归（无窗口）。
    /// 自动打一场，人类座位按向听最优化出牌；出现报听时机时先用 T 键进入 / 取消「报听」待弃态
    /// （与点按钮同一条 <c>BeginDeclareCall</c>），再发与点手牌同一条 <see cref="GameCommand.DeclareCall"/>
    /// 命令（弃一张 + 宣言）。断言：
    /// <list type="bullet">
    /// <item>点「报听」但还没出牌时只改 UI 状态、不发命令、不卡死；</item>
    /// <item>报听并弃牌后牌局立刻推进（不停在 idle，回合交给下一家）；</item>
    /// <item>报听后按钮变成「已报听」灰显，且锁手仍允许出牌；</item>
    /// <item>整场在时限内终局（真卡住时这里的耗时会先耗尽）。</item>
    /// </list>
    /// </summary>
    private static int SmokeCallFlow(string[] args)
    {
        TryUseUtf8Console();
        if (args.Length < 2
            || !uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint seed))
        {
            Console.Error.WriteLine("用法：--smoke-call-flow <seed> [mode]");
            return 2;
        }

        int mode = 4;
        if (args.Length >= 3 && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMode))
        {
            mode = parsedMode;
        }

        // 固定默认设置：不读用户 settings.json（MCTS 开关 / 权重会改变耗时与 AI 档位），
        // 保证「同 seed 同结果」的确定性复现。
        var settings = new Settings();
        var runtime = new MatchRuntime(settings, new Mulberry32Random(seed), _ => { },
            saveStore: null, stats: null, roundLog: null, matchSeed: seed);

        using var atlas = new TileAtlas(AssetsDir);
        var screen = new TableScreen(() => runtime.Snapshot, runtime.Execute, null, atlas);
        var layout = new TableLayoutSpec { CanvasW = 1200f, CanvasH = 1200f };
        YogaTableLayout.Apply(layout);

        int failures = 0;
        void Check(string name, bool ok, string detail)
        {
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name.PadRight(26) + detail);
            if (!ok)
            {
                failures++;
            }
        }

        var clock = Stopwatch.StartNew();
        var budget = TimeSpan.FromSeconds(20);
        runtime.StartMatch(mode);

        bool armOk = false;
        bool declared = false;
        bool advanceOk = false;
        string advanceDetail = "未出现报听时机";
        bool callButtonOk = false;
        bool callButtonChecked = false;
        bool postCallDiscardOk = false;
        int postCallTurns = 0;
        int actions = 0;
        string stuck = string.Empty;

        while (clock.Elapsed < budget)
        {
            if (++actions > 50_000)
            {
                stuck = "动作数超过 50000";
                break;
            }

            var phase = runtime.Phase;
            if (phase == MatchPhase.MatchOver)
            {
                break;
            }

            if (phase == MatchPhase.RoundEnded)
            {
                runtime.NextRound();
                continue;
            }

            if (phase == MatchPhase.HumanRon)
            {
                runtime.HumanRon();
                continue;
            }

            if (phase == MatchPhase.HumanMeld)
            {
                runtime.Pass();
                continue;
            }

            if (phase != MatchPhase.HumanDiscard)
            {
                stuck = "牌局停在 " + phase + "，无人推进";
                break;
            }

            var vm = runtime.Snapshot;
            int human = runtime.HumanIndex;
            var player = runtime.Engine.Players[human];

            if ((vm.Actions & HumanAction.Tsumo) != 0)
            {
                runtime.HumanTsumo();
                continue;
            }

            // 报听时机：按钮可用（真听牌）。先只用 T 键进入 / 取消「待弃」态（UI 路径，
            // 与点「报听」按钮同一条 BeginDeclareCall），再发与点手牌同一条 DeclareCall 命令。
            if (!declared && (vm.Actions & HumanAction.DeclareCall) != 0)
            {
                int index = TenpaiDiscardIndex(runtime.Engine, player);
                if (index < 0 || index >= player.Hand.Count)
                {
                    stuck = "报听按钮可用但找不到保持听牌的弃牌";
                    break;
                }

                // 1. 只点「报听」不进牌：只进入待弃态，不发命令、牌局不动、也不卡。
                var beforeArm = runtime.Snapshot;
                bool armed = screen.OnKey(Keys.T);
                bool armStable = armed && ReferenceEquals(beforeArm, runtime.Snapshot)
                                 && runtime.Phase == MatchPhase.HumanDiscard;
                bool cancelled = screen.OnKey(Keys.T);
                bool cancelStable = cancelled && ReferenceEquals(beforeArm, runtime.Snapshot)
                                    && runtime.Phase == MatchPhase.HumanDiscard;
                armOk = armStable && cancelStable;

                // 2. 点手牌 = 打出这张并宣言（TableScreen 的待弃点击发的就是这条命令）。
                var tile = player.Hand[index];
                int turnBefore = runtime.Engine.TurnIndex;
                int wallBefore = runtime.Engine.Wall.Count;
                var before = runtime.Snapshot;
                runtime.Execute(GameCommand.DeclareCall(index));
                declared = player.IsCalled && !ReferenceEquals(before, runtime.Snapshot);
                // 推进的判据：宣言后主循环真的跑了（有人摸牌 → 牌山变少），或本局直接结束；
                // 停在 Idle、牌山数不变就是「报听后卡住」。
                advanceOk = declared
                            && runtime.Phase != MatchPhase.Idle
                            && runtime.Snapshot.Phase != "idle"
                            && (runtime.Engine.Wall.Count != wallBefore
                                || runtime.Phase is MatchPhase.RoundEnded or MatchPhase.MatchOver);
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  报听: 打出={0} isCalled={1} 阶段={2} 回合={3}->{4} 牌山={5}->{6} 用时={7:F0}ms",
                    tile.Id, player.IsCalled, runtime.Phase, turnBefore,
                    runtime.Engine.TurnIndex, wallBefore, runtime.Engine.Wall.Count,
                    clock.Elapsed.TotalMilliseconds));

                advanceDetail = "宣言后阶段=" + runtime.Phase + "，回合 " + turnBefore + "->"
                                + runtime.Engine.TurnIndex + "，牌山 " + wallBefore + "->"
                                + runtime.Engine.Wall.Count;
                if (!advanceOk)
                {
                    stuck = "报听并弃牌后牌局没有推进（阶段=" + runtime.Phase + "，回合已到 "
                            + runtime.Engine.TurnIndex + "，牌山仍为 " + runtime.Engine.Wall.Count + "）";
                    break;
                }

                // 3. 三态：报听后按钮必须是「已报听」灰显（本局已结束则按钮不可见，跳过）。
                if (runtime.Phase == MatchPhase.HumanDiscard)
                {
                    var bar = new ActionBar();
                    bar.Build(runtime.Snapshot, layout);
                    var callButton = bar.Buttons.FirstOrDefault(b => b.Kind == ActionBar.Kind.DeclareCall);
                    callButtonOk = callButton is not null && callButton.Label == "已报听" && !callButton.Enabled;
                    callButtonChecked = true;
                }

                continue;
            }

            // 已报听：只打锁手允许的牌；未报听：按向听最优化出牌（让人类尽快听牌）。
            int pick = player.IsCalled
                ? LegalCalledDiscardIndex(runtime.Engine, player)
                : BestShantenDiscardIndex(player);
            if (pick < 0 || pick >= player.Hand.Count)
            {
                stuck = "没有可打出的手牌";
                break;
            }

            if (player.IsCalled)
            {
                postCallTurns++;
            }

            var beforeDiscard = runtime.Snapshot;
            runtime.Execute(GameCommand.Discard(pick));
            bool accepted = !ReferenceEquals(beforeDiscard, runtime.Snapshot);
            if (!accepted)
            {
                stuck = "出牌未被接受（手牌=" + player.Hand.Count + "，阶段=" + runtime.Phase
                        + "，动作=" + vm.Actions + "，下标=" + pick + "，牌=" + player.Hand[pick].Id + "）";
                break;
            }

            if (player.IsCalled)
            {
                postCallDiscardOk = true;
            }
        }

        clock.Stop();
        var final = runtime.Snapshot;
        bool completed = runtime.Phase == MatchPhase.MatchOver;
        bool inTime = clock.Elapsed < budget;

        Console.WriteLine($"smoke-call-flow seed={seed} mode={mode}");
        Console.WriteLine($"  报听时机={(declared ? "已宣言" : "未出现")} 人类动作={actions} 用时={clock.Elapsed.TotalSeconds:F2}s");
        Check("点报听未出牌不卡", armOk, "待弃态只改 UI，不发命令；取消后仍可正常出牌");
        Check("报听并弃牌", declared, "人类 isCalled=true");
        Check("报听后牌局推进", advanceOk, stuck.Length > 0 ? stuck : advanceDetail);
        Check("报听按钮三态", !callButtonChecked || callButtonOk,
            callButtonChecked ? "报听后为「已报听」灰显" : "本局已结束，跳过按钮检查");
        Check("报听后仍能出牌", postCallTurns == 0 || postCallDiscardOk,
            postCallTurns == 0 ? "本局未再轮到出牌" : "锁手允许的弃牌被接受");
        Check("整场在时限内终局", completed && inTime,
            $"阶段={final.Phase} 用时={clock.Elapsed.TotalSeconds:F2}s / 上限 {budget.TotalSeconds:F0}s");

        if (stuck.Length > 0)
        {
            Console.WriteLine("  卡住: " + stuck);
        }

        Console.WriteLine(failures == 0 ? "smoke-call-flow: all checks passed" : "smoke-call-flow: " + failures + " failed");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>smoke 用：找一张「打出后仍然听牌」的手牌下标（找不到 -1）。</summary>
    private static int TenpaiDiscardIndex(MahjongEngine engine, Player player)
    {
        var seen = new HashSet<GameTile>();
        for (int i = 0; i < player.Hand.Count; i++)
        {
            if (!seen.Add(player.Hand[i]))
            {
                continue;
            }

            var after = new List<GameTile>(player.Hand);
            after.RemoveAt(i);
            if (after.Count + player.MeldTiles() != 13 + player.KongCount())
            {
                continue;
            }

            ulong mask = engine.WaitMaskFor(
                after, player.Melds, player.AnkanCount, player.MinkanCount, player.SeatWind, player.FirstTurn);
            if (mask != 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>smoke 用：按向听数挑一张最有利的弃牌（同分取手牌靠前者）。</summary>
    private static int BestShantenDiscardIndex(Player player)
    {
        var seen = new HashSet<GameTile>();
        int best = -1;
        int bestShanten = int.MaxValue;
        for (int i = 0; i < player.Hand.Count; i++)
        {
            if (!seen.Add(player.Hand[i]))
            {
                continue;
            }

            var after = new List<GameTile>(player.Hand);
            after.RemoveAt(i);
            int shanten = Shanten.Calculate(after, player.Melds.Count, player.KongCount()).Shanten;
            if (shanten < bestShanten)
            {
                bestShanten = shanten;
                best = i;
            }
        }

        return best;
    }

    /// <summary>smoke 用：报听后合法的一张弃牌（摸切优先，找不到 -1）。</summary>
    private static int LegalCalledDiscardIndex(MahjongEngine engine, Player player)
    {
        if (player.LastDraw is GameTile drawn)
        {
            int index = player.Hand.IndexOf(drawn);
            if (index >= 0 && engine.CallAllowsDiscard(player, player.Hand[index], out _))
            {
                return index;
            }
        }

        for (int i = 0; i < player.Hand.Count; i++)
        {
            if (engine.CallAllowsDiscard(player, player.Hand[i], out _))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// <c>--smoke-layout-editor</c>：区域可枚举 → F2 进入 → 拖动 10,10 → S 保存 →
    /// 重新加载断言生效 → R 两次恢复默认断言回原值；另测非法 JSON / 缺字段 / 启动不崩。
    /// </summary>
    private static int SmokeLayoutEditor()
    {
        TryUseUtf8Console();
        string dir = Path.Combine(Path.GetTempPath(), "tiisu-layout-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        int pass = 0;
        int total = 0;
        void Check(string label, bool ok)
        {
            total++;
            if (ok)
            {
                pass++;
            }

            Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {label}");
        }

        Console.WriteLine("smoke-layout-editor");

        try
        {
            var layout = NewLayout();
            var editor = new LayoutEditor(layout, dir);

            string[] required =
            {
                "hand-self", "meld-self", "river-self", "plate",
                "hud", "action-bar", "settlement", "thumbnail",
            };
            Check($"区域可枚举（{layout.Regions.Count} 个，≥16）", layout.Regions.Count >= 16);
            Check("覆盖必需区域", required.All(id => layout.Regions.Any(r => r.Id == id)));

            int index = 0;
            for (int i = 0; i < layout.Regions.Count; i++)
            {
                if (layout.Regions[i].Id == "hand-self")
                {
                    index = i;
                }
            }

            var before = layout.Regions[index].Rect;
            Console.WriteLine($"  默认 hand-self = {before.X:0},{before.Y:0} {before.W:0}×{before.H:0}");

            editor.Enter();
            Check("进入编辑模式", editor.Active);
            editor.Select(index);
            editor.PointerDown(before.CenterX, before.CenterY);
            editor.PointerMove(before.CenterX + 10f, before.CenterY + 10f);
            editor.PointerUp();

            var moved = layout.Regions[index].Rect;
            Console.WriteLine($"  拖动后 hand-self = {moved.X:0},{moved.Y:0} {moved.W:0}×{moved.H:0}");
            Check("拖拽 +10,+10 生效",
                Math.Abs(moved.X - before.X - 10f) < 0.01f
                && Math.Abs(moved.Y - before.Y - 10f) < 0.01f
                && Math.Abs(moved.W - before.W) < 0.01f
                && Math.Abs(moved.H - before.H) < 0.01f);

            editor.Save();
            string file = CustomLayoutStore.PathFor(dir);
            Check("S 保存文件", File.Exists(file));
            string savedJson = File.ReadAllText(file);
            Check("JSON 含 version 与区域 x/y/w/h",
                savedJson.Contains("\"version\": 1", StringComparison.Ordinal)
                && savedJson.Contains("\"hand-self\"", StringComparison.Ordinal)
                && savedJson.Contains("\"x\": 267", StringComparison.Ordinal));

            var reloaded = NewLayout();
            bool applied = CustomLayoutStore.Load(reloaded, dir);
            var afterReload = reloaded.Regions[index].Rect;
            Console.WriteLine($"  重新加载 hand-self = {afterReload.X:0},{afterReload.Y:0}");
            Check("重新加载生效",
                applied
                && Math.Abs(afterReload.X - moved.X) < 0.01f
                && Math.Abs(afterReload.Y - moved.Y) < 0.01f);

            var editor2 = new LayoutEditor(reloaded, dir);
            editor2.Enter();
            editor2.Select(index);
            editor2.RestoreDefault();
            Check("R 第一次只提醒（文件保留）", File.Exists(file) && editor2.Status.Contains("R", StringComparison.Ordinal));
            editor2.RestoreDefault();
            Check("R 第二次删除自定义文件", !File.Exists(file));

            var afterReset = reloaded.Regions[index].Rect;
            Console.WriteLine($"  恢复默认 hand-self = {afterReset.X:0},{afterReset.Y:0}");
            Check("恢复默认回原值",
                Math.Abs(afterReset.X - before.X) < 0.01f
                && Math.Abs(afterReset.Y - before.Y) < 0.01f
                && Math.Abs(afterReset.W - before.W) < 0.01f
                && Math.Abs(afterReset.H - before.H) < 0.01f);

            // 破坏性测试 1：非法 JSON。
            File.WriteAllText(file, "{ 这不是 json ,,,", new System.Text.UTF8Encoding(false));
            var broken = NewLayout();
            bool brokenApplied = CustomLayoutStore.Load(broken, dir);
            var brokenRect = broken.Regions[index].Rect;
            Check("非法 JSON：不崩、不生效",
                !brokenApplied
                && Math.Abs(brokenRect.X - before.X) < 0.01f
                && Math.Abs(brokenRect.Y - before.Y) < 0.01f);

            // 破坏性测试 2：字段缺失。
            File.WriteAllText(file,
                "{\"version\":1,\"regions\":{\"hand-self\":{\"x\":12},\"no-such\":{\"x\":1,\"y\":2,\"w\":3,\"h\":4}}}",
                new System.Text.UTF8Encoding(false));
            var partial = NewLayout();
            bool partialApplied = CustomLayoutStore.Load(partial, dir);
            var partialRect = partial.Regions[index].Rect;
            Check("字段缺失 / 未知区域：忽略并继续用默认",
                !partialApplied && Math.Abs(partialRect.X - before.X) < 0.01f);

            // 破坏性测试 3：真实启动路径（默认目录写坏文件后构造 TableScreen）。
            string realPath = CustomLayoutStore.PathFor();
            byte[]? backup = File.Exists(realPath) ? File.ReadAllBytes(realPath) : null;
            bool startupOk;
            try
            {
                Directory.CreateDirectory(Settings.DefaultDirectory);
                File.WriteAllText(realPath, "{ \"version\": 1, \"regions\": ]", new System.Text.UTF8Encoding(false));
                var screen = new TableScreen((Func<TableViewModel>)(() => TableDemoModel.Create(1)), null, null, null);
                startupOk = Math.Abs(screen.CanvasWidth - 1200f) < 0.01f
                            && Math.Abs(screen.CanvasHeight - 1200f) < 0.01f;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  启动异常: " + ex);
                startupOk = false;
            }
            finally
            {
                if (backup is not null)
                {
                    File.WriteAllBytes(realPath, backup);
                }
                else if (File.Exists(realPath))
                {
                    File.Delete(realPath);
                }
            }

            Check("启动时布局文件损坏 → 正常启动", startupOk);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
                // 临时目录清理失败不影响结果。
            }
        }

        Console.WriteLine($"smoke-layout-editor: {pass}/{total} passed");
        return pass == total ? 0 : 1;
    }

    /// <summary><c>--render-layout-edit &lt;png&gt;</c>：编辑态截图（含区域框、名称、坐标、选中高亮）。</summary>
    private static void RenderLayoutEdit(string path)
    {
        using var atlas = new TileAtlas(AssetsDir);
        var screen = new TableScreen((Func<TableViewModel>)(() => TableDemoModel.Create(1)), null, null, atlas);
        var editor = screen.Editor;
        editor.Enter();
        for (int i = 0; i < editor.Regions.Count; i++)
        {
            if (editor.Regions[i].Id == "hand-self")
            {
                editor.Select(i);
            }
        }

        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var info = new SKImageInfo((int)screen.CanvasWidth, (int)screen.CanvasHeight);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(new SKColor(20, 30, 24));
        using (var ctx = new SkiaCanvas(surface.Canvas, atlas))
        {
            screen.Draw(ctx, 0.6);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        Console.WriteLine($"rendered layout-edit -> {path}  regions={editor.Regions.Count} selected={editor.Selected + 1}");
    }
}