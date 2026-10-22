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
using Tiisu.Engine.Random;
using UiScreen = Tiisu.App.Ui.Screen;

namespace Tiisu.App;

internal static class Program
{
    private static readonly string AssetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");

    [STAThread]
    private static void Main(string[] args)
    {
        // 无窗口自检（不建窗口、不初始化 WinForms）
        switch (args.Length >= 1 ? args[0] : string.Empty)
        {
            case "--smoke-run":
                Environment.ExitCode = SmokeRun(args);
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

        // 帧率基准：--bench <frames>
        if (args.Length >= 1 && args[0] == "--bench")
        {
            int frames = args.Length >= 2 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 300;
            Bench(frames);
            return;
        }

        ApplicationConfiguration.Initialize();
        var atlas = new TileAtlas(AssetsDir);   // 所有权交给 MainForm
        Application.Run(new MainForm(atlas));
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
            saveStore: null, stats: new StatsStore(), roundLog: log)
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
    /// <c>--smoke-save [--record]</c>：跑到第 3 局结束（handsPlayed = 2）后写存档并退出，
    /// 供 <c>--smoke-resume</c> 验证续局。
    /// </summary>
    private static int SmokeSave(string[] args)
    {
        TryUseUtf8Console();
        var settings = LoadSmokeSettings(args);
        var save = new SaveStore();
        var runtime = new MatchRuntime(settings, new Mulberry32Random(1), _ => { },
            save, new StatsStore(), new RoundLogWriter { Enabled = settings.RecordTiles })
        {
            AutoPilot = true,
        };

        runtime.StartMatch(4);
        int guard = 0;
        while (runtime.Phase != MatchPhase.MatchOver
               && !(runtime.Phase == MatchPhase.RoundEnded && runtime.Engine.HandsPlayed >= 2)
               && ++guard < 256)
        {
            runtime.NextRound();
        }

        if (runtime.Phase != MatchPhase.RoundEnded)
        {
            Console.Error.WriteLine($"没有停在可存档的局末（phase={runtime.Phase}）");
            return 1;
        }

        var state = runtime.CaptureSave(inProgress: true);
        save.SaveNow(state);
        save.Flush();

        Console.WriteLine("smoke-save");
        Console.WriteLine($"  已打 {runtime.Rounds} 局，存档: {save.StatePath}");
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "  handNumber={0} handsPlayed={1} scores=[{2}] dealer={3} honba={4} roundWindIdx={5} dealerCount={6} inProgress={7}",
            state.HandNumber,
            state.HandsPlayed,
            string.Join(",", state.Scores),
            state.DealerIndex,
            state.Honba,
            state.RoundWindIdx,
            state.DealerCount,
            state.InProgress));
        save.Dispose();
        return 0;
    }

    /// <summary><c>--smoke-resume [--record]</c>：读上一份存档，从下一局继续打完整场。</summary>
    private static int SmokeResume(string[] args)
    {
        TryUseUtf8Console();
        var settings = LoadSmokeSettings(args);
        var save = new SaveStore();
        var state = save.Load();
        if (state is null || !state.InProgress)
        {
            Console.Error.WriteLine("没有可恢复的进行中存档（先跑 --smoke-save）");
            return 2;
        }

        var runtime = new MatchRuntime(settings, new Mulberry32Random(2), _ => { },
            save, new StatsStore(), new RoundLogWriter { Enabled = settings.RecordTiles })
        {
            AutoPilot = true,
        };

        Console.WriteLine("smoke-resume");
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "  读档: handNumber={0} handsPlayed={1} scores=[{2}] dealer={3} honba={4} roundWindIdx={5} dealerCount={6}",
            state.HandNumber,
            state.HandsPlayed,
            string.Join(",", state.Scores),
            state.DealerIndex,
            state.Honba,
            state.RoundWindIdx,
            state.DealerCount));

        runtime.Resume(state);
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
            Console.Error.WriteLine($"续局未终局（phase={runtime.Phase}）");
            return 1;
        }

        PrintSmoke(runtime);
        save.Dispose();
        return 0;
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

        // 与 TableScreen 内部同一份布局参数（1200×1200 + Yoga）。
        var layout = new TableLayoutSpec { CanvasW = 1200f, CanvasH = 1200f };
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
    /// </summary>
    private static int RenderRoundEndToPng(string[] args)
    {
        TryUseUtf8Console();
        string path = args[1];
        uint seed = 1;
        if (args.Length >= 3 && !uint.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
        {
            seed = 1;
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

    /// <summary>1200×1200 的新布局（Yoga 计算）。</summary>
    private static TableLayoutSpec NewLayout()
    {
        var layout = new TableLayoutSpec { CanvasW = 1200f, CanvasH = 1200f };
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

    /// <summary><c>--render-actions &lt;png&gt;</c>：动作按钮条 + 听牌提示出图（演示快照）。</summary>
    private static void RenderActionsToPng(string path)
    {
        using var atlas = new TileAtlas(AssetsDir);
        var vm = TableDemoModel.CreateWithActions();
        var layout = new TableLayoutSpec { CanvasW = 1200f, CanvasH = 1200f };
        YogaTableLayout.Apply(layout);

        var info = new SKImageInfo((int)layout.CanvasW, (int)layout.CanvasH);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(20, 30, 24));

        using (var ctx = new SkiaCanvas(canvas, atlas))
        {
            new TableSceneSkia().Draw(ctx, vm, layout, hoverHand: -1);
            var bar = new ActionBar();
            bar.Build(vm, layout);
            bar.ToggleTenpai();
            bar.DrawTenpai(ctx, vm, layout);
            bar.Draw(ctx);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        Console.WriteLine($"rendered actions -> {path}  buttons={new TableLayoutSpec().CanvasW:0}");
    }
}