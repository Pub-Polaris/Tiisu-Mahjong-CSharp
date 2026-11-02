namespace Tiisu.Core;

/// <summary>规则集：七子（完整役种/赋）或推倒胡（1102工程，只判形态、纯胜负）。</summary>
public enum Ruleset
{
    /// <summary>七子麻将：完整役种、赋、起和线、报听、罚符。</summary>
    Qizi = 0,

    /// <summary>推倒胡：只判六种和牌形态，不看役与赋，无报听、无罚符、无庄家倍率。</summary>
    TuidaoHu = 1,
}
