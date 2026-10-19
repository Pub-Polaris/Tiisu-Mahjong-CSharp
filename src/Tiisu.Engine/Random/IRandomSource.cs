namespace Tiisu.Engine.Random;

/// <summary>
/// 可注入的随机源（自走用）：只暴露 <see cref="NextDouble"/>，范围 [0,1)，语义与 JS <c>Math.random()</c> 一致。
/// 回放模式不注入；自走模式由 <see cref="SelfPlay.SelfPlayDriver"/> 注入，保证同 seed 可复现。
/// </summary>
public interface IRandomSource
{
    double NextDouble();
}

/// <summary>
/// mulberry32（与 Web 侧语料生成器 <c>ai_golden.html</c> / <c>engine_golden.html</c> 的 <c>mk(seed)</c> 同算法）：
/// <code>
/// a = (a + 0x6D2B79F5) &gt;&gt;&gt; 0;
/// t = a; t = imul(t ^ (t &gt;&gt;&gt; 15), t | 1); t ^= t + imul(t ^ (t &gt;&gt;&gt; 7), t | 61);
/// return ((t ^ (t &gt;&gt;&gt; 14)) &gt;&gt;&gt; 0) / 4294967296;
/// </code>
/// C# 侧用 <c>uint</c> 的无符号回绕等价 JS 的 <c>ToInt32</c> / <c>Math.imul</c> 位语义。
/// </summary>
public sealed class Mulberry32Random : IRandomSource
{
    private uint _state;

    public Mulberry32Random(uint seed) => _state = seed;

    public double NextDouble()
    {
        unchecked
        {
            _state += 0x6D2B79F5u;

            uint t = _state;
            t = (t ^ (t >> 15)) * (t | 1u);
            t ^= t + ((t ^ (t >> 7)) * (t | 61u));
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }
}
