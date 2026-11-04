using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;


/// <summary>
/// <c>--export-dataset &lt;out.jsonl&gt; [logDir] [--human-only|--ai-only]</c>
/// 把 <c>roundlog_*.jsonl</c> 里的每个「出牌」动作导出成一条训练样本。
/// 标签 = 实际打出的牌；特征 = 该玩家手牌 / 副露 / 自己的牌河 / 牌山 / 场况。
/// </summary>
internal static class ExportDataset
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("用法：--export-dataset <out.jsonl> [logDir] [--human-only|--ai-only]");
            return 2;
        }

        string output = Path.GetFullPath(args[1]);
        string? logDir = null;
        string mode = "all";
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--human-only")
            {
                mode = "human";
            }
            else if (args[i] == "--ai-only")
            {
                mode = "ai";
            }
            else if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                logDir = args[i];
            }
        }

        logDir ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TiisuMahjong", "logs");

        if (!Directory.Exists(logDir))
        {
            Console.Error.WriteLine($"找不到日志目录：{logDir}");
            return 2;
        }

        var files = Directory.GetFiles(logDir, "roundlog_*.jsonl").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        if (files.Length == 0)
        {
            Console.Error.WriteLine($"目录里没有 roundlog_*.jsonl：{logDir}");
            return 2;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        int total = 0;
        int human = 0;
        int ai = 0;
        var perTile = new int[34];

        using var writer = new StreamWriter(output, append: false, new UTF8Encoding(false));
        foreach (string file in files)
        {
            // round 头：player → name（human / ai_*），按 (session, round) 索引。
            var names = new Dictionary<(string Session, int Round, int Player), string>();

            foreach (string line in File.ReadLines(file))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                JsonNode? node;
                try
                {
                    node = JsonNode.Parse(line);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (node is not JsonObject obj)
                {
                    continue;
                }

                string type = (string?)obj["type"] ?? string.Empty;
                if (type == "round")
                {
                    names[(
                        (string?)obj["session"] ?? string.Empty,
                        (int?)obj["round"] ?? 0,
                        (int?)obj["player"] ?? 0)] = (string?)obj["name"] ?? string.Empty;
                    continue;
                }

                if (type != "step" || (string?)obj["a"] != "discard")
                {
                    continue;
                }

                int player = (int?)obj["p"] ?? 0;
                string session = (string?)obj["session"] ?? string.Empty;
                int round = (int?)obj["round"] ?? 0;
                string name = names.TryGetValue((session, round, player), out var n) ? n : string.Empty;
                bool isHuman = player == 0 || name.Equals("human", StringComparison.OrdinalIgnoreCase);

                if (mode == "human" && !isHuman)
                {
                    continue;
                }

                if (mode == "ai" && isHuman)
                {
                    continue;
                }

                string tile = (string?)obj["t"] ?? string.Empty;
                string hand = (string?)obj["hand"] ?? string.Empty;
                var discards = obj["discards"] as JsonArray;
                var melds = obj["melds"] as JsonArray;

                // 新日志带四家牌河 / 副露数；老日志没有，写空数组保持 schema 一致。
                var riversOut = new JsonArray();
                if (obj["rivers"] is JsonArray riversIn)
                {
                    for (int seat = 0; seat < 4; seat++)
                    {
                        var counts = new int[34];
                        if (seat < riversIn.Count && riversIn[seat] is JsonArray river)
                        {
                            foreach (var t in river)
                            {
                                int riverIdx = TileIndex((string?)t ?? string.Empty);
                                if (riverIdx >= 0)
                                {
                                    counts[riverIdx]++;
                                }
                            }
                        }

                        riversOut.Add(new JsonArray(counts.Select(c => (JsonNode)c).ToArray()));
                    }
                }

                var meldCounts = new JsonArray();
                if (obj["meldCounts"] is JsonArray mc)
                {
                    foreach (var v in mc)
                    {
                        meldCounts.Add((int?)v ?? 0);
                    }
                }
                else
                {
                    for (int seat = 0; seat < 4; seat++)
                    {
                        meldCounts.Add(0);
                    }
                }

                var sample = new JsonObject
                {
                    ["v"] = 2,
                    ["src"] = isHuman ? "human" : "ai",
                    ["p"] = player,
                    ["seat"] = (string?)obj["w"] ?? string.Empty,
                    ["dealer"] = (int?)obj["d"] ?? 0,
                    ["wall"] = (int?)obj["wall"] ?? 0,
                    ["h"] = (int?)obj["h"] ?? 0,
                    ["rw"] = (int?)obj["rw"] ?? 0,
                    ["dc"] = (int?)obj["dc"] ?? 0,
                    ["turn"] = discards?.Count ?? 0,
                    ["hand"] = hand,
                    ["melds"] = melds?.DeepClone() ?? new JsonArray(),
                    ["discards"] = discards?.DeepClone() ?? new JsonArray(),
                    ["rivers"] = riversOut,
                    ["meldCounts"] = meldCounts,
                    ["label"] = tile,
                    ["labelIdx"] = TileIndex(tile),
                };

                writer.WriteLine(sample.ToJsonString(WriteOptions));
                total++;
                if (isHuman)
                {
                    human++;
                }
                else
                {
                    ai++;
                }

                int idx = TileIndex(tile);
                if (idx >= 0)
                {
                    perTile[idx]++;
                }
            }
        }

        var meta = new JsonObject
        {
            ["version"] = 1,
            ["logDir"] = logDir,
            ["files"] = files.Length,
            ["mode"] = mode,
            ["total"] = total,
            ["human"] = human,
            ["ai"] = ai,
            ["perTile"] = new JsonArray(perTile.Select(c => (JsonNode)c).ToArray()),
        };
        File.WriteAllText(Path.ChangeExtension(output, ".meta.json"),
            meta.ToJsonString(new JsonSerializerOptions(WriteOptions) { WriteIndented = true }),
            new UTF8Encoding(false));

        Console.WriteLine($"dataset: {output}");
        Console.WriteLine($"  日志文件 {files.Length} 个 · 样本 {total}（人类 {human} / AI {ai}）");
        Console.WriteLine($"  label 分布（34 格，0=1m…33=7z）：{string.Join(",", perTile)}");
        return 0;
    }

    /// <summary>牌 id → 0..33（m 0-8, p 9-17, s 18-26, z 27-33）；未知返回 -1。</summary>
    private static int TileIndex(string id)
    {
        if (id.Length != 2)
        {
            return -1;
        }

        int num = id[0] - '0';
        if (num is < 1 or > 9)
        {
            return -1;
        }

        return id[1] switch
        {
            'm' => num - 1,
            'p' => 9 + num - 1,
            's' => 18 + num - 1,
            'z' => num is >= 1 and <= 7 ? 27 + num - 1 : -1,
            _ => -1,
        };
    }
}
