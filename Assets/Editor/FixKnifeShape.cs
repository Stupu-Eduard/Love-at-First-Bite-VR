#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Fixes the Knife going flat/2D when grabbed and thrown.
///
/// Cause: the grabbable "Knife" root is a flattened built-in Cube with a near-degenerate,
/// non-uniform scale (~0.25, 0.01, 0.03). The real knife mesh is a child given a huge
/// compensating scale (~98, 33, 4) plus a ~90 deg rotation so it only looks right at rest.
/// When thrown, the Rigidbody rotates it, and a non-uniform parent scale shears/flattens the
/// rotated child. XRGrabInteractable.TrackScale then keeps re-applying the flat scale.
///
/// Fix: rebuild the grabbable so its root is UNIFORMLY scaled. The real Knife.prefab mesh
/// becomes the visual child at uniform scale with no compensation, the proxy Cube mesh is
/// removed, the BoxCollider is fit to the real mesh bounds, and TrackScale is turned off.
///
/// Menu: Tools > Love at First Bite > Fix Knife Shape
/// </summary>
public static class FixKnifeShape
{
    // Target overall length of the knife along its longest axis, in metres.
    const float TargetLongestExtent = 0.22f;

    [MenuItem("Tools/Love at First Bite/Fix Knife Shape")]
    public static void Fix()
    {
        var knife = GameObject.Find("Knife");
        if (knife == null)
        {
            Debug.LogError("[Knife] No GameObject named 'Knife' found. Open SampleScene first.");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(knife, "Fix Knife Shape");
        var root = knife.transform;

        // 1) Make the grabbable root uniformly scaled. This removes the shear source.
        root.localScale = Vector3.one;

        // 2) Remove the built-in Cube proxy mesh on the root (it is not the knife, just a hull).
        var rootMf = knife.GetComponent<MeshFilter>();
        if (rootMf != null) Undo.DestroyObjectImmediate(rootMf);
        var rootMr = knife.GetComponent<MeshRenderer>();
        if (rootMr != null) Undo.DestroyObjectImmediate(rootMr);

        // 3) Normalise the visual child (the real Knife.prefab model): uniform scale, identity-ish
        //    local rotation, so rotation of the parent no longer shears it.
        Transform model = null;
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (c.GetComponentInChildren<MeshFilter>() != null) { model = c; break; }
        }
        if (model != null)
        {
            Undo.RecordObject(model, "Normalise knife model");
            model.localScale = Vector3.one;
            model.localRotation = Quaternion.identity;
            model.localPosition = Vector3.zero;

            // The model child must be VISUAL ONLY. Remove any grab/physics/collider
            // components on it (added by earlier fix passes) so the grabbable lives on
            // the root alone. Two interactables sharing colliders cause the
            // "collider already registered with another Interactable" warning.
            foreach (var g in model.GetComponentsInChildren<XRGrabInteractable>(true))
                Undo.DestroyObjectImmediate(g);
            foreach (var r in model.GetComponentsInChildren<Rigidbody>(true))
                Undo.DestroyObjectImmediate(r);
            foreach (var c in model.GetComponentsInChildren<Collider>(true))
                Undo.DestroyObjectImmediate(c);
        }
        else
        {
            Debug.LogWarning("[Knife] No visual child mesh found; proceeding with collider fit only.");
        }

        Physics.SyncTransforms();

        // 4) Measure the real mesh bounds in the ROOT's local space (uniform scale now).
        var renderers = knife.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogError("[Knife] No renderer under Knife after rebuild; cannot fit collider.");
            return;
        }

        // Rescale the model so the knife has a sensible real-world size.
        Bounds worldB = renderers[0].bounds;
        foreach (var r in renderers) worldB.Encapsulate(r.bounds);
        float longest = Mathf.Max(worldB.size.x, Mathf.Max(worldB.size.y, worldB.size.z));
        if (model != null && longest > 1e-4f)
        {
            float factor = TargetLongestExtent / longest;
            model.localScale = Vector3.one * factor;
            Physics.SyncTransforms();
            worldB = renderers[0].bounds;
            foreach (var r in renderers) worldB.Encapsulate(r.bounds);
        }

        // 5) Fit a single BoxCollider on the root to the mesh bounds, expressed in root local space.
        foreach (var extra in knife.GetComponents<BoxCollider>())
            Undo.DestroyObjectImmediate(extra);
        var box = Undo.AddComponent<BoxCollider>(knife);
        Vector3 s = root.lossyScale; // (1,1,1) now, but stay correct if parent scales
        box.center = root.InverseTransformPoint(worldB.center);
        box.size = new Vector3(
            worldB.size.x / Mathf.Abs(s.x),
            worldB.size.y / Mathf.Abs(s.y),
            worldB.size.z / Mathf.Abs(s.z));

        // 6) Rigidbody: dynamic, sensible for a thrown knife.
        var rb = knife.GetComponent<Rigidbody>();
        if (rb == null) rb = Undo.AddComponent<Rigidbody>(knife);
        Undo.RecordObject(rb, "Knife rigidbody");
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.mass = 0.3f;

        // 7) XRGrabInteractable: keep it, but stop it from tracking/re-applying scale.
        var grab = knife.GetComponent<XRGrabInteractable>();
        if (grab == null) grab = Undo.AddComponent<XRGrabInteractable>(knife);
        Undo.RecordObject(grab, "Knife grab settings");
        grab.trackScale = false;
        grab.trackPosition = true;
        grab.trackRotation = true;

        EditorSceneManager.MarkSceneDirty(knife.scene);
        Selection.activeGameObject = knife;
        Debug.Log("[Knife] Rebuilt: uniform scale, real mesh visual, fitted collider, TrackScale off. Save with Ctrl+S and test a throw.");
    }
}
#endif
