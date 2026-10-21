using System;
using System.Collections.Generic;

namespace Tiisu.App.Table;

/// <summary>牌的极简快照（渲染只需要 id 与是否已知）。</summary>
public readonly record struct TileView(string Id, bool Hidden = false);

public sealed class MeldView
{
    public string Type { get; init; } = "pon";   // pon / chi / kan_minkan / kan_ankan / kan_kakan
    public IReadOnlyList<TileView> Tiles { get; init; } = Array.Empty<TileView>();
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
}

/// <summary>一屏牌桌所需的全部数据（渲染层不碰引擎）。</summary>
public sealed class TableViewModel
{
    public string RoundLabel { get; init; } = "东1局";
    public int WallCount { get; init; }
    public int RinshanCount { get; init; }
    public int Honba { get; init; }
    public int Kyotaku { get; init; }
    public int TurnIndex { get; init; }
    public IReadOnlyList<SeatView> Seats { get; init; } = Array.Empty<SeatView>();

    /// <summary>场次目标局数（1 / 4 / 8 / 16）。</summary>
    public int ModeTarget { get; init; } = 1;

    /// <summary>和牌 / 流局后四家手牌明牌。</summary>
    public bool RevealHands { get; init; }

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

    /// <summary>最近一条提示（和牌 / 流局 / 窗口提示）。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>运行阶段：idle / human_discard / human_ron / human_meld / round_end / match_over。</summary>
    public string Phase { get; init; } = "idle";
}
