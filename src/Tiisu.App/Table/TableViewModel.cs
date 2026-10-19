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
}
