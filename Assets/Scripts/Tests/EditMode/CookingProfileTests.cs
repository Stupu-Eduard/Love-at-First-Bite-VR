using NUnit.Framework;
using UnityEngine;

public class CookingProfileTests
{
    Material m_Mat;

    [SetUp]
    public void SetUp() => m_Mat = new Material(Shader.Find("Sprites/Default"));

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(m_Mat);

    CookingProfile.Entry MakeEntry(float a = 10f, float c = 20f, float b = 40f, bool materials = true)
    {
        return new CookingProfile.Entry
        {
            kind = IngredientKind.Steak,
            almostDoneAt = a, cookedAt = c, burnedAt = b,
            raw = materials ? m_Mat : null,
            almostDone = m_Mat, cooked = m_Mat, burned = m_Mat
        };
    }

    [Test]
    public void StateAt_UsesThresholds()
    {
        var e = MakeEntry();
        Assert.AreEqual(CookState.Raw, e.StateAt(0f));
        Assert.AreEqual(CookState.Raw, e.StateAt(9.99f));
        Assert.AreEqual(CookState.AlmostDone, e.StateAt(10f));
        Assert.AreEqual(CookState.Cooked, e.StateAt(20f));
        Assert.AreEqual(CookState.Burned, e.StateAt(40f));
        Assert.AreEqual(CookState.Burned, e.StateAt(1000f));
    }

    [Test]
    public void IsValid_AcceptsIncreasingThresholdsAndAllMaterials()
    {
        Assert.IsTrue(MakeEntry().IsValid(out var error), error);
    }

    [TestCase(0f, 20f, 40f)]
    [TestCase(10f, 10f, 40f)]
    [TestCase(10f, 20f, 15f)]
    public void IsValid_RejectsBadThresholds(float a, float c, float b)
    {
        Assert.IsFalse(MakeEntry(a, c, b).IsValid(out var error));
        StringAssert.Contains("thresholds", error);
    }

    [Test]
    public void IsValid_RejectsMissingMaterial()
    {
        Assert.IsFalse(MakeEntry(materials: false).IsValid(out var error));
        StringAssert.Contains("materials", error);
    }

    [Test]
    public void Profile_TryGetEntry_FindsByKind()
    {
        var profile = ScriptableObject.CreateInstance<CookingProfile>();
        profile.SetEntries(new[] { MakeEntry() });

        Assert.IsTrue(profile.TryGetEntry(IngredientKind.Steak, out var entry));
        Assert.IsNotNull(entry);
        Assert.IsFalse(profile.TryGetEntry(IngredientKind.Mushroom, out _));
        Assert.IsTrue(profile.Validate(out var error), error);

        Object.DestroyImmediate(profile);
    }

    [Test]
    public void Profile_Validate_FailsWhenEmpty()
    {
        var profile = ScriptableObject.CreateInstance<CookingProfile>();
        Assert.IsFalse(profile.Validate(out _));
        Object.DestroyImmediate(profile);
    }
}
