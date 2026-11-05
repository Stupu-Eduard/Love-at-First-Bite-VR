#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Places the ingredient prefabs (steak + mushrooms) on the Kitchen_Set03 counter.
/// The height is read from the real mesh with raycasts, so nothing floats or sinks.
/// Menu: Tools > Love at First Bite > Place Ingredients On Kitchen_Set03
/// </summary>
public static class PlaceIngredientsOnCounter
{
    const string SurfaceName = "Kitchen_Set03";
    const string RootName = "Ingredients";
    const float RaycastStartHeight = 3f;
    const float MaxCornerDifference = 0.02f;

    struct Item
    {
        public string Path;
        public float X, Z, Yaw;
        public Item(string path, float x, float z, float yaw) { Path = path; X = x; Z = z; Yaw = yaw; }
    }

    // World X/Z positions. X runs along the counter, Z is depth (back wall is at z ~ 1.46).
    // Chosen to stay clear of Bread (x -0.8, z 1.2), Knife (x -0.5, z 1.1) and the Sink (x -1.6).
    static readonly Item[] Items =
    {
        new Item("Assets/Models/Ingredients/Steak/Steak_Raw.prefab",        -1.30f, 0.95f, 0f),
        new Item("Assets/Models/Ingredients/Steak/Steak_AlmostDone.prefab", -1.10f, 0.95f, 0f),
        new Item("Assets/Models/Ingredients/Steak/Steak_Cooked.prefab",     -0.90f, 0.95f, 0f),
        new Item("Assets/Models/Ingredients/Steak/Steak_Burned.prefab",     -0.70f, 0.95f, 0f),

        new Item("Assets/Models/Ingredients/Mushrooms/Mushroom_Whole_Raw.prefab",    -1.30f, 1.33f, 0f),
        new Item("Assets/Models/Ingredients/Mushrooms/Mushroom_Whole_Cooked.prefab", -1.20f, 1.33f, 40f),
        new Item("Assets/Models/Ingredients/Mushrooms/Mushroom_Whole_Burned.prefab", -1.10f, 1.33f, 80f),
        new Item("Assets/Models/Ingredients/Mushrooms/Mushroom_Sliced_Raw.prefab",    -1.00f, 1.33f, 0f),
        new Item("Assets/Models/Ingredients/Mushrooms/Mushroom_Sliced_Cooked.prefab", -0.90f, 1.33f, 0f),
        new Item("Assets/Models/Ingredients/Mushrooms/Mushroom_Sliced_Burned.prefab", -0.80f, 1.33f, 0f),
    };

    [MenuItem("Tools/Love at First Bite/Place Ingredients On Kitchen_Set03")]
    public static void Place()
    {
        var surface = GameObject.Find(SurfaceName);
        if (surface == null)
        {
            Debug.LogError($"[Ingredients] '{SurfaceName}' not found in the open scene. Open SampleScene first.");
            return;
        }

        // Temporary colliders, only so the raycasts can hit the mesh. Removed again at the end.
        var tempColliders = new List<MeshCollider>();
        foreach (var mf in surface.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.GetComponent<Collider>() != null || mf.sharedMesh == null) continue;
            tempColliders.Add(mf.gameObject.AddComponent<MeshCollider>());
        }
        Physics.SyncTransforms();

        // Replace a previous run instead of duplicating.
        var old = GameObject.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Place ingredients");

        int placed = 0;
        foreach (var item in Items)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(item.Path);
            if (prefab == null) { Debug.LogWarning($"[Ingredients] Missing prefab: {item.Path}"); continue; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            go.transform.SetPositionAndRotation(
                new Vector3(item.X, 0f, item.Z), Quaternion.Euler(0f, item.Yaw, 0f));

            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) { Debug.LogWarning($"[Ingredients] {go.name} has no renderer."); continue; }
            Physics.SyncTransforms();

            Bounds b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);

            // Probe the centre and the four bottom corners.
            var probes = new[]
            {
                new Vector2(b.center.x, b.center.z),
                new Vector2(b.min.x, b.min.z), new Vector2(b.min.x, b.max.z),
                new Vector2(b.max.x, b.min.z), new Vector2(b.max.x, b.max.z),
            };
            float top = float.MinValue, low = float.MaxValue;
            bool allHit = true;
            foreach (var p in probes)
            {
                if (TryHitSurface(surface.transform, p, out float y))
                {
                    top = Mathf.Max(top, y);
                    low = Mathf.Min(low, y);
                }
                else allHit = false;
            }

            if (top == float.MinValue)
            {
                Debug.LogWarning($"[Ingredients] {go.name}: no surface found at x={item.X}, z={item.Z}. Move it in Items[].");
                Undo.DestroyObjectImmediate(go);
                continue;
            }
            if (!allHit || top - low > MaxCornerDifference)
                Debug.LogWarning($"[Ingredients] {go.name}: surface is uneven or ends under it (x={item.X}, z={item.Z}). Check it by eye.");

            go.transform.position += Vector3.up * (top - b.min.y);
            Debug.Log($"[Ingredients] {go.name} placed on surface at y={top:F3}");
            placed++;
        }

        foreach (var c in tempColliders) if (c != null) Object.DestroyImmediate(c);

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log($"[Ingredients] Done: {placed}/{Items.Length} placed. Save the scene with Ctrl+S.");
    }

    /// <summary>
    /// The ingredients now have a Rigidbody, so the counter needs real colliders
    /// or they fall through it in Play Mode.
    /// </summary>
    [MenuItem("Tools/Love at First Bite/Add Colliders To Kitchen_Set03")]
    public static void AddCounterColliders()
    {
        var surface = GameObject.Find(SurfaceName);
        if (surface == null)
        {
            Debug.LogError($"[Ingredients] '{SurfaceName}' not found in the open scene. Open SampleScene first.");
            return;
        }

        int added = 0;

        // Empty MeshColliders (no mesh) do nothing, remove them.
        foreach (var mc in surface.GetComponentsInChildren<MeshCollider>(true))
            if (mc.sharedMesh == null) Undo.DestroyObjectImmediate(mc);

        foreach (var mf in surface.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;

            if (mf.name.Contains("Cupboard"))
            {
                // The counter top is a single flat polygon. A mesh collider has no thickness there and
                // dynamic objects can sink through it, so use a solid box instead.
                var oldMesh = mf.GetComponent<MeshCollider>();
                if (oldMesh != null) Undo.DestroyObjectImmediate(oldMesh);
                if (mf.GetComponent<BoxCollider>() == null)
                {
                    var box = Undo.AddComponent<BoxCollider>(mf.gameObject);
                    box.center = mf.sharedMesh.bounds.center;
                    box.size = mf.sharedMesh.bounds.size;
                    added++;
                }
            }
            else if (mf.GetComponent<Collider>() == null)
            {
                Undo.AddComponent<MeshCollider>(mf.gameObject);
                added++;
            }
        }
        EditorSceneManager.MarkSceneDirty(surface.scene);
        Debug.Log($"[Ingredients] Added {added} collider(s) on {SurfaceName}. Save the scene with Ctrl+S.");
    }

    static bool TryHitSurface(Transform surface, Vector2 xz, out float y)
    {
        y = 0f;
        var origin = new Vector3(xz.x, RaycastStartHeight, xz.y);
        var hits = Physics.RaycastAll(origin, Vector3.down, RaycastStartHeight + 2f,
            ~0, QueryTriggerInteraction.Ignore);
        float best = float.MinValue;
        foreach (var h in hits)
        {
            if (!h.collider.transform.IsChildOf(surface)) continue;
            if (h.point.y > best) best = h.point.y;
        }
        if (best == float.MinValue) return false;
        y = best;
        return true;
    }
}
#endif
