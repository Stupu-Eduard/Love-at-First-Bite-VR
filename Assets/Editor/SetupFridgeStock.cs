#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Builds the fridge stock in the open scene: static colliders for the fridge shelves and walls (the fridge model
/// has none, so anything dynamic would fall through) and one <see cref="FridgeStockSlot"/> per stock item
/// (2 steaks on the first shelf, 4 mushrooms on the second). Every item rests on the shelf surface.
/// Running it again replaces the previous result, so it never creates duplicates.
/// Menu: Tools > Love at First Bite > Setup Fridge Stock / Remove Fridge Stock
/// </summary>
public static class SetupFridgeStock
{
    const string FridgeName = "fridge";
    const string DoorName = "fridge_door";
    const string CollidersName = "Fridge_ShelfColliders";
    const string StockName = "FridgeStock";

    // ---- Fridge geometry, in fridge-local units (1 unit = 0.05 m with the scale the model has in the scene). ----
    // Taken from the fridge mesh: outer box x -5..5, y 0..25, z -7.5..7.5, slabs and walls 1 unit thick.
    // Unity flips the OBJ x axis, so the front opening faces +X.

    /// <summary>
    /// Height of the shelf surfaces, top-down: [0] = first (top) shelf, [1] = second, [2] = third.
    /// To put the steaks and mushrooms on other shelves, swap the values or change ShelfIndex in Stock below.
    /// </summary>
    static readonly float[] ShelfSurfaceY = { 19f, 13f, 7f };

    const float FridgeHeight = 25f;
    const float SlabThickness = 1f;
    const float WallThickness = 1f;
    const float OuterMinX = -5f;
    const float OuterHalfZ = 7.5f;

    // Usable interior. The front is kept at 4.4 so static colliders never overlap the door collider (starts at x 4.5).
    const float InteriorMinX = -4f;
    const float InteriorMaxX = 4.4f;
    const float InteriorHalfZ = 6.5f;

    /// <summary>Clear height between a shelf surface and the slab above it.</summary>
    const float ClearHeight = 5f;

    /// <summary>X position of the items, measured from the fridge center toward the front.</summary>
    const float SlotDepthX = 0.5f;

    /// <summary>Gap in meters between the lowest point of an item and the shelf surface.</summary>
    const float RestGap = 0.002f;

    /// <summary>Extra radius in meters added to the free-spot check around an item.</summary>
    const float ClearRadiusMargin = 0.02f;

    const float ExpectedScale = 0.05f;

    struct StockItem
    {
        public string Path;
        public int ShelfIndex;
        public int Count;
        public StockItem(string path, int shelfIndex, int count) { Path = path; ShelfIndex = shelfIndex; Count = count; }
    }

    // Shelf 1 (index 0) holds the steaks, shelf 2 (index 1) the mushrooms.
    static readonly StockItem[] Stock =
    {
        new StockItem("Assets/Prefabs/Ingredients/Steak.prefab", 0, 2),
        new StockItem("Assets/Prefabs/Ingredients/Mushroom.prefab", 1, 4),
    };

    // Builds the stock when Play is pressed, so the fridge is never empty. Does nothing if the stock exists
    // or if the open scene has no fridge.
    [InitializeOnLoadMethod]
    static void RegisterAutoSetup()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode) return;

        var fridge = FindFridge();
        if (fridge == null || fridge.Find(StockName) != null) return;

        Setup();
        Debug.Log("[FridgeStock] The fridge had no stock, so it was built automatically for Play. Press Ctrl+S to save the scene and keep it.");
    }

    [MenuItem("Tools/Love at First Bite/Setup Fridge Stock")]
    public static void Setup()
    {
        var fridge = FindFridge();
        if (fridge == null)
        {
            Debug.LogError($"[FridgeStock] Fridge ('{FridgeName}' with a '{DoorName}' child) not found in the open scene. Open SampleScene first.");
            return;
        }

        WarnAboutFridgeTransform(fridge);
        if (Object.FindFirstObjectByType<XRInteractionManager>() == null)
            Debug.LogWarning("[FridgeStock] No XRInteractionManager in the scene. The stock items cannot be grabbed without one.");

        RemoveOld(fridge);
        BuildColliders(fridge);

        var stockRoot = new GameObject(StockName);
        stockRoot.transform.SetParent(fridge, false);
        Undo.RegisterCreatedObjectUndo(stockRoot, "Setup fridge stock");

        int placed = 0, total = 0;
        var checkedMeshPaths = new HashSet<string>();
        foreach (var stock in Stock)
        {
            total += stock.Count;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(stock.Path);
            if (prefab == null)
            {
                Debug.LogError($"[FridgeStock] Missing prefab: {stock.Path}. Skipping {stock.Count} slot(s).");
                continue;
            }
            if (stock.ShelfIndex < 0 || stock.ShelfIndex >= ShelfSurfaceY.Length)
            {
                Debug.LogError($"[FridgeStock] Shelf index {stock.ShelfIndex} is out of range for {stock.Path}.");
                continue;
            }
            placed += PlaceShelfItems(fridge, stockRoot.transform, prefab, stock, checkedMeshPaths);
        }

        EditorSceneManager.MarkSceneDirty(fridge.gameObject.scene);
        Selection.activeGameObject = stockRoot;

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < ShelfSurfaceY.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"shelf {i + 1}: y={ShelfWorldY(fridge, i):F3} m");
        }
        Debug.Log($"[FridgeStock] Done: {placed}/{total} slots placed ({sb}). Save the scene with Ctrl+S, then press Play, " +
                  "open the fridge door, take an item and watch it reappear after 5 s.");
    }

    [MenuItem("Tools/Love at First Bite/Remove Fridge Stock")]
    public static void Remove()
    {
        var fridge = FindFridge();
        if (fridge == null)
        {
            Debug.LogError($"[FridgeStock] Fridge ('{FridgeName}' with a '{DoorName}' child) not found in the open scene. Open SampleScene first.");
            return;
        }

        int removed = RemoveOld(fridge);
        EditorSceneManager.MarkSceneDirty(fridge.gameObject.scene);
        Debug.Log($"[FridgeStock] Removed {removed} object(s). Save the scene with Ctrl+S.");
    }

    // The mesh child is also called 'fridge', so the real root is the one that has the door as a child.
    static Transform FindFridge()
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.name == FridgeName && t.Find(DoorName) != null) return t;
        }
        return null;
    }

    static void WarnAboutFridgeTransform(Transform fridge)
    {
        var s = fridge.lossyScale;
        if (Mathf.Abs(s.x - ExpectedScale) > 0.001f || Mathf.Abs(s.y - ExpectedScale) > 0.001f || Mathf.Abs(s.z - ExpectedScale) > 0.001f)
            Debug.LogWarning($"[FridgeStock] Fridge scale is {s}, expected about {ExpectedScale}. The shelf geometry constants assume that scale.");
        if (Quaternion.Angle(fridge.rotation, Quaternion.identity) > 0.5f)
            Debug.LogWarning($"[FridgeStock] Fridge rotation is {fridge.rotation.eulerAngles}, expected identity. Check that the items sit inside the fridge.");
    }

    static int RemoveOld(Transform fridge)
    {
        int removed = 0;
        for (int i = fridge.childCount - 1; i >= 0; i--)
        {
            var child = fridge.GetChild(i).gameObject;
            if (child.name != CollidersName && child.name != StockName) continue;
            Undo.DestroyObjectImmediate(child);
            removed++;
        }
        return removed;
    }

    static float ShelfWorldY(Transform fridge, int shelfIndex) =>
        fridge.TransformPoint(new Vector3(0f, ShelfSurfaceY[shelfIndex], 0f)).y;

    // ---- Colliders ----

    static void BuildColliders(Transform fridge)
    {
        var go = new GameObject(CollidersName);
        go.transform.SetParent(fridge, false); // local identity, a sibling of the mesh child that owns the kinematic body
        Undo.RegisterCreatedObjectUndo(go, "Setup fridge stock");

        // All boxes are static (no Rigidbody) and stay at x <= InteriorMaxX.
        float outerHalfZ = OuterHalfZ;
        float wallInnerZ = OuterHalfZ - WallThickness;

        AddBox(go, new Vector3(OuterMinX, 0f, -outerHalfZ), new Vector3(InteriorMaxX, SlabThickness, outerHalfZ)); // floor
        AddBox(go, new Vector3(OuterMinX, FridgeHeight - SlabThickness, -outerHalfZ), new Vector3(InteriorMaxX, FridgeHeight, outerHalfZ)); // top
        AddBox(go, new Vector3(OuterMinX, 0f, -outerHalfZ), new Vector3(OuterMinX + WallThickness, FridgeHeight, outerHalfZ)); // back wall
        AddBox(go, new Vector3(OuterMinX, 0f, -outerHalfZ), new Vector3(InteriorMaxX, FridgeHeight, -wallInnerZ)); // side wall -Z
        AddBox(go, new Vector3(OuterMinX, 0f, wallInnerZ), new Vector3(InteriorMaxX, FridgeHeight, outerHalfZ)); // side wall +Z

        foreach (float surfaceY in ShelfSurfaceY)
            AddBox(go, new Vector3(InteriorMinX, surfaceY - SlabThickness, -InteriorHalfZ), new Vector3(InteriorMaxX, surfaceY, InteriorHalfZ));
    }

    // min and max are corners in fridge-local units. The parent has local identity, so they map directly.
    static void AddBox(GameObject go, Vector3 min, Vector3 max)
    {
        var box = Undo.AddComponent<BoxCollider>(go);
        box.center = (min + max) * 0.5f;
        box.size = max - min;
    }

    // ---- Slots ----

    static int PlaceShelfItems(Transform fridge, Transform stockRoot, GameObject prefab, StockItem stock, HashSet<string> checkedMeshPaths)
    {
        float surfaceWorldY = ShelfWorldY(fridge, stock.ShelfIndex);
        float usableWidth = 2f * InteriorHalfZ;
        var placedBounds = new List<Bounds>();
        int placed = 0;

        for (int i = 0; i < stock.Count; i++)
        {
            float localZ = -usableWidth * 0.5f + (i + 0.5f) * usableWidth / stock.Count;
            Vector3 xz = fridge.TransformPoint(new Vector3(SlotDepthX, 0f, localZ));

            // Temporary instance, only to measure the real size of the item.
            var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            temp.transform.SetPositionAndRotation(new Vector3(xz.x, surfaceWorldY, xz.z), Quaternion.identity);
            Physics.SyncTransforms();

            var renderers = temp.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Debug.LogError($"[FridgeStock] {prefab.name} has no renderer, cannot measure it. Skipping.");
                Object.DestroyImmediate(temp);
                continue;
            }
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            float pivotY = temp.transform.position.y;
            float slotY = surfaceWorldY + RestGap - (bounds.min.y - pivotY);
            WarnAboutMeshReadability(temp, checkedMeshPaths);
            Object.DestroyImmediate(temp);

            // Bounds of the item once it sits in the slot.
            bounds.center += Vector3.up * (slotY - pivotY);
            WarnAboutFit(fridge, prefab.name, i + 1, bounds, placedBounds);
            placedBounds.Add(bounds);

            var slotObject = new GameObject($"Slot_{prefab.name}_{i + 1}");
            slotObject.transform.SetParent(stockRoot, false);
            Undo.RegisterCreatedObjectUndo(slotObject, "Setup fridge stock");
            slotObject.transform.SetPositionAndRotation(new Vector3(xz.x, slotY, xz.z), Quaternion.identity);

            var slot = Undo.AddComponent<FridgeStockSlot>(slotObject);
            var so = new SerializedObject(slot);
            so.FindProperty("m_Prefab").objectReferenceValue = prefab;
            so.FindProperty("m_ClearRadius").floatValue = Mathf.Max(bounds.extents.x, bounds.extents.z) + ClearRadiusMargin;
            so.ApplyModifiedPropertiesWithoutUndo();
            placed++;
        }
        return placed;
    }

    static void WarnAboutFit(Transform fridge, string itemName, int number, Bounds worldBounds, List<Bounds> neighbours)
    {
        float clearHeightMeters = ClearHeight * fridge.lossyScale.y;
        if (worldBounds.size.y > clearHeightMeters)
            Debug.LogWarning($"[FridgeStock] {itemName} {number} is {worldBounds.size.y:F3} m tall but the shelf clear height is {clearHeightMeters:F3} m.");

        // Check the box in fridge-local space against the interior.
        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (int c = 0; c < 8; c++)
        {
            var corner = worldBounds.center + Vector3.Scale(worldBounds.extents,
                new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
            var local = fridge.InverseTransformPoint(corner);
            min = Vector3.Min(min, local);
            max = Vector3.Max(max, local);
        }
        if (min.x < InteriorMinX || max.x > InteriorMaxX || min.z < -InteriorHalfZ || max.z > InteriorHalfZ)
            Debug.LogWarning($"[FridgeStock] {itemName} {number} leaves the fridge interior (local x {min.x:F2}..{max.x:F2}, z {min.z:F2}..{max.z:F2}).");

        foreach (var other in neighbours)
        {
            if (worldBounds.min.x < other.max.x && worldBounds.max.x > other.min.x &&
                worldBounds.min.z < other.max.z && worldBounds.max.z > other.min.z)
            {
                Debug.LogWarning($"[FridgeStock] {itemName} {number} overlaps the previous item on the shelf.");
                break;
            }
        }
    }

    // The knife only slices meshes it can read, so the model importer must have Read/Write on.
    static void WarnAboutMeshReadability(GameObject instance, HashSet<string> checkedPaths)
    {
        foreach (var filter in instance.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;
            string path = AssetDatabase.GetAssetPath(filter.sharedMesh);
            if (string.IsNullOrEmpty(path) || !checkedPaths.Add(path)) continue;
            if (AssetImporter.GetAtPath(path) is ModelImporter importer && !importer.isReadable)
                Debug.LogWarning($"[FridgeStock] Mesh '{path}' is not Read/Write enabled, so the knife cannot slice it. " +
                                 "Run Tools > Love at First Bite > Make Ingredient Meshes Readable.");
        }
    }
}
#endif
