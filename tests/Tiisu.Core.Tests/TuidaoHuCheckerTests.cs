using System;
using System.Collections.Generic;
using Tiisu.Core;

namespace Tiisu.Core.Tests;

/// <summary>
/// 推倒胡判定（1102工程）：5 种一般型 + 七对 + 十三幺，含反例。
/// </summary>
public sealed class TuidaoHuCheckerTests
{
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
                _ => throw new ArgumentException($"bad tile spec: {part}", nameof(spec)),
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

    [Fact]
    public void 一般型_四顺一对()
    {
        var shape = TuidaoHuChecker.Classify(H("123m 456m 789p 234s 55z"));
        Assert.Equal(TuidaoHuShape.FourSequencesPair, shape);
    }

    [Fact]
    public void 一般型_一刻三顺一对()
    {
        var shape = TuidaoHuChecker.Classify(H("111m 456m 789p 234s 55z"));
        Assert.Equal(TuidaoHuShape.OneTripletThreeSequencesPair, shape);
    }

    [Fact]
    public void 一般型_二刻二顺一对()
    {
        var shape = TuidaoHuChecker.Classify(H("111m 222p 789p 234s 55z"));
        Assert.Equal(TuidaoHuShape.TwoTripletsTwoSequencesPair, shape);
    }

    [Fact]
    public void 一般型_一顺三刻一对()
    {
        var shape = TuidaoHuChecker.Classify(H("111m 222p 333s 234s 55z"));
        Assert.Equal(TuidaoHuShape.OneSequenceThreeTripletsPair, shape);
    }

    [Fact]
    public void 一般型_四刻一对()
    {
        var shape = TuidaoHuChecker.Classify(H("111m 222p 333s 444s 55z"));
        Assert.Equal(TuidaoHuShape.FourTripletsPair, shape);
    }

    [Fact]
    public void 一般型_有副露也算()
    {
        // 碰 111z + 暗手 456m 789p 234s 55z → 一刻 + 三顺 + 一对
        var shape = TuidaoHuChecker.Classify(H("456m 789p 234s 55z"), new[] { M("pon:111z") });
        Assert.Equal(TuidaoHuShape.OneTripletThreeSequencesPair, shape);
    }

    [Fact]
    public void 七对_正例()
    {
        Assert.Equal(TuidaoHuShape.SevenPairs, TuidaoHuChecker.Classify(H("1122m 3344p 5566s 77z")));
    }

    [Fact]
    public void 七对_四张同牌不能当两对()
    {
        // 1111m 22p 33p 44p 55p 66p 77p = 14 张，但 1m 四张只能算一个「四张」，不是两对
        var shape = TuidaoHuChecker.Classify(H("1111m 22p 33p 44p 55p 66p 77p"));
        Assert.NotEqual(TuidaoHuShape.SevenPairs, shape);
    }

    [Fact]
    public void 七对_不能有副露()
    {
        // 暗手 12 张 + 一组碰，总张数够但七对必须门前
        var shape = TuidaoHuChecker.Classify(H("1122m 3344p 5566s"), new[] { M("pon:77z") });
        Assert.NotEqual(TuidaoHuShape.SevenPairs, shape);
    }

    [Fact]
    public void 十三幺_正例()
    {
        // 13 种幺九字各 1 张 + 1m 重复一次
        Assert.Equal(TuidaoHuShape.ThirteenOrphans,
            TuidaoHuChecker.Classify(H("119m 19p 19s 1234567z")));
    }

    [Fact]
    public void 十三幺_混入中张就不算()
    {
        var shape = TuidaoHuChecker.Classify(H("19m 19p 19s 1234567z 5m"));
        Assert.NotEqual(TuidaoHuShape.ThirteenOrphans, shape);
        Assert.Equal(TuidaoHuShape.None, shape);
    }

    [Fact]
    public void 十三幺_不能有副露()
    {
        var shape = TuidaoHuChecker.Classify(H("19p 19s 1234567z"), new[] { M("pon:111m") });
        Assert.NotEqual(TuidaoHuShape.ThirteenOrphans, shape);
    }

    [Fact]
    public void 非和牌_张数不对()
    {
        var shape = TuidaoHuChecker.Classify(H("123m 456m 789p 234s 5z"));
        Assert.Equal(TuidaoHuShape.None, shape);
    }

    [Fact]
    public void 非和牌_四顺两对()
    {
        var shape = TuidaoHuChecker.Classify(H("123m 456m 789p 234s 55z 66z"));
        Assert.Equal(TuidaoHuShape.None, shape);
    }
}
