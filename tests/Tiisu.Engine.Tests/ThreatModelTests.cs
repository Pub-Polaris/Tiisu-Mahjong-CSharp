using Tiisu.Core;
using Tiisu.Engine;
using Tiisu.Engine.Ai;

namespace Tiisu.Engine.Tests;

/// <summary>
/// B 期防守威胁模型：现物 / 筋 / 壁 / 字牌 / 副露 / 残局 / 场况，以及 Tension 合成。
/// 所有用例只用公开信息（牌河 / 副露 / 场况），不构造别家手牌或牌山。
/// </summary>
public sealed class ThreatModelTests
{
    // ─────────────────────────────────────────────────────────────
    // 现物 / 筋 / 壁
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Genbutsu_IsNearlyZero()
    {
        var input = Input(rivers: Rivers("5m", "", "", ""));

        double danger = ThreatModel.Danger(T("5m"), input);

        Assert.InRange(danger, 0.0, 0.1);
        Assert.Equal(0.02, danger, 9);
    }

    [Fact]
    public void Genbutsu_LowerThanAnyUnseenTile()
    {
        var rivers = Rivers("5m", "", "", "");
        var input = Input(rivers: rivers);

        Assert.True(ThreatModel.Danger(T("5m"), input) < ThreatModel.Danger(T("5p"), input));
    }

    [Fact]
    public void Suji_LowerThanNonSuji()
    {
        var suji = Input(rivers: Rivers("2m", "", "", ""));
        var nonSuji = Input(rivers: Rivers("9p", "", "", ""));

        double sujiDanger = ThreatModel.Danger(T("5m"), suji);
        double plainDanger = ThreatModel.Danger(T("5m"), nonSuji);

        Assert.Equal(0.30, sujiDanger, 9);
        Assert.Equal(0.60, plainDanger, 9);
        Assert.True(sujiDanger < plainDanger);
    }

    [Fact]
    public void Suji_RecognizesBothPartners()
    {
        var viaTwo = Input(rivers: Rivers("", "2m", "", ""));
        var viaEight = Input(rivers: Rivers("", "", "8m", ""));

        Assert.Equal(0.30, ThreatModel.Danger(T("5m"), viaTwo), 9);
        Assert.Equal(0.30, ThreatModel.Danger(T("5m"), viaEight), 9);
    }

    [Fact]
    public void Wall_LowersDanger()
    {
        var plain = Input();
        var walled = Input(melds: FourMelds(
            Array.Empty<Meld>(),
            new[] { new Meld(MeldType.Ankan, Tiles("5555m")) },
            Array.Empty<Meld>(),
            Array.Empty<Meld>()));

        double withWall = ThreatModel.Danger(T("5m"), walled);
        double withoutWall = ThreatModel.Danger(T("5m"), plain);

        Assert.True(withWall < withoutWall);
        Assert.Equal(0.08, withWall, 9);
    }

    [Fact]
    public void SelfHand_CountsTowardWall()
    {
        var input = Input(hand: Tiles("5555m"));

        Assert.Equal(0.08, ThreatModel.Danger(T("5m"), input), 9);
    }

    // ─────────────────────────────────────────────────────────────
    // 中张 / 幺九 / 字牌
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void CenterTile_IsMoreDangerousThanTerminal_WhenUnknown()
    {
        var input = Input();

        Assert.True(ThreatModel.Danger(T("5m"), input) > ThreatModel.Danger(T("1m"), input));
        Assert.Equal(0.60, ThreatModel.Danger(T("5m"), input), 9);
        Assert.Equal(0.32, ThreatModel.Danger(T("1m"), input), 9);
    }

    [Fact]
    public void UnseenHonor_IsMoreDangerousThanCenterTile()
    {
        var input = Input();

        Assert.True(ThreatModel.Danger(T("1z"), input) > ThreatModel.Danger(T("5m"), input));
        Assert.Equal(0.72, ThreatModel.Danger(T("1z"), input), 9);
    }

    [Fact]
    public void UnseenHonor_IsMoreDangerousThanFullyVisibleHonor()
    {
        var unseen = Input();
        var visible = Input(melds: FourMelds(
            Array.Empty<Meld>(),
            new[] { new Meld(MeldType.Ankan, Tiles("1111z")) },
            Array.Empty<Meld>(),
            Array.Empty<Meld>()));

        double unseenDanger = ThreatModel.Danger(T("1z"), unseen);
        double visibleDanger = ThreatModel.Danger(T("1z"), visible);

        Assert.True(visibleDanger < unseenDanger);
        Assert.Equal(0.024, visibleDanger, 9);
    }

    // ─────────────────────────────────────────────────────────────
    // 副露 / 残局 / 场况
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TwoOpenMelds_RaiseDanger_AndThreeRaiseMore()
    {
        var none = Input();
        var two = Input(melds: FourMelds(
            Array.Empty<Meld>(),
            new[] { Pon("111z"), Pon("222z") },
            Array.Empty<Meld>(),
            Array.Empty<Meld>()));
        var three = Input(melds: FourMelds(
            Array.Empty<Meld>(),
            new[] { Pon("111z"), Pon("222z"), Pon("333z") },
            Array.Empty<Meld>(),
            Array.Empty<Meld>()));

        double noneDanger = ThreatModel.Danger(T("5m"), none);
        double twoDanger = ThreatModel.Danger(T("5m"), two);
        double threeDanger = ThreatModel.Danger(T("5m"), three);

        Assert.True(twoDanger > noneDanger);
        Assert.True(threeDanger > twoDanger);
        Assert.Equal(0.69, twoDanger, 9);
        Assert.Equal(0.76, threeDanger, 9);
    }

    [Fact]
    public void SelfMelds_DoNotRaiseDanger()
    {
        var none = Input();
        var own = Input(melds: FourMelds(
            new[] { Pon("111z"), Pon("222z"), Pon("333z") },
            Array.Empty<Meld>(),
            Array.Empty<Meld>(),
            Array.Empty<Meld>()));

        Assert.Equal(
            ThreatModel.Danger(T("5m"), none),
            ThreatModel.Danger(T("5m"), own),
            9);
    }

    [Fact]
    public void LateGame_RaisesDanger()
    {
        var early = Input(wall: 70);
        var late = Input(wall: 10);

        double earlyDanger = ThreatModel.Danger(T("5m"), early);
        double lateDanger = ThreatModel.Danger(T("5m"), late);

        Assert.True(lateDanger > earlyDanger);
        Assert.Equal(0.78, lateDanger, 9);
    }

    [Fact]
    public void LateGame_IsGraduatedByWallCount()
    {
        double wall25 = ThreatModel.Danger(T("5m"), Input(wall: 25));
        double wall12 = ThreatModel.Danger(T("5m"), Input(wall: 12));
        double wall5 = ThreatModel.Danger(T("5m"), Input(wall: 5));

        Assert.True(wall25 < wall12);
        Assert.True(wall12 < wall5);
    }

    [Fact]
    public void HonbaAndScoreBehind_RaiseDangerSlightly()
    {
        var calm = Input(scores: new[] { 25000, 25000, 25000, 25000 });
        var behind = Input(
            scores: new[] { 5000, 30000, 30000, 30000 },
            honba: 3);

        double calmDanger = ThreatModel.Danger(T("5m"), calm);
        double behindDanger = ThreatModel.Danger(T("5m"), behind);

        Assert.True(behindDanger > calmDanger);
        Assert.InRange(behindDanger - calmDanger, 0.0, 0.1);
    }

    [Fact]
    public void Danger_IsClampedToUnitRange()
    {
        var extreme = Input(
            melds: FourMelds(
                new[] { Pon("111z"), Pon("222z"), Pon("333z") },
                new[] { Pon("444z"), Pon("555z"), Pon("666z") },
                new[] { Pon("777z"), Pon("111m"), Pon("999m") },
                Array.Empty<Meld>()),
            wall: 0,
            scores: new[] { 0, 50000, 40000, 30000 },
            honba: 8);

        double danger = ThreatModel.Danger(T("5m"), extreme);

        Assert.InRange(danger, 0.0, 1.0);
        Assert.Equal(1.0, danger, 9);
        Assert.InRange(ThreatModel.Danger(T("5m"), Input(rivers: Rivers("5m", "", "", ""))), 0.0, 1.0);
    }

    // ─────────────────────────────────────────────────────────────
    // Tension
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Tension_QuietBoard_LowerThanOpenDangerousBoard()
    {
        // 安静：四家牌河覆盖全部 34 种牌（全是现物）、无副露、开局、分数持平。
        var quiet = Input(rivers: Rivers(
            "123456789m",
            "123456789p",
            "123456789s",
            "1234567z"));
        var quietTension = ThreatModel.Tension(quiet);

        // 紧张：对手 9 组副露、残局、大比分落后、牌河几乎空白。
        var tense = Input(
            rivers: Rivers("1m", "", "", ""),
            melds: FourMelds(
                new[] { Pon("111z"), Pon("222z"), Pon("333z") },
                new[] { Pon("444z"), Pon("555z"), Pon("666z") },
                new[] { Pon("777z"), Pon("111m"), Pon("999m") },
                Array.Empty<Meld>()),
            wall: 10,
            scores: new[] { 5000, 40000, 35000, 30000 });
        var tenseTension = ThreatModel.Tension(tense);

        Assert.True(quietTension < tenseTension);
        Assert.InRange(quietTension, 0.0, 0.1);
        Assert.InRange(tenseTension, 0.6, 1.0);
    }

    [Fact]
    public void Tension_NoMeldsAndAllGenbutsu_IsLow()
    {
        var input = Input(rivers: Rivers(
            "123456789m",
            "123456789p",
            "123456789s",
            "1234567z"));

        Assert.InRange(ThreatModel.Tension(input), 0.0, 0.1);
    }

    // ─────────────────────────────────────────────────────────────
    // 确定性
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void Danger_IsDeterministic()
    {
        var first = Input(rivers: Rivers("2m", "", "", ""), wall: 20);
        var second = Input(rivers: Rivers("2m", "", "", ""), wall: 20);

        Assert.Equal(ThreatModel.Danger(T("5m"), first), ThreatModel.Danger(T("5m"), first), 12);
        Assert.Equal(ThreatModel.Danger(T("5m"), first), ThreatModel.Danger(T("5m"), second), 12);
        Assert.Equal(ThreatModel.Danger(T("7z"), first), ThreatModel.Danger(T("7z"), second), 12);
    }

    [Fact]
    public void Tension_IsDeterministic()
    {
        var first = Input(rivers: Rivers("2m", "", "", ""), wall: 20);
        var second = Input(rivers: Rivers("2m", "", "", ""), wall: 20);

        Assert.Equal(ThreatModel.Tension(first), ThreatModel.Tension(second), 12);
        Assert.Equal(ThreatModel.Tension(first), ThreatModel.Tension(first), 12);
    }

    // ─────────────────────────────────────────────────────────────
    // 辅助
    // ─────────────────────────────────────────────────────────────

    private static ThreatInput Input(
        IReadOnlyList<IReadOnlyList<Tile>>? rivers = null,
        IReadOnlyList<IReadOnlyList<Meld>>? melds = null,
        int wall = 70,
        int[]? scores = null,
        int honba = 0,
        int self = 0,
        IReadOnlyList<Tile>? hand = null) =>
        new(
            rivers ?? Rivers("", "", "", ""),
            melds ?? FourMelds(
                Array.Empty<Meld>(),
                Array.Empty<Meld>(),
                Array.Empty<Meld>(),
                Array.Empty<Meld>()),
            new[] { wall, wall, wall, wall },
            wall,
            scores ?? new[] { 25000, 25000, 25000, 25000 },
            honba,
            self)
        {
            SelfHand = hand,
        };

    private static IReadOnlyList<IReadOnlyList<Meld>> FourMelds(params IReadOnlyList<Meld>[] seats) =>
        seats;

    private static Meld Pon(string spec) => new(MeldType.Pon, Tiles(spec));

    private static IReadOnlyList<IReadOnlyList<Tile>> Rivers(params string[] specs) =>
        specs.Select(spec => (IReadOnlyList<Tile>)Tiles(spec)).ToList();

    private static Tile T(string id) => Tiles(id)[0];

    private static List<Tile> Tiles(string spec)
    {
        var result = new List<Tile>();
        var digits = new List<char>();
        foreach (char c in spec)
        {
            if (char.IsDigit(c))
            {
                digits.Add(c);
                continue;
            }

            if (!char.IsLetter(c))
            {
                throw new FormatException("非法牌串: " + spec);
            }

            char suit = char.ToLowerInvariant(c);
            foreach (char digit in digits)
            {
                result.Add(Tile.Parse(suit + digit.ToString()));
            }

            digits.Clear();
        }

        if (digits.Count > 0)
        {
            throw new FormatException("牌串末尾缺少花色: " + spec);
        }

        return result;
    }
}
