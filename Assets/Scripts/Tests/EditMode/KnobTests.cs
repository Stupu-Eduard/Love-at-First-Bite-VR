using NUnit.Framework;

public class KnobTests
{
    const float H = 8f;

    [TestCase(KnobPosition.Off, 0f)]
    [TestCase(KnobPosition.Low, 60f)]
    [TestCase(KnobPosition.High, 120f)]
    public void AngleFor_ReturnsDetentAngles(KnobPosition position, float expected)
    {
        Assert.AreEqual(expected, Knob.AngleFor(position));
    }

    [TestCase(0f, KnobPosition.Off)]
    [TestCase(60f, KnobPosition.Low)]
    [TestCase(120f, KnobPosition.High)]
    public void AtDetentAngle_ReturnsThatPosition_FromAnyStart(float angle, KnobPosition expected)
    {
        foreach (KnobPosition start in System.Enum.GetValues(typeof(KnobPosition)))
            Assert.AreEqual(expected, Knob.PositionFor(angle, start, H), $"start = {start}");
    }

    [Test]
    public void Off_NeedsToPassBorderPlusHysteresis_ToLeave()
    {
        Assert.AreEqual(KnobPosition.Off, Knob.PositionFor(30f, KnobPosition.Off, H));
        Assert.AreEqual(KnobPosition.Off, Knob.PositionFor(37.9f, KnobPosition.Off, H));
        Assert.AreEqual(KnobPosition.Low, Knob.PositionFor(38f, KnobPosition.Off, H));
    }

    [Test]
    public void Low_StaysInsideTheHysteresisBand()
    {
        Assert.AreEqual(KnobPosition.Low, Knob.PositionFor(22f, KnobPosition.Low, H));
        Assert.AreEqual(KnobPosition.Off, Knob.PositionFor(21.9f, KnobPosition.Low, H));
        Assert.AreEqual(KnobPosition.Low, Knob.PositionFor(97.9f, KnobPosition.Low, H));
        Assert.AreEqual(KnobPosition.High, Knob.PositionFor(98f, KnobPosition.Low, H));
    }

    [Test]
    public void High_NeedsToDropBelowBorderMinusHysteresis_ToLeave()
    {
        Assert.AreEqual(KnobPosition.High, Knob.PositionFor(82f, KnobPosition.High, H));
        Assert.AreEqual(KnobPosition.Low, Knob.PositionFor(81.9f, KnobPosition.High, H));
    }

    [Test]
    public void Border_DoesNotFlickerWhenAngleJittersAroundIt()
    {
        var position = KnobPosition.Off;
        foreach (var angle in new[] { 29f, 31f, 33f, 35f, 31f, 29f, 33f })
            position = Knob.PositionFor(angle, position, H);
        Assert.AreEqual(KnobPosition.Off, position);

        foreach (var angle in new[] { 40f, 31f, 25f, 35f, 28f })
            position = Knob.PositionFor(angle, position, H);
        Assert.AreEqual(KnobPosition.Low, position);
    }

    [Test]
    public void AnglesOutsideTheRange_AreClamped()
    {
        Assert.AreEqual(KnobPosition.Off, Knob.PositionFor(-50f, KnobPosition.Off, H));
        Assert.AreEqual(KnobPosition.High, Knob.PositionFor(500f, KnobPosition.Off, H));
        Assert.AreEqual(KnobPosition.Off, Knob.PositionFor(-50f, KnobPosition.High, H));
    }

    [Test]
    public void Off_ToHigh_InOneMove_Works()
    {
        Assert.AreEqual(KnobPosition.High, Knob.PositionFor(110f, KnobPosition.Off, H));
        Assert.AreEqual(KnobPosition.Off, Knob.PositionFor(5f, KnobPosition.High, H));
    }

    [Test]
    public void ZeroHysteresis_SwitchesAtTheBorder()
    {
        Assert.AreEqual(KnobPosition.Low, Knob.PositionFor(30f, KnobPosition.Off, 0f));
        Assert.AreEqual(KnobPosition.Off, Knob.PositionFor(29.9f, KnobPosition.Low, 0f));
    }
}
