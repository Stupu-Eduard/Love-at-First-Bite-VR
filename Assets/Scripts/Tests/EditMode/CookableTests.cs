using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class CookableTests
{
    GameObject m_Go;
    Cookable m_Cookable;
    CookingProfile m_Profile;
    Material[] m_Mats;

    [SetUp]
    public void SetUp()
    {
        m_Mats = new Material[4];
        for (int i = 0; i < 4; i++) m_Mats[i] = new Material(Shader.Find("Sprites/Default")) { name = "M" + i };

        m_Profile = ScriptableObject.CreateInstance<CookingProfile>();
        m_Profile.SetEntries(new[]
        {
            new CookingProfile.Entry
            {
                kind = IngredientKind.Steak,
                almostDoneAt = 10f, cookedAt = 20f, burnedAt = 40f,
                raw = m_Mats[0], almostDone = m_Mats[1], cooked = m_Mats[2], burned = m_Mats[3]
            }
        });

        m_Go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        m_Cookable = m_Go.AddComponent<Cookable>();
        m_Cookable.Configure(IngredientKind.Steak, m_Profile, m_Go.GetComponent<Renderer>());
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(m_Go);
        Object.DestroyImmediate(m_Profile);
        foreach (var m in m_Mats) Object.DestroyImmediate(m);
    }

    [Test]
    public void StartsRaw_WithRawMaterial()
    {
        Assert.AreEqual(CookState.Raw, m_Cookable.State);
        Assert.AreSame(m_Mats[0], m_Go.GetComponent<Renderer>().sharedMaterial);
    }

    [Test]
    public void AddHeat_ReachesEachStateAtItsThreshold()
    {
        m_Cookable.AddHeat(1f, 9f);
        Assert.AreEqual(CookState.Raw, m_Cookable.State);

        m_Cookable.AddHeat(1f, 1f);
        Assert.AreEqual(CookState.AlmostDone, m_Cookable.State);

        m_Cookable.AddHeat(1f, 10f);
        Assert.AreEqual(CookState.Cooked, m_Cookable.State);

        m_Cookable.AddHeat(1f, 20f);
        Assert.AreEqual(CookState.Burned, m_Cookable.State);
    }

    [Test]
    public void AddHeat_SwapsSharedMaterial()
    {
        m_Cookable.AddHeat(1f, 20f);
        Assert.AreSame(m_Mats[2], m_Go.GetComponent<Renderer>().sharedMaterial);
    }

    [Test]
    public void LowHeat_IsHalfAsFastAsHighHeat()
    {
        m_Cookable.AddHeat(0.5f, 19f);   // 9.5 heat-seconds
        Assert.AreEqual(CookState.Raw, m_Cookable.State);

        m_Cookable.AddHeat(0.5f, 1f);    // 10 heat-seconds
        Assert.AreEqual(CookState.AlmostDone, m_Cookable.State);
    }

    [Test]
    public void OnStateChanged_FiresOncePerTransition()
    {
        var seen = new List<CookState>();
        m_Cookable.OnStateChanged += seen.Add;

        m_Cookable.AddHeat(1f, 5f);
        m_Cookable.AddHeat(1f, 5f);   // reaches AlmostDone
        m_Cookable.AddHeat(1f, 1f);   // still AlmostDone, no event
        m_Cookable.AddHeat(1f, 100f); // jumps straight to Burned

        CollectionAssert.AreEqual(new[] { CookState.AlmostDone, CookState.Burned }, seen);
    }

    [TestCase(0f, 1f)]
    [TestCase(1f, 0f)]
    [TestCase(-1f, 1f)]
    [TestCase(1f, -1f)]
    public void ZeroOrNegativeHeat_IsIgnored(float heat, float dt)
    {
        m_Cookable.AddHeat(heat, dt);
        Assert.AreEqual(0f, m_Cookable.HeatSeconds);
        Assert.AreEqual(CookState.Raw, m_Cookable.State);
    }

    [Test]
    public void State_NeverGoesBackwards()
    {
        m_Cookable.AddHeat(1f, 25f);
        Assert.AreEqual(CookState.Cooked, m_Cookable.State);

        m_Cookable.AddHeat(-5f, 1f);
        Assert.AreEqual(CookState.Cooked, m_Cookable.State);
    }

    [Test]
    public void WithoutProfile_AccumulatesButDoesNotThrow()
    {
        var go = new GameObject("NoProfile");
        var c = go.AddComponent<Cookable>();

        Assert.DoesNotThrow(() => c.AddHeat(1f, 100f));
        Assert.AreEqual(CookState.Raw, c.State);
        Assert.AreEqual(100f, c.HeatSeconds, 0.001f);

        Object.DestroyImmediate(go);
    }
}
