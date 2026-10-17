namespace Tiisu.Core;

/// <summary>拆牌后的一组牌：雀头 / 顺子 / 刻子。JS 侧的 <c>type</c> 只有这三种参与判据。</summary>
public enum GroupKind
{
    Pair,
    Sequence,
    Pung,

    /// <summary>未知副露类型（JS 会把不认识的 type 原样保留）；绝大多数判据按「非顺子」处理。</summary>
    Other,
}

/// <summary>
/// 一组牌。<see cref="Tiles"/> 保持 JS 侧的顺序：副露按输入顺序，暗手拆出的面子按升序。
/// </summary>
public sealed record Group(GroupKind Kind, IReadOnlyList<Tile> Tiles)
{
    /// <summary>JS 判据大量使用 <c>tiles[0]</c>，这里保持同样语义。</summary>
    public Tile First => Tiles[0];
}

/// <summary>原始副露。type 保留原始字符串，供「全食顺 / 全碰刻」等需要区分吃与碰的判据使用。</summary>
public sealed record Meld(string Type, IReadOnlyList<Tile> Tiles)
{
    public int KongCount => Tiles.Count == 4 ? 1 : 0;

    public GroupKind ToGroupKind() => Type switch
    {
        "chi" or "sequence" => GroupKind.Sequence,
        "pon" or "pung" or "minkan" or "ankan" or "kakan" => GroupKind.Pung,
        "pair" => GroupKind.Pair,
        _ => GroupKind.Other,
    };

    public Group ToGroup() => new(ToGroupKind(), Tiles);
}
