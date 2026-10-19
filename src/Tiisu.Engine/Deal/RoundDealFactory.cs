namespace Tiisu.Engine.Deal;

using Tiisu.Engine.Random;

/// <summary>
/// 自走用开局工厂：洗牌 → 掷切牌骰 → 切牌 → 发牌，产出带真实牌序的 <see cref="RoundDeal"/>。
/// 定庄骰由调用方（<see cref="SelfPlay.SelfPlayDriver"/>）只在整场第一局掷一次；后续局沿用引擎维护的庄家。
/// </summary>
public static class RoundDealFactory
{
    public sealed record DealLayout(
        RoundDeal Deal,
        IReadOnlyList<Tile> ShuffledDeck,
        int[] Dice,
        int SkipTiles,
        TileSetFactory.WallLayout Wall);

    /// <summary>
    /// 生成一局。<paramref name="dealerIndex"/> 是本局庄家（首局由定庄骰决定，之后由引擎庄家轮转决定）。
    /// </summary>
    public static RoundDeal Create(int dealerIndex, IRandomSource random) => Cut(dealerIndex, random).Deal;

    /// <summary>同 <see cref="Create"/>，但额外返回洗牌结果 / 骰子 / 切牌布局，便于测试与审计。</summary>
    public static DealLayout Cut(int dealerIndex, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (dealerIndex is < 0 or >= MahjongEngine.PlayerCount)
        {
            throw new ArgumentOutOfRangeException(nameof(dealerIndex));
        }

        var deck = TileSetFactory.CreateDeck();

        // 顺序与 engine.js startNewRound 一致：先掷切牌骰（定庄骰在调用方、整场只在首局掷），再洗牌、切牌。
        var dice = TileSetFactory.RollDice(random);
        int skipTiles = (dice[0] + dice[1]) * 2; // 跳墩张数 = 2 × 骰子和

        TileSetFactory.Shuffle(deck, random);

        var layout = TileSetFactory.Cut(deck, skipTiles);

        // 发牌从开局牌山末尾 pop，与 engine.js 的 deal() 一致；剩下的 79 张就是本局牌山。
        var wall = new List<Tile>(layout.Wall);
        var hands = TileSetFactory.DealFromWall(wall, dealerIndex);

        var roundDeal = new RoundDeal(dealerIndex, skipTiles, hands, deck);
        return new DealLayout(roundDeal, deck, dice, skipTiles, layout);
    }
}
