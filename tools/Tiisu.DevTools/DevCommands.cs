using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Tiisu.Localization;
using SkiaSharp;
using Tiisu.App.Assets;
using Tiisu.App.Rendering;
using Tiisu.App.Runtime;
using Tiisu.App.Ui;
using Tiisu.App.Table;
using Tiisu.App.Svg;
using Tiisu.Engine;
using Tiisu.Core;
using Tiisu.Engine.Ai;
using Tiisu.Engine.Random;
using GameTile = Tiisu.Core.Tile;
using GameSuit = Tiisu.Core.Suit;
using Color = Tiisu.App.Rendering.Color;
using UiScreen = Tiisu.App.Ui.Screen;

namespace Tiisu.DevTools;

internal static class DevCommands
{

    /// <summary>
    /// <c>--smoke-run &lt;seed&gt; [mode] [--record]</c>：无窗口跑一整场（人类 AutoPilot：摸什么打什么 / 能和则和），
    /// 打印局数、和牌/流局、四家分数、事件流 SHA256。
    /// </summary>
    /// <summary>
    /// <c>--audit-games [count] [mode] [startSeed]</c>：批量跑完整对局，收集结算自检问题。
    /// 用 Simple AI 提速；每局都走 MatchRuntime 的完整命令链路（人类座位 AutoPilot）。
    /// </summary>
    /// <summary>
    /// <c>--export-selfplay &lt;out.jsonl&gt; [--games N] [--start-seed K] [--mode 4]</c>：
    /// 自博弈弃牌样本；特征为 SelfPlayFeatures v3（除牌山外全部场况）。
    /// </summary>
    private static int ExportSelfPlay(string[] args)
    {
        string outPath = args.Length > 1 ? args[1] : "dist/selfplay.jsonl";
        int games = 100;
        int gameSeed = 1;
        int mode = 4;
        int jobs = 1;
        int sampleEvery = 1;
        double explore = 0;
        string policy = "argmax";
        double temperature = 1.0;
        int topK = 0;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--games" && i + 1 < args.Length && int.TryParse(args[i + 1], out int g)) games = Math.Max(1, g);
            if (args[i] == "--start-seed" && i + 1 < args.Length && int.TryParse(args[i + 1], out int sd)) gameSeed = sd;
            if (args[i] == "--mode" && i + 1 < args.Length && int.TryParse(args[i + 1], out int m)) mode = Math.Max(1, m);
            if (args[i] == "--jobs" && i + 1 < args.Length && int.TryParse(args[i + 1], out int j)) jobs = Math.Clamp(j, 1, Environment.ProcessorCount);
            if (args[i] == "--sample-every" && i + 1 < args.Length && int.TryParse(args[i + 1], out int se)) sampleEvery = Math.Max(1, se);
            if (args[i] == "--explore" && i + 1 < args.Length && double.TryParse(args[i + 1], out double ex)) explore = Math.Clamp(ex, 0, 0.9);
            if (args[i] == "--policy" && i + 1 < args.Length) policy = args[++i].ToLowerInvariant();
            if (args[i] == "--temperature" && i + 1 < args.Length && double.TryParse(args[i + 1], out double temp)) temperature = Math.Clamp(temp, 0.05, 5.0);
            if (args[i] == "--top-k" && i + 1 < args.Length && int.TryParse(args[i + 1], out int k)) topK = Math.Clamp(k, 0, 14);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        int written = 0;
        object writeLock = new();
        using var writer = new StreamWriter(outPath, false, new System.Text.UTF8Encoding(false));
        Parallel.For(0, games, new ParallelOptions { MaxDegreeOfParallelism = jobs }, g =>
        {
            var samples = new List<SelfPlaySample>();
            var opts = new Tiisu.Engine.SelfPlay.SelfPlayOptions
            {
                ModeTarget = mode,
                MaxHands = 512,
                ExploreRate = explore,
                DiscardObserver = (engine, player, index) =>
                {
                    var p = engine.Players[player];
                    int before = Tiisu.Engine.Ai.Shanten.Calculate(p.Hand, p.Melds).Shanten;
                    var after = p.Hand.ToList();
                    if (index >= 0 && index < after.Count) after.RemoveAt(index);
                    int afterShanten = Tiisu.Engine.Ai.Shanten.Calculate(after, p.Melds).Shanten;
                    samples.Add(new SelfPlaySample
                    {
                        Features = Tiisu.Engine.SelfPlay.SelfPlayFeatures.Build(engine, player),
                        Action = p.Hand[index].Index,
                        Player = player,
                        Round = engine.HandNumber,
                        Step = samples.Count,
                        Game = g,
                        ShantenBefore = before,
                        ShantenAfter = afterShanten,
                    });
                },
                DiscardPolicy = policy == "neural"
                    ? (engine, player, rng) => Tiisu.Engine.Ai.NeuralPolicyAi.SampleDiscard(
                        Tiisu.Engine.SelfPlay.SelfPlayDriver.BuildNeuralContext(engine, player),
                        null,
                        temperature,
                        topK,
                        rng)
                    : null,
            };
            var result = Tiisu.Engine.SelfPlay.SelfPlayDriver.Run((uint)(gameSeed + g), opts);
            int initial = mode >= 16 ? 77000 : 49000;
            var melds = new int[4];
            var callWins = new int[4];
            var dealsIn = new int[4];
            var misfires = new int[4];
            foreach (var ev in result.Events)
            {
                if (ev.Player is < 0 or > 3) continue;
                if (ev.Type == "meld") melds[ev.Player]++;
                if (ev.Type == "call" && ev.Detail is not null && ev.Detail.Contains("misfire", StringComparison.OrdinalIgnoreCase))
                    misfires[ev.Player]++;
                if (ev.Type == "win")
                {
                    if (ev.Detail is not null && ev.Detail.Contains("ron:from=", StringComparison.Ordinal))
                    {
                        int from = ev.Detail.IndexOf("from=", StringComparison.Ordinal);
                        if (from >= 0 && from + 5 < ev.Detail.Length && int.TryParse(ev.Detail.Substring(from + 5, 1), out int loser))
                            dealsIn[loser]++;
                    }

                    if (melds[ev.Player] > 0) callWins[ev.Player]++;
                }
            }

            var order = Enumerable.Range(0, 4).OrderByDescending(i => result.FinalScores[i]).ToArray();
            var rank = new int[4];
            for (int i = 0; i < 4; i++) rank[order[i]] = i + 1;
            lock (writeLock)
            {
                foreach (var sample in samples)
                {
                    if (sample.Step % sampleEvery != 0) continue;
                    sample.FinalScore = result.FinalScores[sample.Player];
                    sample.InitialScore = initial;
                    sample.FinalRank = rank[sample.Player];
                    sample.Lead = rank[sample.Player] == 1
                        ? result.FinalScores[sample.Player] - result.FinalScores.Where((_, i) => i != sample.Player).Max()
                        : 0;
                    sample.Melds = melds[sample.Player];
                    sample.CallWins = callWins[sample.Player];
                    sample.DealsIn = dealsIn[sample.Player];
                    sample.Misfires = misfires[sample.Player];
                    writer.WriteLine(System.Text.Json.JsonSerializer.Serialize(sample));
                    written++;
                }
            }
        });

        Console.WriteLine($"selfplay: {games} games, {written} samples -> {outPath} (jobs={jobs}, sampleEvery={sampleEvery}, explore={explore})");
        return 0;
    }

    private sealed class SelfPlaySample
    {
        public int V { get; set; } = Tiisu.Engine.SelfPlay.SelfPlayFeatures.Version;
        public float[] Features { get; set; } = Array.Empty<float>();
        public int Action { get; set; }
        public int Player { get; set; }
        public int Round { get; set; }
        public int ShantenBefore { get; set; }
        public int ShantenAfter { get; set; }
        public int Game { get; set; }
        public int Step { get; set; }
        public int FinalRank { get; set; }
        public int Lead { get; set; }
        public int FinalScore { get; set; }
        public int InitialScore { get; set; }
        public int Melds { get; set; }
        public int CallWins { get; set; }
        public int DealsIn { get; set; }
        public int Misfires { get; set; }
    }

    /// <summary>
    /// <c>--check-publish [publishDir]</c>：发布前自检——许可全文、字体、素材哈希、
    /// 禁止项（语音/测试依赖/FFmpeg/espeak）是否干净。
    /// </summary>
    /// <summary>
    /// <c>--check-font</c>：检查字体子集是否覆盖当前版本代号、UI 常用字与 docs。
    /// 对应线上曾出现的「北斗」缺字；有缺失返回 1。
    /// </summary>
    /// <summary>
    /// <c>--audit-ui-strings [root] [csv]</c>：盘点 App 里的中文 UI 字符串字面量，
    /// 为英文本地化评估规模；默认输出到 artifacts/localization/ui-strings.csv。
    /// </summary>
    /// <summary>
    /// <c>--check-steam [root]</c>：Steam 接入就绪检查。没有 AppID 时只报告阻塞项，不报错退出。
    /// </summary>
    /// <summary>
    /// <c>--check-i18n [root]</c>：检查 <c>UiText.Tr("中文键")</c> 与 <c>L.T("键")</c> 在 zh/en/ja 三份
    /// <c>NET/lang</c> 语言包里是否齐全；缺失即失败（退出码 1）。
    /// </summary>
    private static int CheckI18n(string[] args)
    {
        Tiisu.App.Program.TryUseUtf8Console();
        string root = args.Length >= 2 && Directory.Exists(args[1])
            ? Path.GetFullPath(args[1])
            : Directory.GetCurrentDirectory();
        string langDir = Path.Combine(root, "lang");
        if (!Directory.Exists(langDir))
        {
            Console.WriteLine(L.T("tools.cli.devtools.CheckI18n.1") + langDir);
            return 1;
        }

        var maps = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (string lang in new[] { "zh-CN", "en-US", "ja-JP" })
        {
            string path = Path.Combine(langDir, lang + ".json");
            if (!File.Exists(path))
            {
                Console.WriteLine(L.T("tools.cli.devtools.CheckI18n.1") + path);
                return 1;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var keys = new HashSet<string>(StringComparer.Ordinal);
            FlattenLangKeys(doc.RootElement, string.Empty, keys);
            maps[lang] = keys;
        }

        var uiTextPattern = new Regex("UiText\\.Tr\\(\"((?:[^\"\\\\]|\\\\.)*)\"\\)");
        var lKeyPattern = new Regex("\\bL\\.T\\(\"([A-Za-z_][A-Za-z0-9_.]*)\"");
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        int checkedKeys = 0;
        foreach (string project in new[] { "src", "tools" })
        {
            string dir = Path.Combine(root, project);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (IsBuildArtifact(file))
                {
                    continue;
                }

                foreach (string line in File.ReadLines(file))
                {
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal)
                        || trimmed.StartsWith("*", StringComparison.Ordinal)
                        || trimmed.StartsWith("/*", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (Match match in uiTextPattern.Matches(line))
                    {
                        string key = Regex.Unescape(match.Groups[1].Value);
                        if (!key.Any(c => c >= 0x4e00 && c <= 0x9fff))
                        {
                            continue;
                        }

                        checkedKeys++;
                        AddMissing(missing, maps, key);
                    }

                    foreach (Match match in lKeyPattern.Matches(line))
                    {
                        checkedKeys++;
                        AddMissing(missing, maps, match.Groups[1].Value);
                    }
                }
            }
        }

        Console.WriteLine(L.T("tools.cli.devtools.CheckI18n.2", ("p1", missing.Count)));
        foreach (string key in missing.Take(40))
        {
            Console.WriteLine(L.T("tools.cli.devtools.CheckI18n.3") + key);
        }

        return missing.Count == 0 ? 0 : 1;
    }

    private static void FlattenLangKeys(JsonElement element, string prefix, HashSet<string> keys)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                FlattenLangKeys(property.Value, prefix.Length == 0 ? property.Name : prefix + "." + property.Name, keys);
            }

            return;
        }

        if (element.ValueKind == JsonValueKind.String && prefix.Length > 0)
        {
            keys.Add(prefix);
        }
    }

    private static void AddMissing(SortedSet<string> missing, Dictionary<string, HashSet<string>> maps, string key)
    {
        // 动态拼接的键（如 "tools.devtools.category." + id）只校验到前缀，跳过。
        if (key.EndsWith(".", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var (lang, translated) in maps)
        {
            if (!translated.Contains(key))
            {
                missing.Add(lang + ": " + key);
            }
        }
    }

    private static bool IsBuildArtifact(string path) =>
        path.Contains(Path.Combine("bin", string.Empty), StringComparison.Ordinal)
        || path.Contains(Path.Combine("obj", string.Empty), StringComparison.Ordinal)
        || path.Contains(Path.Combine("Generated", string.Empty), StringComparison.Ordinal);

    private static int CheckSteam(string[] args)
    {
        Tiisu.App.Program.TryUseUtf8Console();
        string root = args.Length >= 2 && Directory.Exists(args[1])
            ? Path.GetFullPath(args[1])
            : Directory.GetCurrentDirectory();
        string docs = Path.Combine(root, "docs");
        string mapPath = Path.Combine(docs, "steam", "achievements.json");
        var blockers = new List<string>();
        var ready = new List<string>();

        if (File.Exists(Path.Combine(docs, "STEAM_INTEGRATION.md"))) ready.Add(L.T("tools.cli.devtools.CheckSteam.1"));
        if (File.Exists(Path.Combine(docs, "STEAM_ACHIEVEMENTS.md"))) ready.Add(L.T("tools.cli.devtools.CheckSteam.2"));
        if (File.Exists(Path.Combine(docs, "STEAM_RELEASE_CHECKLIST.md"))) ready.Add(L.T("tools.cli.devtools.CheckSteam.3"));

        int mapCount = 0;
        if (File.Exists(mapPath))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(mapPath))?["achievements"] is JsonArray items)
                {
                    mapCount = items.Count;
                }
            }
            catch (JsonException)
            {
                blockers.Add(L.T("tools.cli.devtools.CheckSteam.4"));
            }
        }

        if (mapCount == 10) ready.Add(L.T("tools.cli.devtools.CheckSteam.5")); else blockers.Add(L.T("tools.cli.devtools.CheckSteam.6"));

        bool hasAppId = File.Exists(Path.Combine(root, "steam_appid.txt"))
                        || File.Exists(Path.Combine(root, "dist", "publish", "steam_appid.txt"));
        bool hasNative = File.Exists(Path.Combine(root, "dist", "publish", "steam_api64.dll"));
        if (hasAppId) ready.Add("steam_appid.txt"); else blockers.Add(L.T("tools.cli.devtools.CheckSteam.7"));
        if (hasNative) ready.Add("steam_api64.dll"); else blockers.Add("steam_api64.dll / Steamworks SDK");

        if (File.Exists(Path.Combine(root, "dist", "steam", "depot-manifest.json"))) ready.Add("Steam depot"); else blockers.Add("Steam depot manifest");
        if (File.Exists(Path.Combine(root, "dist", "steam", "app_build.vdf.template"))) ready.Add(L.T("tools.cli.devtools.CheckSteam.8"));

        Console.WriteLine(L.T("tools.cli.devtools.CheckSteam.9"));
        Console.WriteLine(L.T("tools.cli.devtools.CheckSteam.10") + string.Join(" / ", ready));
        Console.WriteLine(L.T("tools.cli.devtools.CheckSteam.11") + string.Join(" / ", blockers));
        Console.WriteLine(hasAppId && hasNative ? L.T("tools.cli.devtools.CheckSteam.12") : L.T("tools.cli.devtools.CheckSteam.13"));
        return 0;
    }


    private static int AuditUiStrings(string[] args)
    {
        Tiisu.App.Program.TryUseUtf8Console();
        string root = args.Length >= 2 && Directory.Exists(args[1])
            ? Path.GetFullPath(args[1])
            : Directory.GetCurrentDirectory();
        string[] projects =
        {
            Path.Combine(root, "src", "Tiisu.App"),
            Path.Combine(root, "tools", "Tiisu.DevTools"),
            Path.Combine(root, "tools", "Tiisu.Tools"),
            Path.Combine(root, "tools", "Tiisu.AudioGen"),
            Path.Combine(root, "src", "Tiisu.Updater"),
        };
        if (!projects.Any(Directory.Exists))
        {
            Console.WriteLine(L.T("tools.cli.devtools.AuditUiStrings.1") + root);
            return 1;
        }

        string csv = args.Length >= 3 ? args[2] : Path.Combine(root, "artifacts", "localization", "ui-strings.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(csv))!);
        var pattern = new Regex("\"[^\"\r\n]*[\u4e00-\u9fff][^\"\r\n]*\"");
        var rows = new List<(string File, int Count)>();
        int total = 0;
        foreach (string project in projects.Where(Directory.Exists))
        {
            foreach (string file in Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
            {
                if (IsBuildArtifact(file))
                {
                    continue;
                }

                int count = pattern.Matches(File.ReadAllText(file)).Count;
                if (count == 0)
                {
                    continue;
                }

                rows.Add((Path.GetRelativePath(root, file), count));
                total += count;
            }
        }

        rows.Sort((a, b) => b.Count.CompareTo(a.Count));
        var csvLines = new List<string> { "file,count" };
        csvLines.AddRange(rows.Select(r => r.File.Replace(',', '_') + "," + r.Count));
        File.WriteAllText(csv, string.Join("\n", csvLines) + "\n", new System.Text.UTF8Encoding(false));

        Console.WriteLine(L.T("tools.cli.devtools.AuditUiStrings.2", ("p1", total), ("p2", rows.Count)));
        foreach (var row in rows.Take(15))
        {
            Console.WriteLine($"  {row.Count,4}  {row.File}");
        }

        Console.WriteLine("  CSV: " + csv);
        return 0;
    }


    private static int CheckFont(string[] args)
    {
        Tiisu.App.Program.TryUseUtf8Console();
        string root = args.Length >= 2 && Directory.Exists(args[1])
            ? Path.GetFullPath(args[1])
            : Directory.GetCurrentDirectory();
        string fontPath = Path.Combine(root, "src", "Tiisu.App", "Assets", "fonts", "TiisuRoundedSC-subset.ttf");
        if (!File.Exists(fontPath))
        {
            fontPath = Path.Combine(Tiisu.App.Program.AssetsDir, "fonts", "TiisuRoundedSC-subset.ttf");
        }

        if (!File.Exists(fontPath))
        {
            Console.WriteLine(L.T("tools.cli.devtools.CheckFont.1") + fontPath);
            return 1;
        }

        using var typeface = SKTypeface.FromFile(fontPath);
        if (typeface is null)
        {
            Console.WriteLine(L.T("tools.cli.devtools.CheckFont.2") + fontPath);
            return 1;
        }

        using var font = new SKFont(typeface);
        var chars = new HashSet<char>("北斗开源信息许可参考新手引导设置下一局和牌荣和报听战绩统计");

        string buildInfo = Path.Combine(root, "src", "Tiisu.App", "Generated", "BuildInfo.g.cs");
        if (File.Exists(buildInfo))
        {
            string buildText = File.ReadAllText(buildInfo);
            const string marker = "Version = \"";
            int start = buildText.IndexOf(marker, StringComparison.Ordinal);
            if (start >= 0)
            {
                int valueStart = start + marker.Length;
                int valueEnd = buildText.IndexOf('"', valueStart);
                if (valueEnd > valueStart)
                {
                    foreach (char ch in buildText[valueStart..valueEnd])
                    {
                        chars.Add(ch);
                    }
                }
            }
        }

        string docs = Path.Combine(root, "docs");
        if (Directory.Exists(docs))
        {
            foreach (string file in Directory.EnumerateFiles(docs, "*.md", SearchOption.AllDirectories))
            {
                foreach (char ch in File.ReadAllText(file))
                {
                    chars.Add(ch);
                }
            }
        }

        foreach (string extra in new[] { Path.Combine(root, "README.md"), Path.Combine(root, "THIRD-PARTY-NOTICES.md") })
        {
            if (File.Exists(extra))
            {
                foreach (char ch in File.ReadAllText(extra))
                {
                    chars.Add(ch);
                }
            }
        }

        // 圆体子集没有 emoji；Markdown 渲染器会把它们替换成 [OK]/[X]/[!]。
        chars.RemoveWhere(ch => ch is '✅' or '❌' or '⚠' or '\uFE0F');
        var missing = chars.Where(ch => !char.IsWhiteSpace(ch) && !font.ContainsGlyph(ch))
            .Distinct()
            .OrderBy(ch => ch)
            .ToArray();
        Console.WriteLine($"check-font: {fontPath}");
        Console.WriteLine(L.T("tools.cli.devtools.CheckFont.3", ("p1", chars.Count), ("p2", missing.Length)));
        if (missing.Length > 0)
        {
            Console.WriteLine(L.T("tools.cli.devtools.CheckFont.4") + string.Join(" ", missing));
            return 1;
        }

        Console.WriteLine("  PASS");
        return 0;
    }


    private static int CheckPublish(string[] args)
    {
        string dir = args.Length > 1 ? args[1] : Path.Combine(Directory.GetCurrentDirectory(), "dist", "publish");
        var errors = new List<string>();
        var notes = new List<string>();

        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine(L.T("tools.cli.devtools.CheckPublish.1") + dir);
            return 2;
        }

        string[] required =
        {
            "Assets/licenses/MIT.txt",
            "Assets/licenses/Apache-2.0.txt",
            "Assets/licenses/OFL-1.1.txt",
            "Assets/licenses/CC-BY-4.0.txt",
            "Assets/licenses/REFERENCES.txt",
            "Assets/docs/REFERENCES.md",
            "Assets/docs/ASSET-PROVENANCE.md",
            "Assets/docs/ML-PROVENANCE.md",
            "Assets/docs/SELFPLAY_TRAINING.md",
            "Assets/docs/RELEASE_COMPLIANCE.md",
            "Assets/docs/THIRD-PARTY-NOTICES.md",
            "Assets/licenses/TILES-LICENSE.txt",
            "Assets/licenses/Zlib.txt",
            "Assets/fonts/LICENSE-WenYuan.md",
            "Assets/fonts/TiisuRoundedSC-subset.ttf",
            "Assets/tiles.png",
            "Assets/ml/policy_weights_v4.json",
        };
        foreach (string rel in required)
        {
            if (!File.Exists(Path.Combine(dir, rel)))
            {
                errors.Add(L.T("tools.cli.devtools.CheckPublish.2") + rel);
            }
        }

        if (Directory.Exists(Path.Combine(dir, "Assets", "voices")))
        {
            errors.Add(L.T("tools.cli.devtools.CheckPublish.3"));
        }

        string[] forbidden = { "xunit", "testhost", "coverlet", "Microsoft.TestPlatform", "ffmpeg", "espeak" };
        foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file).ToLowerInvariant();
            foreach (string bad in forbidden)
            {
                if (name.Contains(bad, StringComparison.Ordinal))
                {
                    errors.Add(L.T("tools.cli.devtools.CheckPublish.4") + Path.GetRelativePath(dir, file));
                    break;
                }
            }
        }

        string manifestPath = Path.Combine(Directory.GetCurrentDirectory(), "ASSET-HASHES.md");
        if (!File.Exists(manifestPath))
        {
            notes.Add(L.T("tools.cli.devtools.CheckPublish.5"));
        }
        else
        {
            int checkedHashes = 0;
            foreach (string raw in File.ReadAllLines(manifestPath))
            {
                string line = raw.Trim();
                if (line.Length < 70 || line[64] != ' ') continue;
                string expected = line[..64];
                string rel = line[66..].Trim();
                if (!(rel.StartsWith("sounds/", StringComparison.Ordinal)
                      || rel.StartsWith("bgm/", StringComparison.Ordinal)
                      || rel.StartsWith("fonts/", StringComparison.Ordinal)))
                {
                    continue;
                }

                string path = Path.Combine(dir, "Assets", rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    errors.Add(L.T("tools.cli.devtools.CheckPublish.6") + rel);
                    continue;
                }

                using var sha = System.Security.Cryptography.SHA256.Create();
                using var stream = File.OpenRead(path);
                string actual = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    errors.Add(L.T("tools.cli.devtools.CheckPublish.7") + rel);
                }

                checkedHashes++;
            }

            notes.Add(L.T("tools.cli.devtools.CheckPublish.8") + checkedHashes + L.T("tools.cli.devtools.CheckPublish.9"));
        }

        foreach (string note in notes)
        {
            Console.WriteLine("  " + note);
        }

        if (errors.Count == 0)
        {
            Console.WriteLine("check-publish: OK  " + dir);
            return 0;
        }

        foreach (string error in errors)
        {
            Console.Error.WriteLine("  FAIL " + error);
        }

        Console.Error.WriteLine("check-publish: " + errors.Count + L.T("tools.cli.devtools.CheckPublish.10"));
        return 1;
    }

    /// <summary>
    /// <c>--bench-snapshot [iterations]</c>：用真实引擎状态反复构建 UI 快照，
    /// 统计单次耗时 / 分配，作为性能检查基线（不改游戏逻辑）。
    /// </summary>
    private static int BenchSnapshot(string[] args)
    {
        Tiisu.App.Program.TryUseUtf8Console();
        int iterations = args.Length >= 2 && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
            ? Math.Max(1, n)
            : 20000;
        var settings = new Settings { RecordTiles = false, AiSeatLocks = new[] { 0, 0, 0, 0 } };
        var runtime = new MatchRuntime(settings, new Mulberry32Random(1), _ => { }, null, null, null, 1)
        {
            AutoPilot = true,
        };
        runtime.StartMatch(4);
        var engine = runtime.Engine;

        // 预热
        for (int i = 0; i < 1000; i++)
        {
            SnapshotBuilder.Build(engine, "human_discard", HumanAction.Discard, null, null, settings);
        }

        // 缓存版：同一状态重复构建（对应按和失败后的补发、重复 EmitState）。
        var cache = new TenpaiHintCache();
        for (int i = 0; i < 1000; i++)
        {
            SnapshotBuilder.Build(engine, "human_discard", HumanAction.Discard, null, null, settings, null, cache);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            SnapshotBuilder.Build(engine, "human_discard", HumanAction.Discard, null, null, settings, null, cache);
        }

        sw.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"bench-snapshot: {iterations} iters");
        Console.WriteLine($"  avg: {sw.Elapsed.TotalMilliseconds * 1000.0 / iterations:F2} us/iter");
        Console.WriteLine($"  alloc: {(double)allocated / iterations:F1} B/iter");
        Console.WriteLine(L.T("tools.cli.devtools.BenchSnapshot.1"));
        return 0;
    }

    private static int AuditGames(string[] args)
    {
        Tiisu.App.Program.TryUseUtf8Console();

        int count = args.Length >= 2 && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCount)
            ? Math.Max(1, parsedCount)
            : 100;
        int mode = args.Length >= 3 && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMode)
            ? parsedMode
            : 4;
        uint startSeed = args.Length >= 4 && uint.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsedSeed)
            ? parsedSeed
            : 1;
        bool record = args.Contains("--record", StringComparer.Ordinal);
        bool uiAudit = args.Contains("--ui", StringComparer.Ordinal);
        bool passive = args.Contains("--passive", StringComparer.Ordinal);
        bool callMode = args.Contains("--call", StringComparer.Ordinal);

        int jobs = 1;
        int jobsIndex = Array.FindIndex(args, a => a == "--jobs");
        if (jobsIndex >= 0 && jobsIndex + 1 < args.Length
            && int.TryParse(args[jobsIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedJobs))
        {
            jobs = Math.Clamp(parsedJobs, 1, Environment.ProcessorCount);
        }

        int failed = 0;
        int issueTotal = 0;
        int totalRounds = 0;
        int wins = 0;
        int flows = 0;
        int completed = 0;
        var samples = new System.Collections.Concurrent.ConcurrentBag<string>();
          int uiIssueTotal = 0;
          var uiSamples = new System.Collections.Concurrent.ConcurrentBag<string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        void RunOne(int i)
        {
            uint seed = startSeed + (uint)i;
            var settings = new Settings
            {
                AiSeatLocks = new[] { 0, 0, 0, 0 },
                AiMctsEnabled = false,
                RecordTiles = record,
            };
            var roundLog = record ? new RoundLogWriter { Enabled = true } : null;
            var auditor = uiAudit ? new UiInvariantAuditor() : null;
            MatchRuntime? self = null;
            var runtime = new MatchRuntime(settings, new Mulberry32Random(seed), ev =>
                {
                    if (auditor is not null && ev is StateChangedEvent changed && self is not null)
                    {
                        auditor.Audit(self.Engine, changed.Snapshot);
                    }
                },
                saveStore: null, stats: null, roundLog: roundLog, matchSeed: seed)
            {
                AutoPilot = !passive,
            };
            self = runtime;

            runtime.StartMatch(mode);
            int guard = 0;
            if (passive)
            {
                // 被动的“人类”：能和也不和、有荣和窗口就过，用来覆盖提示与听牌分叉。
                while (runtime.Phase != MatchPhase.MatchOver && ++guard < 20_000)
                {
                    switch (runtime.Phase)
                    {
                        case MatchPhase.RoundEnded:
                            runtime.NextRound();
                            break;
                        case MatchPhase.HumanRon:
                        case MatchPhase.HumanMeld:
                            runtime.Pass();
                            break;
                        case MatchPhase.HumanDiscard:
                        {
                            int human = runtime.HumanIndex;
                            var player = runtime.Engine.Players[human];
                            if (callMode && !player.IsCalled
                                && (runtime.AvailableActions() & HumanAction.DeclareCall) != 0)
                            {
                                // 逐个试打，打出后能听就报听；失败的不改状态。
                                for (int k = 0; k < player.Hand.Count && !player.IsCalled; k++)
                                {
                                    runtime.HumanDeclareCall(k);
                                }

                                if (player.IsCalled)
                                {
                                    break;
                                }
                            }

                            int index = runtime.Snapshot.Seats[human].DrawnIndex;
                            if (index < 0)
                            {
                                index = runtime.Snapshot.Seats[human].Hand.Count - 1;
                            }

                            runtime.HumanDiscard(index);
                            break;
                        }

                        default:
                            throw new InvalidOperationException(L.T("tools.cli.devtools.AuditGames.1") + runtime.Phase);
                    }
                }
            }
            else
            {
                while (runtime.Phase != MatchPhase.MatchOver && ++guard < 512)
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
            }

            if (runtime.Phase != MatchPhase.MatchOver)
            {
                Interlocked.Increment(ref failed);
                return;
            }

            Interlocked.Add(ref totalRounds, runtime.Rounds);
            Interlocked.Add(ref wins, runtime.TsumoWins + runtime.RonWins);
            Interlocked.Add(ref flows, runtime.DrawRounds);
            Interlocked.Add(ref issueTotal, runtime.AuditIssueCount);
            foreach (string issue in runtime.AuditIssues)
            {
                samples.Add(issue);
            }

              if (auditor is not null)
              {
                  Interlocked.Add(ref uiIssueTotal, auditor.Count);
                  foreach (string issue in auditor.Samples)
                  {
                      uiSamples.Add(issue);
                  }
              }

            int done = Interlocked.Increment(ref completed);
            if (jobs > 1 && done % 1000 == 0)
            {
                Console.WriteLine(L.T("tools.cli.devtools.AuditGames.2", ("p1", done), ("p2", count), ("p3", Volatile.Read(ref issueTotal)), ("p4", (clock.Elapsed.TotalSeconds).ToString("F0", CultureInfo.InvariantCulture))));
            }
            else if (jobs == 1 && done % 100 == 0)
            {
                Console.WriteLine(L.T("tools.cli.devtools.AuditGames.2", ("p1", done), ("p2", count), ("p3", issueTotal), ("p4", (clock.Elapsed.TotalSeconds).ToString("F0", CultureInfo.InvariantCulture))));
            }
        }

        if (jobs <= 1)
        {
            for (int i = 0; i < count; i++)
            {
                RunOne(i);
            }
        }
        else
        {
            Console.WriteLine(L.T("tools.cli.devtools.AuditGames.3", ("p1", jobs), ("p2", Environment.ProcessorCount)));
            System.Threading.Tasks.Parallel.For(0, count,
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = jobs },
                RunOne);
        }

        clock.Stop();
        Console.WriteLine($"audit-games: count={count} mode={mode} start={startSeed} jobs={jobs}");
        Console.WriteLine(L.T("tools.cli.devtools.AuditGames.4", ("p1", count - failed), ("p2", count), ("p3", failed), ("p4", totalRounds), ("p5", wins), ("p6", flows)));
        Console.WriteLine(L.T("tools.cli.devtools.AuditGames.5", ("p1", issueTotal)));
        Console.WriteLine(L.T("tools.cli.devtools.AuditGames.6", ("p1", uiIssueTotal)));
        foreach (string sample in samples.Take(10))
        {
            Console.WriteLine("   - " + sample);
        }

        return issueTotal == 0 && uiIssueTotal == 0 && failed == 0 ? 0 : 1;
    }

    private static int SmokeRun(string[] args)
    {
        Tiisu.App.Program.TryUseUtf8Console();
        if (args.Length < 2
            || !uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint seed))
        {
            Console.Error.WriteLine(L.T("tools.cli.devtools.SmokeRun.1"));
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
            Console.Error.WriteLine(L.T("tools.cli.devtools.SmokeRun.2", ("p1", guard), ("p2", runtime.Phase)));
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
        Tiisu.App.Program.TryUseUtf8Console();
        if (args.Length < 2
            || !uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint seed))
        {
            Console.Error.WriteLine(L.T("tools.cli.devtools.SmokeAi.1"));
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
        Console.WriteLine(L.T("tools.cli.devtools.SmokeAi.2") + string.Join("/", settings.AiWeights) + L.T("tools.cli.devtools.SmokeAi.3") + (settings.AiMctsEnabled ? L.T("tools.cli.devtools.SmokeAi.4") : L.T("tools.cli.devtools.SmokeAi.5")));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeAi.6") + string.Join("  ", names.Select((name, i) =>
            name + "=" + runtime.AiLevels[i])));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeAi.7") + runtime.AiDiscardDecisions + L.T("tools.cli.devtools.SmokeAi.8") + string.Join(" ", names.Select((name, i) =>
            name + "=" + runtime.AiDiscardDecisionsBySeat[i])) + L.T("tools.cli.devtools.SmokeAi.9"));
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            L.T("tools.cli.devtools.SmokeAi.10"),
            completed ? L.T("tools.cli.devtools.SmokeAi.11") : L.T("tools.cli.devtools.SmokeAi.12"),
            runtime.Rounds,
            runtime.Engine.HandsPlayed,
            clock.Elapsed.TotalMilliseconds,
            runtime.AiDecisionAverageMilliseconds));
        Console.WriteLine("  EventHash: " + runtime.EventHash);

        if (!completed)
        {
            Console.Error.WriteLine(L.T("tools.cli.devtools.SmokeAi.13", ("p1", runtime.Phase)));
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
        Tiisu.App.Program.TryUseUtf8Console();
        if (args.Length < 2
            || !uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint seed))
        {
            Console.Error.WriteLine(L.T("tools.cli.devtools.SmokeAiCall.1"));
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
        Console.WriteLine(L.T("tools.cli.devtools.SmokeAi.6") + string.Join("  ", names.Select((name, i) =>
            name + "=" + runtime.AiLevels[i])));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeAiCall.2") + runtime.AiCallDeclarations + L.T("tools.cli.devtools.SmokeAiCall.3") + string.Join(" ", names.Select((name, i) =>
            name + "=" + calls[i])) + L.T("tools.cli.devtools.SmokeAi.9"));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeAi.7") + runtime.AiDiscardDecisions + L.T("tools.cli.devtools.SmokeAiCall.4"));
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            L.T("tools.cli.devtools.SmokeAiCall.5"),
            completed ? L.T("tools.cli.devtools.SmokeAi.11") : L.T("tools.cli.devtools.SmokeAi.12"),
            runtime.Rounds,
            runtime.Engine.HandsPlayed,
            clock.Elapsed.TotalMilliseconds));
        Console.WriteLine("  EventHash: " + runtime.EventHash);

        if (!completed)
        {
            Console.Error.WriteLine(L.T("tools.cli.devtools.SmokeAiCall.6", ("p1", runtime.Phase)));
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
        Tiisu.App.Program.TryUseUtf8Console();
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

        Console.WriteLine(L.T("tools.cli.devtools.SmokeSave.1"));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeSave.2", ("p1", save.Enabled), ("p2", runtime.Rounds), ("p3", completed)));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeSave.3", ("p1", statePath)));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeSave.4", ("p1", existedBefore), ("p2", existedAfter), ("p3", (unchanged ? L.T("tools.cli.devtools.SmokeSave.unchanged") : L.T("tools.cli.devtools.SmokeSave.changed")))));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeSave.5", ("p1", (ok ? L.T("tools.cli.devtools.SmokeSave.saveNotWritten") : L.T("tools.cli.devtools.SmokeSave.saveWritten")))));
        return ok ? 0 : 1;
    }

    /// <summary>
    /// <c>--smoke-resume</c>：验证「恢复上局」已整体停用 —— 菜单没有 resume 入口、
    /// <see cref="GameSession"/> 没有 PostResume、<see cref="SaveStore.Load"/> 读不出任何存档。
    /// </summary>
    private static int SmokeResume(string[] args)
    {
        Tiisu.App.Program.TryUseUtf8Console();
        var layout = UiLayout.Load(Path.Combine(Tiisu.App.Program.AssetsDir, "layout", "menu.json"));
        bool hasMenuEntry = layout.Elements.Any(el =>
            string.Equals(el.Action, "resume", StringComparison.Ordinal));
        bool hasPostResume = typeof(GameSession).GetMethod("PostResume") is not null;
        using var save = new SaveStore();
        bool loadBlocked = save.Load() is null;
        bool ok = !hasMenuEntry && !hasPostResume && loadBlocked;

        Console.WriteLine(L.T("tools.cli.devtools.SmokeResume.1"));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeResume.2", ("p1", (hasMenuEntry ? L.T("tools.cli.devtools.SmokeResume.hasEntry") : L.T("tools.cli.devtools.SmokeResume.noEntry")))));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeResume.3", ("p1", (hasPostResume ? L.T("tools.cli.devtools.SmokeResume.exists") : L.T("tools.cli.devtools.SmokeResume.missing")))));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeResume.4", ("p1", save.StatePath), ("p2", (loadBlocked ? L.T("tools.cli.devtools.SmokeResume.loadBlocked") : L.T("tools.cli.devtools.SmokeResume.loadRead")))));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeSave.5", ("p1", (ok ? L.T("tools.cli.devtools.SmokeSave.noResumeEntry") : L.T("tools.cli.devtools.SmokeSave.resumeEntryStill")))));
        return ok ? 0 : 1;
    }

    /// <summary>
    /// <c>--smoke-session [seed] [mode]</c>：走 <see cref="GameSession"/> 的命令通道（与 UI 点击同一个入口）
    /// 打完整场，验证「后台线程 + 快照 + 事件」这条链路。
    /// </summary>
    private static int SmokeSession(string[] args)
    {
        Tiisu.App.Program.TryUseUtf8Console();
        uint seed = 1;
        if (args.Length >= 2 && !uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
        {
            Console.Error.WriteLine(L.T("tools.cli.devtools.SmokeSession.1"));
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
        Console.WriteLine(L.T("tools.cli.devtools.SmokeSession.2", ("p1", final.Phase), ("p2", final.RoundLabel), ("p3", actions), ("p4", events)));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeSession.3") + string.Join("  ", final.Seats.Select(seat =>
            seat.Name + "=" + seat.Score.ToString("+0;-0;0", CultureInfo.InvariantCulture))));
        Console.WriteLine("  LastError: " + (session.LastError ?? "-"));

        if (!ok)
        {
            Console.Error.WriteLine(L.T("tools.cli.devtools.SmokeSession.4"));
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
        Tiisu.App.Program.TryUseUtf8Console();
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
        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
        var screen = new TableScreen(session, atlas);

        // 与 TableScreen 内部同一份布局参数（1400×1400 + 牌河 7×5 + Yoga）。
        var layout = new TableLayoutSpec();
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
                        if (index < rects.Count && screen.OnClick(rects[index].CenterX, rects[index].CenterY))
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
        Console.WriteLine(L.T("tools.cli.devtools.SmokePointer.1", ("p1", final.Phase), ("p2", final.RoundLabel), ("p3", actions), ("p4", discards)));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeSession.3") + string.Join("  ", final.Seats.Select(seat =>
            seat.Name + "=" + seat.Score.ToString("+0;-0;0", CultureInfo.InvariantCulture))));
        Console.WriteLine("  LastError: " + (session.LastError ?? "-"));

        if (!ok)
        {
            Console.Error.WriteLine(L.T("tools.cli.devtools.SmokePointer.2"));
            return 1;
        }

        return 0;
    }

    /// <summary><c>--check-voices</c>：打印「副露类型 → 语音名」映射表，供人眼核对。</summary>
    private static int CheckVoices()
    {
        Tiisu.App.Program.TryUseUtf8Console();
        Console.WriteLine(L.T("tools.cli.devtools.CheckVoices.1"));
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
        Tiisu.App.Program.TryUseUtf8Console();

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
        var menuLayout = UiLayout.Load(Path.Combine(Tiisu.App.Program.AssetsDir, "layout", "menu.json"));
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
        Console.WriteLine(L.T("tools.cli.devtools.SmokeEscape.1", ("p1", meldHandled), ("p2", meldCommand)));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeEscape.2", ("p1", dismissHandled), ("p2", !backAfterDismiss)));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeEscape.3", ("p1", menuHandled), ("p2", backAfterSecond)));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeEscape.4", ("p1", lobbyHandled), ("p2", (menuEscQuit ? clicked : "-"))));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeEscape.5", ("p1", quitHandled), ("p2", clicked)));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeEscape.6", ("p1", settingsHandled), ("p2", settingsBack)));
        return ok ? 0 : 1;
    }

    /// <summary>
    /// <c>--smoke-credits</c>：无窗口验证菜单底部署名 —— 每个可点片段的矩形中心点下去
    /// 是否打开对应 URL，以及 ESC / 「退出」按钮行为是否仍与原来一致（不真的开浏览器）。
    /// </summary>
    private static int SmokeCredits()
    {
        Tiisu.App.Program.TryUseUtf8Console();
        var layout = UiLayout.Load(Path.Combine(Tiisu.App.Program.AssetsDir, "layout", "menu.json"));
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
            (L.T("tools.cli.devtools.SmokeCredits.1"), "https://github.com/Pub-Polaris/Tiisu-Mahjong-CSharp"),
        };

        bool ok = rects.Count == credits.Links!.Count;
        Console.WriteLine("smoke-credits");
        Console.WriteLine(L.T("tools.cli.devtools.SmokeCredits.2", ("p1", credits.Text), ("p2", (credits.Y).ToString("F0", CultureInfo.InvariantCulture)), ("p3", (credits.FontSize).ToString("F0", CultureInfo.InvariantCulture))));
        foreach (var (needle, url) in expected)
        {
            var hit = rects.FirstOrDefault(r => r.Link.Url == url && r.Link.Text.Contains(needle, StringComparison.Ordinal));
            if (hit.Link is null)
            {
                Console.WriteLine(L.T("tools.cli.devtools.SmokeCredits.3", ("p1", needle), ("p2", url)));
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

        Console.WriteLine(L.T("tools.cli.devtools.SmokeCredits.4", ("p1", emptyUrlIgnored)));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeCredits.5", ("p1", escapeHandled), ("p2", noActionOnEsc)));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeCredits.6", ("p1", quitHandled), ("p2", clickedAction)));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeCredits.7", ("p1", overlap)));

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

        Console.WriteLine(L.T("tools.cli.devtools.PrintSmoke.1", ("p1", runtime.Rounds), ("p2", runtime.Engine.HandsPlayed), ("p3", runtime.ModeTarget)));
        Console.WriteLine(
            L.T("tools.cli.devtools.PrintSmoke.2", ("p1", winRounds), ("p2", runtime.TsumoWins), ("p3", runtime.RonWins), ("p4", runtime.DrawRounds), ("p5", runtime.SpecialFlowRounds)));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeSession.3") + string.Join("  ", names.Select((name, i) =>
            name + "=" + scores[i].ToString("+0;-0;0", CultureInfo.InvariantCulture))));
        Console.WriteLine("  EventHash: " + runtime.EventHash);
    }

    private static UiScreen CreateScreen(string name, TileAtlas? atlas) => name switch
    {
        "menu" => new MenuScreen(
            UiLayout.Load(Path.Combine(Tiisu.App.Program.AssetsDir, "layout", "menu.json")),
            _ => false),
        "settings" => new SettingsScreen(Settings.Load(), _ => { }, () => { }),
      "settings-large" => new SettingsScreen(new Settings { UiTextScale = 1.15 }, _ => { }, () => { }),
        "tutorial" => new TutorialScreen(() => { }, () => { }),
      "stats" => new StatsScreen(() => { }, () => { }),
      "achievements" => new AchievementsScreen(() => { }),
      "replay" => new ReplayScreen(() => { }),
      "replay-demo" => CreateReplayDemoScreen(),
        "rules" => new RulesScreen(() => { }),
        "licenses" => new LicensesScreen(() => { }),
        "markdown" => new MarkdownScreen(
          L.T("tools.cli.devtools.CreateScreen.1"),
          System.IO.File.ReadAllText(Path.Combine(Tiisu.App.Program.AssetsDir, "docs",
              UiText.IsEnglish ? "en/REFERENCES.md" : UiText.IsJapanese ? "ja/REFERENCES.md" : "REFERENCES.md")),
          () => { }),
        _ => new TableScreen(atlas),
    };

    private static UiScreen CreateReplayDemoScreen()
    {
        ReplayRound round = CreateDemoReplayRound();
        var screen = new ReplayScreen(() => { }, new[] { round });
        screen.ShowStep(1);
        return screen;
    }

    private static ReplayRound CreateDemoReplayRound()
    {
        var headers = new[]
        {
            new ReplayHeader(0, "你", "东", true, true, "你"),
            new ReplayHeader(1, "AI 南", "南", false, false, null),
            new ReplayHeader(2, "AI 西", "西", false, false, null),
            new ReplayHeader(3, "AI 北", "北", false, false, null),
        };

        IReadOnlyList<ReplayMeld> none = Array.Empty<ReplayMeld>();
        var noMelds = new[] { none, none, none, none };
        var rivers = new[] { "", "", "", "" };
        var allHands = new[]
        {
            "123m456p789s1122z5z",
            "234m567p678s3344z",
            "345m678p789s5566z",
            "456m789p123s7788z",
        };
        var handCounts = new[] { 14, 13, 13, 13 };
        var scores = new[] { 25000, 25000, 25000, 25000 };

        var steps = new List<ReplayStep>
        {
            new(0, "draw", null, "123m456p789s1122z5z", 69, null, null, rivers,
                allHands, noMelds, handCounts, scores, 0, none),
            new(0, "discard", "5z", "123m456p789s1122z", 69, null, null, rivers,
                allHands, noMelds, new[] { 13, 13, 13, 13 }, scores, 0, none),
            new(1, "draw", null, "234m567p678s3344z5m", 68, null, null, rivers,
                allHands, noMelds, new[] { 13, 14, 13, 13 }, scores, 1, none),
            new(1, "discard", "5m", "234m567p678s3344z", 68, null, null, rivers,
                allHands, noMelds, new[] { 13, 13, 13, 13 }, scores, 1, none),
        };

        return new ReplayRound("replay-demo.jsonl", 1, 0, 0, headers, steps);
    }

    private static void RenderToPng(string path, double time, string screenName)
    {
        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
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
        Tiisu.App.Program.TryUseUtf8Console();
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
        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
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
            hands.Add(L.T("tools.cli.devtools.RenderHandsToPng.1", ("p1", wind), ("p2", player.Hand.Count), ("p3", tiles)));
        }

        float boxH = 60f + (hands.Count * 30f) + 10f;
        canvas.DrawRoundRect(new SKRect(16, 16, 1184, 16 + boxH), 10, 10, panel);
        canvas.DrawText(L.T("tools.cli.devtools.RenderHandsToPng.2", ("p1", (DateTime.Now).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))), 30, 46, SKTextAlign.Left, titleFont, paint);
        canvas.DrawText(L.T("tools.cli.devtools.RenderHandsToPng.3", ("p1", vm.RoundLabel), ("p2", vm.WallCount), ("p3", seed), ("p4", mode)),
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
        Console.WriteLine(L.T("tools.cli.devtools.RenderHandsToPng.4", ("p1", path), ("p2", vm.RoundLabel), ("p3", vm.WallCount), ("p4", humanDiscards), ("p5", runtime.Phase)));
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
        Tiisu.App.Program.TryUseUtf8Console();
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

        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
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
    /// <summary>
    /// <c>--render-tile-rotate &lt;outdir&gt;</c>：用牌面雪碧图的幺鸡（1s），
    /// 先斜置（-35°），再向右旋转到 +35°，输出逐帧 PNG。Skia 用 4×4 透视矩阵
    /// 做真正的纹理透视，不是把四角手动收窄。
    /// </summary>
    /// <summary>
    /// <c>--export-ui-atlas &lt;outdir&gt;</c>：把当前代码画的 2D 界面元素（动作按钮/面板/徽章）
    /// 栅格化成一张 ui-atlas.png + ui-atlas.json；不改任何运行时渲染。
    /// </summary>
    private static int ExportUiAtlas(string outDir)
    {
        Directory.CreateDirectory(outDir);
        const int scale = 2;
        const int pad = 16;
        const int maxWidth = 2048;

        var items = new List<(string Id, string Group, int LogicalW, int LogicalH, Action<SkiaCanvas, float, float> Draw)>();

        // ── 动作按钮：8 种 × 3 态 ─────────────────────────────
        var buttons = new (ActionBar.Kind Kind, string Label)[]
        {
            (ActionBar.Kind.Kan, "杠"),
            (ActionBar.Kind.Tsumo, "和牌"),
            (ActionBar.Kind.Ron, "荣和"),
            (ActionBar.Kind.Pon, "碰"),
            (ActionBar.Kind.Chi, "吃"),
            (ActionBar.Kind.Pass, "过"),
            (ActionBar.Kind.Flow, "流局"),
            (ActionBar.Kind.DeclareCall, "报听"),
        };
        foreach (var (kind, label) in buttons)
        {
            foreach (var (state, hover, enabled) in new[] { ("normal", false, true), ("hover", true, true), ("disabled", false, false) })
            {
                string id = "button." + ActionBar.SemanticId(kind) + "." + state;
                items.Add((id, "button", 120, 44, (c, x, y) =>
                {
                    var colors = ActionBar.ColorsFor(kind, hover, enabled);
                    var rect = new RectF(x, y, 120, 44);
                    c.FillRoundRect(rect, 10f, colors.Fill);
                    c.StrokeRoundRect(rect, 10f, colors.Border, 1.5f);
                    c.DrawText(label, rect, colors.Text, new TextStyle(17, Bold: true, Center: true));
                }));
            }
        }

        // ── 面板 ─────────────────────────────────────────────
        void Panel(SkiaCanvas c, float x, float y, float w, float h, Color fill, Color border, string label)
        {
            var rect = new RectF(x, y, w, h);
            c.FillRoundRect(rect, 14f, fill);
            c.StrokeRoundRect(rect, 14f, border, 1.5f);
            if (!string.IsNullOrEmpty(label))
            {
                c.DrawText(label, rect, Color.Rgb(232, 218, 176), new TextStyle(22, Bold: true, Center: true));
            }
        }

        items.Add(("panel.dark", "panel", 240, 110, (c, x, y) =>
            Panel(c, x, y, 240, 110, Color.Rgb(30, 42, 36), Color.Rgb(160, 132, 72), "本局结算")));
        items.Add(("panel.gold", "panel", 240, 110, (c, x, y) =>
            Panel(c, x, y, 240, 110, Color.Rgb(36, 52, 44), Color.Rgb(214, 178, 92), "南 3 局")));
        items.Add(("panel.warning", "panel", 240, 110, (c, x, y) =>
            Panel(c, x, y, 240, 110, Color.Rgb(56, 38, 28), Color.Rgb(255, 176, 64), "错和警告")));

        // ── 徽章 ─────────────────────────────────────────────
        void Badge(SkiaCanvas c, float x, float y, float w, string text, Color fill, Color border, Color textColor)
        {
            var rect = new RectF(x, y, w, 34);
            c.FillRoundRect(rect, 17f, fill);
            c.StrokeRoundRect(rect, 17f, border, 1.2f);
            c.DrawText(text, rect, textColor, new TextStyle(16, Bold: true, Center: true));
        }

        items.Add(("badge.dealer", "badge", 100, 40, (c, x, y) =>
            Badge(c, x, y, 100, "庄", Color.Rgb(92, 64, 20), Color.Rgb(242, 201, 76), Color.Rgb(255, 236, 176))));
        foreach (string wind in new[] { "东", "南", "西", "北" })
        {
            string id = "badge.wind." + wind;
            items.Add((id, "badge", 80, 40, (c, x, y) =>
                Badge(c, x, y, 80, wind, Color.Rgb(30, 42, 36), Color.Rgb(160, 132, 72), Color.Rgb(232, 218, 176))));
        }

        items.Add(("badge.called", "badge", 100, 40, (c, x, y) =>
            Badge(c, x, y, 100, "报听", Color.Rgb(18, 70, 78), Color.Rgb(34, 195, 214), Color.Rgb(196, 248, 255))));
        items.Add(("badge.banned", "badge", 100, 40, (c, x, y) =>
            Badge(c, x, y, 100, "禁和", Color.Rgb(74, 26, 26), Color.Rgb(255, 120, 120), Color.Rgb(255, 210, 210))));
        items.Add(("badge.honba", "badge", 120, 40, (c, x, y) =>
            Badge(c, x, y, 120, "本场 3", Color.Rgb(30, 42, 36), Color.Rgb(160, 132, 72), Color.Rgb(232, 218, 176))));

        // 大到小：先按面积，再按长边；从左到右、从上到下排。
        var ordered = items
            .OrderByDescending(i => (long)i.LogicalW * i.LogicalH)
            .ThenByDescending(i => Math.Max(i.LogicalW, i.LogicalH))
            .ToList();

        var placed = new List<(string Id, string Group, int X, int Y, int W, int H, int LogicalW, int LogicalH, Action<SkiaCanvas, float, float> Draw)>(ordered.Count);
        int cursorX = pad;
        int cursorY = pad;
        int rowHeight = 0;
        int usedWidth = 0;
        foreach (var item in ordered)
        {
            int w = item.LogicalW * scale;
            int h = item.LogicalH * scale;
            if (cursorX + w + pad > maxWidth && cursorX > pad)
            {
                cursorX = pad;
                cursorY += rowHeight + pad;
                rowHeight = 0;
            }

            placed.Add((item.Id, item.Group, cursorX, cursorY, w, h, item.LogicalW, item.LogicalH, item.Draw));
            usedWidth = Math.Max(usedWidth, cursorX + w + pad);
            cursorX += w + pad;
            rowHeight = Math.Max(rowHeight, h);
        }

        int imageW = Math.Max(usedWidth, maxWidth);
        int imageH = cursorY + rowHeight + pad;
        using var surface = SKSurface.Create(new SKImageInfo(imageW, imageH, SKColorType.Rgba8888, SKAlphaType.Premul));
        var raw = surface.Canvas;
        raw.Clear(new SKColor(0, 0, 0, 0));

        var manifest = new List<object>(placed.Count);
        foreach (var item in placed)
        {
            raw.Save();
            raw.Translate(item.X, item.Y);
            raw.Scale(scale);
            var canvas = new SkiaCanvas(raw);
            item.Draw(canvas, 0f, 0f);
            raw.Restore();

            manifest.Add(new
            {
                id = item.Id,
                group = item.Group,
                x = item.X,
                y = item.Y,
                w = item.W,
                h = item.H,
                logicalW = item.LogicalW,
                logicalH = item.LogicalH,
            });
        }

        string imagePath = Path.Combine(outDir, "ui-atlas.png");
        using (var image = surface.Snapshot())
        using (var data = image.Encode(SKEncodedImageFormat.Png, 95))
        using (var stream = File.OpenWrite(imagePath))
        {
            data.SaveTo(stream);
        }

        string jsonPath = Path.Combine(outDir, "ui-atlas.json");
        string json = JsonSerializer.Serialize(new
        {
            image = "ui-atlas.png",
            scale,
            padding = pad,
            cells = manifest,
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(jsonPath, json, new System.Text.UTF8Encoding(false));

        Console.WriteLine($"ui atlas: {placed.Count} cells → {imagePath} ({imageW}x{imageH}, pad={pad}) + {jsonPath}");
        return 0;
    }

    private static int RenderTileRotate(string outDir)
    {
        Directory.CreateDirectory(outDir);
        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
        if (!atlas.IsLoaded || atlas.Sheet is not { } sheet
            || !atlas.TryGetSource("1s", out var src))
        {
            Console.Error.WriteLine(L.T("tools.cli.devtools.RenderTileRotate.1"));
            return 1;
        }

        const int canvasW = 900;
        const int canvasH = 900;
        const int tileW = 216;
        const int tileH = 300;
        const float distance = 1000f;
        const float fovDegrees = 50f;
        float focal = canvasH / 2f / MathF.Tan(fovDegrees * MathF.PI / 360f);

        int frames = 25;
        for (int frame = 0; frame < frames; frame++)
        {
            // -35° → +35°，两端慢、中间快（easeInOut）
            float t = frames <= 1 ? 1f : frame / (float)(frames - 1);
            float eased = t * t * (3f - 2f * t);
            float degrees = -35f + 70f * eased;
            float rad = degrees * MathF.PI / 180f;
            float cos = MathF.Cos(rad);
            float sin = MathF.Sin(rad);

            var matrix = new SKMatrix44();
            // x' = focal*cos*x + canvasW/2*distance（再除以 w）
            matrix[0, 0] = focal * cos;
            matrix[3, 0] = canvasW / 2f * distance;
            // y' = focal*y + canvasH/2*distance
            matrix[1, 1] = focal;
            matrix[3, 1] = canvasH / 2f * distance;
            matrix[2, 2] = 1f;
            // w = sin*x + distance（右侧靠近/远离，形成透视）
            matrix[0, 3] = sin;
            matrix[3, 3] = distance;

            using var surface = SKSurface.Create(new SKImageInfo(canvasW, canvasH, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(18, 26, 22));

            // 地面阴影（随旋转轻微移动）
            using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 110), IsAntialias = true, ImageFilter = SKImageFilter.CreateBlur(18, 18) };
            canvas.DrawOval(new SKRect(250, 700, 650, 780), shadow);

            canvas.Save();
            canvas.Concat(matrix);
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawImage(sheet, src, new SKRect(-tileW / 2f, -tileH / 2f, tileW / 2f, tileH / 2f), paint);
            canvas.Restore();

            string path = Path.Combine(outDir, $"tile-1s-{frame:000}.png");
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 95);
            using var stream = File.OpenWrite(path);
            data.SaveTo(stream);
        }

        Console.WriteLine($"tile rotation frames: {frames} → {outDir}");
        return 0;
    }

    private static int RenderRoundEndToPng(string[] args)
    {
        Tiisu.App.Program.TryUseUtf8Console();
        string path = args[1];
        bool doubleWin = args.Contains("--double", StringComparer.Ordinal);
        bool flow = args.Contains("--flow", StringComparer.Ordinal);
        int langAt = Array.IndexOf(args, "--lang");
        if (langAt >= 0 && langAt + 1 < args.Length)
        {
            UiText.SetLanguage(args[langAt + 1]);
        }

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
            Console.Error.WriteLine(L.T("tools.cli.devtools.RenderRoundEndToPng.1", ("p1", runtime.Phase)));
            return 1;
        }

        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
        var screen = new TableScreen(() => runtime.Snapshot, null);
        screen.ShowSettlement(captured);
        RenderScreen(path, screen, atlas);

        Console.WriteLine($"rendered roundend -> {path}  seed={seed} mode={mode} phase={runtime.Phase}");
        Console.WriteLine(L.T("tools.cli.devtools.RenderRoundEndToPng.2", ("p1", captured.Kind), ("p2", string.Join(",", captured.Winners))) +
                          $"draw={captured.DrawReason ?? "-"} matchOver={captured.MatchOver}");
        Console.WriteLine(L.T("tools.cli.devtools.RenderRoundEndToPng.3") + string.Join("  ", captured.Scores.Select((value, i) =>
            runtime.Engine.Players[i].Name + "=" + value.ToString("+0;-0;0", CultureInfo.InvariantCulture))));
        var details = captured.Details ?? Array.Empty<SettlementDetail>();
        Console.WriteLine(L.T("tools.cli.devtools.RenderRoundEndToPng.4", ("p1", details.Count)));
        foreach (var detail in details)
        {
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "    {0}: {1} totalFu={2} tableSafe={3} base={4} tableless={5} bonus={6} sub={7} dealer={8} honba={9} final={10} pay=\"{11}\"",
                runtime.Engine.Players[detail.WinnerIndex].Name,
                detail.SelfDraw ? L.T("tools.cli.devtools.RenderRoundEndToPng.5") : L.T("tools.cli.devtools.RenderRoundEndToPng.6"),
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
            Console.WriteLine(L.T("tools.cli.devtools.RenderRoundEndToPng.7") + string.Join(" / ", detail.Yaku.Select(y =>
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
        Tiisu.App.Program.TryUseUtf8Console();
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

        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
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
        Tiisu.App.Program.TryUseUtf8Console();
        var result = BuildDoubleSettlementResult();

        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
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

    /// <summary><c>--render-simplified &lt;png&gt;</c>：推倒胡结算演示（只显示和牌形态，不显示役/赋/点数）。</summary>
    private static void RenderSimplifiedSettlement(string path)
    {
        Tiisu.App.Program.TryUseUtf8Console();
        var detail = new SettlementDetail
        {
            WinnerIndex = 0,
            SelfDraw = false,
            PayerIndex = 3,
            Yaku = new (string Name, int Fu, SettlementYakuFlag Flag)[]
            {
                ("四顺一对", 0, SettlementYakuFlag.Normal),
            },
            TotalFu = 0,
            TableSafeFu = 0,
            BasePoints = 0,
            SubPoints = 0,
            FinalPoints = 0,
            PaymentText = "推倒胡 · 只记胜负",
        };

        var result = new RoundResult(
            2,
            "ron",
            new[] { 0 },
            null,
            new[] { 49000, 49000, 49000, 49000 },
            0,
            0,
            false,
            new[] { detail },
            null,
            new[] { 49000, 49000, 49000, 49000 },
            null,
            Simplified: true);

        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
        var screen = new TableScreen(TableDemoModel.CreateWithFlow, null, null, atlas);
        screen.ShowSettlement(result);
        RenderScreen(path, screen, atlas);
        Console.WriteLine($"rendered simplified -> {path}");
    }

    /// <summary><c>--render-roundend &lt;png&gt; --flow</c>：荒牌流局演示（不列役，只给四家分数变化）。</summary>
    private static void RenderFlowSettlement(string path, int page = 1)
    {
        Tiisu.App.Program.TryUseUtf8Console();
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

        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
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
        Tiisu.App.Program.TryUseUtf8Console();
        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
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
    private static int ExportSteamCapsules(string outputDirectory)
    {
        Tiisu.App.Program.TryUseUtf8Console();
        string dir = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(dir);
        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);

        var sizes = new (string Name, int Width, int Height)[]
        {
            ("main-616x353", 616, 353),
            ("header-460x215", 460, 215),
            ("small-231x87", 231, 87),
            ("library-600x900", 600, 900),
            ("library-hero-920x430", 920, 430),
        };

        foreach (var (name, width, height) in sizes)
        {
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            SKCanvas canvas = surface.Canvas;
            using (var bg = new SKPaint
                   {
                       Shader = SKShader.CreateLinearGradient(
                           new SKPoint(0, 0), new SKPoint(width, height),
                           new[] { new SKColor(28, 48, 42), new SKColor(6, 10, 9) },
                           null, SKShaderTileMode.Clamp),
                   })
            {
                canvas.DrawRect(new SKRect(0, 0, width, height), bg);
            }

            using (var border = new SKPaint
                   {
                       IsAntialias = true,
                       Style = SKPaintStyle.Stroke,
                       StrokeWidth = Math.Max(2f, width / 300f),
                       Color = new SKColor(196, 168, 96, 210),
                   })
            {
                canvas.DrawRoundRect(new SKRect(10, 10, width - 10, height - 10), 12, 12, border);
            }

            using var ctx = new SkiaCanvas(canvas, atlas);
            bool small = width < 300;
            bool vertical = height > width * 1.25f;
            float scale = MathF.Min(width / 616f, height / 353f);
            float titleSize = small
                ? 14f
                : Math.Clamp(44f * scale, 20f, 60f);
            float x = small ? 10f : width * 0.06f;
            float y = small ? 14f : vertical ? height * 0.12f : height * 0.22f;

            ctx.DrawText("Tiisu Mahjong", new RectF(x, y, width - x * 2, titleSize * 1.3f),
                Color.Rgb(242, 230, 190), new TextStyle(titleSize, Bold: true, Center: false));
            if (!small)
            {
                ctx.DrawText("TIISU MAHJONG · SINGLE PLAYER",
                    new RectF(x, y + titleSize * 1.35f, width - x * 2, titleSize * 0.55f),
                    Color.Rgb(159, 180, 170), new TextStyle(Math.Max(10, titleSize * 0.26f)));
            }

            string[] tiles = small ? new[] { "7m", "7p" } : new[] { "7m", "7p", "7s" };
            float tileW = small
                ? 34f
                : vertical ? Math.Clamp(width * 0.15f, 54f, 104f) : Math.Clamp(width * 0.12f, 44f, 90f);
            float tileH = tileW * 1.38f;
            float gap = tileW * 0.14f;
            float total = tileW * tiles.Length + gap * (tiles.Length - 1);
            float tileX = small
                ? width - total - 6f
                : vertical ? (width - total) / 2f : width - total - width * 0.04f;
            float tileY = small
                ? (height - tileH) / 2f
                : vertical ? height * 0.44f : height * 0.42f;
            for (int i = 0; i < tiles.Length; i++)
            {
                ctx.DrawTile(tiles[i], new RectF(tileX + i * (tileW + gap), tileY, tileW, tileH));
            }

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            string path = Path.Combine(dir, name + ".png");
            File.WriteAllBytes(path, data.ToArray());
            Console.WriteLine($"  capsule: {path}  {width}x{height}");
        }

        Console.WriteLine($"steam capsules: {sizes.Length} -> {dir}");
        return 0;
    }


    private static int ExportSteamShots(string outputDirectory, string language)
    {
        Tiisu.App.Program.TryUseUtf8Console();
        UiText.SetLanguage(language);
        string root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);

        var screens = new (string Name, Func<UiScreen> Create)[]
        {
            ("01-table", () => new TableScreen(atlas)),
            ("02-menu", () => new MenuScreen(
                UiLayout.Load(Path.Combine(Tiisu.App.Program.AssetsDir, "layout", "menu.json")),
                _ => false)),
            ("03-settings", () => new SettingsScreen(Settings.Load(), _ => { }, () => { })),
            ("04-stats", () => new StatsScreen(() => { }, () => { })),
            ("05-achievements", () => new AchievementsScreen(() => { })),
            ("06-replay", () => new ReplayScreen(() => { })),
        };

        const int width = 1920;
        const int height = 1080;
        foreach (var (name, create) in screens)
        {
            UiScreen screen = create();
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            SKCanvas canvas = surface.Canvas;
            canvas.Clear(new SKColor(8, 12, 16));
            using (var backdrop = new SKPaint
                   {
                       Shader = SKShader.CreateLinearGradient(
                           new SKPoint(0, 0),
                           new SKPoint(width, height),
                           new[] { new SKColor(18, 26, 30), new SKColor(6, 9, 8) },
                           null,
                           SKShaderTileMode.Clamp),
                   })
            {
                canvas.DrawRect(new SKRect(0, 0, width, height), backdrop);
            }

            float scale = MathF.Min(width / screen.CanvasWidth, height / screen.CanvasHeight);
            float offsetX = (width - screen.CanvasWidth * scale) / 2f;
            float offsetY = (height - screen.CanvasHeight * scale) / 2f;
            canvas.Save();
            canvas.Translate(offsetX, offsetY);
            canvas.Scale(scale);
            screen.Update(0.6);
            using (var ctx = new SkiaCanvas(canvas, atlas))
            {
                screen.Draw(ctx, 0.6);
            }

            canvas.Restore();
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            string path = Path.Combine(root, name + ".png");
            File.WriteAllBytes(path, data.ToArray());
            Console.WriteLine($"  steam shot: {path}  {width}x{height}");
        }

        Console.WriteLine($"steam shots: {screens.Length} -> {root}");
        return 0;
    }


    private static void RenderScreenToPng(string path, string screenName)
    {
        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
        bool english = screenName.EndsWith("-en", StringComparison.OrdinalIgnoreCase);
        bool japanese = screenName.EndsWith("-ja", StringComparison.OrdinalIgnoreCase);
        if (english)
        {
            screenName = screenName[..^3];
            UiText.SetLanguage("en-US");
        }
        else if (japanese)
        {
            screenName = screenName[..^3];
            UiText.SetLanguage("ja-JP");
        }
        else
        {
            UiText.SetLanguage("zh-CN");
        }

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
        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
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
        Tiisu.App.Program.TryUseUtf8Console();
        frames = Math.Max(1, frames);

        using GlHostWindow? host = GlHostWindow.TryCreate(out string renderer, out string? error);
        var gr = host?.Context;
        if (host is null || gr is null)
        {
            Console.WriteLine(L.T("tools.cli.devtools.BenchGl.1", ("p1", error ?? L.T("tools.cli.devtools.BenchGl.unknownReason"))));
            return 0;
        }

        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
        var table = new TableScreen(atlas);
        var info = new SKImageInfo((int)table.CanvasWidth, (int)table.CanvasHeight);
        using var surface = SKSurface.Create(gr, true, info);
        if (surface is null)
        {
            Console.WriteLine(L.T("tools.cli.devtools.BenchGl.2"));
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
    /// <c>--render-table-png &lt;png&gt; --variant mixed|flat|light|medium</c>：按指定观感出 1400×1400 牌桌图。
    /// 与游戏内同一条 TableScreen 绘制路径（含手牌、牌河、动作条、HUD），便于对照可读性。
    /// </summary>
    /// <summary>
    /// <c>--render-soft3d-tile &lt;png&gt; [tileId]</c>：用 <see cref="Software3D"/> 画一张 3D 麻将牌。
    /// 牌身是 CPU 三角形；牌面用 Skia 的 texture-coord 顶点贴到投影后的正面四边形上。
    /// </summary>
    private static void RenderSoft3DTile(string[] args)
    {
        Tiisu.App.Program.TryUseUtf8Console();
        string path = args[1];
        string tileId = args.Length >= 3 ? args[2] : "5z";

        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
        if (!atlas.IsLoaded || !atlas.TryGetSource(tileId, out SKRect src) || atlas.Sheet is null)
        {
            Console.Error.WriteLine($"tile atlas missing: {tileId}");
            return;
        }

        const int size = 800;
        var info = new SKImageInfo(size, size);
        using var surface = SKSurface.Create(info);
        if (surface is null)
        {
            return;
        }

        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using (var ctx = new SkiaCanvas(canvas, atlas))
        {
            ctx.FillLinearGradient(new RectF(0, 0, size, size), 0,
                Color.Rgb(24, 31, 36), Color.Rgb(8, 11, 14));
        }

        var camera = new Software3D.Camera3D(
            new Software3D.Vertex3(0.78f, 0.52f, 2.25f),
            new Software3D.Vertex3(0f, 0f, 0f),
            new Software3D.Vertex3(0f, 1f, 0f),
            FovDegrees: 33f,
            Near: 0.05f,
            Far: 20f);

        var triangles = BuildSoft3DTileTriangles(out Software3D.Vertex3 faceTl, out Software3D.Vertex3 faceTr,
            out Software3D.Vertex3 faceBr, out Software3D.Vertex3 faceBl);
        using (var ctx = new SkiaCanvas(canvas, atlas))
        {
            Software3D.Render(ctx, triangles, camera, size, size);
        }

        if (Software3D.TryProject(camera, faceTl, size, size, out CanvasPoint tl, out _)
            && Software3D.TryProject(camera, faceTr, size, size, out CanvasPoint tr, out _)
            && Software3D.TryProject(camera, faceBr, size, size, out CanvasPoint br, out _)
            && Software3D.TryProject(camera, faceBl, size, size, out CanvasPoint bl, out _))
        {
            // 左上、右上、右下、左下 + 两个三角形索引。
            var positions = new[]
            {
                new SKPoint(tl.X, tl.Y), new SKPoint(tr.X, tr.Y), new SKPoint(br.X, br.Y),
                new SKPoint(tl.X, tl.Y), new SKPoint(br.X, br.Y), new SKPoint(bl.X, bl.Y),
            };
            var texs = new[]
            {
                new SKPoint(src.Left, src.Top), new SKPoint(src.Right, src.Top), new SKPoint(src.Right, src.Bottom),
                new SKPoint(src.Left, src.Top), new SKPoint(src.Right, src.Bottom), new SKPoint(src.Left, src.Bottom),
            };
            var colors = Enumerable.Repeat(SKColors.White, 6).ToArray();
            using var shader = SKShader.CreateImage(atlas.Sheet, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader, IsAntialias = false };
            canvas.DrawVertices(SKVertexMode.Triangles, positions, texs, colors, paint);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        Console.WriteLine($"rendered soft3d tile {tileId} -> {path}");
    }

    private static List<Software3D.Triangle3D> BuildSoft3DTileTriangles(
        out Software3D.Vertex3 faceTl,
        out Software3D.Vertex3 faceTr,
        out Software3D.Vertex3 faceBr,
        out Software3D.Vertex3 faceBl)
    {
        const float w = 0.62f;
        const float h = 0.90f;
        const float d = 0.18f;
        float x = w / 2f;
        float y = h / 2f;
        float z = d / 2f;

        faceTl = new Software3D.Vertex3(-x, y, z);
        faceTr = new Software3D.Vertex3(x, y, z);
        faceBr = new Software3D.Vertex3(x, -y, z);
        faceBl = new Software3D.Vertex3(-x, -y, z);

        var backTl = new Software3D.Vertex3(-x, y, -z);
        var backTr = new Software3D.Vertex3(x, y, -z);
        var backBr = new Software3D.Vertex3(x, -y, -z);
        var backBl = new Software3D.Vertex3(-x, -y, -z);

        var white = Color.Rgb(236, 230, 206);
        var ivory = Color.Rgb(208, 198, 166);
        var blue = Color.Rgb(31, 118, 196);
        var list = new List<Software3D.Triangle3D>(24);

        void Quad(Software3D.Vertex3 a, Software3D.Vertex3 b, Software3D.Vertex3 c, Software3D.Vertex3 e, Color color)
        {
            list.Add(new Software3D.Triangle3D(a, b, c, color));
            list.Add(new Software3D.Triangle3D(a, c, e, color));
        }

        Quad(faceTl, faceTr, faceBr, faceBl, white);
        Quad(backTl, backBl, backBr, backTr, blue);
        Quad(faceTl, faceBl, backBl, backTl, ivory);
        Quad(faceTr, backTr, backBr, faceBr, ivory);
        Quad(faceTl, backTl, backTr, faceTr, ivory);
        Quad(faceBl, faceBr, backBr, backBl, ivory);
        return list;
    }

    private static void Render25DToPng(string[] args)
    {
        string path = args[1];
        var style = TableRenderStyle.Flat;
        for (int i = 2; i + 1 < args.Length; i++)
        {
            if (!string.Equals(args[i], "--variant", StringComparison.Ordinal))
            {
                continue;
            }

            style = args[i + 1].ToLowerInvariant() switch
            {
                "mixed" or "c" => TableRenderStyle.Flat,
                "flat" or "2d" => TableRenderStyle.Flat,
                "medium" or "medium25d" => TableRenderStyle.Flat,
                "light" or "light25d" => TableRenderStyle.Flat,
                "soft3d" or "soft-3d" => TableRenderStyle.Soft3D,
                var other => throw new ArgumentException(L.T("tools.cli.devtools.Render25DToPng.1", ("p1", other))),
            };
        }

        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
        var screen = new TableScreen(atlas, style);
        RenderScreen(path, screen, atlas);
        Console.WriteLine($"rendered 25d variant={style} -> {path}");
    }

    private static void RenderTableToPng(string path, bool debug = false)
    {
        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
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

    /// <summary>1400×1400 的新布局（Yoga 计算，Central 400×400 + 牌河 7×5）。</summary>
    private static TableLayoutSpec NewLayout()
    {
        var layout = new TableLayoutSpec();
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
        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);

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

        Console.WriteLine(L.T("tools.cli.devtools.ExportSvg.1", ("p1", main), ("p2", exporter.TileCount)));
    }

    /// <summary>
    /// <c>--export-ui-svg &lt;file&gt;</c>：合并导出一张 1400×1400 SVG ——
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
        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
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
        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
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
    /// <c>_callable</c>（真听牌，报听按钮出现 + 待牌提示）/ <c>_disabled</c>（不听牌，不出报听按钮）/
    /// <c>_noten</c>（荒牌流局：四家翻牌 + 未听牌灰显 + 已报听标记）。
    /// </summary>
    private static void RenderCallToPng(string basePath)
    {
        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
        var layout = new TableLayoutSpec();
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
        Tiisu.App.Program.TryUseUtf8Console();
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
        Check(L.T("tools.cli.devtools.SmokeCall.1"), engine.IsTenpai(player), "hand=1111m23m456m789m5z");
        Check(L.T("tools.cli.devtools.SmokeCall.2"), engine.DeclareCall(player) && player.IsCalled,
            "waitMask=" + player.CalledWaitMask.ToString("x"));
        Check(L.T("tools.cli.devtools.SmokeCall.3"), !engine.DeclareCall(player), "");
        var junkEngine = NewRound("1234m6789p12345s", 0, out _);
        Check(L.T("tools.cli.devtools.SmokeCall.4"), !junkEngine.DeclareCall(junkEngine.Players[0]), "hand=1234m6789p12345s");

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

        Check(L.T("tools.cli.devtools.SmokeCall.5"), breakTile is not null, breakTile is null ? L.T("tools.cli.devtools.SmokeCall.6") : "tile=" + breakTile.Value.Id + " · " + breakReason);
        if (breakTile is not null)
        {
            int before = player.Hand.Count;
            var blocked = engine.Discard(player, breakTile.Value);
            Check(L.T("tools.cli.devtools.SmokeCall.7"), blocked is null && player.Hand.Count == before, "hand=" + before);
        }

        // ── 3. 吃碰被拒 ─────────────────────────────────────────
        bool ponDenied = !engine.CallAllowsMeld(player, MeldType.Pon, out string? ponReason);
        bool chiDenied = !engine.CallAllowsMeld(player, MeldType.Chi, out _);
        bool minkanDenied = !engine.CallAllowsMeld(player, MeldType.Minkan, out _);
        bool kakanDenied = !engine.CallAllowsMeld(player, MeldType.Kakan, out _);
        Check(L.T("tools.cli.devtools.SmokeCall.8"), ponDenied && chiDenied && minkanDenied && kakanDenied, ponReason ?? string.Empty);

        bool meldThrew = false;
        try
        {
            engine.ApplyMeld(0, MeldType.Pon, new GameTile(GameSuit.Man, 1), 1);
        }
        catch (InvalidOperationException)
        {
            meldThrew = true;
        }

        Check(L.T("tools.cli.devtools.SmokeCall.9"), meldThrew, "ApplyMeld → InvalidOperationException");

        // ── 4. 暗杠例外 ─────────────────────────────────────────
        var kanEngine = NewRound("1111m23m456m789m5z", 0, out _);
        var kanPlayer = kanEngine.Players[0];
        bool kanDeclared = kanEngine.DeclareCall(kanPlayer);

        // 摸 4m：暗杠 1m 后 23m 由 4m 补成 234m，听牌张仍是 5z（形不变 → 允许）。
        kanEngine.Draw(kanPlayer, new GameTile(GameSuit.Man, 4));
        bool ankanAllowed = kanEngine.CallAllowsAnkan(kanPlayer, new GameTile(GameSuit.Man, 1), out string? ankanYes);
        bool ankanOther = kanEngine.CallAllowsAnkan(kanPlayer, new GameTile(GameSuit.Man, 2), out string? ankanNo);
        Check(L.T("tools.cli.devtools.SmokeCall.10"), kanDeclared && ankanAllowed, ankanYes ?? "ankan 1m allowed");
        Check(L.T("tools.cli.devtools.SmokeCall.11"), !ankanOther, ankanNo ?? string.Empty);

        var kakanEngine = NewRound("1111m23m456m789m5z", 0, out _);
        var kakanPlayer = kakanEngine.Players[0];
        kakanEngine.DeclareCall(kakanPlayer);
        kakanEngine.Draw(kakanPlayer, new GameTile(GameSuit.Pin, 9));
        kakanPlayer.AddMeld(MeldType.Pon, new[] { new GameTile(GameSuit.Honor, 5), new GameTile(GameSuit.Honor, 5), new GameTile(GameSuit.Honor, 5) });
        Check(L.T("tools.cli.devtools.SmokeCall.12"), !kakanEngine.DoKan(kakanPlayer, new GameTile(GameSuit.Honor, 5), MeldType.Kakan), L.T("tools.cli.devtools.SmokeCall.13"));

        // ── 5. 普通错和（Z = 赋 × 700）──────────────────────────
        var scoresBefore = engine.Scores.ToArray();
        var penalty = engine.ApplyCallMisfire(0, 2);
        bool minorOk = penalty is not null && !penalty.Severe && penalty.Z == 1400
                       && engine.Scores[0] == scoresBefore[0] - 4200
                       && engine.Scores[1] == scoresBefore[1] + 1400
                       && engine.Scores[2] == scoresBefore[2] + 1400
                       && engine.Scores[3] == scoresBefore[3] + 1400;
        Check(L.T("tools.cli.devtools.SmokeCall.14"), minorOk,
            penalty is null ? "null" : $"fu={penalty.Fu} z={penalty.Z} scores={string.Join(",", engine.Scores)}");
        Check(L.T("tools.cli.devtools.SmokeCall.15"), !player.IsCalled, "isCalled=false");
        Check(L.T("tools.cli.devtools.SmokeCall.16"), engine.IsWinBanned(player), "isWinBanned=true");

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
        Check(L.T("tools.cli.devtools.SmokeCall.17"), severeOk,
            severe is null ? "null" : $"z={severe.Z} paid={string.Join(",", severe.Paid)} scores={string.Join(",", severeEngine.Scores)}");
        Check(L.T("tools.cli.devtools.SmokeCall.18"),
            severeEngine.IsWinBanned(severePlayer) && severeEngine.IsWinBannedNextHand(0), "hand+nextHand=true");

        // ── 7. 流局：已报听者自动算听牌 ─────────────────────────
        var flowEngine = NewRound("1111m23m456m789m5z", 0, out _);
        var flowPlayer = flowEngine.Players[0];
        flowEngine.DeclareCall(flowPlayer);
        flowEngine.Players[3].IsCalled = true; // 非听牌但已报听（直接置位验证「自动算听牌」）
        flowEngine.Flow();
        Check(L.T("tools.cli.devtools.SmokeCall.19"),
            flowEngine.FlowTenpai.Count == 4 && flowEngine.FlowTenpai[0] && flowEngine.FlowTenpai[3],
            "tenpai=" + string.Join(",", flowEngine.FlowTenpai));
        Check(L.T("tools.cli.devtools.SmokeCall.20"),
            !flowEngine.FlowTenpai[1] && !flowEngine.FlowTenpai[2],
            "seat1/2 noten");
        Check(L.T("tools.cli.devtools.SmokeCall.21"), flowEngine.RevealHands, "revealHands=true");

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
        Tiisu.App.Program.TryUseUtf8Console();
        if (args.Length < 2
            || !uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint seed))
        {
            Console.Error.WriteLine(L.T("tools.cli.devtools.PlayReal.1"));
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
                string stall = string.Empty;
            int lastTurn = -1;
            MatchPhase lastPhase = MatchPhase.Idle;
            int sameState = 0;

            while (clock.Elapsed < TimeSpan.FromSeconds(45))
            {
                if (++steps > 200_000)
                {
                    stall = L.T("tools.cli.devtools.PlayReal.2");
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
                        stall = L.T("tools.cli.devtools.PlayReal.3", ("p1", phase), ("p2", runtime.Engine.TurnIndex), ("p3", handCount), ("p4", runtime.Snapshot.Actions));
                        break;
                    }
                }
                else
                {
                    sameState = 0;
                    lastPhase = phase;
                    lastTurn = runtime.Engine.TurnIndex;
                    if (Tiisu.Core.TiisuConfig.Current.PlayTrace)
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
                                stall = L.T("tools.cli.devtools.PlayReal.4", ("p1", beforeCount), ("p2", player.Hand.Count))
                                        + L.T("tools.cli.devtools.PlayReal.5", ("p1", concealed), ("p2", concealed + 1));
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
                            stall = L.T("tools.cli.devtools.PlayReal.noPlayableTile", ("count", player.Hand.Count), ("hand", string.Join(" ", player.Hand.Select(t => t.Id))), ("called", player.IsCalled));
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
                stall = L.T("tools.cli.devtools.PlayReal.timeoutNotFinished", ("phase", runtime.Phase));
            }

            Console.WriteLine($"play-real game={game + 1}/{games} seed={gameSeed} mode={mode} "
                + L.T("tools.cli.devtools.PlayReal.10", ("p1", (clock.Elapsed.TotalSeconds).ToString("F1", CultureInfo.InvariantCulture)), ("p2", steps), ("p3", ronWindows), ("p4", ronPasses))
                + L.T("tools.cli.devtools.PlayReal.11", ("p1", callDeclares), ("p2", runtime.Phase), ("p3", (stall.Length == 0 ? "OK" : "FAIL " + stall))));

            if (stall.Length > 0)
            {
                failures++;
            }
        }

        Console.WriteLine(failures == 0 ? "play-real: all games passed" : $"play-real: {failures} game(s) failed");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// <c>--smoke-called-ron-pass [seed]</c>：报听后出现荣和窗口，必须能「过」并继续。
    /// </summary>
    private static int SmokeCalledRonPass(string[] args)
    {
        Tiisu.App.Program.TryUseUtf8Console();
        uint seed = 3;
        if (args.Length >= 2 && !uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
        {
            seed = 3;
        }

        var runtime = new MatchRuntime(new Settings(), new Mulberry32Random(seed), _ => { },
            saveStore: null, stats: null, roundLog: null, matchSeed: seed);
        runtime.StartMatch(4);

        bool called = false;
        bool sawCalledRon = false;
        bool passedOk = false;
        bool lastPassFlag = false;
        string detail = L.T("tools.cli.devtools.SmokeCalledRonPass.1");
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < TimeSpan.FromSeconds(40) && runtime.Phase != MatchPhase.MatchOver)
        {
            var phase = runtime.Phase;
            if (phase == MatchPhase.RoundEnded)
            {
                runtime.NextRound();
                called = false;
                continue;
            }

            var player = runtime.Engine.Players[runtime.HumanIndex];
            if (phase == MatchPhase.HumanDiscard)
            {
                var vm = runtime.Snapshot;
                if (!called && (vm.Actions & HumanAction.DeclareCall) != 0)
                {
                    int idx = TenpaiDiscardIndex(runtime.Engine, player);
                    if (idx >= 0)
                    {
                        runtime.Execute(GameCommand.DeclareCall(idx));
                        called = player.IsCalled;
                        continue;
                    }
                }

                int pick = player.IsCalled
                    ? LegalCalledDiscardIndex(runtime.Engine, player)
                    : BestShantenDiscardIndex(player);
                if (pick < 0)
                {
                    detail = L.T("tools.cli.devtools.SmokeCalledRonPass.2");
                    break;
                }

                runtime.Execute(GameCommand.Discard(pick));
                continue;
            }

            if (phase == MatchPhase.HumanRon)
            {
                if (player.IsCalled)
                {
                    sawCalledRon = true;
                    var ronVm = runtime.Snapshot;
                    bool hasPassFlag = (ronVm.Actions & HumanAction.Pass) != 0;
                    lastPassFlag = hasPassFlag;
                    var bar = new ActionBar();
                    var barLayout = new TableLayoutSpec();
                    YogaTableLayout.Apply(barLayout);
                    bar.Build(ronVm, barLayout);
                    var passBtn = bar.Buttons.FirstOrDefault(b => b.Kind == ActionBar.Kind.Pass);
                    bool passClickable = passBtn is not null && passBtn.Enabled;
                    bool overlap = false;
                    if (passBtn is not null)
                    {
                        foreach (var other in bar.Buttons)
                        {
                            if (ReferenceEquals(other, passBtn))
                            {
                                continue;
                            }

                            var a = passBtn.Rect;
                            var b = other.Rect;
                            if (a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom)
                            {
                                overlap = true;
                            }
                        }
                    }

                    Console.WriteLine(L.T("tools.cli.devtools.SmokeCalledRonPass.3", ("p1", ronVm.Actions), ("p2", hasPassFlag))
                        + L.T("tools.cli.devtools.SmokeCalledRonPass.4", ("p1", (passBtn is null ? L.T("tools.cli.devtools.SmokeCalledRonPass.noPassButton") : passBtn.Label)), ("p2", passClickable), ("p3", overlap)));
                    int before = player.Hand.Count;
                    runtime.Execute(GameCommand.Pass());
                    passedOk = runtime.Phase != MatchPhase.HumanRon || player.Hand.Count == before;
                    detail = L.T("tools.cli.devtools.SmokeCalledRonPass.5", ("p1", runtime.Phase), ("p2", before), ("p3", player.Hand.Count));
                    break;
                }

                runtime.Execute(GameCommand.Ron());
                continue;
            }

            if (phase == MatchPhase.HumanMeld)
            {
                runtime.Execute(GameCommand.Pass());
                continue;
            }

            Thread.Sleep(5);
        }

        var legalFlags = runtime.Snapshot.Seats.Count > 0 ? runtime.Snapshot.Seats[0].LegalHand : Array.Empty<bool>();
        bool lockOk = runtime.Engine.Players[runtime.HumanIndex].IsCalled
            ? legalFlags.Count > 0 && legalFlags.Any(f => !f)
            : true;
        bool ok = sawCalledRon && passedOk && lastPassFlag && lockOk;
        int meldSeen = 0, claimSeen = 0;
        foreach (var s in runtime.Snapshot.Seats)
        {
            foreach (var m in s.Melds)
            {
                meldSeen++;
                if (m.ClaimedIndex >= 0)
                {
                    claimSeen++;
                }
            }
        }

        Console.WriteLine(L.T("tools.cli.devtools.SmokeCalledRonPass.6", ("p1", meldSeen), ("p2", claimSeen)));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeCalledRonPass.7", ("p1", seed), ("p2", called), ("p3", sawCalledRon))
            + L.T("tools.cli.devtools.SmokeCalledRonPass.8", ("p1", detail), ("p2", lockOk), ("p3", (ok ? "PASS" : "FAIL"))));
        return ok ? 0 : 1;
    }

    /// <summary><c>--list-locks</c>：打印四座锁档循环结果，确认菜单能选到 7Neuu。</summary>
    private static int ListLocks()
    {
        Tiisu.App.Program.TryUseUtf8Console();
        var settings = new Settings();
        settings.AiSeatLocks = new[] { -1, -1, -1, -1 };
        for (int seat = 0; seat < 4; seat++)
        {
            var seq = new List<string>();
            for (int i = 0; i < 7; i++)
            {
                int next = settings.AiSeatLocks[seat] + 1;
                settings.AiSeatLocks[seat] = next > 4 ? -1 : next;
                seq.Add(settings.AiSeatLocks[seat] < 0 ? L.T("tools.cli.devtools.ListLocks.1") : new[] { L.T("tools.cli.devtools.ListLocks.2"), L.T("tools.cli.devtools.ListLocks.3"), L.T("tools.cli.devtools.ListLocks.4"), L.T("tools.cli.devtools.ListLocks.5"), "7Neuu" }[settings.AiSeatLocks[seat]]);
            }

            settings.AiSeatLocks[seat] = -1;
            Console.WriteLine(L.T("tools.cli.devtools.ListLocks.6", ("p1", seat), ("p2", string.Join(" → ", seq))));
        }

        return 0;
    }
    /// <summary><c>--smoke-7neuu</c>：设置里把某座锁到 7Neuu，引擎必须真的走小策略网。</summary>
    private static int Smoke7Neuu()
    {
        Tiisu.App.Program.TryUseUtf8Console();
        var settings = new Settings();
        settings.AiSeatLocks = new[] { 4, -1, -1, -1 };
        var runtime = new MatchRuntime(settings, new Mulberry32Random(7), _ => { },
            saveStore: null, stats: null, roundLog: null, matchSeed: 7);
        runtime.AllSeatsAi = true;
        runtime.StartMatch(4);
        int guard = 0;
        while (runtime.Phase != MatchPhase.MatchOver && guard++ < 500)
        {
            if (runtime.Phase == MatchPhase.RoundEnded)
            {
                runtime.NextRound();
            }
            else
            {
                Thread.Sleep(2);
            }
        }

        bool ok = runtime.NeuralDecisions > 0;
        Console.WriteLine(L.T("tools.cli.devtools.Smoke7Neuu.1", ("p1", runtime.NeuralDecisions), ("p2", runtime.NeuralDifferent), ("p3", (ok ? " PASS" : " FAIL"))));
        return ok ? 0 : 1;
    }

    /// <summary>
    /// <c>--selfplay N [--out file] [--start-seed K] [--seats 0,1] [--baseline]</c>
    /// 无人值守批量自对弈：每局一行 JSONL，追加写入，可中断后从 --start-seed 续跑。
    /// </summary>
    private static int SelfPlay(string[] args)
    {
        Tiisu.App.Program.TryUseUtf8Console();
        int games = 100;
        string outPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TiisuMahjong", "logs", "selfplay_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".jsonl");
        uint startSeed = 1;
        var seats = new List<int> { 0 };
        bool baseline = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out":
                    if (i + 1 < args.Length) { outPath = args[++i]; }
                    break;
                case "--start-seed":
                    if (i + 1 < args.Length && uint.TryParse(args[++i], out uint s)) { startSeed = s; }
                    break;
                case "--baseline":
                    baseline = true;
                    break;
                case "--seats":
                    if (i + 1 < args.Length)
                    {
                        seats = args[++i].Split(',')
                            .Select(x => int.TryParse(x, out int v) ? v : -1)
                            .Where(v => v is >= 0 and <= 3)
                            .ToList();
                    }
                    break;
                default:
                    if (int.TryParse(args[i], out int n)) { games = Math.Max(1, n); }
                    break;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        var lines = new List<string>();
        double rankSum = 0;
        int wins = 0;
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < games; i++)
        {
            uint seed = startSeed + (uint)i;
            int seat = seats.Count == 0 ? 0 : seats[i % seats.Count];
            var settings = new Settings();
            var runtime = new MatchRuntime(settings, new Mulberry32Random(seed), _ => { },
                saveStore: null, stats: null, roundLog: null, matchSeed: seed);
            runtime.AllSeatsAi = true;
            if (!baseline)
            {
                runtime.NeuralSeats.Add(seat);
            }

            runtime.StartMatch(4);
            int guard = 0;
            while (runtime.Phase != MatchPhase.MatchOver && guard++ < 5000)
            {
                if (runtime.Phase == MatchPhase.RoundEnded)
                {
                    runtime.NextRound();
                }
                else
                {
                    Thread.Sleep(1);
                }
            }

            var scores = runtime.Engine.Scores;
            int rank = 1 + scores.Count(s => s > scores[seat]);
            rankSum += rank;
            if (rank == 1)
            {
                wins++;
            }

            var record = new
            {
                ts = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
                seed,
                seat,
                neural = !baseline,
                rank,
                scores,
                rounds = runtime.Engine.HandsPlayed,
                decisions = runtime.NeuralDecisions,
                different = runtime.NeuralDifferent,
                ms = sw.ElapsedMilliseconds,
            };
            string json = JsonSerializer.Serialize(record, new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            lines.Add(json);
            File.AppendAllText(outPath, json + "\n", new System.Text.UTF8Encoding(false));

            if ((i + 1) % 10 == 0 || i + 1 == games)
            {
                Console.WriteLine(L.T("tools.cli.devtools.SelfPlay.1", ("p1", i + 1), ("p2", games), ("p3", (rankSum / (i + 1)).ToString("F2", CultureInfo.InvariantCulture)), ("p4", (wins / (double)(i + 1)).ToString("P0", CultureInfo.InvariantCulture))));
            }
        }

        Console.WriteLine(L.T("tools.cli.devtools.SelfPlay.2", ("p1", games), ("p2", outPath)));
        Console.WriteLine(L.T("tools.cli.devtools.SelfPlay.3", ("p1", (rankSum / games).ToString("F2", CultureInfo.InvariantCulture)), ("p2", (wins / (double)games).ToString("P0", CultureInfo.InvariantCulture)), ("p3", (sw.Elapsed.TotalSeconds).ToString("F0", CultureInfo.InvariantCulture))));
        return 0;
    }
    /// <summary><c>--smoke-ab [games]</c>：指定座位走小策略网，其余 TieredAi，统计该座位顺位。</summary>
    private static int SmokeAb(string[] args)
    {
        Tiisu.App.Program.TryUseUtf8Console();
        int games = args.Length >= 2 && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 8;
        var neuralRanks = new List<int>();
        var baseRanks = new List<int>();
        int decisions = 0;
        int different = 0;
        int aiCalls = 0;

        for (int seed = 1; seed <= games; seed++)
        {
            foreach (bool useNeural in new[] { true, false })
            {
                var settings = new Settings();
                var runtime = new MatchRuntime(settings, new Mulberry32Random((uint)seed), _ => { },
                    saveStore: null, stats: null, roundLog: null, matchSeed: (uint)seed);
                runtime.AllSeatsAi = true;
                int seat = (seed - 1) % 4;
                if (useNeural)
                {
                    runtime.NeuralSeats.Add(seat);
                }

                runtime.StartMatch(4);
                int guard = 0;
                while (runtime.Phase != MatchPhase.MatchOver && guard++ < 3000)
                {
                    if (runtime.Phase == MatchPhase.RoundEnded)
                    {
                        runtime.NextRound();
                    }
                    else
                    {
                        Thread.Sleep(5);
                    }
                }

                var scores = runtime.Engine.Scores;
                int rank = 1 + scores.Count(s => s > scores[seat]);
                (useNeural ? neuralRanks : baseRanks).Add(rank);
                if (useNeural)
                {
                    decisions += runtime.NeuralDecisions;
                    different += runtime.NeuralDifferent;
                }

                aiCalls = Math.Max(aiCalls, runtime.AiDiscardDecisions);
            }
        }

        double Avg(List<int> list) => list.Count == 0 ? 0 : list.Average();
        double WinRate(List<int> list) => list.Count == 0 ? 0 : list.Count(r => r == 1) / (double)list.Count;
        Console.WriteLine($"smoke-ab: games={games}");
        Console.WriteLine(L.T("tools.cli.devtools.SmokeAb.1", ("p1", (Avg(neuralRanks)).ToString("F2", CultureInfo.InvariantCulture)), ("p2", (WinRate(neuralRanks)).ToString("P0", CultureInfo.InvariantCulture))));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeAb.2", ("p1", (Avg(baseRanks)).ToString("F2", CultureInfo.InvariantCulture)), ("p2", (WinRate(baseRanks)).ToString("P0", CultureInfo.InvariantCulture))));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeAb.3", ("p1", decisions), ("p2", different), ("p3", aiCalls)));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeAb.4", ("p1", string.Join(",", neuralRanks)), ("p2", string.Join(",", baseRanks))));
        return 0;
    }

    // ─────────────────────────────────────────────────────────────
    // A/B 评测：v4 基准 vs 追役 / v2 / 候选模型 / 打分机；MCTS 单独隔离
    // ─────────────────────────────────────────────────────────────

    /// <summary>全部可选组的规范顺序（MCTS 放最后，默认不跑）。</summary>
    internal static readonly string[] AbAiAllVariants =
        { "baseline", "pursuit", "v2", "wide", "simple", "normal", "mcts" };

    /// <summary>未显式给 <c>--variants</c> 时的默认组：不含 MCTS（200ms/步太慢）。</summary>
    internal static readonly string[] AbAiDefaultVariants = { "baseline", "simple", "normal" };

    /// <summary>稀有役关注列表（按结算面板的役名计数，用于追役 A/B 的「稀有役次数必须上升」）。</summary>
    internal static readonly string[] AbAiRareYakuNames =
    {
        "七对子", "混一色", "清一色", "字一色", "九莲宝灯", "四暗杠", "四明杠", "四杠子",
        "国士无双", "大三元", "四暗刻", "绿一色", "清老头", "四喜和",
    };

    /// <summary>MCTS 隔离说明（CLI 与报告都标注）。</summary>
    internal const string AbAiMctsNote =
        "MCTS 组按现有 AiTuning 为 200ms/步（关键局面 300 次模拟），耗时远高于其它组，单列且不可与其他组同表比较。";

    /// <summary>双巡目口径（原样写进 JSON/Markdown 输出）。</summary>
    internal const string AbAiTurnDefinition =
        "双巡目口径：discardTurn = ceil(该局总弃牌数 / 4)；总弃牌数 = 四家牌河张数之和（荣和铳张计入放铳者牌河），0 弃牌记第 1 巡。"
        + "drawTurn = ceil((83 - wall) / 4)；wall = 局末活牌山张数，wall = 0（荒牌/海底）按摸完 83 张记，drawTurn = 21。"
        + "和牌巡目只统计有和牌者的局；同时保留局末墙牌数 wall 供复核。";

    internal sealed record AbAiOptions(
        int Games,
        IReadOnlyList<string> Variants,
        string? V2Path,
        string? CandidatePath,
        bool PursuitArm,
        bool Pursuit,
        string Style,
        int Mode,
        uint StartSeed,
        string OutDirectory,
        bool Quiet);

    internal sealed record AbAiGameRow(
        uint Seed,
        int GameIndex,
        int Seat,
        int Rank,
        int Rounds,
        int FocusWins,
        int FocusDealIns,
        int FocusMelds,
        int NeuralDecisions,
        int NeuralDifferent,
        int PursuitEntries,
        int PursuitDecisions,
        double FocusEntropy,
        int[] Scores);

    internal sealed record AbAiRoundRow(
        uint Seed,
        int Hand,
        string Kind,
        int[] Winners,
        int[] Payers,
        int TotalDiscards,
        int Wall,
        int DiscardTurn,
        int DrawTurn,
        string[] Yaku,
        int[] ScoresAfter);

    /// <summary>和牌时间分布的一个巡目桶（累计口径）。</summary>
    internal sealed record AbAiTurnBucket(
        string Bucket,
        int MinTurn,
        int MaxTurn,
        int Wins,
        int CumulativeWins,
        double CumulativeWinRate,
        int Flows,
        int CumulativeFlows,
        double CumulativeFlowRate);

    internal sealed record AbAiVariantReport(
        string Variant,
        string FocusDescription,
        bool Isolated,
        int Games,
        int Rounds,
        int FocusSeatWins,
        int FocusSeatDealIns,
        int FocusSeatMelds,
        double AvgRank,
        double Top1Rate,
        double Top2Rate,
        double RoundWinRate,
        double DealInRate,
        double FlowRate,
        double MeldsPerRound,
        double AvgWinnersPerRound,
        double AvgTurnPerRound,
        double DiscardTurnMean,
        double DiscardTurnP50,
        double DiscardTurnP90,
        double DrawTurnMean,
        double DrawTurnP50,
        double DrawTurnP90,
        double WinWallMean,
        double FocusWinTurnMean,
        double FocusWinTurnP50,
        double FocusWinTurnP90,
        double StrategyEntropy,
        int YakuCoverage,
        IReadOnlyDictionary<string, int> YakuCounts,
        IReadOnlyDictionary<string, int> RareYakuCounts,
        IReadOnlyList<AbAiTurnBucket> Histogram,
        string? ModelPath,
        int ModelFeatures,
        int ModelHidden,
        int ModelLayers,
        int NeuralDecisions,
        int NeuralDifferent,
        int PursuitEntries,
        int PursuitExits,
        int PursuitDecisions,
        IReadOnlyList<AbAiGameRow> GameRows,
        IReadOnlyList<AbAiRoundRow> RoundRows);

    internal sealed record AbAiReport(
        AbAiOptions Options,
        string TurnDefinition,
        string MctsNote,
        IReadOnlyList<AbAiVariantReport> Variants);

    /// <summary>
    /// 解析 <c>--ab-ai</c> 参数。非法（未知组 / v2/候选缺路径 / 未知 preset / 负数局数）抛
    /// <see cref="ArgumentException"/>，CLI 捕获后返回退出码 2。
    /// </summary>
    internal static AbAiOptions ParseAbAiOptions(string[] args)
    {
        int games = 100;
        var variants = new List<string>(AbAiDefaultVariants);
        bool variantsSpecified = false;
        string? v2Path = null;
        string? candidatePath = null;
        bool pursuitArm = false;
        bool pursuit = false;
        string style = "balanced";
        int mode = 4;
        uint startSeed = 1;
        string outDir = Path.Combine("artifacts", "ab");
        bool quiet = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--games":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int g) || g <= 0)
                    {
                        throw new ArgumentException("--games 需要正整数");
                    }

                    games = Math.Min(g, 100_000);
                    break;
                case "--variants":
                case "--variant":
                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException("--variants 需要逗号分隔的组名");
                    }

                    variants = ParseAbAiVariants(args[++i]);
                    variantsSpecified = true;
                    break;
                case "--v2":
                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException("--v2 需要权重文件路径");
                    }

                    v2Path = args[++i];
                    break;
                case "--candidate":
                case "--model":
                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException("--candidate 需要候选权重文件路径");
                    }

                    candidatePath = args[++i];
                    break;
                case "--pursuit-arm":
                    pursuitArm = true;
                    break;
                case "--pursuit":
                    pursuit = true;
                    break;
                case "--style":
                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException("--style 需要 steady|balanced|pursuit");
                    }

                    style = args[++i];
                    break;
                case "--mode":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) || m <= 0)
                    {
                        throw new ArgumentException("--mode 需要正整数");
                    }

                    mode = Math.Min(m, 32);
                    break;
                case "--start-seed":
                    if (i + 1 >= args.Length || !uint.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint sd))
                    {
                        throw new ArgumentException("--start-seed 需要非负整数");
                    }

                    startSeed = sd;
                    break;
                case "--out":
                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException("--out 需要输出目录");
                    }

                    outDir = args[++i];
                    break;
                case "--quiet":
                    quiet = true;
                    break;
                default:
                    // 兼容 `--ab-ai 100` 这种旧写法。
                    if (int.TryParse(args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int legacyGames) && legacyGames > 0)
                    {
                        games = Math.Min(legacyGames, 100_000);
                    }
                    else
                    {
                        throw new ArgumentException($"未知参数: {args[i]}");
                    }

                    break;
            }
        }

        if (!AbAiStylePreset(style, out _))
        {
            throw new ArgumentException($"未知 --style: {style}（可选 steady|balanced|pursuit）");
        }

        // 未显式给 --variants：默认 baseline + simple + normal，并按传入路径/开关追加组。
        // 明确不含 MCTS（200ms/步不能和其它组一起跑）。
        if (!variantsSpecified)
        {
            var defaults = new List<string>(AbAiDefaultVariants);
            if (pursuitArm)
            {
                defaults.Add("pursuit");
            }

            if (!string.IsNullOrWhiteSpace(v2Path))
            {
                defaults.Add("v2");
            }

            if (!string.IsNullOrWhiteSpace(candidatePath))
            {
                defaults.Add("wide");
            }

            variants = AbAiAllVariants.Where(v => defaults.Contains(v, StringComparer.Ordinal)).ToList();
        }
        else if (pursuitArm && !variants.Contains("pursuit", StringComparer.Ordinal))
        {
            // 显式给 --variants 时 --pursuit-arm 仍然保证追役臂在场。
            var withPursuit = new List<string>(variants) { "pursuit" };
            variants = AbAiAllVariants.Where(v => withPursuit.Contains(v, StringComparer.Ordinal)).ToList();
        }

        if (variants.Contains("v2", StringComparer.Ordinal) && string.IsNullOrWhiteSpace(v2Path))
        {
            throw new ArgumentException("选到 v2 组时必须用 --v2 <path> 指定 v2 权重文件");
        }

        if (!string.IsNullOrWhiteSpace(v2Path) && !File.Exists(v2Path))
        {
            throw new ArgumentException($"--v2 权重文件不存在: {v2Path}");
        }

        if (variants.Contains("wide", StringComparer.Ordinal) && string.IsNullOrWhiteSpace(candidatePath))
        {
            throw new ArgumentException("选到 wide 组时必须用 --candidate <path> 指定候选权重文件");
        }

        if (!string.IsNullOrWhiteSpace(candidatePath) && !File.Exists(candidatePath))
        {
            throw new ArgumentException($"--candidate 权重文件不存在: {candidatePath}");
        }

        return new AbAiOptions(
            games, variants, v2Path, candidatePath, pursuitArm, pursuit, style, mode, startSeed, outDir, quiet);
    }

    private static List<string> ParseAbAiVariants(string spec)
    {
        var result = new List<string>();
        foreach (string raw in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string name = raw.ToLowerInvariant() switch
            {
                "all" => null!,
                "v4" or "b" or "neural" => "baseline",
                _ => raw.ToLowerInvariant(),
            };

            if (name is null)
            {
                foreach (string all in AbAiAllVariants)
                {
                    if (!result.Contains(all, StringComparer.Ordinal))
                    {
                        result.Add(all);
                    }
                }

                continue;
            }

            if (!AbAiAllVariants.Contains(name, StringComparer.Ordinal))
            {
                throw new ArgumentException($"未知 A/B 组: {raw}（可选 {string.Join(",", AbAiAllVariants)} 或 all）");
            }

            if (!result.Contains(name, StringComparer.Ordinal))
            {
                result.Add(name);
            }
        }

        if (result.Count == 0)
        {
            throw new ArgumentException("--variants 为空");
        }

        // 保持规范顺序，输出稳定（MCTS 永远排最后）。
        return AbAiAllVariants.Where(v => result.Contains(v, StringComparer.Ordinal)).ToList();
    }

    private static bool AbAiStylePreset(string name, out AiStyle style)
    {
        switch (name.ToLowerInvariant())
        {
            case "steady":
                style = AiStyle.Steady;
                return true;
            case "balanced":
                style = AiStyle.Balanced;
                return true;
            case "pursuit":
                style = AiStyle.PursuitFocused;
                return true;
            default:
                style = AiStyle.Default;
                return false;
        }
    }

    /// <summary>
    /// 双巡目之一：<c>drawTurn = ceil((83 - wall) / 4)</c>。wall = 局末墙牌；
    /// wall = 0（荒牌/海底）按摸完 83 张记 → 21 巡。
    /// </summary>
    internal static int AbAiDrawTurn(int wall) => (83 - Math.Clamp(wall, 0, 83) + 3) / 4;

    /// <summary>按巡目桶（1-6 / 7-12 / 13-18 / 19+）累计和牌率 / 流局率。</summary>
    internal static IReadOnlyList<AbAiTurnBucket> AbAiBuildTurnHistogram(
        IReadOnlyList<int> winTurns,
        IReadOnlyList<int> flowTurns,
        int totalRounds)
    {
        (string Bucket, int Min, int Max)[] buckets =
        {
            ("1-6", 1, 6),
            ("7-12", 7, 12),
            ("13-18", 13, 18),
            ("19+", 19, int.MaxValue),
        };

        var result = new List<AbAiTurnBucket>(buckets.Length);
        int cumulativeWins = 0;
        int cumulativeFlows = 0;
        foreach (var (bucket, min, max) in buckets)
        {
            int wins = winTurns.Count(t => t >= min && t <= max);
            int flows = flowTurns.Count(t => t >= min && t <= max);
            cumulativeWins += wins;
            cumulativeFlows += flows;
            result.Add(new AbAiTurnBucket(
                bucket,
                min,
                max,
                wins,
                cumulativeWins,
                totalRounds == 0 ? 0 : cumulativeWins / (double)totalRounds,
                flows,
                cumulativeFlows,
                totalRounds == 0 ? 0 : cumulativeFlows / (double)totalRounds));
        }

        return result;
    }

    /// <summary>
    /// 跑选定组 A/B：同一批 seed，组内焦点座位轮换 0..3；
    /// 焦点座位用该组的档位 / 策略权重路径 / 追役开关，其余三家固定 7Neuu + 默认 v4。
    /// 不写文件（<see cref="WriteAbAiReport"/> 负责落盘），方便单测直接读返回值。
    /// </summary>
    internal static AbAiReport RunAbAi(AbAiOptions options, Action<string>? log = null)
    {
        AbAiStylePreset(options.Style, out AiStyle style);
        var variantReports = new List<AbAiVariantReport>();

        // 策略熵诊断：只在本函数执行期间开启（按座位累计，每局前清空），结束恢复关闭。
        NeuralPolicyAi.CollectDiagnostics = true;
        try
        {
            foreach (string variant in options.Variants)
            {
                var gameRows = new List<AbAiGameRow>(options.Games);
                var roundRows = new List<AbAiRoundRow>(options.Games * 4);
                var winTurns = new List<int>();
                var drawTurns = new List<int>();
                var winWalls = new List<double>();
                var focusWinTurns = new List<int>();
                var flowTurns = new List<int>();
                var yakuCounts = new Dictionary<string, int>(StringComparer.Ordinal);

                double rankSum = 0;
                int top1 = 0;
                int top2 = 0;
                int rounds = 0;
                int focusWins = 0;
                int focusDeals = 0;
                int focusMelds = 0;
                double winnersSum = 0;
                double turnSum = 0;
                double entropySum = 0;
                long entropyDecisions = 0;
                int neuralDecisions = 0;
                int neuralDifferent = 0;
                int pursuitEntries = 0;
                int pursuitExits = 0;
                int pursuitDecisions = 0;

                var setup = AbAiVariantSetup(variant, options.V2Path, options.CandidatePath, options.Pursuit);
                NeuralModelInfo? model = null;
                if (setup.Level == AiLevel.Neural)
                {
                    model = NeuralPolicyAi.DescribeWeights(setup.WeightsPath);
                }

                for (int gameIndex = 0; gameIndex < options.Games; gameIndex++)
                {
                    uint seed = options.StartSeed + (uint)gameIndex;
                    int seat = (int)((seed - options.StartSeed) % 4);

                    var settings = new Settings
                    {
                        AiSeatLocks = new[] { 4, 4, 4, 4 },
                        AiStyle = style,
                    };

                    var runtime = new MatchRuntime(
                        settings,
                        new Mulberry32Random(seed),
                        _ => { },
                        saveStore: null,
                        stats: null,
                        roundLog: null,
                        matchSeed: seed)
                    {
                        AllSeatsAi = true,
                        AiAbOverride = new MatchRuntime.AiSeatAbOverride(
                            seat, setup.Level, setup.WeightsPath, setup.Pursuit, style, setup.Mcts),
                    };

                    NeuralPolicyAi.ResetDiagnostics();
                    runtime.StartMatch(options.Mode);
                    int guard = 0;
                    while (runtime.Phase != MatchPhase.MatchOver && guard++ < 10_000)
                    {
                        if (runtime.Phase == MatchPhase.RoundEnded)
                        {
                            runtime.NextRound();
                        }
                        else
                        {
                            Thread.Sleep(1);
                        }
                    }

                    var scores = runtime.Engine.Scores.ToArray();
                    int rank = 1 + scores.Count(s => s > scores[seat]);
                    rankSum += rank;
                    if (rank == 1)
                    {
                        top1++;
                    }

                    if (rank <= 2)
                    {
                        top2++;
                    }

                    int gameFocusWins = 0;
                    int gameFocusDeals = 0;
                    int gameFocusMelds = 0;
                    foreach (var round in runtime.RoundStats)
                    {
                        int[] winners = round.Winners.ToArray();
                        int[] payers = round.Payers.ToArray();
                        int discardTurn = round.Turn;
                        int drawTurn = AbAiDrawTurn(round.Wall);
                        roundRows.Add(new AbAiRoundRow(
                            seed, round.HandNumber, round.Kind, winners, payers,
                            round.TotalDiscards, round.Wall, discardTurn, drawTurn,
                            round.YakuNames.ToArray(), round.ScoresAfter.ToArray()));

                        rounds++;
                        winnersSum += winners.Length;
                        turnSum += discardTurn;
                        if (winners.Length > 0)
                        {
                            winTurns.Add(discardTurn);
                            drawTurns.Add(drawTurn);
                            winWalls.Add(round.Wall);
                        }
                        else if (round.Kind is "flow" or "special_flow")
                        {
                            flowTurns.Add(discardTurn);
                        }

                        if (winners.Contains(seat))
                        {
                            focusWins++;
                            gameFocusWins++;
                            focusWinTurns.Add(discardTurn);
                        }

                        if (payers.Contains(seat))
                        {
                            focusDeals++;
                            gameFocusDeals++;
                        }

                        gameFocusMelds += round.MeldsBySeat[seat];
                        focusMelds += round.MeldsBySeat[seat];
                        foreach (string yaku in round.YakuNames)
                        {
                            yakuCounts[yaku] = yakuCounts.TryGetValue(yaku, out int count) ? count + 1 : 1;
                        }
                    }

                    double focusEntropy = NeuralPolicyAi.DiagnosticsAverageEntropyForPlayer(seat);
                    long focusEntropyDecisions = NeuralPolicyAi.DiagnosticsDecisionsForPlayer(seat);
                    entropySum += focusEntropy * focusEntropyDecisions;
                    entropyDecisions += focusEntropyDecisions;

                    neuralDecisions += runtime.NeuralDecisions;
                    neuralDifferent += runtime.NeuralDifferent;
                    pursuitEntries += runtime.AiPursuitEntries;
                    pursuitExits += runtime.AiPursuitExits;
                    pursuitDecisions += runtime.AiPursuitDecisions;

                    gameRows.Add(new AbAiGameRow(
                        seed, gameIndex, seat, rank, runtime.RoundStats.Count,
                        gameFocusWins, gameFocusDeals, gameFocusMelds,
                        runtime.NeuralDecisions, runtime.NeuralDifferent,
                        runtime.AiPursuitEntries, runtime.AiPursuitDecisions,
                        focusEntropy, scores));

                    log?.Invoke(
                        $"ab-ai[{variant}] {gameIndex + 1}/{options.Games} seed={seed} seat={seat} rank={rank}");
                }

                var rareYakuCounts = AbAiRareYakuNames
                    .Where(yakuCounts.ContainsKey)
                    .ToDictionary(name => name, name => yakuCounts[name], StringComparer.Ordinal);
                int flowRounds = flowTurns.Count;

                variantReports.Add(new AbAiVariantReport(
                    variant,
                    setup.Description,
                    setup.Isolated,
                    options.Games,
                    rounds,
                    focusWins,
                    focusDeals,
                    focusMelds,
                    options.Games == 0 ? 0 : rankSum / options.Games,
                    options.Games == 0 ? 0 : top1 / (double)options.Games,
                    options.Games == 0 ? 0 : top2 / (double)options.Games,
                    rounds == 0 ? 0 : focusWins / (double)rounds,
                    rounds == 0 ? 0 : focusDeals / (double)rounds,
                    rounds == 0 ? 0 : flowRounds / (double)rounds,
                    rounds == 0 ? 0 : focusMelds / (double)rounds,
                    rounds == 0 ? 0 : winnersSum / rounds,
                    rounds == 0 ? 0 : turnSum / rounds,
                    Mean(winTurns),
                    Percentile(winTurns, 0.5),
                    Percentile(winTurns, 0.9),
                    Mean(drawTurns),
                    Percentile(drawTurns, 0.5),
                    Percentile(drawTurns, 0.9),
                    Mean(winWalls),
                    Mean(focusWinTurns),
                    Percentile(focusWinTurns, 0.5),
                    Percentile(focusWinTurns, 0.9),
                    entropyDecisions == 0 ? 0 : entropySum / entropyDecisions,
                    yakuCounts.Count,
                    yakuCounts,
                    rareYakuCounts,
                    AbAiBuildTurnHistogram(winTurns, flowTurns, rounds),
                    model?.Path,
                    model?.Features ?? 0,
                    model?.Hidden ?? 0,
                    model?.LayerCount ?? 0,
                    neuralDecisions,
                    neuralDifferent,
                    pursuitEntries,
                    pursuitExits,
                    pursuitDecisions,
                    gameRows,
                    roundRows));
            }
        }
        finally
        {
            NeuralPolicyAi.ResetDiagnostics();
            NeuralPolicyAi.CollectDiagnostics = false;
        }

        return new AbAiReport(options, AbAiTurnDefinition, AbAiMctsNote, variantReports);
    }

    private static (AiLevel Level, string? WeightsPath, bool Mcts, bool Pursuit, bool Isolated, string Description)
        AbAiVariantSetup(string variant, string? v2Path, string? candidatePath, bool pursuitFlag) => variant switch
    {
        "baseline" => (AiLevel.Neural, null, false, pursuitFlag, false,
            pursuitFlag
                ? "焦点座位 7Neuu v4 + 追役(--pursuit)；其余三家 7Neuu v4（无追役）"
                : "焦点座位 7Neuu + 默认 v4（无追役）；其余三家 7Neuu v4"),
        "pursuit" => (AiLevel.Neural, null, false, true, false,
            "焦点座位 7Neuu v4 + 自动追役；其余三家 7Neuu v4（无追役）"),
        "v2" => (AiLevel.Neural, v2Path, false, pursuitFlag, false,
            "焦点座位 7Neuu + v2 权重；其余三家 7Neuu v4"),
        "wide" => (AiLevel.Neural, candidatePath, false, pursuitFlag, false,
            "焦点座位 7Neuu + 候选模型（--candidate，339 维/384 hidden 或任意 hidden）；其余三家 7Neuu v4"),
        "mcts" => (AiLevel.Shura, null, true, false, true,
            "焦点座位 Shura + MCTS（200ms/步，隔离组）；其余三家 7Neuu v4"),
        "simple" => (AiLevel.Simple, null, false, false, false,
            "焦点座位打分机 Simple；其余三家 7Neuu v4"),
        "normal" => (AiLevel.Normal, null, false, false, false,
            "焦点座位打分机 Normal；其余三家 7Neuu v4"),
        _ => throw new ArgumentException($"未知 A/B 组: {variant}"),
    };

    private static double Mean(IReadOnlyList<int> values) =>
        values.Count == 0 ? 0.0 : values.Sum() / (double)values.Count;

    private static double Mean(IReadOnlyList<double> values) =>
        values.Count == 0 ? 0.0 : values.Sum() / values.Count;

    /// <summary>线性插值百分位（p ∈ [0,1]）；空列表返回 0。</summary>
    internal static double Percentile(IReadOnlyList<int> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0.0;
        }

        double p = Math.Clamp(percentile, 0.0, 1.0);
        double[] sorted = values.OrderBy(x => x).Select(x => (double)x).ToArray();
        double position = (sorted.Length - 1) * p;
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        return lower == upper
            ? sorted[lower]
            : sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    /// <summary>线性插值百分位（double 版本）。</summary>
    internal static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0.0;
        }

        double p = Math.Clamp(percentile, 0.0, 1.0);
        double[] sorted = values.OrderBy(x => x).ToArray();
        double position = (sorted.Length - 1) * p;
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        return lower == upper
            ? sorted[lower]
            : sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    /// <summary>把 A/B 报告落盘为 JSON + CSV + Markdown；返回三个文件路径。</summary>
    internal static (string Json, string Csv, string Markdown) WriteAbAiReport(AbAiReport report, string outDirectory)
    {
        Directory.CreateDirectory(outDirectory);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string baseName = Path.Combine(outDirectory, "ab-ai_" + stamp);

        var payload = new
        {
            generatedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
            config = new
            {
                games = report.Options.Games,
                variants = report.Options.Variants,
                v2 = report.Options.V2Path,
                candidate = report.Options.CandidatePath,
                pursuitArm = report.Options.PursuitArm,
                pursuit = report.Options.Pursuit,
                style = report.Options.Style,
                mode = report.Options.Mode,
                startSeed = report.Options.StartSeed,
                focusRotation = "seat = (seed - startSeed) % 4",
                mctsNote = report.MctsNote,
            },
            turnDefinition = report.TurnDefinition,
            variants = report.Variants.Select(v => new
            {
                variant = v.Variant,
                focus = v.FocusDescription,
                isolated = v.Isolated,
                games = v.Games,
                rounds = v.Rounds,
                avgRank = v.AvgRank,
                top1Rate = v.Top1Rate,
                top2Rate = v.Top2Rate,
                roundWinRate = v.RoundWinRate,
                dealInRate = v.DealInRate,
                flowRate = v.FlowRate,
                meldsPerRound = v.MeldsPerRound,
                avgWinnersPerRound = v.AvgWinnersPerRound,
                avgTurnPerRound = v.AvgTurnPerRound,
                discardTurn = new { mean = v.DiscardTurnMean, p50 = v.DiscardTurnP50, p90 = v.DiscardTurnP90 },
                drawTurn = new { mean = v.DrawTurnMean, p50 = v.DrawTurnP50, p90 = v.DrawTurnP90 },
                focusWinTurn = new { mean = v.FocusWinTurnMean, p50 = v.FocusWinTurnP50, p90 = v.FocusWinTurnP90 },
                winWallMean = v.WinWallMean,
                histogram = v.Histogram.Select(b => new
                {
                    bucket = b.Bucket,
                    minTurn = b.MinTurn,
                    maxTurn = b.MaxTurn < int.MaxValue ? (int?)b.MaxTurn : null,
                    wins = b.Wins,
                    cumulativeWins = b.CumulativeWins,
                    cumulativeWinRate = b.CumulativeWinRate,
                    flows = b.Flows,
                    cumulativeFlows = b.CumulativeFlows,
                    cumulativeFlowRate = b.CumulativeFlowRate,
                }),
                strategyEntropy = v.StrategyEntropy,
                yakuCoverage = v.YakuCoverage,
                yakuCounts = v.YakuCounts,
                rareYakuCounts = v.RareYakuCounts,
                model = v.ModelPath is null
                    ? null
                    : new
                    {
                        path = v.ModelPath,
                        features = v.ModelFeatures,
                        hidden = v.ModelHidden,
                        layers = v.ModelLayers,
                    },
                neuralDecisions = v.NeuralDecisions,
                neuralDifferent = v.NeuralDifferent,
                pursuitEntries = v.PursuitEntries,
                pursuitExits = v.PursuitExits,
                pursuitDecisions = v.PursuitDecisions,
                gameRows = v.GameRows,
                roundRows = v.RoundRows,
            }),
        };

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        string jsonPath = baseName + ".json";
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(payload, jsonOptions), new System.Text.UTF8Encoding(false));

        string csvPath = baseName + ".csv";
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("variant,isolated,games,rounds,avgRank,top1Rate,top2Rate,roundWinRate,dealInRate,flowRate,"
            + "meldsPerRound,avgWinnersPerRound,avgTurnPerRound,discardTurnMean,discardTurnP50,discardTurnP90,"
            + "drawTurnMean,drawTurnP50,drawTurnP90,focusWinTurnMean,focusWinTurnP50,focusWinTurnP90,winWallMean,"
            + "strategyEntropy,yakuCoverage,modelPath,modelFeatures,modelHidden,neuralDecisions,pursuitEntries,pursuitDecisions");
        foreach (var v in report.Variants)
        {
            csv.AppendLine(string.Join(",",
                v.Variant,
                v.Isolated ? "1" : "0",
                v.Games.ToString(CultureInfo.InvariantCulture),
                v.Rounds.ToString(CultureInfo.InvariantCulture),
                Fmt(v.AvgRank), Fmt(v.Top1Rate), Fmt(v.Top2Rate), Fmt(v.RoundWinRate), Fmt(v.DealInRate),
                Fmt(v.FlowRate), Fmt(v.MeldsPerRound), Fmt(v.AvgWinnersPerRound), Fmt(v.AvgTurnPerRound),
                Fmt(v.DiscardTurnMean), Fmt(v.DiscardTurnP50), Fmt(v.DiscardTurnP90),
                Fmt(v.DrawTurnMean), Fmt(v.DrawTurnP50), Fmt(v.DrawTurnP90),
                Fmt(v.FocusWinTurnMean), Fmt(v.FocusWinTurnP50), Fmt(v.FocusWinTurnP90),
                Fmt(v.WinWallMean), Fmt(v.StrategyEntropy),
                v.YakuCoverage.ToString(CultureInfo.InvariantCulture),
                v.ModelPath ?? "",
                v.ModelFeatures.ToString(CultureInfo.InvariantCulture),
                v.ModelHidden.ToString(CultureInfo.InvariantCulture),
                v.NeuralDecisions.ToString(CultureInfo.InvariantCulture),
                v.PursuitEntries.ToString(CultureInfo.InvariantCulture),
                v.PursuitDecisions.ToString(CultureInfo.InvariantCulture)));
        }

        File.WriteAllText(csvPath, csv.ToString(), new System.Text.UTF8Encoding(false));

        string mdPath = baseName + ".md";
        var md = new System.Text.StringBuilder();
        md.AppendLine("# 7Neuu A/B 评测报告");
        md.AppendLine();
        md.AppendLine($"- 生成时间：{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        md.AppendLine($"- 命令：`--ab-ai --games {report.Options.Games} --variants {string.Join(",", report.Options.Variants)}"
            + (report.Options.V2Path is null ? string.Empty : $" --v2 {report.Options.V2Path}")
            + (report.Options.CandidatePath is null ? string.Empty : $" --candidate {report.Options.CandidatePath}")
            + (report.Options.PursuitArm ? " --pursuit-arm" : string.Empty)
            + (report.Options.Pursuit ? " --pursuit" : string.Empty)
            + $" --style {report.Options.Style} --mode {report.Options.Mode} --start-seed {report.Options.StartSeed}`");
        md.AppendLine($"- 焦点座位轮换：seat = (seed - startSeed) % 4；其余三家固定 7Neuu + 默认 v4。");
        md.AppendLine($"- 追役：{(report.Options.Pursuit ? "baseline/v2/wide 组焦点座位开启（--pursuit）" : "默认关闭")}；"
            + $"追役臂（pursuit）始终开启；风格预设：{report.Options.Style}。");
        md.AppendLine($"- 巡目定义：{report.TurnDefinition}");
        md.AppendLine($"- MCTS：{report.MctsNote}");
        md.AppendLine();

        var main = report.Variants.Where(v => !v.Isolated).ToList();
        var isolated = report.Variants.Where(v => v.Isolated).ToList();
        if (main.Count > 0)
        {
            AppendAbAiMarkdownTable(md, main);
            md.AppendLine();
            md.AppendLine("> 和牌巡目只统计有和牌者的局；`discardTurn` 按弃牌数、`drawTurn` 按摸牌数（83-wall）折算。"
                + "`策略熵` 为焦点座位策略网动作分布熵（非策略网组为 0）；`模型` 为 features/hidden。");
            md.AppendLine();
        }

        if (isolated.Count > 0)
        {
            md.AppendLine("## MCTS 单列（不可与上表同表比较）");
            md.AppendLine();
            md.AppendLine($"> {report.MctsNote}");
            md.AppendLine();
            AppendAbAiMarkdownTable(md, isolated);
            md.AppendLine();
        }

        md.AppendLine("## 和牌时间分布（累计口径）");
        md.AppendLine();
        md.AppendLine("| 组 | 巡目桶 | 累计和牌率 | 累计流局率 |");
        md.AppendLine("|---|---|---|---|");
        foreach (var v in report.Variants)
        {
            foreach (var bucket in v.Histogram)
            {
                md.AppendLine($"| {v.Variant} | {bucket.Bucket} | {Fmt(bucket.CumulativeWinRate)} | {Fmt(bucket.CumulativeFlowRate)} |");
            }
        }

        File.WriteAllText(mdPath, md.ToString(), new System.Text.UTF8Encoding(false));

        return (jsonPath, csvPath, mdPath);
    }

    private static void AppendAbAiMarkdownTable(System.Text.StringBuilder md, IReadOnlyList<AbAiVariantReport> variants)
    {
        md.AppendLine("| 组 | 局数 | 平均顺位 | top1 | top2 | 和牌率 | 放铳率 | 流局率 | 副露/局 | "
            + "discardTurn mean/p50/p90 | drawTurn mean/p50/p90 | 每局和牌数 | 平均巡目 | 策略熵 | 役种覆盖 | 模型(features/hidden) |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var v in variants)
        {
            string model = v.ModelPath is null ? "-" : $"{v.ModelFeatures}/{v.ModelHidden}";
            md.AppendLine($"| {v.Variant} | {v.Games} | {Fmt(v.AvgRank)} | {Fmt(v.Top1Rate)} | {Fmt(v.Top2Rate)} | "
                + $"{Fmt(v.RoundWinRate)} | {Fmt(v.DealInRate)} | {Fmt(v.FlowRate)} | {Fmt(v.MeldsPerRound)} | "
                + $"{Fmt(v.DiscardTurnMean)}/{Fmt(v.DiscardTurnP50)}/{Fmt(v.DiscardTurnP90)} | "
                + $"{Fmt(v.DrawTurnMean)}/{Fmt(v.DrawTurnP50)}/{Fmt(v.DrawTurnP90)} | "
                + $"{Fmt(v.AvgWinnersPerRound)} | {Fmt(v.AvgTurnPerRound)} | {Fmt(v.StrategyEntropy)} | {v.YakuCoverage} | {model} |");
        }
    }

    private static string Fmt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary><c>--ab-ai</c>：A/B 评测（默认 baseline + 打分机 Simple/Normal；MCTS 隔离），输出 JSON/CSV/Markdown。</summary>
    private static int AbAi(string[] args)
    {
        Tiisu.App.Program.TryUseUtf8Console();
        AbAiOptions options;
        try
        {
            options = ParseAbAiOptions(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine("ab-ai: " + ex.Message);
            return 2;
        }

        try
        {
            if (options.Variants.Contains("mcts", StringComparer.Ordinal))
            {
                Console.WriteLine("ab-ai: 注意 " + AbAiMctsNote);
            }

            var report = RunAbAi(options, line =>
            {
                if (!options.Quiet)
                {
                    Console.WriteLine(line);
                }
            });
            var (json, csv, md) = WriteAbAiReport(report, options.OutDirectory);

            Console.WriteLine(
                $"ab-ai: variants={string.Join(",", options.Variants)} games={options.Games} "
                + $"pursuit={(options.Pursuit || options.PursuitArm)} style={options.Style}"
                + (options.CandidatePath is null ? string.Empty : $" candidate={options.CandidatePath}"));
            Console.WriteLine("variant\tavgRank\ttop1\ttop2\twin\tdelIn\tflow\tmelds\t"
                + "discardTurn(mean/p50/p90)\tdrawTurn(mean/p50/p90)\tentropy\tYaku\tmodel");
            foreach (var v in report.Variants.Where(v => !v.Isolated))
            {
                PrintAbAiConsoleRow(v);
            }

            var isolated = report.Variants.Where(v => v.Isolated).ToList();
            if (isolated.Count > 0)
            {
                Console.WriteLine("[隔离] " + report.MctsNote);
                foreach (var v in isolated)
                {
                    PrintAbAiConsoleRow(v);
                }
            }

            Console.WriteLine("json: " + json);
            Console.WriteLine("csv:  " + csv);
            Console.WriteLine("md:   " + md);
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine("ab-ai failed: " + ex.Message);
            return 3;
        }
    }

    private static void PrintAbAiConsoleRow(AbAiVariantReport v)
    {
        string model = v.ModelPath is null ? "-" : $"{v.ModelFeatures}/{v.ModelHidden}";
        Console.WriteLine(
            $"{v.Variant}\t{Fmt(v.AvgRank)}\t{Fmt(v.Top1Rate)}\t{Fmt(v.Top2Rate)}\t"
            + $"{Fmt(v.RoundWinRate)}\t{Fmt(v.DealInRate)}\t{Fmt(v.FlowRate)}\t{Fmt(v.MeldsPerRound)}\t"
            + $"{Fmt(v.DiscardTurnMean)}/{Fmt(v.DiscardTurnP50)}/{Fmt(v.DiscardTurnP90)}\t"
            + $"{Fmt(v.DrawTurnMean)}/{Fmt(v.DrawTurnP50)}/{Fmt(v.DrawTurnP90)}\t"
            + $"{Fmt(v.StrategyEntropy)}\t{v.YakuCoverage}\t{model}");
    }

    /// <summary><c>--smoke-ruleset-switch</c>：设置切到推倒胡并重置后，引擎必须真的换成推倒胡。</summary>
    private static int SmokeRulesetSwitch()
    {
        Tiisu.App.Program.TryUseUtf8Console();
        var settings = new Settings();
        var runtime = new MatchRuntime(settings, new Mulberry32Random(1), _ => { },
            saveStore: null, stats: null, roundLog: null, matchSeed: 1);
        runtime.StartMatch(4);
        bool before = runtime.Engine.Ruleset == Ruleset.Qizi;

        var changed = new Settings { Ruleset = Ruleset.TuidaoHu };
        runtime.ApplySettings(changed);
        runtime.ResetAll();
        bool after = runtime.Engine.Ruleset == Ruleset.TuidaoHu
            && runtime.Engine.Checker.Ruleset == Ruleset.TuidaoHu;

        bool ok = before && after;
        Console.WriteLine(L.T("tools.cli.devtools.SmokeRulesetSwitch.1", ("p1", before), ("p2", after), ("p3", (ok ? "PASS" : "FAIL"))));
        return ok ? 0 : 1;
    }

    /// <summary><c>--smoke-neural</c>：小策略网能否加载权重并给出合法弃牌。</summary>
    private static int SmokeNeural()
    {
        Tiisu.App.Program.TryUseUtf8Console();
        var hand = new[] { "m1", "m2", "m3", "m5", "m5", "m7", "p2", "p3", "p4", "s1", "z3", "p6", "s8" }
            .Select(Tile.Parse).ToArray();
        var ctx = new AiDecisionContext(
            hand,
            Array.Empty<Tiisu.Engine.Meld>(),
            Array.Empty<Tile>(),
            new IReadOnlyList<Tile>[]
            {
                Array.Empty<Tile>(), Array.Empty<Tile>(), Array.Empty<Tile>(), Array.Empty<Tile>(),
            },
            Array.Empty<Tiisu.Engine.Meld>(),
            0,
            70,
            new[] { 49000, 49000, 49000, 49000 },
            SeatWind.East,
            SeatWind.East,
            0,
            0);

        int idx = NeuralPolicyAi.ChooseDiscard(ctx);
        bool ok = NeuralPolicyAi.Available && idx >= 0 && idx < hand.Length;
        Console.WriteLine($"smoke-neural: available={NeuralPolicyAi.Available} weights={NeuralPolicyAi.LoadedFrom}");
        Console.WriteLine(L.T("tools.cli.devtools.SmokeNeural.1", ("p1", string.Join(" ", hand.Select(x => x.Id)))));
        Console.WriteLine(L.T("tools.cli.devtools.SmokeNeural.2", ("p1", (idx >= 0 ? hand[idx].Id : L.T("tools.cli.devtools.SmokeNeural.none"))), ("p2", (ok ? "PASS" : "FAIL"))));
        return ok ? 0 : 1;
    }

    /// <summary>
    /// <c>--smoke-optional</c>：可选役开关要真的进到引擎判役器里（不是只改设置文件）。
    /// </summary>
    private static int SmokeOptional()
    {
        Tiisu.App.Program.TryUseUtf8Console();
        var settings = new Settings();
        var runtime = new MatchRuntime(settings, new Mulberry32Random(1), _ => { },
            saveStore: null, stats: null, roundLog: null, matchSeed: 1);
        runtime.StartMatch(4);

        bool offByDefault = runtime.Engine.Checker.OptionalEnabled.Count == 0;

        var changed = new Settings();
        changed.OptionalYaku.Add("all_number");
        runtime.ApplySettings(changed);

        bool onAfterApply = runtime.Engine.Checker.OptionalEnabled.Contains("all_number");

        bool ok = offByDefault && onAfterApply;
        Console.WriteLine(L.T("tools.cli.devtools.SmokeOptional.1", ("p1", offByDefault), ("p2", onAfterApply), ("p3", (ok ? "PASS" : "FAIL"))));
        return ok ? 0 : 1;
    }

    /// <summary>
    /// <c>--smoke-tuidaohu [seed]</c>：推倒胡整场自走，断言「零点数 / 无报听 / 结算是简化版」。
    /// </summary>
    private static int SmokeTuidaoHu(string[] args)
    {
        Tiisu.App.Program.TryUseUtf8Console();
        uint seed = 11;
        if (args.Length >= 2 && !uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
        {
            seed = 11;
        }

        RoundResult? last = null;
        var settings = new Settings { Ruleset = Ruleset.TuidaoHu };
        var runtime = new MatchRuntime(settings, new Mulberry32Random(seed), uiEvent =>
        {
            if (uiEvent is RoundEndEvent end)
            {
                last = end.Result;
            }
        }, saveStore: null, stats: null, roundLog: null, matchSeed: seed);

        runtime.StartMatch(4);
        bool called = false;
        var clock = Stopwatch.StartNew();
        int start = 0;

        while (clock.Elapsed < TimeSpan.FromSeconds(40) && runtime.Phase != MatchPhase.MatchOver)
        {
            var phase = runtime.Phase;
            if (phase == MatchPhase.RoundEnded)
            {
                runtime.NextRound();
                continue;
            }

            var player = runtime.Engine.Players[runtime.HumanIndex];
            if (player.IsCalled)
            {
                called = true;
            }

            if (phase == MatchPhase.HumanDiscard)
            {
                int pick = player.IsCalled
                    ? LegalCalledDiscardIndex(runtime.Engine, player)
                    : BestShantenDiscardIndex(player);
                if (pick < 0)
                {
                    break;
                }

                if (start == 0)
                {
                    start = runtime.Engine.Scores[0];
                }

                runtime.Execute(GameCommand.Discard(pick));
                continue;
            }

            if (phase is MatchPhase.HumanMeld or MatchPhase.HumanRon)
            {
                runtime.Execute(GameCommand.Pass());
                continue;
            }

            Thread.Sleep(5);
        }

        bool zeroScores = runtime.Engine.Scores.All(s => s == start);
        bool simplified = last is { Simplified: true };
        bool ok = simplified && zeroScores && !called;
        Console.WriteLine(L.T("tools.cli.devtools.SmokeTuidaoHu.1", ("p1", seed), ("p2", runtime.Phase), ("p3", simplified))
            + L.T("tools.cli.devtools.SmokeTuidaoHu.2", ("p1", zeroScores), ("p2", called), ("p3", (ok ? "PASS" : "FAIL"))));
        return ok ? 0 : 1;
    }

    /// <summary>
    /// <c>--smoke-drawn</c>：摸到的牌必须单独排在右手边（与前一格间距明显大于普通 HandGap）。
    /// </summary>
    private static int SmokeDrawn()
    {
        Tiisu.App.Program.TryUseUtf8Console();
        var runtime = new MatchRuntime(new Settings(), new Mulberry32Random(3), _ => { },
            saveStore: null, stats: null, roundLog: null, matchSeed: 3);
        runtime.StartMatch(4);

        var layout = new TableLayoutSpec();
        YogaTableLayout.Apply(layout);

        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(15) && runtime.Phase != MatchPhase.HumanDiscard)
        {
            if (runtime.Phase == MatchPhase.RoundEnded)
            {
                runtime.NextRound();
                continue;
            }

            if (runtime.Phase is MatchPhase.HumanMeld or MatchPhase.HumanRon)
            {
                runtime.Execute(GameCommand.Pass());
                continue;
            }

            Thread.Sleep(5);
        }

        var vm = runtime.Snapshot;
        var seat = vm.Seats[0];
        var rects = TableSceneSkia.HandTileRects(layout, seat, 0);
        int drawn = seat.DrawnIndex;
        string detail = $"phase={runtime.Phase} hand={seat.Hand.Count} drawn={drawn} gap=";
        bool ok = drawn >= 0 && drawn < rects.Count && seat.Hand.Count > 1;
        float gap = 0f;
        if (ok)
        {
            float nearestRight = float.MinValue;
            for (int i = 0; i < rects.Count; i++)
            {
                if (i != drawn)
                {
                    nearestRight = Math.Max(nearestRight, rects[i].Right);
                }
            }

            gap = rects[drawn].Left - nearestRight;
            detail += $"{gap:F1} (handGap={layout.HandGap:F1})";
            ok = gap >= layout.HandGap * 1.5f;
        }
        else
        {
            detail += "n/a";
        }

        Console.WriteLine($"smoke-drawn: {detail} {(ok ? "PASS" : "FAIL")}");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// <c>--smoke-dice</c>：按住右下角「骰子」按钮时，牌桌中心弹出开局骰面板（像素级判定）；
    /// 同时打印本局定庄骰 / 切牌骰 / 跳墩，确认按 Web 口径生成。
    /// </summary>
    private static int SmokeDice()
    {
        Tiisu.App.Program.TryUseUtf8Console();
        var settings = new Settings { ShowWallViewer = true, ThinkSeconds = 0 };
        var runtime = new MatchRuntime(settings, new Mulberry32Random(9), _ => { },
            saveStore: null, stats: null, roundLog: null, matchSeed: 9);
        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
        var screen = new TableScreen(() => runtime.Snapshot, runtime.Execute, null, atlas,
            TableRenderStyle.Flat, settings);
        runtime.StartMatch(4);

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
        int y0 = held.Height / 2 - 110;
        int y1 = held.Height / 2 + 110;
        for (int y = y0; y < y1; y += 4)
        {
            for (int x = 220; x < held.Width - 220; x += 4)
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

        var cut = runtime.Engine.DiceCut;
        var dealerDice = runtime.Engine.DiceDealer;
        string cutText = cut is { Count: 2 } ? $"{cut[0]}+{cut[1]}={cut[0] + cut[1]}" : "-";
        string dealerText = dealerDice is { Count: 2 } ? $"{dealerDice[0]}+{dealerDice[1]}={dealerDice[0] + dealerDice[1]}" : "-";
        double ratio = total == 0 ? 0 : (double)changed / total;
        bool passed = ratio > 0.5 && cut is { Count: 2 } && runtime.Engine.CutSkipTiles == (cut[0] + cut[1]) * 2;
        Console.WriteLine(L.T("tools.cli.devtools.SmokeDice.1", ("p1", (ratio).ToString("P1", CultureInfo.InvariantCulture)), ("p2", dealerText), ("p3", cutText))
            + L.T("tools.cli.devtools.SmokeDice.2", ("p1", runtime.Engine.CutSkipTiles), ("p2", (passed ? "PASS" : "FAIL"))));
        return passed ? 0 : 1;
    }

    /// <summary>
    /// <c>--smoke-think</c>：长考 2 秒后应自动摸切并离开人类出牌阶段。
    /// </summary>
    private static int SmokeThink()
    {
        Tiisu.App.Program.TryUseUtf8Console();
        var settings = new Settings { ThinkSeconds = 2 };
        var runtime = new MatchRuntime(settings, new Mulberry32Random(5), _ => { },
            saveStore: null, stats: null, roundLog: null, matchSeed: 5);
        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
        var screen = new TableScreen(() => runtime.Snapshot, runtime.Execute, null, atlas,
            TableRenderStyle.Flat, settings);
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
        Console.WriteLine(L.T("tools.cli.devtools.SmokeThink.1", ("p1", sawHuman), ("p2", before), ("p3", lastActed), ("p4", runtime.Phase))
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
    /// <item>报听后报听按钮消失（已报听不需要该入口），锁手仍允许出牌；</item>
    /// <item>整场在时限内终局（真卡住时这里的耗时会先耗尽）。</item>
    /// </list>
    /// </summary>
    private static int SmokeCallFlow(string[] args)
    {
        Tiisu.App.Program.TryUseUtf8Console();
        if (args.Length < 2
            || !uint.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint seed))
        {
            Console.Error.WriteLine(L.T("tools.cli.devtools.SmokeCallFlow.1"));
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

        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
        var screen = new TableScreen(() => runtime.Snapshot, runtime.Execute, null, atlas);
        var layout = new TableLayoutSpec();
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
        string advanceDetail = L.T("tools.cli.devtools.SmokeCallFlow.2");
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
                stuck = L.T("tools.cli.devtools.SmokeCallFlow.3");
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
                stuck = L.T("tools.cli.devtools.SmokeCallFlow.stuckAt", ("phase", phase));
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
                    stuck = L.T("tools.cli.devtools.SmokeCallFlow.6");
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
                    L.T("tools.cli.devtools.SmokeCallFlow.7"),
                    tile.Id, player.IsCalled, runtime.Phase, turnBefore,
                    runtime.Engine.TurnIndex, wallBefore, runtime.Engine.Wall.Count,
                    clock.Elapsed.TotalMilliseconds));

                advanceDetail = L.T("tools.cli.devtools.SmokeCallFlow.advanceDetail", ("phase", runtime.Phase), ("turnBefore", turnBefore), ("turnAfter", runtime.Engine.TurnIndex), ("wallBefore", wallBefore), ("wallAfter", runtime.Engine.Wall.Count));
                if (!advanceOk)
                {
                    stuck = L.T("tools.cli.devtools.SmokeCallFlow.noAdvance", ("phase", runtime.Phase), ("turn", runtime.Engine.TurnIndex), ("wall", runtime.Engine.Wall.Count));
                    break;
                }

                // 3. 报听后报听按钮必须消失（已报听不再需要入口；本局已结束则同样不可见）。
                if (runtime.Phase == MatchPhase.HumanDiscard)
                {
                    var bar = new ActionBar();
                    bar.Build(runtime.Snapshot, layout);
                    var callButton = bar.Buttons.FirstOrDefault(b => b.Kind == ActionBar.Kind.DeclareCall);
                    callButtonOk = callButton is null;
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
                stuck = L.T("tools.cli.devtools.SmokeCallFlow.14");
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
                stuck = L.T("tools.cli.devtools.SmokeCallFlow.discardRejected", ("count", player.Hand.Count), ("phase", runtime.Phase), ("actions", vm.Actions), ("pick", pick), ("tile", player.Hand[pick].Id));
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
        Console.WriteLine(L.T("tools.cli.devtools.SmokeCallFlow.20", ("p1", (declared ? L.T("tools.cli.devtools.SmokeCallFlow.declared") : L.T("tools.cli.devtools.SmokeCallFlow.notSeen"))), ("p2", actions), ("p3", (clock.Elapsed.TotalSeconds).ToString("F2", CultureInfo.InvariantCulture))));
        Check(L.T("tools.cli.devtools.SmokeCallFlow.21"), armOk, L.T("tools.cli.devtools.SmokeCallFlow.22"));
        Check(L.T("tools.cli.devtools.SmokeCallFlow.23"), declared, L.T("tools.cli.devtools.SmokeCallFlow.24"));
        Check(L.T("tools.cli.devtools.SmokeCallFlow.25"), advanceOk, stuck.Length > 0 ? stuck : advanceDetail);
        Check(L.T("tools.cli.devtools.SmokeCallFlow.26"), !callButtonChecked || callButtonOk,
            callButtonChecked ? L.T("tools.cli.devtools.SmokeCallFlow.27") : L.T("tools.cli.devtools.SmokeCallFlow.28"));
        Check(L.T("tools.cli.devtools.SmokeCallFlow.29"), postCallTurns == 0 || postCallDiscardOk,
            postCallTurns == 0 ? L.T("tools.cli.devtools.SmokeCallFlow.30") : L.T("tools.cli.devtools.SmokeCallFlow.31"));
        Check(L.T("tools.cli.devtools.SmokeCallFlow.32"), completed && inTime,
            L.T("tools.cli.devtools.SmokeCallFlow.33", ("p1", final.Phase), ("p2", (clock.Elapsed.TotalSeconds).ToString("F2", CultureInfo.InvariantCulture)), ("p3", (budget.TotalSeconds).ToString("F0", CultureInfo.InvariantCulture))));

        if (stuck.Length > 0)
        {
            Console.WriteLine(L.T("tools.cli.devtools.SmokeCallFlow.34") + stuck);
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
        Tiisu.App.Program.TryUseUtf8Console();
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
            Check(L.T("tools.cli.devtools.SmokeLayoutEditor.1", ("p1", layout.Regions.Count)), layout.Regions.Count >= 16);
            Check(L.T("tools.cli.devtools.SmokeLayoutEditor.2"), required.All(id => layout.Regions.Any(r => r.Id == id)));

            int index = 0;
            for (int i = 0; i < layout.Regions.Count; i++)
            {
                if (layout.Regions[i].Id == "hand-self")
                {
                    index = i;
                }
            }

            var before = layout.Regions[index].Rect;
            Console.WriteLine(L.T("tools.cli.devtools.SmokeLayoutEditor.3", ("p1", (before.X).ToString("0", CultureInfo.InvariantCulture)), ("p2", (before.Y).ToString("0", CultureInfo.InvariantCulture)), ("p3", (before.W).ToString("0", CultureInfo.InvariantCulture)), ("p4", (before.H).ToString("0", CultureInfo.InvariantCulture))));

            editor.Enter();
            Check(L.T("tools.cli.devtools.SmokeLayoutEditor.4"), editor.Active);
            editor.Select(index);
            editor.PointerDown(before.CenterX, before.CenterY);
            editor.PointerMove(before.CenterX + 10f, before.CenterY + 10f);
            editor.PointerUp();

            var moved = layout.Regions[index].Rect;
            Console.WriteLine(L.T("tools.cli.devtools.SmokeLayoutEditor.5", ("p1", (moved.X).ToString("0", CultureInfo.InvariantCulture)), ("p2", (moved.Y).ToString("0", CultureInfo.InvariantCulture)), ("p3", (moved.W).ToString("0", CultureInfo.InvariantCulture)), ("p4", (moved.H).ToString("0", CultureInfo.InvariantCulture))));
            Check(L.T("tools.cli.devtools.SmokeLayoutEditor.6"),
                Math.Abs(moved.X - before.X - 10f) < 0.01f
                && Math.Abs(moved.Y - before.Y - 10f) < 0.01f
                && Math.Abs(moved.W - before.W) < 0.01f
                && Math.Abs(moved.H - before.H) < 0.01f);

            editor.Save();
            string file = CustomLayoutStore.PathFor(dir);
            Check(L.T("tools.cli.devtools.SmokeLayoutEditor.7"), File.Exists(file));
            string savedJson = File.ReadAllText(file);
            Check(L.T("tools.cli.devtools.SmokeLayoutEditor.8"),
                savedJson.Contains("\"version\": 1", StringComparison.Ordinal)
                && savedJson.Contains("\"hand-self\"", StringComparison.Ordinal)
                && savedJson.Contains("\"x\": 267", StringComparison.Ordinal));

            var reloaded = NewLayout();
            bool applied = CustomLayoutStore.Load(reloaded, dir);
            var afterReload = reloaded.Regions[index].Rect;
            Console.WriteLine(L.T("tools.cli.devtools.SmokeLayoutEditor.9", ("p1", (afterReload.X).ToString("0", CultureInfo.InvariantCulture)), ("p2", (afterReload.Y).ToString("0", CultureInfo.InvariantCulture))));
            Check(L.T("tools.cli.devtools.SmokeLayoutEditor.10"),
                applied
                && Math.Abs(afterReload.X - moved.X) < 0.01f
                && Math.Abs(afterReload.Y - moved.Y) < 0.01f);

            var editor2 = new LayoutEditor(reloaded, dir);
            editor2.Enter();
            editor2.Select(index);
            editor2.RestoreDefault();
            Check(L.T("tools.cli.devtools.SmokeLayoutEditor.11"), File.Exists(file) && editor2.Status.Contains("R", StringComparison.Ordinal));
            editor2.RestoreDefault();
            Check(L.T("tools.cli.devtools.SmokeLayoutEditor.12"), !File.Exists(file));

            var afterReset = reloaded.Regions[index].Rect;
            Console.WriteLine(L.T("tools.cli.devtools.SmokeLayoutEditor.13", ("p1", (afterReset.X).ToString("0", CultureInfo.InvariantCulture)), ("p2", (afterReset.Y).ToString("0", CultureInfo.InvariantCulture))));
            Check(L.T("tools.cli.devtools.SmokeLayoutEditor.14"),
                Math.Abs(afterReset.X - before.X) < 0.01f
                && Math.Abs(afterReset.Y - before.Y) < 0.01f
                && Math.Abs(afterReset.W - before.W) < 0.01f
                && Math.Abs(afterReset.H - before.H) < 0.01f);

            // 破坏性测试 1：非法 JSON。
            File.WriteAllText(file, "{ 这不是 json ,,,", new System.Text.UTF8Encoding(false));
            var broken = NewLayout();
            bool brokenApplied = CustomLayoutStore.Load(broken, dir);
            var brokenRect = broken.Regions[index].Rect;
            Check(L.T("tools.cli.devtools.SmokeLayoutEditor.15"),
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
            Check(L.T("tools.cli.devtools.SmokeLayoutEditor.16"),
                !partialApplied && Math.Abs(partialRect.X - before.X) < 0.01f);

            // 破坏性测试 3：真实启动路径（默认目录写坏文件后构造 TableScreen）。
            string realPath = CustomLayoutStore.PathFor();
            byte[]? backup = File.Exists(realPath) ? File.ReadAllBytes(realPath) : null;
            bool startupOk;
            try
            {
                Directory.CreateDirectory(Settings.DefaultDirectory);
                File.WriteAllText(realPath, "{ \"version\": 1, \"regions\": ]", new System.Text.UTF8Encoding(false));
                var screen = new TableScreen((Func<TableViewModel>)(() => TableDemoModel.Create(1)), null, null, (TileAtlas?)null);
                startupOk = Math.Abs(screen.CanvasWidth - 1400f) < 0.01f
                            && Math.Abs(screen.CanvasHeight - 1400f) < 0.01f;
            }
            catch (Exception ex)
            {
                Console.WriteLine(L.T("tools.cli.devtools.SmokeLayoutEditor.17") + ex);
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

            Check(L.T("tools.cli.devtools.SmokeLayoutEditor.18"), startupOk);
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
        using var atlas = new TileAtlas(Tiisu.App.Program.AssetsDir);
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
    /// <summary>
    /// <c>--yaku-stats N [--jobs K] [--mode 4] [--start-seed S] [--out file]</c>：
    /// 批量自对弈，统计每种役的和出次数、占和牌手数比例、占总局数比例。
    /// 默认启用 ike.json 里全部可选役，四家全 AI，无 UI。
    /// </summary>
    private static int YakuStats(string[] args)
    {
        int games = 1_000_000;
        int jobs = Math.Clamp(Environment.ProcessorCount, 1, 32);
        int mode = 4;
        long roundLimit = 0;
        bool enableOptional = false;
        uint startSeed = 1;
        string outPath = Path.Combine("dist", "yaku-stats-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json");
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--jobs":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int j)) jobs = Math.Clamp(j, 1, 64);
                    break;
                case "--mode":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int m)) mode = Math.Max(1, m);
                    break;
                case "--start-seed":
                    if (i + 1 < args.Length && uint.TryParse(args[++i], out uint seed)) startSeed = seed;
                    break;
                case "--rounds":
                    if (i + 1 < args.Length && long.TryParse(args[++i], out long rounds)) roundLimit = Math.Max(0, rounds);
                    break;
                case "--optional":
                    enableOptional = i + 1 < args.Length
                        && args[++i].Equals("all", StringComparison.OrdinalIgnoreCase);
                    break;
                case "--out":
                    if (i + 1 < args.Length) outPath = args[++i];
                    break;
                default:
                    if (int.TryParse(args[i], out int n)) games = Math.Max(1, n);
                    break;
            }
        }

        var yakuData = YakuData.LoadDefault();
        var optionals = yakuData.Yaku.Concat(yakuData.HighYaku)
            .Where(y => y.Optional)
            .Select(y => y.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var optional = enableOptional ? optionals : new List<string>();
        var allYakuNames = yakuData.Yaku.Concat(yakuData.HighYaku)
            .Select(y => y.Name)
            .Append("大七星")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var total = new YakuAgg();
        var roundCounter = new RoundCounter();
        int done = 0;
        var sw = Stopwatch.StartNew();
        var options = new ParallelOptions { MaxDegreeOfParallelism = jobs };
        Console.WriteLine($"yaku-stats: games={games} jobs={jobs} mode={mode} roundsLimit={roundLimit} optional={(enableOptional ? "all" : "off")} ({optional.Count})");

        Parallel.For(0, games, options,
            () => new YakuAgg(),
            (index, _, local) =>
            {
                var settings = new Settings
                {
                    RecordTiles = false,
                    OptionalYaku = new List<string>(optional),
                };
                RunYakuGame(startSeed + (uint)index, settings, mode, local, roundCounter, roundLimit);
                int n = Interlocked.Increment(ref done);
                if (n % 10000 == 0 || n == games)
                {
                    Console.WriteLine($"  {n}/{games}  {sw.Elapsed.TotalSeconds:F0}s");
                }
                return local;
            },
            local =>
            {
                lock (total)
                {
                    total.Merge(local);
                }
            });

        if (!Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(outPath))))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        }

        var rows = allYakuNames
            .Select(name => new
            {
                yaku = name,
                count = total.Yaku.TryGetValue(name, out long count) ? count : 0,
                shareOfWinningHands = total.WinningHands == 0 ? 0 : (double)(total.Yaku.TryGetValue(name, out count) ? count : 0) / total.WinningHands,
                shareOfRounds = total.Rounds == 0 ? 0 : (double)(total.Yaku.TryGetValue(name, out count) ? count : 0) / total.Rounds,
            })
            .OrderByDescending(x => x.count)
            .ThenBy(x => x.yaku, StringComparer.Ordinal)
            .ToList();
        var report = new
        {
            generatedAt = DateTimeOffset.Now,
            games,
            rounds = total.Rounds,
            winningHands = total.WinningHands,
            tsumoWins = total.TsumoWins,
            ronWins = total.RonWins,
            settings = new
            {
                optionalYaku = enableOptional ? "all" : "off",
                optionalCount = optional.Count,
                mode,
                jobs,
            },
            yaku = rows,
            elapsedSeconds = sw.Elapsed.TotalSeconds,
        };
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        File.WriteAllText(outPath, JsonSerializer.Serialize(report, jsonOptions) + "\n", new System.Text.UTF8Encoding(false));
        string csvPath = Path.ChangeExtension(outPath, ".csv");
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("yaku,count,shareOfWinningHands,shareOfRounds");
        foreach (var row in rows)
        {
            csv.Append('"').Append(row.yaku.Replace("\"", "\"\"")).Append("\",")
                .Append(row.count).Append(',')
                .Append(row.shareOfWinningHands.ToString("F8", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.shareOfRounds.ToString("F8", CultureInfo.InvariantCulture)).AppendLine();
        }
        File.WriteAllText(csvPath, csv.ToString(), new System.Text.UTF8Encoding(false));
        Console.WriteLine(L.T("tools.cli.devtools.YakuStats.1", ("p1", total.Rounds), ("p2", total.WinningHands), ("p3", outPath)));
        Console.WriteLine($"csv -> {csvPath}");
        return 0;
    }

    private sealed class RoundCounter
    {
        public long Value;
    }

    private sealed class YakuAgg
    {
        public long Rounds;
        public long WinningHands;
        public long TsumoWins;
        public long RonWins;
        public Dictionary<string, long> Yaku { get; } = new(StringComparer.Ordinal);

        public void Merge(YakuAgg other)
        {
            Rounds += other.Rounds;
            WinningHands += other.WinningHands;
            TsumoWins += other.TsumoWins;
            RonWins += other.RonWins;
            foreach (var kv in other.Yaku)
            {
                Yaku[kv.Key] = Yaku.TryGetValue(kv.Key, out long v) ? v + kv.Value : kv.Value;
            }
        }
    }

    private static void RunYakuGame(uint seed, Settings settings, int mode, YakuAgg agg, RoundCounter roundCounter, long roundLimit)
    {
        if (roundLimit > 0 && Volatile.Read(ref roundCounter.Value) >= roundLimit)
        {
            return;
        }

        var runtime = new MatchRuntime(settings, new Mulberry32Random(seed), _ => { },
            saveStore: null, stats: null, roundLog: null, matchSeed: seed);
        runtime.AllSeatsAi = true;
        runtime.AiStepDelayMilliseconds = 0;
        runtime.StartMatch(mode);
        int guard = 0;
        while (guard++ < 1000)
        {
            if (runtime.Phase == MatchPhase.MatchOver)
            {
                break;
            }

            if (runtime.Phase != MatchPhase.RoundEnded)
            {
                Thread.Yield();
                continue;
            }

            agg.Rounds++;
            long seen = Interlocked.Increment(ref roundCounter.Value);
            var details = runtime.LastSettlementDetails;
            int handWins = details.Count;
            if (handWins == 0 && runtime.LastWinKind == "tiisuin")
            {
                handWins = 1;
                AddYaku(agg, "大七星");
            }
            agg.WinningHands += handWins;
            foreach (var detail in details)
            {
                if (detail.SelfDraw)
                {
                    agg.TsumoWins++;
                }
                else
                {
                    agg.RonWins++;
                }
                foreach (var yaku in detail.Yaku)
                {
                    AddYaku(agg, yaku.Name);
                }
            }
            if (roundLimit > 0 && seen >= roundLimit)
            {
                return;
            }

            runtime.NextRound();
        }
    }

    private static void AddYaku(YakuAgg agg, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        agg.Yaku[name] = agg.Yaku.TryGetValue(name, out long value) ? value + 1 : 1;
    }

    /// <summary>开发/自检命令分发；返回 null 表示不是开发命令，交给 GUI。</summary>
    public static int? Run(string[] args)
    {
        // CLI 文案跟随 TIISU_LANG；缺省 zh-CN。需要固定语言的渲染命令仍可用 --lang 覆盖。
        L.SetLanguage(Environment.GetEnvironmentVariable("TIISU_LANG"));
        switch (args.Length >= 1 ? args[0] : string.Empty)
        {
            case "--version":
                // 构建时由 Tiisu.App.csproj 从 git 生成；无 git 时为 dev。
                Console.WriteLine(Tiisu.App.BuildInfo.Version);
                return 0;
            case "--smoke-run":
                return SmokeRun(args);

            case "--export-selfplay":
                return ExportSelfPlay(args);

            case "--check-publish":
                return CheckPublish(args);

            case "--check-font":
                return CheckFont(args);

            case "--audit-ui-strings":
                return AuditUiStrings(args);

            case "--check-steam":
                return CheckSteam(args);

            case "--check-i18n":
                return CheckI18n(args);

            case "--bench-snapshot":
                return BenchSnapshot(args);

            case "--audit-games":
                return AuditGames(args);
            case "--smoke-ai":
                return SmokeAi(args);
            case "--smoke-ai-call":
                return SmokeAiCall(args);
            case "--smoke-save":
                return SmokeSave(args);
            case "--smoke-resume":
                return SmokeResume(args);
            case "--smoke-session":
                return SmokeSession(args);
            case "--smoke-pointer":
                return SmokePointer(args);
            case "--check-voices":
                return CheckVoices();
            case "--smoke-escape":
                return SmokeEscape();
            case "--smoke-credits":
                return SmokeCredits();
            case "--smoke-layout-editor":
                return SmokeLayoutEditor();
            case "--smoke-call":
                return SmokeCall();
            case "--smoke-call-flow":
                return SmokeCallFlow(args);
            case "--play-real":
                return PlayReal(args);
            case "--smoke-think":
                return SmokeThink();
            case "--list-locks":
                return ListLocks();
            case "--smoke-7neuu":
                return Smoke7Neuu();
            case "--selfplay":
                return SelfPlay(args);
            case "--yaku-stats":
                return YakuStats(args);
            case "--smoke-ab":
                return SmokeAb(args);
            case "--ab-ai":
                return AbAi(args);
            case "--smoke-ruleset-switch":
                return SmokeRulesetSwitch();
            case "--smoke-neural":
                return SmokeNeural();
            case "--smoke-optional":
                return SmokeOptional();
            case "--smoke-tuidaohu":
                return SmokeTuidaoHu(args);
            case "--smoke-called-ron-pass":
                return SmokeCalledRonPass(args);
            case "--smoke-drawn":
                return SmokeDrawn();            case "--smoke-dice":
                return SmokeDice();
        }

        // 推倒胡结算出图：--render-simplified <png>
        if (args.Length >= 2 && args[0] == "--render-simplified")
        {
            RenderSimplifiedSettlement(args[1]);
            return 0;
        }

        // 编辑态出图：--render-layout-edit <png>
        if (args.Length >= 2 && args[0] == "--render-layout-edit")
        {
            RenderLayoutEdit(args[1]);
            return 0;
        }

        // 牌面斜置/向右旋转演示：--render-tile-rotate <outdir>
        if (args.Length >= 2 && args[0] == "--render-tile-rotate")
        {
            return RenderTileRotate(args[1]);
        }

        // 2D 界面元素图集：--export-ui-atlas <outdir>
        if (args.Length >= 2 && args[0] == "--export-ui-atlas")
        {
            return ExportUiAtlas(args[1]);
        }

        // 四家手牌明示出图：--render-hands <png> [seed] [mode]
        if (args.Length >= 2 && args[0] == "--render-hands")
        {
            RenderHandsToPng(args);
            return 0;
        }

        // 无窗口验证：--render <png> [time] [table|menu]
        if (args.Length >= 2 && args[0] == "--render")
        {
            double time = args.Length >= 3 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 0.6;
            string screen = args.Length >= 4 ? args[3] : "table";
            RenderToPng(args[1], time, screen);
            return 0;
        }

        // 带编号的布局图（看图说话用）：--render-annotated <png>
        if (args.Length >= 2 && args[0] == "--render-annotated")
        {
            RenderAnnotated(args[1]);
            return 0;
        }

        // 导出可编辑 SVG：--export-svg <dir>
        if (args.Length >= 2 && args[0] == "--export-svg")
        {
            ExportSvg(args[1]);
            return 0;
        }

        // 合并 UI SVG（牌桌 + 四家 + 五个语义动作按钮 + HUD）：--export-ui-svg <file>
        if (args.Length >= 2 && args[0] == "--export-ui-svg")
        {
            ExportUiSvg(args[1]);
            return 0;
        }

        // Skia 牌桌渲染（布局验证）：--render-table <png>
        if (args.Length >= 2 && args[0] == "--render-table")
        {
            RenderTableToPng(args[1], args.Length >= 3 && args[2] == "debug");
            return 0;
        }

        // 真实会话快照出图（自检：后台引擎 → 快照 → Skia 绘制）：--render-session <png> [seed] [mode]
        if (args.Length >= 2 && args[0] == "--render-session")
        {
            RenderSessionToPng(args);
            return 0;
        }

        // 局末结算屏出图：--render-roundend <png> [seed] [mode]
        if (args.Length >= 2 && args[0] == "--render-roundend")
        {
            return RenderRoundEndToPng(args);
        }

        // 音效自检：--export-sounds <dir>
        if (args.Length >= 2 && args[0] == "--export-sounds")
        {
            using var sound = new SoundEffects();
            foreach (var (cue, file, bytes) in sound.ExportWavs(args[1]))
            {
                Console.WriteLine($"  {cue,-9} {bytes,6} B  {file}");
            }

            return 0;
        }

        // 报听三态出图：--render-call <png 基名>（_callable / _disabled / _noten 三张）
        if (args.Length >= 2 && args[0] == "--render-call")
        {
            RenderCallToPng(args[1]);
            return 0;
        }

        // 动作按钮条出图：--render-actions <png>
        if (args.Length >= 2 && args[0] == "--render-actions")
        {
            RenderActionsToPng(args[1]);
            return 0;
        }

        // 设置屏出图：--render-settings <png>
        if (args.Length >= 2 && args[0] == "--render-settings")
        {
            RenderSettingsToPng(args[1]);
            return 0;
        }

        // 界面截图（与游戏内同一条 Screen 路径）：--render-screen <png> [table|menu]
        if (args.Length >= 2 && args[0] == "--render-screen")
        {
            RenderScreenToPng(args[1], args.Length >= 3 ? args[2] : "table");
            return 0;
        }

        // Steam 商店截图：1920×1080，黑底等比居中，输出到 artifacts/steam。
        if (args.Length >= 1 && args[0] == "--export-steam-shots")
        {
            string language = args.Length >= 4 && args[2] == "--lang" ? args[3] : "zh-CN";
            return ExportSteamShots(args.Length >= 2 ? args[1] : "artifacts/steam", language);
        }

        // Steam 胶囊图：主图/页头/小图/库图/库头图。
        if (args.Length >= 1 && args[0] == "--export-steam-capsules")
        {
            return ExportSteamCapsules(args.Length >= 2 ? args[1] : "artifacts/steam/capsules");
        }



        // 暗杠展示自检：--render-ankan <png 基名 | 目录> → 出「对局中 / 和牌后」两张图
        if (args.Length >= 2 && args[0] == "--render-ankan")
        {
            RenderAnkanToPng(args[1]);
            return 0;
        }

        // Yoga 布局预览：--render-yoga <png> [画布边长] [副露组数 0..4]
        if (args.Length >= 2 && args[0] == "--render-yoga")
        {
            float canvasY = args.Length >= 3 ? float.Parse(args[2], CultureInfo.InvariantCulture) : 1400f;
            int meldsY = args.Length >= 4 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 1;
            RenderYogaToPng(args[1], canvasY, meldsY);
            return 0;
        }

        // 2.5D 观感出图：--render-table-png <png> --variant mixed|flat|light|medium
        if (args.Length >= 2 && args[0] == "--render-table-png")
        {
            Render25DToPng(args);
            return 0;
        }

        // 单张牌 Soft-3D 实验：--render-soft3d-tile <png> [tileId]
        if (args.Length >= 2 && args[0] == "--render-soft3d-tile")
        {
            RenderSoft3DTile(args);
            return 0;
        }

        // 帧率基准：--bench <frames>（CPU 光栅，离屏 surface）
        if (args.Length >= 1 && args[0] == "--bench")
        {
            int frames = args.Length >= 2 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 300;
            Bench(frames);
            return 0;
        }

        // GPU 帧率基准：--bench-gl [frames]（隐藏 GL 窗口 → 离屏 GPU surface；无 GPU 时跳过）
        if (args.Length >= 1 && args[0] == "--bench-gl")
        {
            int frames = args.Length >= 2 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 300;
            return BenchGl(frames);
        }

        return null;
    }

}
