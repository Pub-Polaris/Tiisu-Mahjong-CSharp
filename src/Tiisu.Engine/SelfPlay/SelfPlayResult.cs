using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Tiisu.Engine.SelfPlay;

/// <summary>
/// 一次自走（整场）的结果：局数、和牌 / 流局统计、最终点数、事件流及其 SHA256。
/// </summary>
public sealed class SelfPlayResult
{
    public required uint Seed { get; init; }

    public required int ModeTarget { get; init; }

    /// <summary>整场实际打了几局（含连庄 / 延长；= round_end 事件数）。</summary>
    public int Rounds { get; init; }

    /// <summary>引擎的 <c>HandsPlayed</c>（advanceRound 累计；出岭 / 大七星等立即终局的最后一手不计）。</summary>
    public int HandsPlayed { get; init; }

    /// <summary>有和牌（自摸或荣和）的局数。</summary>
    public int WinRounds { get; init; }

    public int TsumoWins { get; init; }

    /// <summary>荣和人数合计（双和计 2）。</summary>
    public int RonWins { get; init; }

    /// <summary>流局局数（荒牌流局 + 特殊流局 + 推牌流局；不含和牌局）。</summary>
    public int DrawRounds { get; init; }

    /// <summary>特殊流局局数（四风连打 / 三家和牌等）。</summary>
    public int SpecialFlowRounds { get; init; }

    /// <summary>终局时的点数（顺序 = 玩家 0..3）。</summary>
    public required IReadOnlyList<int> FinalScores { get; init; }

    /// <summary>是否触发出岭（击飞）：任一家分数 &lt; 0 → 记 −7000 并立即终局（会破坏总分守恒）。</summary>
    public bool Busted => FinalScores.Contains(-7000);

    /// <summary>终局时的段位点。</summary>
    public required IReadOnlyList<double> FinalRankPoints { get; init; }

    /// <summary>事件流 SHA256（小写十六进制），同 seed 必然一致。</summary>
    public required string EventHash { get; init; }

    /// <summary>完整事件流（按发生顺序）。</summary>
    public required IReadOnlyList<GameEvent> Events { get; init; }

    /// <summary>事件流的 SHA256：对每条 <see cref="GameEvent.Canonical"/> 以 <c>\n</c> 连接后取 UTF-8 字节。</summary>
    public static string ComputeHash(IEnumerable<GameEvent> events)
    {
        var builder = new StringBuilder();
        foreach (var ev in events)
        {
            builder.Append(ev.Canonical()).Append('\n');
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexStringLower(hash);
    }

    public string Describe() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "seed={0} mode={1} rounds={2} win={3} (tsumo={4}/ron={5}) draw={6} special={7} scores=[{8}] hash={9}",
            Seed,
            ModeTarget,
            Rounds,
            WinRounds,
            TsumoWins,
            RonWins,
            DrawRounds,
            SpecialFlowRounds,
            string.Join(",", FinalScores),
            EventHash);
}
