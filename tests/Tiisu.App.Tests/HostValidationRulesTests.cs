using System.Reflection;
using Tiisu.App.Runtime;
using Tiisu.App.Table;
using Xunit;

namespace Tiisu.App.Tests;

/// <summary>Host 指令白名单的纯逻辑回归（不启动端口 / 引擎）。</summary>
public sealed class HostValidationRulesTests
{
    private static bool Allowed(GameCommand command, HumanAction actions) =>
        (bool)typeof(HostEngine)
            .GetMethod("IsCommandAllowed", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { command, actions })!;

    [Fact]
    public void DeclareCall_uses_its_own_action_bit_not_discard()
    {
        // 荣和 / 副露窗口只置 DeclareCall，不置 Discard；报听不能被误判成作弊。
        Assert.True(Allowed(GameCommand.DeclareCall(), HumanAction.DeclareCall | HumanAction.Pass));
        Assert.False(Allowed(GameCommand.DeclareCall(), HumanAction.Discard));

        // 出牌阶段仍然只认 Discard 位。
        Assert.True(Allowed(GameCommand.Discard(0), HumanAction.Discard));
        Assert.False(Allowed(GameCommand.Discard(0), HumanAction.DeclareCall));
    }

    [Fact]
    public void Meld_and_ron_commands_still_require_their_bits()
    {
        Assert.True(Allowed(GameCommand.Ron(), HumanAction.Ron));
        Assert.False(Allowed(GameCommand.Ron(), HumanAction.Pon));
        Assert.True(Allowed(GameCommand.Pon(), HumanAction.Pon));
        Assert.False(Allowed(GameCommand.Pon(), HumanAction.Chi));
        Assert.True(Allowed(GameCommand.Pass(), HumanAction.Pass));
        Assert.False(Allowed(GameCommand.Pass(), HumanAction.None));
    }
}
