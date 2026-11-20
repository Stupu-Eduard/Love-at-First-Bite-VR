using NUnit.Framework;
using UnityEngine;

public class BurnerVisualTests
{
    [TestCase(0f, 3f, 0f)]
    [TestCase(0.5f, 3f, 1.5f)]
    [TestCase(1f, 3f, 3f)]
    [TestCase(2f, 3f, 3f)]
    [TestCase(-1f, 3f, 0f)]
    public void EmissionFor_ScalesWithHeatAndClamps(float heat, float high, float expected)
    {
        Assert.AreEqual(expected, BurnerVisual.EmissionFor(heat, high), 0.0001f);
    }

    [TestCase(0f, 0f)]
    [TestCase(0.5f, 0.5f)]
    [TestCase(1f, 1f)]
    [TestCase(5f, 1f)]
    public void FlameScaleFor_FollowsHeat(float heat, float expected)
    {
        Assert.AreEqual(expected, BurnerVisual.FlameScaleFor(heat), 0.0001f);
    }

    [Test]
    public void FlameScale_MatchesBurnerPositions()
    {
        Assert.AreEqual(0f, BurnerVisual.FlameScaleFor(Burner.HeatFor(KnobPosition.Off)));
        Assert.Less(
            BurnerVisual.FlameScaleFor(Burner.HeatFor(KnobPosition.Low)),
            BurnerVisual.FlameScaleFor(Burner.HeatFor(KnobPosition.High)));
    }
}
