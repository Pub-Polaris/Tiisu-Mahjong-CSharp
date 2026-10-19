using System.Text.Json;

namespace Tiisu.Engine;

/// <summary>
/// 赋位表（ike.json 的 <c>points</c> 节）。引擎不联网、不读环境变量：
/// 直接读 Core 内嵌的同一份 ike.json，保证与 Web 侧同源。
/// </summary>
/// <param name="PerFu">每赋点数（JS <c>points.perFu</c>）。</param>
/// <param name="Thresholds">赋数档位（JS <c>points.thresholds</c>）。</param>
/// <param name="Baselines">各档基础点（JS <c>points.baselines</c>，比档位多一项）。</param>
public sealed record PointsConfig(int PerFu, IReadOnlyList<int> Thresholds, IReadOnlyList<int> Baselines)
{
    public static PointsConfig LoadDefault()
    {
        var assembly = typeof(Tiisu.Core.YakuData).Assembly;
        using var stream = assembly.GetManifestResourceStream("Tiisu.Core.ike.json")
            ?? throw new InvalidOperationException("找不到 Tiisu.Core 内嵌的 ike.json");
        using var document = JsonDocument.Parse(stream);
        var points = document.RootElement.GetProperty("points");
        int perFu = points.GetProperty("perFu").GetInt32();
        var thresholds = points.GetProperty("thresholds").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        var baselines = points.GetProperty("baselines").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        if (baselines.Length != thresholds.Length + 1)
        {
            throw new InvalidOperationException("ike.json 的 points.baselines 长度应为 thresholds + 1");
        }

        return new PointsConfig(perFu, thresholds, baselines);
    }
}
