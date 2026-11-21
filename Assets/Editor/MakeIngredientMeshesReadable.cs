#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Runtime mesh slicing needs the source meshes to be Read/Write enabled, so the slicer
/// can read vertex data on device. Imported FBX models default to isReadable = 0.
///
/// This tool enables Read/Write on every mesh model asset under Assets/Models/Ingredients
/// (the Steak and Mushroom models the scene ingredients use) and reimports them.
///
/// Menu: Tools > Love at First Bite > Make Ingredient Meshes Readable
/// </summary>
public static class MakeIngredientMeshesReadable
{
    // The ingredient models live here; their meshes come from these FBX sources.
    static readonly string[] SearchFolders = { "Assets/Models/Ingredients", "Assets/Pandazole_Ultimate_Pack" };

    [MenuItem("Tools/Love at First Bite/Make Ingredient Meshes Readable")]
    public static void Run()
    {
        // Collect every mesh asset referenced by prefabs under Models/Ingredients.
        var modelPaths = new HashSet<string>();

        foreach (var prefabGuid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Models/Ingredients" }))
        {
            var prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) continue;

            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var meshPath = AssetDatabase.GetAssetPath(mf.sharedMesh);
                if (!string.IsNullOrEmpty(meshPath))
                    modelPaths.Add(meshPath);
            }
        }

        if (modelPaths.Count == 0)
        {
            Debug.LogWarning("[Readable] No ingredient meshes found under Assets/Models/Ingredients.");
            return;
        }

        int changed = 0;
        foreach (var path in modelPaths)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) continue; // not a model (could be a .asset mesh)
            if (!importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
                changed++;
                Debug.Log($"[Readable] Enabled Read/Write on {path}");
            }
        }

        Debug.Log($"[Readable] Done. {changed} model(s) updated, {modelPaths.Count} checked. Ingredients can now be sliced.");
    }
}
#endif
