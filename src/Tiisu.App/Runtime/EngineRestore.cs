using System;
using System.Reflection;
using Tiisu.Engine;

namespace Tiisu.App.Runtime;

/// <summary>
/// 存档恢复：把 <see cref="SaveState"/> 的场次头写回引擎。
/// <para>
/// 引擎（<c>Tiisu.Engine</c>）只暴露只读包装，<c>HandsPlayed / DealerIndex / Honba / RoundWindIdx / DealerCount</c>
/// 的 setter 是私有的，<c>_scores</c> 也是私有字段。这里用反射写入**运行时状态**，
/// 不改引擎代码（与旧 WebView2 外壳 <c>EngineHost.ApplySavedState</c> 同一手法）。
/// 引擎规则 / 判役不受影响。
/// </para>
/// </summary>
internal static class EngineRestore
{
    public static void Apply(MahjongEngine engine, SaveState state)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(state);

        SetAutoProperty(engine, "HandsPlayed", Math.Max(0, state.HandsPlayed));
        SetAutoProperty(engine, "DealerIndex", Math.Clamp(state.DealerIndex, 0, MahjongEngine.PlayerCount - 1));
        SetAutoProperty(engine, "Honba", Math.Max(0, state.Honba));
        SetAutoProperty(engine, "RoundWindIdx", Math.Max(0, state.RoundWindIdx));
        SetAutoProperty(engine, "DealerCount", Math.Max(0, state.DealerCount));
        engine.HandNumber = Math.Max(0, state.HandNumber);

        if (state.Scores is { Length: MahjongEngine.PlayerCount })
        {
            var field = typeof(MahjongEngine).GetField("_scores", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field?.GetValue(engine) is int[] scores)
            {
                for (int i = 0; i < scores.Length; i++)
                {
                    scores[i] = state.Scores[i];
                }
            }
        }

        engine.UpdateSeatWinds();

        // 报听 / 禁和标记（跨局的那部分才真正生效：玩家牌面在开新局时重置）。
        engine.RestoreCallState(state.IsCalled, state.CallWinBanned, state.CallWinBannedNextHand);
    }

    private static void SetAutoProperty(object target, string property, object value)
    {
        var field = target.GetType().GetField(
            $"<{property}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field is null)
        {
            throw new InvalidOperationException($"找不到属性 {property} 的 backing field");
        }

        field.SetValue(target, value);
    }
}
