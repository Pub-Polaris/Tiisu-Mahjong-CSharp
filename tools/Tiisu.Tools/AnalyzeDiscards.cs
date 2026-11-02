using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// <c>--analyze-discards &lt;dataset.jsonl&gt; [--out report.md]</c>
/// 统计人类 / AI 的弃牌偏好：牌种分布、类别占比、巡目影响，并给出分布差异。
/// </summary>
internal static class AnalyzeDiscards
{
    private static readonly string[] TileNames =
    {
        "1m","2m","3m","4m","5m","6m","7m","8m","9m",
        "1p","2p","3p","4p","5p","6p","7p","8p","9p",
        "1s","2s","3s","4s","5s","6s","7s","8s","9s",
        "1z","2z","3z","4z","5z","6z","7z",
    };

    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("用法：--analyze-discards <dataset.jsonl> [--out report.md]");
            return 2;
        }

        string input = Path.GetFullPath(args[1]);
        string output = Path.ChangeExtension(input, ".report.md");
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--out" && i + 1 < args.Length)
            {
                output = Path.GetFullPath(args[i + 1]);
            }
        }

        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"找不到数据集：{input}");
            return 2;
        }

        var human = new Stats();
        var ai = new Stats();
        var json = new JsonDocumentOptions { AllowTrailingCommas = true };

        foreach (string line in File.ReadLines(input))
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

            int idx = (int?)obj["labelIdx"] ?? -1;
            if (idx is < 0 or > 33)
            {
                continue;
            }

            string src = (string?)obj["src"] ?? "ai";
            var target = src == "human" ? human : ai;
            int turn = (int?)obj["turn"] ?? 0;
            target.Add(idx, turn);
        }

        var md = new StringBuilder();
        md.AppendLine("# 弃牌偏好统计（人类 vs AI）");
        md.AppendLine();
        md.AppendLine($"数据：`{Path.GetFileName(input)}`");
        md.AppendLine();
        md.AppendLine("## 样本量");
        md.AppendLine();
        md.AppendLine("| 来源 | 样本 | 平均巡目 |");
        md.AppendLine("|---|---:|---:|");
        md.AppendLine($"| 人类 | {human.Total} | {human.AverageTurn():F1} |");
        md.AppendLine($"| AI | {ai.Total} | {ai.AverageTurn():F1} |");

        md.AppendLine();
        md.AppendLine("## 类别占比");
        md.AppendLine();
        md.AppendLine("| 类别 | 人类 | AI | 差（人−AI） |");
        md.AppendLine("|---|---:|---:|---:|");
        AppendCategory(md, "字牌（1z-7z）", human.HonorRate(), ai.HonorRate());
        AppendCategory(md, "幺九（1/9 数牌）", human.TerminalRate(), ai.TerminalRate());
        AppendCategory(md, "中张（2-8 数牌）", human.MiddleRate(), ai.MiddleRate());

        md.AppendLine();
        md.AppendLine("## 人类更爱打的牌（前 10，按「人类占比 − AI 占比」）");
        md.AppendLine();
        md.AppendLine("| 牌 | 人类 | AI | 差 |");
        md.AppendLine("|---|---:|---:|---:|");
        foreach (var (idx, delta) in Enumerable.Range(0, 34)
                     .Select(i => (i, human.Share(i) - ai.Share(i)))
                     .OrderByDescending(x => x.Item2)
                     .Take(10))
        {
            md.AppendLine($"| {TileNames[idx]} | {human.Share(idx):P1} | {ai.Share(idx):P1} | {delta:+0.0%;-0.0%;0.0%} |");
        }

        md.AppendLine();
        md.AppendLine("## AI 更爱打的牌（前 10）");
        md.AppendLine();
        md.AppendLine("| 牌 | 人类 | AI | 差 |");
        md.AppendLine("|---|---:|---:|---:|");
        foreach (var (idx, delta) in Enumerable.Range(0, 34)
                     .Select(i => (i, human.Share(i) - ai.Share(i)))
                     .OrderBy(x => x.Item2)
                     .Take(10))
        {
            md.AppendLine($"| {TileNames[idx]} | {human.Share(idx):P1} | {ai.Share(idx):P1} | {delta:+0.0%;-0.0%;0.0%} |");
        }

        md.AppendLine();
        md.AppendLine("## 巡目影响（字牌占各自弃牌的比例）");
        md.AppendLine();
        md.AppendLine("| 巡目段 | 人类 | AI |");
        md.AppendLine("|---|---:|---:|");
        foreach (var (lo, hi, label) in new[] { (0, 5, "0-5"), (6, 11, "6-11"), (12, 17, "12-17"), (18, 99, "18+") })
        {
            md.AppendLine($"| {label} | {human.HonorRate(lo, hi):P1} | {ai.HonorRate(lo, hi):P1} |");
        }

        md.AppendLine();
        md.AppendLine("## 分布差异");
        md.AppendLine();
        md.AppendLine($"- 总变差距离（0=完全相同，1=完全不同）：**{human.TotalVariationDistance(ai):F3}**");
        md.AppendLine($"- 人类 vs AI 的 JS 散度：**{human.JensenShannon(ai):F4}**");

        md.AppendLine();
        md.AppendLine("## 说明");
        md.AppendLine();
        md.AppendLine("- 样本来自 `roundlog_*.jsonl`，每条 = 一次实际弃牌。");
        md.AppendLine("- 巡目 = 该玩家本局已打出的牌数，`0` 表示第一打。");
        md.AppendLine("- 人类样本量远少于 AI 时会抖，建议攒到 5000+ 再看差异。");

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, md.ToString(), new UTF8Encoding(false));

        Console.WriteLine($"report: {output}");
        Console.WriteLine($"  人类 {human.Total} 条 · AI {ai.Total} 条 · TV 距离 {human.TotalVariationDistance(ai):F3}");
        return 0;
    }

    private static void AppendCategory(StringBuilder md, string name, double humanRate, double aiRate)
    {
        md.AppendLine($"| {name} | {humanRate:P1} | {aiRate:P1} | {humanRate - aiRate:+0.0%;-0.0%;0.0%} |");
    }

    private sealed class Stats
    {
        private readonly int[] _tiles = new int[34];
        private readonly int[] _honorByTurn = new int[20];
        private readonly int[] _turnCount = new int[20];

        public int Total { get; private set; }

        private long _turnSum;

        public void Add(int tileIdx, int turn)
        {
            _tiles[tileIdx]++;
            Total++;
            _turnSum += turn;
            int bucket = Math.Min(turn, 19);
            _turnCount[bucket]++;
            if (tileIdx >= 27)
            {
                _honorByTurn[bucket]++;
            }
        }

        public double AverageTurn() => Total == 0 ? 0 : (double)_turnSum / Total;

        public double Share(int tile) => Total == 0 ? 0 : (double)_tiles[tile] / Total;

        public double HonorRate()
        {
            int sum = 0;
            for (int i = 27; i < 34; i++)
            {
                sum += _tiles[i];
            }

            return Total == 0 ? 0 : (double)sum / Total;
        }

        public double TerminalRate()
        {
            int sum = 0;
            for (int suit = 0; suit < 3; suit++)
            {
                sum += _tiles[suit * 9] + _tiles[suit * 9 + 8];
            }

            return Total == 0 ? 0 : (double)sum / Total;
        }

        public double MiddleRate() => 1 - HonorRate() - TerminalRate();

        public double HonorRate(int lo, int hi)
        {
            int h = 0;
            int t = 0;
            for (int i = lo; i <= hi && i < 20; i++)
            {
                h += _honorByTurn[i];
                t += _turnCount[i];
            }

            return t == 0 ? 0 : (double)h / t;
        }

        public double TotalVariationDistance(Stats other)
        {
            if (Total == 0 || other.Total == 0)
            {
                return 0;
            }

            double sum = 0;
            for (int i = 0; i < 34; i++)
            {
                sum += Math.Abs(Share(i) - other.Share(i));
            }

            return sum / 2;
        }

        public double JensenShannon(Stats other)
        {
            if (Total == 0 || other.Total == 0)
            {
                return 0;
            }

            double js = 0;
            for (int i = 0; i < 34; i++)
            {
                double p = Share(i);
                double q = other.Share(i);
                double m = (p + q) / 2;
                if (p > 0)
                {
                    js += 0.5 * p * Math.Log2(p / m);
                }

                if (q > 0)
                {
                    js += 0.5 * q * Math.Log2(q / m);
                }
            }

            return js;
        }
    }
}
