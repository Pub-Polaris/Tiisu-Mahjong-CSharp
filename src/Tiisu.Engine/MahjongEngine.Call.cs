using Tiisu.Core;

namespace Tiisu.Engine;

/// <summary>
/// 报听（叫胡）机制：宣言、锁手（听牌张集合不变）、暗杠例外、禁吃碰、错和罚则、本巡/本局/下局禁和。
/// <para>
/// 规则口径见 <c>docs/DSH/报听机制_261023.md</c>：
/// <list type="bullet">
/// <item>手牌处于听牌状态即可报听，不限门清 / 副露，宣言时机是「本次操作执行之后」；</item>
/// <item>报听后不得改变听牌张集合，禁吃碰，只允许听牌形不变的暗杠；</item>
/// <item>报听不给赋、无收益、无二次确认、不可主动取消（和牌 / 错和 / 流局时解除）；</item>
/// <item>错和 = 报听后主动宣布和牌但该手赋 &lt; 起和线：<c>Z = 赋 × 700</c> 向其他三家各付；
/// <c>Z &gt; 2100</c> 为重罚（子家违规：庄家 14000 + 另两家各 7000），并附本局 + 下局禁和。</item>
/// </list>
/// </para>
/// 本文件只做规则判定与判罚，不含任何计时 / UI；由 Runtime 决定何时调用。
/// </summary>
public sealed partial class MahjongEngine
{
    /// <summary>流局时四家是否听牌（已报听者自动算听牌）；非流局态为空表。</summary>
    private bool[] _flowTenpai = Array.Empty<bool>();

    /// <summary>本局禁和（错和罚）。</summary>
    private readonly bool[] _callBannedHand = new bool[PlayerCount];

    /// <summary>下局禁和（重罚错和）。开局时并入 <see cref="_callBannedHand"/>。</summary>
    private readonly bool[] _callBannedNextHand = new bool[PlayerCount];

    /// <summary>本巡禁和（普通错和）：到该家下一次轮到自己为止。</summary>
    private readonly bool[] _callBannedTurn = new bool[PlayerCount];

    /// <summary>起和线（赋数门槛，ike.json 的 baseFu；默认 7）。</summary>
    public int BaseFu => _yakuData.BaseFu;

    /// <summary>流局翻牌用的听牌表（四家；已报听者恒为 true）。空表 = 当前不是流局态。</summary>
    public IReadOnlyList<bool> FlowTenpai => _flowTenpai;

    /// <summary>该家当前是否被禁和（本局 / 本巡）。</summary>
    public bool IsWinBanned(Player player) =>
        _callBannedHand[player.Index] || _callBannedTurn[player.Index];

    /// <summary>该家下局是否禁和（重罚错和的余波）。</summary>
    public bool IsWinBannedNextHand(int playerIndex) =>
        playerIndex >= 0 && playerIndex < PlayerCount && _callBannedNextHand[playerIndex];

    /// <summary>本局禁和标记（只读，存档 / 自检用）。</summary>
    public IReadOnlyList<bool> CallWinBanned => _callBannedHand;

    /// <summary>下局禁和标记（只读，存档 / 自检用）。</summary>
    public IReadOnlyList<bool> CallWinBannedNextHand => _callBannedNextHand;

    // ── 宣言 ─────────────────────────────────────────────────────

    /// <summary>
    /// 报听条件：本局未结束、尚未报听、手牌是 13 张形（<c>13 + 杠数</c>）且真的听牌。
    /// 荣和窗口里手牌含待牌，用 <see cref="DeclareCallWithHand"/> 另行判定。
    /// </summary>
    public bool CanDeclareCall(Player player)
    {
        if (GameOver || player.IsCalled)
        {
            return false;
        }

        if (player.TotalTiles() != 13 + player.KongCount())
        {
            return false;
        }

        return IsTenpai(player);
    }

    /// <summary>报听宣言（手牌已是 13 张形）。成功即锁定听牌张集合。</summary>
    public bool DeclareCall(Player player) => DeclareCallWithHand(player, player.Hand);

    /// <summary>
    /// 报听宣言（按给定 13 张形手牌判定）。用于荣和窗口：那时手牌里临时留着荣和待牌，
    /// 判定要用「去掉待牌」的 13 张形。
    /// </summary>
    public bool DeclareCallWithHand(Player player, IReadOnlyList<Tile> hand)
    {
        if (GameOver || player.IsCalled)
        {
            return false;
        }

        if (hand.Count + player.MeldTiles() != 13 + player.KongCount())
        {
            return false;
        }

        ulong mask = WaitMaskFor(hand, player.Melds, player.AnkanCount, player.MinkanCount, player.SeatWind, player.FirstTurn);
        if (mask == 0)
        {
            return false;
        }

        player.IsCalled = true;
        player.CalledWaitMask = mask;
        return true;
    }

    // ── 锁手 ─────────────────────────────────────────────────────

    /// <summary>
    /// 锁手校验（弃牌）：报听后打出这张牌，听牌张集合必须与宣言时完全一致。
    /// 只在报听后生效；未报听恒为允许。
    /// </summary>
    public bool CallAllowsDiscard(Player player, Tile tile, out string? reason)
    {
        reason = null;
        if (!player.IsCalled)
        {
            return true;
        }

        var hand = new List<Tile>(player.Hand);
        int index = hand.IndexOf(tile);
        if (index < 0)
        {
            reason = "这张牌不在手里";
            return false;
        }

        hand.RemoveAt(index);
        if (hand.Count + player.MeldTiles() != 13 + player.KongCount())
        {
            reason = "手牌张数不对，无法校验听牌形";
            return false;
        }

        ulong mask = WaitMaskFor(hand, player.Melds, player.AnkanCount, player.MinkanCount, player.SeatWind, player.FirstTurn);
        if (mask != player.CalledWaitMask)
        {
            reason = "报听后听牌张不能变";
            return false;
        }

        return true;
    }

    /// <summary>锁手校验（副露）：报听后禁止吃 / 碰 / 明杠 / 加杠。</summary>
    public bool CallAllowsMeld(Player player, MeldType type, out string? reason)
    {
        reason = null;
        if (!player.IsCalled)
        {
            return true;
        }

        switch (type)
        {
            case MeldType.Chi:
            case MeldType.Pon:
            case MeldType.Minkan:
                reason = "报听后禁止吃碰明杠";
                return false;
            case MeldType.Kakan:
                reason = "报听后禁止加杠";
                return false;
            default:
                // 暗杠要看听牌形（见 CallAllowsAnkan），这里只放行不判定。
                return true;
        }
    }

    /// <summary>
    /// 锁手校验（暗杠）：只有「听牌形不变」的暗杠允许。暗杠发生在摸牌后的 14 张形，
    /// 所以判定按「去掉这四张 + 把这组算成暗杠副露」后的听牌张集合与宣言时比对。
    /// </summary>
    public bool CallAllowsAnkan(Player player, Tile tile, out string? reason)
    {
        reason = null;
        if (!player.IsCalled)
        {
            return true;
        }

        if (player.Hand.Count + player.MeldTiles() != 14 + player.KongCount())
        {
            reason = "暗杠要按摸牌后的 14 张形判定";
            return false;
        }

        var hand = new List<Tile>(player.Hand);
        int removed = 0;
        for (int i = 0; i < hand.Count && removed < 4;)
        {
            if (hand[i] == tile)
            {
                hand.RemoveAt(i);
                removed++;
            }
            else
            {
                i++;
            }
        }

        if (removed < 4)
        {
            reason = "手里没有四张同样的牌";
            return false;
        }

        var melds = new List<Meld>(player.Melds) { new(MeldType.Ankan, new[] { tile, tile, tile, tile }) };
        ulong mask = WaitMaskFor(hand, melds, player.AnkanCount + 1, player.MinkanCount, player.SeatWind, player.FirstTurn);
        if (mask != player.CalledWaitMask)
        {
            reason = "这组暗杠会改变听牌形";
            return false;
        }

        return true;
    }

    // ── 错和 ─────────────────────────────────────────────────────

    /// <summary>
    /// 错和判定 + 判罚：报听后主动宣布和牌，但该手赋 &lt; 起和线。
    /// <para>
    /// 普通（<c>Z = 赋 × 700 ≤ 2100</c>）：违规家向其他三家各付 Z，本巡禁和。
    /// 重罚（<c>Z &gt; 2100</c>，即赋 ≥ 4）：子家违规 → 庄家 14000、另两家各 7000；
    /// 庄家违规 → 三家各 7000；本局 + 下局禁和。两种都解除报听、本次和牌不成立。
    /// </para>
    /// 返回 null 表示不构成错和（未报听 / 赋已达起和线）。
    /// </summary>
    public CallPenalty? ApplyCallMisfire(int playerIndex, int fu)
    {
        if (playerIndex < 0 || playerIndex >= PlayerCount)
        {
            return null;
        }

        var player = _players[playerIndex];
        if (!player.IsCalled || fu <= 0 || fu >= BaseFu)
        {
            return null;
        }

        int z = fu * 700;
        bool severe = z > 2100;
        var paid = new int[PlayerCount];

        if (severe)
        {
            if (player.IsDealer)
            {
                // 庄家违规：文档未给数值，按子家档的一半（三家各 7000）处理。
                for (int i = 0; i < PlayerCount; i++)
                {
                    if (i != playerIndex)
                    {
                        paid[i] = 7000;
                    }
                }
            }
            else
            {
                for (int i = 0; i < PlayerCount; i++)
                {
                    if (i == playerIndex)
                    {
                        continue;
                    }

                    paid[i] = i == DealerIndex ? 14000 : 7000;
                }
            }

            _callBannedHand[playerIndex] = true;
            _callBannedNextHand[playerIndex] = true;
        }
        else
        {
            for (int i = 0; i < PlayerCount; i++)
            {
                if (i != playerIndex)
                {
                    paid[i] = z;
                }
            }

            _callBannedTurn[playerIndex] = true;
        }

        for (int i = 0; i < PlayerCount; i++)
        {
            if (paid[i] == 0)
            {
                continue;
            }

            _scores[playerIndex] -= paid[i];
            _scores[i] += paid[i];
        }

        player.IsCalled = false;
        player.CalledWaitMask = 0;

        return new CallPenalty(playerIndex, fu, z, severe, paid);
    }

    // ── 听牌张集合 ───────────────────────────────────────────────

    /// <summary>某手牌形（含副露）的听牌张位掩码：bit = <see cref="Tile.Index"/>。</summary>
    public ulong WaitMaskFor(
        IReadOnlyList<Tile> hand,
        IReadOnlyList<Meld> melds,
        int ankanCount,
        int minkanCount,
        SeatWind seatWind,
        bool firstTurn)
    {
        ulong mask = 0;
        foreach (var entry in ListTenpaiForHand(hand, melds, ankanCount, minkanCount, seatWind, firstTurn))
        {
            if (entry.Success)
            {
                mask |= 1UL << entry.Tile.Index;
            }
        }

        return mask;
    }

    /// <summary>按给定手牌 / 副露列听牌（不改任何状态）。</summary>
    public IReadOnlyList<TenpaiEntry> ListTenpaiForHand(
        IReadOnlyList<Tile> hand,
        IReadOnlyList<Meld> melds,
        int ankanCount,
        int minkanCount,
        SeatWind seatWind,
        bool firstTurn)
    {
        var options = new TenpaiOptions
        {
            SelfDraw = true,
            HasCalled = melds.Count > 0 || minkanCount > 0,
            AnkanCount = ankanCount,
            MinkanCount = minkanCount,
            FirstTurn = firstTurn,
            SeatWind = seatWind,
            RoundWind = RoundWind,
            BaseFu = _yakuData.BaseFu,
        };

        try
        {
            return TenpaiCalculator.ListTenpai(_checker, hand, ToCoreMelds(melds), options);
        }
        catch
        {
            return Array.Empty<TenpaiEntry>();
        }
    }

    /// <summary>该家当前的听牌张位掩码（13 张形才有意义）。</summary>
    public ulong WaitMaskOf(Player player) =>
        WaitMaskFor(player.Hand, player.Melds, player.AnkanCount, player.MinkanCount, player.SeatWind, player.FirstTurn);

    /// <summary>
    /// 待牌提示用的 13 张形手牌：14 张形（刚摸牌）时去掉本次摸到的牌（摸切口径）。
    /// 不是 14 / 13 张形时原样返回。
    /// </summary>
    public List<Tile> TenpaiHandOf(Player player)
    {
        var hand = new List<Tile>(player.Hand);
        if (hand.Count + player.MeldTiles() != 14 + player.KongCount() || hand.Count == 0)
        {
            return hand;
        }

        Tile drop = player.LastDraw ?? hand[^1];
        int index = hand.IndexOf(drop);
        if (index < 0)
        {
            index = hand.Count - 1;
        }

        hand.RemoveAt(index);
        return hand;
    }

    // ── 局内生命周期 ─────────────────────────────────────────────

    /// <summary>
    /// 存档恢复：写回报听 / 禁和标记（长度不是四家时忽略对应项）。
    /// 报听手牌本身在 <see cref="StartNewRound"/> 重置，这里只保证禁和标记能跨局续上。
    /// </summary>
    public void RestoreCallState(
        IReadOnlyList<bool>? isCalled,
        IReadOnlyList<bool>? bannedThisHand,
        IReadOnlyList<bool>? bannedNextHand)
    {
        for (int i = 0; i < PlayerCount; i++)
        {
            if (isCalled is { Count: PlayerCount } called)
            {
                _players[i].IsCalled = called[i];
            }

            if (bannedThisHand is { Count: PlayerCount } banned)
            {
                _callBannedHand[i] = banned[i];
            }

            if (bannedNextHand is { Count: PlayerCount } next)
            {
                _callBannedNextHand[i] = next[i];
            }
        }
    }
    /// <summary>开新一局时清流局听牌表、轮转禁和标记。由 <see cref="StartNewRound"/> 调用。</summary>
    private void ResetCallStateForNewRound()
    {
        _flowTenpai = Array.Empty<bool>();
        Array.Clear(_callBannedTurn);
        for (int i = 0; i < PlayerCount; i++)
        {
            _callBannedHand[i] = _callBannedNextHand[i];
            _callBannedNextHand[i] = false;
        }
    }

    /// <summary>新的一巡开始：轮到的这家解除「本巡禁和」。</summary>
    private void ClearTurnBan(int playerIndex)
    {
        if (playerIndex >= 0 && playerIndex < PlayerCount)
        {
            _callBannedTurn[playerIndex] = false;
        }
    }

    /// <summary>流局时记录四家听牌（已报听者自动算听牌）。由 <see cref="Flow"/> 调用。</summary>
    private bool[] CaptureFlowTenpai()
    {
        var tenpai = new bool[PlayerCount];
        for (int i = 0; i < PlayerCount; i++)
        {
            tenpai[i] = _players[i].IsCalled || IsTenpai(_players[i]);
        }

        _flowTenpai = tenpai;
        return tenpai;
    }
}

/// <summary>一次错和判罚的结果（供 Runtime 记日志 / 提示）。</summary>
public sealed record CallPenalty(int PlayerIndex, int Fu, int Z, bool Severe, IReadOnlyList<int> Paid);
