using System;
using System.Collections.Generic;

namespace Tiisu.Engine.Ai;

/// <summary>
/// 五项玩家可调 AI 风格权重（A 期追役原型）。全部取值 0..2，1.0 = 中性。
///
/// <para>
/// 只在 7Neuu（<see cref="AiLevel.Neural"/>）且设置里开启 <c>aiPursuitEnabled</c> 时参与决策。
/// 其他档位、以及未开启时的 7Neuu 完全不受影响（普通弃牌仍是策略网 argmax）。
/// </para>
///
/// <para>语义（本轮原型里都作用在追役子系统上）：</para>
/// <list type="bullet">
/// <item><see cref="Attack"/> 进攻：追役状态弃牌 bonus 的幅度（越高越愿意为目标保留牌）。</item>
/// <item><see cref="Defense"/> 防守：追役状态温度采样里的放铳危险惩罚（越高越先切安全牌）。</item>
/// <item><see cref="Meld"/> 副露：追役时允许副露的倾向（越高越愿意副露，0 = 尽量门清）。</item>
/// <item><see cref="Value"/> 打点：追役目标的价值权重（清一色 / 字一色 / 九莲等优先于七对）。</item>
/// <item><see cref="Pursuit"/> 追役：进入追役的积极性、追役温度与副露抑制强度。</item>
/// </list>
///
/// 不可变 record：设置里保存一份实例，热更新时整体替换，不需要深拷贝。
/// </summary>
public sealed record AiStyle
{
    public const double Min = 0.0;
    public const double Max = 2.0;
    public const double Neutral = 1.0;

    /// <summary>进攻：追役保留牌 bonus 的幅度。</summary>
    public double Attack { get; init; } = Neutral;

    /// <summary>防守：追役采样里的放铳危险惩罚。</summary>
    public double Defense { get; init; } = Neutral;

    /// <summary>副露：追役时允许副露的倾向。</summary>
    public double Meld { get; init; } = Neutral;

    /// <summary>打点：追役目标价值权重。</summary>
    public double Value { get; init; } = Neutral;

    /// <summary>追役：进入追役的积极性 / 温度 / 副露抑制强度。</summary>
    public double Pursuit { get; init; } = Neutral;

    /// <summary>中性风格（五项全 1.0）。</summary>
    public static AiStyle Default { get; } = new();

    /// <summary>稳守：多防守、少副露、少追役。</summary>
    public static AiStyle Steady { get; } = new()
    {
        Attack = 0.8,
        Defense = 1.4,
        Meld = 1.0,
        Value = 0.9,
        Pursuit = 0.5,
    };

    /// <summary>均衡：与 <see cref="Default"/> 相同（五项全 1.0）。</summary>
    public static AiStyle Balanced { get; } = new();

    /// <summary>追役：多进攻、少防守、少副露、打点与追役拉高。</summary>
    public static AiStyle PursuitFocused { get; } = new()
    {
        Attack = 1.3,
        Defense = 0.7,
        Meld = 0.7,
        Value = 1.4,
        Pursuit = 1.7,
    };

    /// <summary>设置页三个预设的顺序：稳守 / 均衡 / 追役。</summary>
    public static IReadOnlyList<AiStyle> Presets { get; } = new[] { Steady, Balanced, PursuitFocused };

    /// <summary>五个字段全部合法（有限且落在 0..2）。</summary>
    public bool IsInRange =>
        IsFiniteInRange(Attack) && IsFiniteInRange(Defense) && IsFiniteInRange(Meld)
        && IsFiniteInRange(Value) && IsFiniteInRange(Pursuit);

    /// <summary>把每个字段夹到 0..2；非有限值（NaN / ∞，坏存档）回退 1.0。</summary>
    public AiStyle Clamped() => this with
    {
        Attack = Clamp(Attack),
        Defense = Clamp(Defense),
        Meld = Clamp(Meld),
        Value = Clamp(Value),
        Pursuit = Clamp(Pursuit),
    };

    /// <summary>与三个预设之一逐字段比较（浮点容差 1e-6）；返回 0=稳守 / 1=均衡 / 2=追役 / -1=自定义。</summary>
    public int MatchingPreset()
    {
        for (int i = 0; i < Presets.Count; i++)
        {
            if (SameAs(Presets[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private bool SameAs(AiStyle other) =>
        Math.Abs(Attack - other.Attack) < 1e-6
        && Math.Abs(Defense - other.Defense) < 1e-6
        && Math.Abs(Meld - other.Meld) < 1e-6
        && Math.Abs(Value - other.Value) < 1e-6
        && Math.Abs(Pursuit - other.Pursuit) < 1e-6;

    private static bool IsFiniteInRange(double value) =>
        double.IsFinite(value) && value is >= Min and <= Max;

    private static double Clamp(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, Min, Max) : Neutral;
}
