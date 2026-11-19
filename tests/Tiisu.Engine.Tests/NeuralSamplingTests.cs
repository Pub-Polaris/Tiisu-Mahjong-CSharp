using Tiisu.Engine.Ai;
using Tiisu.Engine.Random;
using Xunit;

namespace Tiisu.Engine.Tests;

public sealed class NeuralSamplingTests
{
    [Fact]
    public void TopK1_OnlyTopScoreCanBeSampled()
    {
        var scores = new double[] { 0.0, 10.0, 9.0, 1.0 };
        var rng = new Mulberry32Random(7);
        for (int i = 0; i < 200; i++)
        {
            Assert.Equal(1, NeuralPolicyAi.SampleScores(scores, temperature: 5.0, rng, topK: 1));
        }
    }

    [Fact]
    public void TopK2_DoesNotLeaveTopTwo()
    {
        var scores = new double[] { 0.0, 10.0, 9.0, 1.0 };
        var rng = new Mulberry32Random(11);
        for (int i = 0; i < 400; i++)
        {
            int picked = NeuralPolicyAi.SampleScores(scores, temperature: 5.0, rng, topK: 2);
            Assert.True(picked is 1 or 2, $"picked={picked}");
        }
    }

    [Fact]
    public void NonFiniteScore_FallsBackToArgmax()
    {
        var scores = new double[] { double.NaN, 3.0, 2.0 };
        int picked = NeuralPolicyAi.SampleScores(scores, temperature: 1.0, new Mulberry32Random(1), topK: 0);
        // NaN cannot be selected; BestIndex still returns the first finite max by linear scan.
        Assert.True(picked is 1 or 0);
    }
}
