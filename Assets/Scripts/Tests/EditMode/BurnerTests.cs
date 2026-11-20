using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class BurnerTests
{
    readonly List<GameObject> m_Created = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (var go in m_Created) Object.DestroyImmediate(go);
        m_Created.Clear();
    }

    Burner MakeBurner(KnobPosition position = KnobPosition.Off)
    {
        var go = new GameObject("Burner");
        m_Created.Add(go);
        var burner = go.AddComponent<Burner>();
        burner.SetPosition(position);
        return burner;
    }

    BurnerZone MakeZone(Burner burner)
    {
        var go = new GameObject("Zone");
        m_Created.Add(go);
        var zone = go.AddComponent<BurnerZone>();
        zone.Bind(burner);
        return zone;
    }

    [TestCase(KnobPosition.Off, 0f)]
    [TestCase(KnobPosition.Low, 0.5f)]
    [TestCase(KnobPosition.High, 1f)]
    public void Heat_MatchesPosition(KnobPosition position, float expected)
    {
        var burner = MakeBurner(position);
        Assert.AreEqual(expected, burner.Heat, 0.0001f);
        Assert.AreEqual(position != KnobPosition.Off, burner.IsOn);
    }

    [Test]
    public void SetPosition_RaisesEventOnlyOnChange()
    {
        var burner = MakeBurner();
        var seen = new List<KnobPosition>();
        burner.OnPositionChanged += seen.Add;

        burner.SetPosition(KnobPosition.Low);
        burner.SetPosition(KnobPosition.Low);
        burner.SetPosition(KnobPosition.High);
        burner.SetPosition(KnobPosition.Off);

        CollectionAssert.AreEqual(
            new[] { KnobPosition.Low, KnobPosition.High, KnobPosition.Off }, seen);
    }

    [Test]
    public void Zone_ReportsBurnerHeat()
    {
        var burner = MakeBurner(KnobPosition.Low);
        var zone = MakeZone(burner);
        Assert.AreEqual(0.5f, zone.Heat, 0.0001f);

        burner.SetPosition(KnobPosition.High);
        Assert.AreEqual(1f, zone.Heat, 0.0001f);
    }

    [Test]
    public void Zone_WithoutBurner_HasNoHeat()
    {
        var go = new GameObject("Zone");
        m_Created.Add(go);
        Assert.AreEqual(0f, go.AddComponent<BurnerZone>().Heat);
    }

    [Test]
    public void MaxHeat_PicksTheHottestZone()
    {
        var zones = new List<BurnerZone>
        {
            MakeZone(MakeBurner(KnobPosition.Off)),
            MakeZone(MakeBurner(KnobPosition.Low)),
            null,
            MakeZone(MakeBurner(KnobPosition.High))
        };
        Assert.AreEqual(1f, BurnerZone.MaxHeat(zones), 0.0001f);
    }

    [Test]
    public void MaxHeat_OfNothing_IsZero()
    {
        Assert.AreEqual(0f, BurnerZone.MaxHeat(new List<BurnerZone>()));
        Assert.AreEqual(0f, BurnerZone.MaxHeat(null));
    }
}
