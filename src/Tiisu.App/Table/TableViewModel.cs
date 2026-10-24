using System;
using System.Collections.Generic;
using System.Linq;

namespace Tiisu.App.Table;

/// <summary>牌的极简快照（渲染只需要 id 与是否已知）。</summary>
public readonly record struct TileView(string Id, bool Hidden = false);

/// <summary>
/// 副露快照。暗杠（<see cref="IsAnkan"/>）有两条展示规则：
/// <para>1. 对局中只翻第 2、4 张（索引 1、3），另外两张盖牌背；</para>
/// <para>2. 和牌后（结算态）整组翻开，并盖 30% 黑遮罩表示「和牌后才公开」。</para>
/// 对手的暗杠同样按此规则显示，公开层面不泄露具体牌。
/// </summary>
public sealed class MeldView
{
    /// <summary>pon / chi / minkan / ankan / kakan（引擎 <c>MeldType.Wire()</c> 口径）。</summary>
    public string Type { get; init; } = "pon";

    public IReadOnlyList<TileView> Tiles { get; init; } = Array.Empty<TileView>();

    public bool IsAnkan => string.Equals(Type, "ankan", StringComparison.Ordinal);

    /// <summary>对局中翻开的牌位（0 基）。null 表示不设限（非暗杠，或已进入结算态）。</summary>
    public IReadOnlyList<int>? RevealedTiles { get; init; }

    /// <summary>该暗杠已按「和牌后」规则整组翻开（结算屏 / 和牌展示）。</summary>
    public bool RevealedAfterWin { get; init; }

    /// <summary>某张牌在指定状态下是否翻牌面。<paramref name="afterWin"/> 为结算态。</summary>
    public bool IsFaceUp(int index, bool afterWin)
    {
        if (!IsAnkan || afterWin || RevealedAfterWin)
        {
            return true;
        }

        if (RevealedTiles is { Count: > 0 })
        {
            return RevealedTiles.Contains(index);
        }

        // 默认规则：4 张里只翻第 2、4 张。
        return index is 1 or 3;
    }
}

public sealed class SeatView
{
    public int Index { get; init; }
    public string Name { get; init; } = "AI";
    public string Wind { get; init; } = "东";
    public int Score { get; init; }
    public bool IsDealer { get; init; }
    public bool IsHuman { get; init; }
    public bool IsTurn { get; init; }

    /// <summary>已报听（叫胡）。</summary>
    public bool IsCalled { get; init; }

    /// <summary>流局时未听牌（手牌加盖 30% 黑遮罩「灰显」）；非流局态恒为 false。</summary>
    public bool Noten { get; init; }

    public int HandCount { get; init; }
    public IReadOnlyList<TileView> Hand { get; init; } = Array.Empty<TileView>();   // 仅自己可见
    public IReadOnlyList<MeldView> Melds { get; init; } = Array.Empty<MeldView>();
    public IReadOnlyList<TileView> Discards { get; init; } = Array.Empty<TileView>();
    public TileView? LastDiscard { get; init; }
}

/// <summary>人类当前可用的动作（按 Web 侧按钮口径：杠 &gt; 和 &gt; 碰 &gt; 吃 &gt; 过）。</summary>
[Flags]
public enum HumanAction
{
    None = 0,

    /// <summary>轮到自己出牌（点手牌 / 摸打）。</summary>
    Discard = 1,

    /// <summary>自摸和牌。</summary>
    Tsumo = 2,

    /// <summary>荣和。</summary>
    Ron = 4,

    /// <summary>碰。</summary>
    Pon = 8,

    /// <summary>吃（可能有多组，见 <see cref="TableViewModel.ActionLabels"/>）。</summary>
    Chi = 16,

    /// <summary>杠：自己回合的暗杠 / 加杠，或副露窗口的明杠。</summary>
    Kan = 32,

    /// <summary>过：清掉待决的荣和 / 副露窗口。</summary>
    Pass = 64,

    /// <summary>推牌流局（九种九牌 / 十三不靠）。</summary>
    Flow = 128,

    /// <summary>报听（叫胡）：真听牌时才可用；报听后变「已报听」。</summary>
    DeclareCall = 256,
}

/// <summary>一屏牌桌所需的全部数据（渲染层不碰引擎）。</summary>
public sealed class TableViewModel
{
    public string RoundLabel { get; init; } = "东1局";
    public int WallCount { get; init; }
    public int RinshanCount { get; init; }
    public int Honba { get; init; }

    /// <summary>
    /// 场赏点数 = 700 × <see cref="Honba"/>（历史字段名 Kyotaku 沿用，语义已改为「场赏」）。
    /// 与连庄奖励同额，不重复加；和牌者取走。
    /// </summary>
    public int Kyotaku { get; init; }
    public int TurnIndex { get; init; }
    public IReadOnlyList<SeatView> Seats { get; init; } = Array.Empty<SeatView>();

    /// <summary>场次目标局数（1 / 4 / 8 / 16）。</summary>
    public int ModeTarget { get; init; } = 1;

    /// <summary>和牌 / 流局后四家手牌明牌。</summary>
    public bool RevealHands { get; init; }

    /// <summary>结算态（和牌 / 流局之后）：暗杠整组翻开并盖 30% 黑遮罩。</summary>
    public bool InSettlement => Phase is "round_end" or "match_over" || RevealHands;

    /// <summary>整场已结束。</summary>
    public bool MatchOver { get; init; }

    /// <summary>岭上（马）牌；<see cref="TileView.Hidden"/> 为 true 时 UI 不应显示牌面。</summary>
    public IReadOnlyList<TileView> Rinshan { get; init; } = Array.Empty<TileView>();

    /// <summary>当前可用的动作（人类回合之外为 <see cref="HumanAction.None"/>）。</summary>
    public HumanAction Actions { get; init; }

    /// <summary>自己手里有暗杠（K 键走 <c>KanAn</c>）。</summary>
    public bool HasAnkan { get; init; }

    /// <summary>自己有可加杠的碰（K 键走 <c>KanKakan</c>）。</summary>
    public bool HasKakan { get; init; }

    /// <summary>吃牌等需要多选的按钮文案。</summary>
    public IReadOnlyList<string> ActionLabels { get; init; } = Array.Empty<string>();

    /// <summary>听牌提示：当前可和的牌（牌面图集 id，如 1m / 5z）。真听牌时常显，不限阶段。</summary>
    public IReadOnlyList<string> TenpaiTiles { get; init; } = Array.Empty<string>();

    /// <summary>自己已报听（叫胡）：按钮显示「已报听」。</summary>
    public bool IsCalled { get; init; }

    /// <summary>
    /// 报听后和牌会算错和：听牌形成立、但所有待牌的最高赋都低于起和线。
    /// UI 用它在报听按钮附近显示【错和可能】提示。
    /// </summary>
    public bool CallWouldMisfire { get; init; }

    /// <summary>自己本局 / 本巡禁和（错和罚）。</summary>
    public bool WinBanned { get; init; }

    /// <summary>最近一条提示（和牌 / 流局 / 窗口提示）。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>调试：四家 AI 档位（Simple / Normal / Hard / Shura，按座位 0..3）。</summary>
    public IReadOnlyList<string> AiLevels { get; init; } = Array.Empty<string>();

    /// <summary>运行阶段：idle / human_discard / human_ron / human_meld / round_end / match_over。</summary>
    public string Phase { get; init; } = "idle";
}
