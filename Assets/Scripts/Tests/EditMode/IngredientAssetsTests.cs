using System;
using LoveAtFirstBite.Slicing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Checks the saved ingredient assets: the default cooking profile covers every ingredient and the Steak and
/// Mushroom prefabs have everything the game needs (cook, slice, grab, physics).
/// </summary>
public class IngredientAssetsTests
{
    const string k_ProfilePath = "Assets/Data/CookingProfile_Default.asset";
    const string k_SteakPath = "Assets/Prefabs/Ingredients/Steak.prefab";
    const string k_MushroomPath = "Assets/Prefabs/Ingredients/Mushroom.prefab";

    static CookingProfile LoadProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<CookingProfile>(k_ProfilePath);
        Assert.IsNotNull(profile, "Missing " + k_ProfilePath);
        return profile;
    }

    static GameObject LoadPrefab(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.IsNotNull(prefab, "Missing " + path);
        return prefab;
    }

    [Test]
    public void DefaultProfile_IsValid()
    {
        Assert.IsTrue(LoadProfile().Validate(out var error), error);
    }

    [Test]
    public void DefaultProfile_HasAnEntryForEveryIngredientKind()
    {
        var profile = LoadProfile();
        foreach (IngredientKind kind in Enum.GetValues(typeof(IngredientKind)))
            Assert.IsTrue(profile.TryGetEntry(kind, out _), "No cooking entry for " + kind);
    }

    [Test]
    public void DefaultProfile_MushroomsCookFasterThanSteak()
    {
        var profile = LoadProfile();
        Assert.IsTrue(profile.TryGetEntry(IngredientKind.Steak, out var steak));
        Assert.IsTrue(profile.TryGetEntry(IngredientKind.Mushroom, out var mushroom));

        Assert.Less(mushroom.almostDoneAt, steak.almostDoneAt);
        Assert.Less(mushroom.cookedAt, steak.cookedAt);
        Assert.Less(mushroom.burnedAt, steak.burnedAt);
    }

    [TestCase(k_SteakPath, IngredientKind.Steak)]
    [TestCase(k_MushroomPath, IngredientKind.Mushroom)]
    public void IngredientPrefab_HasEverythingNeeded(string path, IngredientKind kind)
    {
        var prefab = LoadPrefab(path);

        var cookable = prefab.GetComponentInChildren<Cookable>(true);
        Assert.IsNotNull(cookable, path + " needs a Cookable.");
        Assert.AreEqual(kind, cookable.Kind);
        var profileProperty = new SerializedObject(cookable).FindProperty("m_Profile");
        Assert.IsNotNull(profileProperty.objectReferenceValue, path + ": Cookable has no cooking profile.");

        Assert.IsNotNull(prefab.GetComponentInChildren<Sliceable>(true), path + " needs a Sliceable so the knife can cut it.");
        Assert.IsNotNull(prefab.GetComponentInChildren<XRGrabInteractable>(true), path + " needs an XRGrabInteractable.");

        var body = prefab.GetComponentInChildren<Rigidbody>(true);
        Assert.IsNotNull(body, path + " needs a Rigidbody.");
        Assert.IsFalse(body.isKinematic, path + ": the Rigidbody must be dynamic.");

        Assert.IsNotNull(prefab.GetComponentInChildren<Collider>(true), path + " needs a Collider.");
    }
}
