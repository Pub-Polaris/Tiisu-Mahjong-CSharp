using Tiisu.Core;

namespace Tiisu.Engine;

/// <summary>结算部分：和牌支付、多家荣和、流局罚符、连庄 / 进庄、终局。engine.js 的分数相关段落。</summary>
public sealed partial class MahjongEngine
{
    /// <summary>
    /// 和牌结算（engine.js <c>declareWin</c>）。
    /// <paramref name="result"/> 的 <see cref="WinSettlement.TotalFu"/> 是不含赐马的赋数；
    /// 回放时赐马已由语料结算（<see cref="WinSettlement.ResolvedMaFu"/>），活局则由 <c>resolveMa</c> 现算。
    /// </summary>
    public void DeclareWin(int winIndex, bool selfDraw, WinSettlement result, int? discarderIndex = null)
    {
        State = EngineState.Ended;
        GameOver = true;
        TurnIndex = winIndex;
        var winner = _players[winIndex];

        // 大七星（tiisuin）：做成七种字牌七对子 → 无条件直接结束整场（不计分）
        if (result.SevenHonors)
        {
            DeclareTiisuin(winIndex, discarderIndex);
            return;
        }

        // 四子七对是独立形态：不另算赐马。
        bool fourPairsSeven = result.ActiveYaku.Any(y => y.Name == "四子七对");
        int maFu = result.ResolvedMaFu;
        if (!fourPairsSeven && Ruleset != Ruleset.TuidaoHu && maFu == 0)
        {
            maFu = ResolveMa(winner, result);
            if (maFu > 0)
            {
                result.ActiveYaku.Add(new WinYaku { Name = "赐马", Fu = maFu, IsMa = true });
            }
        }

        int fu = result.TotalFu + maFu;
        // 推倒胡：只看胜负，不结算点数（无赋、无庄家 ×1.5、无连庄加分）。
        int subPoints = Ruleset == Ruleset.TuidaoHu ? 0 : CalcFinalPoints(result, fu);
        int finalPoints = Ruleset == Ruleset.TuidaoHu
            ? 0
            : (winner.IsDealer ? DealerBonus(subPoints) : subPoints) + HonbaBonus();

        // 场供池：和牌者拿走之前累积的底池。
        int pot = PotPoints;
        PotPoints = 0;
        if (pot > 0)
        {
            _scores[winIndex] += pot;
        }

        if (selfDraw)
        {
            // 三家均摊按 350 点蓝筹取整：除不尽的尾数（0/350/700）进底池，不记给任何人。
            var (each, remainder) = SplitPayment(finalPoints);
            for (int pi = 0; pi < PlayerCount; pi++)
            {
                if (pi == winIndex)
                {
                    continue;
                }

                _scores[winIndex] += each;
                _scores[pi] -= each;
            }

            PotPoints += remainder;
        }
        else
        {
            int payer = discarderIndex ?? TurnIndex;
            _scores[winIndex] += finalPoints;
            _scores[payer] -= finalPoints;
        }

        if (winIndex == DealerIndex)
        {
            Honba++;
        }
        else
        {
            AdvanceDealer();
        }

        RevealHands = true;

        if (CheckDeungnyeong())
        {
            return;
        }

        ScheduleNextRound(2500);
    }

    /// <summary>大七星（tiisuin）：立即结束整场。语料里没有，但状态影响照 JS 复刻（不含网络上报）。</summary>
    public void DeclareTiisuin(int winIndex, int? discarderIndex)
    {
        RevealHands = true;
        JuFinished = true;
        GameOver = true;
        State = EngineState.Ended;
        MatchOver = true;
    }

    /// <summary>
    /// 多家荣和结算（engine.js <c>declareMultiRon</c>）：放铳者对每家各付全额；
    /// 三家和牌 → 自动流局；双和子跳庄 → 子家手牌 ≥ 庄家时庄家放弃并付 3500。
    /// </summary>
    public void DeclareMultiRon(IReadOnlyList<RonWinner> winners, int discarderIndex)
    {
        if (winners.Count == 0)
        {
            return;
        }

        if (winners.Count >= 3)
        {
            DeclareSpecialFlow("三家和牌 → 自动流局");
            return;
        }

        State = EngineState.Ended;
        GameOver = true;
        int payer = discarderIndex;

        int pot = PotPoints;
        PotPoints = 0;

        // 双和子跳庄（10.3）：子家与庄家同时荣和时，若子家手牌较大 → 庄家放弃和牌，并向该子家付 3500 点
        var effective = winners.ToList();
        if (winners.Count == 2)
        {
            int dealerSlot = -1;
            for (int i = 0; i < 2; i++)
            {
                if (winners[i].PlayerIndex == DealerIndex)
                {
                    dealerSlot = i;
                }
            }

            if (dealerSlot != -1)
            {
                int childSlot = 1 - dealerSlot;
                var dealerWinner = winners[dealerSlot];
                var childWinner = winners[childSlot];
                // 推倒胡：不结算 3500 点。
                if (Ruleset != Ruleset.TuidaoHu
                    && childWinner.Result.TotalFu >= dealerWinner.Result.TotalFu)
                {
                    _scores[DealerIndex] -= 3500;
                    _scores[childWinner.PlayerIndex] += 3500;
                    _roundTransfers.Add(new ScoreTransfer(DealerIndex, childWinner.PlayerIndex, 3500, "双和子跳庄"));
                    effective = new List<RonWinner> { childWinner };
                }
            }
        }

        // 每个胜者各得全额（放铳者一家付所有胜者）；赐马只按胜者自己的手牌计
        foreach (var winner in effective)
        {
            var player = _players[winner.PlayerIndex];
            int maFu = winner.Result.ResolvedMaFu;
            if (Ruleset != Ruleset.TuidaoHu && maFu == 0)
            {
                maFu = ResolveMa(player, winner.Result);
                if (maFu > 0)
                {
                    winner.Result.ActiveYaku.Add(new WinYaku { Name = "赐马", Fu = maFu, IsMa = true });
                }
            }

            winner.TotalFu = winner.Result.TotalFu + maFu;
            int sub = Ruleset == Ruleset.TuidaoHu ? 0 : CalcFinalPoints(winner.Result, winner.TotalFu);
            int finalPoints = Ruleset == Ruleset.TuidaoHu
                ? 0
                : (player.IsDealer ? DealerBonus(sub) : sub) + HonbaBonus();
            winner.FinalPoints = finalPoints;
            _scores[winner.PlayerIndex] += finalPoints;
            _scores[payer] -= finalPoints;
        }

        if (pot > 0 && effective.Count > 0)
        {
            _scores[effective[0].PlayerIndex] += pot;
        }

        _lastRonMulti = effective.ToList();

        // 连庄判定：胜者含庄家 → 连庄；否则庄家顺移一位
        if (effective.Any(w => w.PlayerIndex == DealerIndex))
        {
            Honba++;
        }
        else
        {
            AdvanceDealer();
        }

        RevealHands = true;

        if (CheckDeungnyeong())
        {
            return;
        }

        ScheduleNextRound(2500);
    }

    /// <summary>出岭（击飞，8.2）：分数跌破 0 → 记 −7000 点并立即终局。返回是否触发。</summary>
    public bool CheckDeungnyeong()
    {
        for (int i = 0; i < PlayerCount; i++)
        {
            if (_scores[i] < 0)
            {
                _scores[i] = -7000;
                MatchOver = true;
                State = EngineState.Ended;
                GameOver = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>赋位表（engine.js <c>calcPoints</c>）。</summary>
    public int CalcPoints(int fu)
    {
        if (fu >= 28)
        {
            return 35000;
        }

        var thresholds = _points.Thresholds;
        var baselines = _points.Baselines;
        int baseline = baselines[0];
        int threshold = 0;
        for (int i = thresholds.Count - 1; i >= 0; i--)
        {
            if (fu >= thresholds[i])
            {
                baseline = baselines[i + 1];
                threshold = thresholds[i];
                break;
            }
        }

        return baseline + ((fu - threshold) * _points.PerFu);
    }

    /// <summary>
    /// 子家点数（engine.js <c>calcFinalPoints</c>）：
    /// 固定点数役扁平返回；否则「表内赋查表 + 表外赋（听牌型奖赏 / 赐马）×700 + 点数加项」。
    /// </summary>
    public int CalcFinalPoints(WinSettlement result, int fu)
    {
        var yaku = result.ActiveYaku;

        var fixedPoints = yaku.FirstOrDefault(y => y.FixedPoints is not null);
        if (fixedPoints is not null)
        {
            return fixedPoints.FixedPoints!.Value;
        }

        int tableless = yaku.Where(y => y.IsBonus || y.IsMa).Sum(y => y.Fu);
        int basePoints;
        if (result.SpecialPoints is not null)
        {
            basePoints = result.SpecialPoints.Value + (tableless * 700);
        }
        else
        {
            basePoints = CalcPoints(fu - tableless) + (tableless * 700);
        }

        int pointsBonus = yaku.Sum(y => y.PointsBonus ?? 0);
        return basePoints + pointsBonus;
    }

    /// <summary>连庄奖励（8.1「700 点 / 根」）：本场数每 +1 加 700 点，不论庄家，扁平相加。</summary>
    public int HonbaBonus() => 700 * Honba;

    /// <summary>庄家 ×1.5。JS 用浮点乘法；赋位表的点数都是 700 的倍数，故结果仍是整数。</summary>
    public static int DealerBonus(int points) => (int)(points * 1.5);

    /// <summary>
    /// 三家均摊按 350 点蓝筹取整：每家付 350 的整数倍，除不尽的尾数（0/350/700）作为场供进池。
    /// 例：7700 → 每家 2450（7 蓝筹），余 350 进场供。
    /// </summary>
    public (int Each, int Remainder) SplitPayment(int total)
    {
        int chip = 350;
        int each = (total / (3 * chip)) * chip;
        int remainder = total - (each * 3);
        return (each, remainder);
    }

    /// <summary>赐马指示牌：数牌 1→…→9→1；风 东→南→西→北→东；三元 白→发→中→白。</summary>
    public static Tile MaNextId(Tile tile)
    {
        if (tile.Suit == Suit.Honor)
        {
            if (tile.Num <= 4)
            {
                return new Tile(Suit.Honor, tile.Num == 4 ? 1 : tile.Num + 1);
            }

            return new Tile(Suit.Honor, tile.Num == 7 ? 5 : tile.Num + 1);
        }

        return new Tile(tile.Suit, tile.Num == 9 ? 1 : tile.Num + 1);
    }

    /// <summary>
    /// 赐马计赋（engine.js <c>resolveMa</c>）：每张翻开的岭上指示牌，若它指向的牌在胜者手牌 + 副露里，
    /// 该指示牌计 1 赋（手牌张数不影响）。倍满 / 役满 / 大七星不计。
    /// 回放的岭上只有长度（元素为 null），因此这里恒返回 0 —— 语料的赐马已在 <c>y</c> 里给出。
    /// </summary>
    public int ResolveMa(Player winner, WinSettlement result)
    {
        if (result.HandType == HandKind.HighYaku || result.SevenHonors)
        {
            return 0;
        }

        if (_rinshan.Count == 0)
        {
            return 0;
        }

        var own = new int[TileUtil.Count];
        foreach (var tile in winner.Hand)
        {
            own[tile.Index]++;
        }

        foreach (var meld in winner.Melds)
        {
            foreach (var tile in meld.Tiles)
            {
                own[tile.Index]++;
            }
        }

        int fu = 0;
        foreach (var indicator in _rinshan)
        {
            if (indicator is null)
            {
                continue;
            }

            var next = MaNextId(indicator.Value);
            if (own[next.Index] > 0)
            {
                fu++;
            }
        }

        return fu;
    }

    /// <summary>
    /// 荒牌流局（engine.js <c>flow</c>）。规则书 4.2 / 4.3 / 4.4：
    /// 听牌流局本场 +1；未听返杠 700/杠；鸣牌后无役听牌罚 1400/2100；七日终战强制过庄。
    /// </summary>
    /// <summary>最近一次荒牌流局实际执行的罚项（人读文案，供结算屏展示；无罚则空）。</summary>
    /// <summary>本局行牌中发生的结构化分数变动（拔厄 / 报听错和 / 双和跳庄）；开局清空。</summary>
    public IReadOnlyList<ScoreTransfer> RoundTransfers => _roundTransfers;

    private readonly List<ScoreTransfer> _roundTransfers = new();

    /// <summary>场供池（底池）：自摸均摊除不尽的 350 点尾数进池，下一手和牌者拿走。</summary>
    public int PotPoints { get; private set; }

    public IReadOnlyList<string> LastFlowPenalties { get; private set; } = Array.Empty<string>();

    /// <summary>最近一次荒牌流局的结构化分数转移（结算拆账 / 审计用；无罚则空）。</summary>
    public IReadOnlyList<ScoreTransfer> LastFlowTransfers { get; private set; } = Array.Empty<ScoreTransfer>();

    /// <summary>罚项账本与实际分数变化不一致时的差异描述；正常为空。</summary>
    public IReadOnlyList<string> LastFlowPenaltyAudit { get; private set; } = Array.Empty<string>();

    public void Flow()
    {
        State = EngineState.Ended;
        GameOver = true;
        RevealHands = true;

        var scoresBefore = _scores.ToArray();

        // 报听：已报听者自动算听牌（不用再申报）；其余按实际听牌判定。
        var tenpai = CaptureFlowTenpai();

        int tenpaiCount = tenpai.Count(x => x);

        // 未听返杠（4.4）+ 鸣牌后无役听牌（4.4）；推倒胡不罚。
        var penaltyLog = new List<string>();
        var transfers = new List<ScoreTransfer>();
        if (Ruleset != Ruleset.TuidaoHu)
        {
            ApplyNotenKanPenalty(tenpai, penaltyLog, transfers);
            ApplyNoYakuTenpaiPenalty(tenpai, penaltyLog, transfers);
        }

        // 连庄判定：庄家听牌 → 连庄（本场 +1）；否则过庄（本场归零）
        if (tenpai[DealerIndex])
        {
            Honba++;
        }
        else
        {
            AdvanceDealer();
        }

        // 听牌流局：三家听牌则本场 +1（无论庄家是否听牌；已 +1 则不重复）
        if (tenpaiCount == 3 && Honba == 0)
        {
            Honba++;
        }

        string sevenLog = ApplySevenHandRule(transfers);
        if (!string.IsNullOrEmpty(sevenLog))
        {
            penaltyLog.Add(sevenLog);
        }

        LastFlowPenalties = penaltyLog.ToArray();
        LastFlowTransfers = transfers.ToArray();
        LastFlowPenaltyAudit = AuditFlowTransfers(scoresBefore, transfers);

        ScheduleNextRound(2000);
    }

    /// <summary>把「账本」和实际分数变化逐家对账；差异写进审计列表（正常为空）。</summary>
    private IReadOnlyList<string> AuditFlowTransfers(int[] scoresBefore, List<ScoreTransfer> transfers)
    {
        var expected = new int[PlayerCount];
        foreach (var transfer in transfers)
        {
            expected[transfer.From] -= transfer.Amount;
            expected[transfer.To] += transfer.Amount;
        }

        var problems = new List<string>();
        for (int i = 0; i < PlayerCount; i++)
        {
            int actual = _scores[i] - scoresBefore[i];
            if (actual != expected[i])
            {
                problems.Add(
                    _players[i].Name + ": 实际Δ" + actual.ToString("+#;-#;0", System.Globalization.CultureInfo.InvariantCulture)
                    + "，账本Δ" + expected[i].ToString("+#;-#;0", System.Globalization.CultureInfo.InvariantCulture)
                    + "，差" + (actual - expected[i]).ToString("+#;-#;0", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return problems;
    }

    /// <summary>未听返杠（4.4）：杠牌最多且流局未听者，按 700 点/杠向各家返还；子家额外向庄家付 700。</summary>
    private void ApplyNotenKanPenalty(bool[] tenpai, List<string> log, List<ScoreTransfer> transfers)
    {
        static int KanCount(Player player) => player.AnkanCount + player.MinkanCount;

        int maxKan = 0;
        foreach (var player in _players)
        {
            maxKan = Math.Max(maxKan, KanCount(player));
        }

        if (maxKan <= 0)
        {
            return;
        }

        for (int pi = 0; pi < PlayerCount; pi++)
        {
            var player = _players[pi];
            if (tenpai[pi] || KanCount(player) != maxKan)
            {
                continue;
            }

            int per = 700 * maxKan;
            for (int k = 0; k < PlayerCount; k++)
            {
                if (k == pi)
                {
                    continue;
                }

                _scores[pi] -= per;
                _scores[k] += per;
                transfers.Add(new ScoreTransfer(pi, k, per, "未听返杠"));
            }

            if (pi != DealerIndex)
            {
                _scores[pi] -= 700;
                _scores[DealerIndex] += 700;
                transfers.Add(new ScoreTransfer(pi, DealerIndex, 700, "未听返杠·庄家加成"));
            }

            log.Add(player.Name + " 未听返杠 −" + per);
        }
    }

    /// <summary>
    /// 鸣牌后无役听牌（4.4）：子家罚 1400（350×其他子家 + 庄家 700）；庄家罚 2100（三子家各 700）。
    /// <para>
    /// 「无役」= 所有和牌张都数不出<b>一个役</b>（表外赋不算）。判定口径（261024 北极星）：
    /// 对每张听牌张用 <see cref="TenpaiCalculator.CheckCandidate"/> 判一次，
    /// <b>有役</b> = 役列表里存在 <c>Fu &gt; 0 &amp;&amp; !IsBonus</c> 的项——哪怕只有 1 赋的役牌也算，
    /// 「断幺九 2 赋」也算有役；听牌型奖赏（嵌张 / 边张 / 单钓 / 双碰 / 筋和）与赐马是表外赋，不算。
    /// 判役走荣和口径（<c>selfDraw: false</c>），因为自摸 2 赋 / 卡七自摸加成只是加赋、不是役，
    /// 否则任何手都「有役」、罚项永不成立。
    /// </para>
    /// <para>只在「有副露 + 有听牌形 + 全部和牌张都无役」时才罚；门清不罚。</para>
    /// </summary>
    private void ApplyNoYakuTenpaiPenalty(bool[] tenpai, List<string> log, List<ScoreTransfer> transfers)
    {
        for (int pi = 0; pi < PlayerCount; pi++)
        {
            var player = _players[pi];
            bool hasMeld = player.Melds.Count > 0 || player.MinkanCount > 0;
            if (!hasMeld)
            {
                continue;
            }

            var melds = ToCoreMelds(player.Melds);
            var options = BuildTenpaiOptions(player);

            List<TenpaiEntry> entries;
            try
            {
                entries = TenpaiCalculator.ListTenpai(_checker, player.Hand, melds, options);
            }
            catch
            {
                continue;
            }

            // 听牌形：至少一张牌能和出形（成和，或成和但赋数差起和线）。
            var waits = entries.Where(x => x.Success || x.NoYakuGap > 0).ToList();
            if (waits.Count == 0)
            {
                continue;
            }

            // 有役：任一张和牌张能数出役 → 不罚。
            bool hasYaku = waits.Any(entry => TileHasYaku(player.Hand, melds, options, entry.Tile));
            if (hasYaku)
            {
                continue;
            }

            if (pi == DealerIndex)
            {
                for (int k = 0; k < PlayerCount; k++)
                {
                    if (k == pi)
                    {
                        continue;
                    }

                    _scores[pi] -= 700;
                    _scores[k] += 700;
                    transfers.Add(new ScoreTransfer(pi, k, 700, "鸣牌后无役听牌（庄）"));
                }

                log.Add(player.Name + "（庄）鸣牌后无役听牌 −2100");
            }
            else
            {
                _scores[pi] -= 1400;
                _scores[DealerIndex] += 700;
                transfers.Add(new ScoreTransfer(pi, DealerIndex, 700, "鸣牌后无役听牌"));
                for (int k = 0; k < PlayerCount; k++)
                {
                    if (k == pi || k == DealerIndex)
                    {
                        continue;
                    }

                    _scores[k] += 350;
                    transfers.Add(new ScoreTransfer(pi, k, 350, "鸣牌后无役听牌"));
                }

                log.Add(player.Name + " 鸣牌后无役听牌 −1400");
            }
        }
    }

    /// <summary>
    /// 该和牌张是否数得出至少一个役：役列表里存在 <c>Fu &gt; 0 &amp;&amp; !IsBonus</c> 的项。
    /// 走荣和口径判役（自摸加赋不算役，见 <see cref="ApplyNoYakuTenpaiPenalty"/>）。
    /// </summary>
    private bool TileHasYaku(
        IReadOnlyList<Tile> hand,
        IReadOnlyList<Tiisu.Core.Meld> melds,
        TenpaiOptions options,
        Tile tile)
    {
        var win = TenpaiCalculator.CheckCandidate(_checker, hand, melds, options, tile, selfDraw: false);
        return win is not null && win.ActiveYaku.Any(y => y.Fu > 0 && !y.IsBonus);
    }

    /// <summary>七日终战（4.3）：半庄 / 东风场本场数为 7 流局 → 强制过庄进入 ALL LAST。</summary>
    private string ApplySevenHandRule(List<ScoreTransfer> transfers)
    {
        if (WeekEndHandled)
        {
            return string.Empty;
        }

        if (ModeTarget is not (4 or 8))
        {
            return string.Empty;
        }

        if (Honba < 7)
        {
            return string.Empty;
        }

        WeekEndHandled = true;
        AdvanceDealer();
        FinalWindIdx = RoundWindIdx;

        int maxScore = _scores.Max();
        if (_scores[DealerIndex] == maxScore)
        {
            int lowIndex = 0;
            for (int i = 1; i < PlayerCount; i++)
            {
                if (_scores[i] < _scores[lowIndex])
                {
                    lowIndex = i;
                }
            }

            // 推倒胡：纯胜负，不结算七日终战的 2100 点。
            if (Ruleset != Ruleset.TuidaoHu && lowIndex != DealerIndex)
            {
                _scores[DealerIndex] -= 2100;
                _scores[lowIndex] += 2100;
                transfers.Add(new ScoreTransfer(DealerIndex, lowIndex, 2100, "七日终战"));
                return "七日终战：庄家为 1 位 → 向最低点 " + _players[lowIndex].Name + " 付 2100（强制过庄进入 ALL LAST）";
            }
        }

        return "七日终战 → 强制过庄进入 ALL LAST";
    }

    /// <summary>
    /// 一局结束：推进局数 / 检查终局 / 结算段位点（engine.js <c>advanceRound</c>，去掉网络上报）。
    /// 返回 false 表示整场已结束（不再开新局）。
    /// </summary>
    public bool AdvanceRound()
    {
        if (MatchOver)
        {
            return false;
        }

        HandsPlayed++;
        var rank = RankedScores();
        int top = rank[0].Score;
        bool over = false;
        if (ModeTarget == 1)
        {
            over = true; // 一局制：一手即终
        }
        else if (InExtension)
        {
            over = true; // 延长的那一手已打完 → 立即终局
        }
        else if (RoundWindIdx > FinalWindIdx)
        {
            // 最后一场打完：1 位达标 → 终局；未达标 → 只补一手（南入/西入/北增）
            if (top >= InitialPoints)
            {
                over = true;
            }
            else
            {
                InExtension = true;
            }
        }

        if (HandsPlayed >= ModeTarget + 16)
        {
            over = true; // 防跑飞兜底
        }

        if (over)
        {
            State = EngineState.Ended;
            GameOver = true;
            MatchOver = true;
            SettleRankPoints();
            return false;
        }

        return true;
    }

    /// <summary>
    /// 段位点（8.3）：每 700 点 = 0.1 分（余数 ≥350 进位 700）；次位赏：1 位 +14、2 位 +7、3 位 0、4 位 −7。
    /// </summary>
    public void SettleRankPoints()
    {
        var rank = RankedScores();
        int[] bonus = { 14, 7, 0, -7 };
        for (int order = 0; order < PlayerCount; order++)
        {
            int i = rank[order].Index;
            int diff = _scores[i] - InitialPoints;
            int units = diff / 700; // C# 整数除法向零截断，与 JS Math.trunc 一致
            int remainder = diff - (units * 700);
            if (remainder >= 350)
            {
                units++;
            }

            if (remainder <= -350)
            {
                units--;
            }

            // JS Math.round(x) = floor(x + 0.5)
            _rankPoints[i] = Math.Floor(((bonus[order] + (units * 0.1)) * 10) + 0.5) / 10;
        }
    }

    private List<(int Score, int Index)> RankedScores() =>
        _scores.Select((score, index) => (Score: score, Index: index))
            .OrderByDescending(x => x.Score)
            .ToList();
}
