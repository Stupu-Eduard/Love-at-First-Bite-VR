#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 1) Makes every object under "Grabbables" grabbable (Rigidbody + collider + XRGrabInteractable).
/// 2) Sits Plate_A / Plate_B on the Table so they no longer float.
/// Menu: Tools > Love at First Bite > Fix Grabbables And Plates
/// </summary>
public static class FixGrabbablesAndPlates
{
    const string GrabbablesName = "Grabbables";
    const string TableName = "Table";
    static readonly string[] PlateNames = { "Plate_A", "Plate_B" };

    [MenuItem("Tools/Love at First Bite/Fix Grabbables And Plates")]
    public static void Fix()
    {
        var grabbables = GameObject.Find(GrabbablesName);
        if (grabbables == null)
        {
            Debug.LogError($"[Fix] '{GrabbablesName}' not found. Open SampleScene first.");
            return;
        }

        // Plates first, so their new collider exists before grab is added.
        SnapPlates(grabbables.transform);
        AddPanBodyCollider(grabbables.transform);
        MoveBoardToGrabbables(grabbables.transform);

        int added = 0;
        foreach (Transform child in grabbables.transform)
        {
            var go = child.gameObject;

            if (go.GetComponent<Collider>() == null)
                Undo.AddComponent<BoxCollider>(go);

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = Undo.AddComponent<Rigidbody>(go);
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            if (go.GetComponent<XRGrabInteractable>() == null)
            {
                Undo.AddComponent<XRGrabInteractable>(go);
                added++;
            }
        }

        if (Object.FindFirstObjectByType<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>() == null)
            Debug.LogWarning("[Fix] No XR Interaction Manager in the scene - grab will not work.");

        EditorSceneManager.MarkSceneDirty(grabbables.scene);
        Debug.Log($"[Fix] XRGrabInteractable added on {added} object(s). Save the scene with Ctrl+S.");
    }

    /// <summary>
    /// Pan_Handle only has a collider on the handle; the pan body (about 0.5 m away) has none,
    /// so the handle balances on the stove and the pan tips off the burner.
    /// Adds a box collider that covers the pan body, computed in the pan's local space.
    /// </summary>
    static void AddPanBodyCollider(Transform grabbables)
    {
        var pan = grabbables.Find("Pan_Handle");
        if (pan == null) { Debug.LogWarning("[Fix] Pan_Handle not found."); return; }

        bool has = false;
        Bounds local = default;
        foreach (var mf in pan.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.transform == pan || mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = pan.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!has) { local = new Bounds(p, Vector3.zero); has = true; }
                else local.Encapsulate(p);
            }
        }
        if (!has) { Debug.LogWarning("[Fix] Pan body mesh not found under Pan_Handle."); return; }

        // Reuse the body collider if the script already ran, otherwise add a second box next to the handle one.
        // The first BoxCollider is the handle one; the second (if present) is the body one.
        var boxes = pan.GetComponents<BoxCollider>();
        var body = boxes.Length >= 2 ? boxes[1] : Undo.AddComponent<BoxCollider>(pan.gameObject);

        body.center = local.center;
        body.size = new Vector3(local.size.x, Mathf.Max(local.size.y, 0.1f), local.size.z);
        Debug.Log("[Fix] Added a collider on the pan body.");
    }

    /// <summary>
    /// The wooden cutting board ("Board") is a child of Sink_Basin, which is static, so it can't be grabbed.
    /// Moves it under Grabbables (same world position) and gives it a collider fitted to its mesh.
    /// </summary>
    static void MoveBoardToGrabbables(Transform grabbables)
    {
        var board = GameObject.Find("Board");
        if (board == null) { Debug.LogWarning("[Fix] 'Board' not found."); return; }

        if (board.transform.parent != grabbables)
            Undo.SetTransformParent(board.transform, grabbables, "Move board to Grabbables");

        var t = board.transform;
        bool has = false;
        Bounds local = default;
        foreach (var mf in board.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = t.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!has) { local = new Bounds(p, Vector3.zero); has = true; }
                else local.Encapsulate(p);
            }
        }
        if (!has) { Debug.LogWarning("[Fix] Board has no mesh."); return; }

        // Drop any colliders that came with the model so there is just one clean box.
        foreach (var c in board.GetComponentsInChildren<Collider>())
            if (!(c is BoxCollider bc0 && bc0.gameObject == board)) Undo.DestroyObjectImmediate(c);

        var box = board.GetComponent<BoxCollider>();
        if (box == null) box = Undo.AddComponent<BoxCollider>(board);
        box.center = local.center;
        box.size = local.size;

        Debug.Log("[Fix] Board moved under Grabbables with a collider.");
    }

    static void SnapPlates(Transform grabbables)
    {
        var table = GameObject.Find(TableName);
        var tableCol = table != null ? table.GetComponent<Collider>() : null;
        if (tableCol == null)
        {
            Debug.LogWarning($"[Fix] '{TableName}' with a collider not found, plates not moved.");
            return;
        }
        float tableTop = tableCol.bounds.max.y;

        foreach (var plateName in PlateNames)
        {
            var plate = grabbables.Find(plateName);
            if (plate == null) { Debug.LogWarning($"[Fix] {plateName} not found."); continue; }

            var rends = plate.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) { Debug.LogWarning($"[Fix] {plateName} has no renderer."); continue; }

            // The old capsule collider (scaled flat) behaves like a big sphere and holds the plate in the air.
            var cap = plate.GetComponent<CapsuleCollider>();
            if (cap != null) Undo.DestroyObjectImmediate(cap);

            Undo.RecordObject(plate, "Snap plate to table");

            Bounds b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);

            // Sit the real mesh on the table top.
            plate.position += Vector3.up * (tableTop - b.min.y);
            Physics.SyncTransforms();

            b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);

            var box = plate.GetComponent<BoxCollider>();
            if (box == null) box = Undo.AddComponent<BoxCollider>(plate.gameObject);
            Vector3 s = plate.lossyScale;
            box.center = plate.InverseTransformPoint(b.center);
            box.size = new Vector3(
                b.size.x / Mathf.Abs(s.x),
                Mathf.Max(b.size.y, 0.02f) / Mathf.Abs(s.y),
                b.size.z / Mathf.Abs(s.z));

            Debug.Log($"[Fix] {plateName} placed on table top (y={tableTop:F3}).");
        }
    }
}
#endif
