using Tiisu.Core;
using Xunit;

namespace Tiisu.Core.Tests;

/// <summary>稀有役的规则可达性：构造牌例证明判役器能识别，不依赖 AI 自然打出。</summary>
public sealed class RareYakuReachabilityTests
{
    private static readonly WinChecker Checker = new(YakuData.LoadDefault());

    private static List<Tile> H(string spec)
    {
        var tiles = new List<Tile>();
        foreach (string part in spec.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            char suitChar = part[^1];
            Suit suit = suitChar switch
            {
                'm' => Suit.Man,
                'p' => Suit.Pin,
                's' => Suit.Sou,
                'z' => Suit.Honor,
                _ => throw new ArgumentException(part, nameof(spec)),
            };
            foreach (char digit in part[..^1])
            {
                tiles.Add(new Tile(suit, digit - '0'));
            }
        }

        return tiles;
    }

    private static Meld M(string spec)
    {
        int colon = spec.IndexOf(':');
        return new Meld(spec[..colon], H(spec[(colon + 1)..]));
    }

    private static WinResult Check(string hand, string[]? melds = null, bool selfDraw = true, bool? hasCalled = null)
    {
        var meldList = melds?.Select(M).ToList() ?? new List<Meld>();
        int ankan = meldList.Count(m => m.Type == "ankan");
        int minkan = meldList.Count(m => m.Type is "minkan" or "kakan");
        bool called = hasCalled ?? (meldList.Count > 0);
        return Checker.CheckWin(H(hand), selfDraw, called, ankan, minkan, meldList, false, null, null);
    }

    private static bool Has(WinResult r, string name) => r.ActiveYaku.Any(y => y.Name == name);

    [Fact]
    public void 大七星_七对全字牌可达()
    {
        var r = Check("11z 22z 33z 44z 55z 66z 77z");
        Assert.True(r.Success, r.Reason);
        Assert.True(r.SevenHonors);
        Assert.True(r.ThresholdFu >= 7);
    }

    [Fact]
    public void 九莲宝灯_门清九面可达()
    {
        var r = Check("11112345678999m");
        Assert.True(r.Success, r.Reason);
        Assert.True(Has(r, "九莲宝灯"), string.Join(",", r.ActiveYaku.Select(y => y.Name)));
    }

    [Fact]
    public void 字一色_全字牌刻子可达()
    {
        var r = Check("222z 555z 666z 33z", new[] { "pon:111z" });
        Assert.True(r.Success, r.Reason);
        Assert.True(Has(r, "字一色"), string.Join(",", r.ActiveYaku.Select(y => y.Name)));
    }

    [Fact]
    public void 清老头_全幺九刻子可达()
    {
        var r = Check("999m 111p 999p 11s", new[] { "pon:111m" });
        Assert.True(r.Success, r.Reason);
        Assert.True(Has(r, "清老头"), string.Join(",", r.ActiveYaku.Select(y => y.Name)));
    }

    [Fact]
    public void 四暗杠_四组暗杠可达()
    {
        var r = Check("11z", new[] { "ankan:1111m", "ankan:9999m", "ankan:1111p", "ankan:9999p" });
        Assert.True(r.Success, r.Reason);
        Assert.True(Has(r, "四暗杠"), string.Join(",", r.ActiveYaku.Select(y => y.Name)));
    }

    [Fact]
    public void 四喜和_四风刻可达()
    {
        var r = Check("111z 222z 333z 444z 55z");
        Assert.True(r.Success, r.Reason);
        Assert.True(Has(r, "四喜和"), string.Join(",", r.ActiveYaku.Select(y => y.Name)));
    }
}
