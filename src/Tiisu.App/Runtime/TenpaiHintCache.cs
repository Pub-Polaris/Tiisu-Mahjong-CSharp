using System;
using System.Collections.Generic;
using System.Linq;
using Tiisu.App.Table;
using Tiisu.Core;
using Tiisu.Engine;

namespace Tiisu.App.Runtime;

/// <summary>听牌提示的两块数据：牌 id 列表 + 顶部摘要（余张/打点）。</summary>
public sealed record TenpaiHints(IReadOnlyList<string> Tiles, string Summary)
{
    public static readonly TenpaiHints Empty = new(Array.Empty<string>(), string.Empty);
}

/// <summary>
/// 听牌提示缓存（安全路径，替代 unsafe 快照方案）：
/// 只有手牌 / 副露 / 报听 / 风 / 第一巡 / 可选役配置完全一致时才复用上一份
/// <see cref="SnapshotBuilder.TenpaiTileIds"/> 结果，避免同一状态重复跑 34 次判和。
/// </summary>
public sealed class TenpaiHintCache
{
    private readonly object _gate = new();
    private MahjongEngine? _engine;
    private Key _key;
    private TenpaiHints _value = TenpaiHints.Empty;

    public TenpaiHints Get(MahjongEngine engine, Player player, Settings settings)
    {
        var key = Key.From(engine, player, settings);
        lock (_gate)
        {
            if (ReferenceEquals(_engine, engine) && _key.Equals(key))
            {
                return _value;
            }

            var value = SnapshotBuilder.BuildTenpaiHints(engine, player);
            _engine = engine;
            _key = key;
            _value = value;
            return value;
        }
    }

    private readonly struct Key : IEquatable<Key>
    {
        private readonly int[] _tiles;
        private readonly int[] _melds;
        private readonly bool _isCalled;
        private readonly bool _firstTurn;
        private readonly int _seatWind;
        private readonly int _roundWind;
        private readonly int _ankan;
        private readonly int _minkan;
        private readonly int _ruleset;
        private readonly int[] _optional;

        private Key(int[] tiles, int[] melds, bool isCalled, bool firstTurn, int seatWind, int roundWind,
            int ankan, int minkan, int ruleset, int[] optional)
        {
            _tiles = tiles;
            _melds = melds;
            _isCalled = isCalled;
            _firstTurn = firstTurn;
            _seatWind = seatWind;
            _roundWind = roundWind;
            _ankan = ankan;
            _minkan = minkan;
            _ruleset = ruleset;
            _optional = optional;
        }

        public static Key From(MahjongEngine engine, Player player, Settings settings)
        {
            var tiles = new int[player.Hand.Count];
            for (int i = 0; i < player.Hand.Count; i++)
            {
                tiles[i] = player.Hand[i].Index;
            }

            Array.Sort(tiles);
            var melds = new int[player.Melds.Count * 4];
            int slot = 0;
            foreach (var meld in player.Melds)
            {
                melds[slot++] = (int)meld.Type;
                melds[slot++] = meld.Tiles.Count;
                melds[slot++] = meld.Tiles.Count > 0 ? meld.Tiles[0].Index : -1;
                melds[slot++] = meld.Tiles.Count > 1 ? meld.Tiles[^1].Index : -1;
            }

            var optional = settings.OptionalYaku
                .OrderBy(id => id, StringComparer.Ordinal)
                .Select(id => id.GetHashCode(StringComparison.Ordinal))
                .ToArray();
            return new Key(tiles, melds, player.IsCalled, player.FirstTurn, (int)player.SeatWind,
                (int)engine.RoundWind, player.AnkanCount, player.MinkanCount, (int)settings.Ruleset, optional);
        }

        public bool Equals(Key other) =>
            _isCalled == other._isCalled
            && _firstTurn == other._firstTurn
            && _seatWind == other._seatWind
            && _roundWind == other._roundWind
            && _ankan == other._ankan
            && _minkan == other._minkan
            && _ruleset == other._ruleset
            && _tiles.AsSpan().SequenceEqual(other._tiles)
            && _melds.AsSpan().SequenceEqual(other._melds)
            && _optional.AsSpan().SequenceEqual(other._optional);

        public override bool Equals(object? obj) => obj is Key other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(_isCalled);
            hash.Add(_seatWind);
            hash.Add(_roundWind);
            hash.Add(_ruleset);
            foreach (int t in _tiles)
            {
                hash.Add(t);
            }

            foreach (int m in _melds)
            {
                hash.Add(m);
            }

            return hash.ToHashCode();
        }
    }
}
