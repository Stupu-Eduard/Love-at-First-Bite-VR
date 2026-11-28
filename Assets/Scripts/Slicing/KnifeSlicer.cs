using System.Collections.Generic;
using UnityEngine;
using Hanzzz.MeshSlicerFree;

namespace LoveAtFirstBite.Slicing
{
    /// <summary>
    /// Put this on the knife. When the knife moves fast enough and its trigger passes
    /// through an object marked <see cref="Sliceable"/>, the object is cut in two along
    /// the knife's blade plane.
    ///
    /// Setup (see the walkthrough): the knife needs a trigger Collider roughly matching
    /// the blade, a Rigidbody, and the blade's flat side must face this object's local
    /// RIGHT (+X) so the slice plane lines up with the edge. Adjust <see cref="bladeNormalAxis"/>
    /// if your blade faces a different local axis.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class KnifeSlicer : MonoBehaviour
    {
        public enum Axis { X, Y, Z }

        [Header("Swipe detection")]
        [Tooltip("Require a fast swipe to cut. Turn OFF to cut on any contact (useful for editor demos where the simulator can't produce a fast swing).")]
        public bool requireSwipe = true;
        [Tooltip("Minimum knife speed (metres/second) to count as a cutting swipe. Slower contact is ignored.")]
        public float minSwipeSpeed = 1.2f;
        [Tooltip("Log to the Console why a cut was or wasn't triggered. Turn off for the final build.")]
        public bool debugLog = true;

        // Speed measured from the knife's own movement (reliable in the simulator, unlike grabbed-Rigidbody velocity).
        Vector3 m_lastPos;
        float m_measuredSpeed;

        [Header("Blade orientation")]
        [Tooltip("Which local axis of the knife points along the FLAT of the blade (the slice-plane normal). For a blade lying in the knife's X-Z plane, this is usually X.")]
        public Axis bladeNormalAxis = Axis.X;

        [Header("Cut result")]
        [Tooltip("Material applied to the freshly exposed cut face. Leave empty to reuse the food's own material.")]
        public Material cutFaceMaterial;

        readonly MeshSlicer m_slicer = new MeshSlicer();
        Rigidbody m_rb;
        bool m_busy; // one slice at a time; SliceAsync runs across frames
        float m_cooldownUntil; // ignore cuts briefly after one, so a single swipe doesn't chain-cut the fresh halves

        void Awake()
        {
            m_rb = GetComponent<Rigidbody>();
            m_lastPos = transform.position;
        }

        void FixedUpdate()
        {
            // Measure the knife's own speed from how far it moved this physics step.
            float dt = Time.fixedDeltaTime;
            if (dt > 0f)
                m_measuredSpeed = (transform.position - m_lastPos).magnitude / dt;
            m_lastPos = transform.position;
        }

        void OnTriggerEnter(Collider other) => TryCut(other);
        void OnTriggerStay(Collider other)  => TryCut(other);

        void TryCut(Collider other)
        {
            if (m_busy) return;
            if (Time.time < m_cooldownUntil) return;

            // Find the Sliceable on the thing we touched (on it or a parent).
            var sliceable = other.GetComponentInParent<Sliceable>();
            if (sliceable == null) return;
            if (sliceable.cutsSoFar >= sliceable.maxCuts)
            {
                if (debugLog) Debug.Log($"[KnifeSlicer] '{sliceable.name}' reached its cut limit ({sliceable.maxCuts}).", sliceable);
                return;
            }

            // Speed gate: only a fast swipe cuts (unless requireSwipe is off).
            if (requireSwipe && m_measuredSpeed < minSwipeSpeed)
            {
                if (debugLog) Debug.Log($"[KnifeSlicer] Touched '{sliceable.name}' but too slow to cut: {m_measuredSpeed:F2} m/s (need {minSwipeSpeed}). Swipe faster or lower Min Swipe Speed.", sliceable);
                return;
            }

            if (debugLog) Debug.Log($"[KnifeSlicer] Cutting '{sliceable.name}' at {m_measuredSpeed:F2} m/s.", sliceable);
            m_busy = true; // set synchronously so OnTriggerStay can't start a second slice before the await
            m_cooldownUntil = Time.time + 0.5f;
            TrySlice(sliceable);
        }

        async void TrySlice(Sliceable sliceable)
        {
            var target = sliceable.gameObject;

            // The slicer needs a MeshFilter with a readable mesh. Find it on the object or a child.
            var mf = target.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
            {
                Debug.LogWarning($"[KnifeSlicer] '{target.name}' has no MeshFilter/mesh to slice.", target);
                m_busy = false;
                return;
            }
            if (!mf.sharedMesh.isReadable)
            {
                Debug.LogWarning($"[KnifeSlicer] Mesh '{mf.sharedMesh.name}' on '{target.name}' is not Read/Write enabled. " +
                                 "Run Tools > Love at First Bite > Make Ingredient Meshes Readable.", target);
                m_busy = false;
                return;
            }

            // Slice object is whichever GameObject actually holds the MeshFilter.
            var sliceObject = mf.gameObject;

            // Capture the slice object's world pose so the halves can be placed exactly there
            // (the clone's inherited LOCAL transform can land at the wrong spot, e.g. world origin,
            // when the mesh sits on a nested-prefab child).
            Vector3 worldPos = sliceObject.transform.position;
            Quaternion worldRot = sliceObject.transform.rotation;
            Vector3 worldScale = sliceObject.transform.lossyScale;

            // Build the slice plane: origin = knife position, normal = knife's blade axis.
            Vector3 normal = bladeNormalAxis switch
            {
                Axis.X => transform.right,
                Axis.Y => transform.up,
                _      => transform.forward,
            };
            var plane = new Plane(normal, transform.position);
            (Vector3, Vector3, Vector3) planePoints = ThreePointsOnPlane(plane);

            (GameObject top, GameObject bottom) result;
            try
            {
                result = await m_slicer.SliceAsync(sliceObject, planePoints, cutFaceMaterial);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[KnifeSlicer] Slice failed on '{target.name}': {e.Message}", target);
                m_busy = false;
                return;
            }
            m_busy = false;

            if (result.top == null || result.bottom == null)
            {
                // Plane missed the mesh (e.g. glancing edge contact). Not an error.
                return;
            }

            SetUpHalf(result.top, sliceable, normal, worldPos, worldRot, worldScale);
            SetUpHalf(result.bottom, sliceable, -normal, worldPos, worldRot, worldScale);

            if (debugLog)
            {
                LogHalf(result.top);
                LogHalf(result.bottom);
            }

            // Remove the original uncut object.
            Destroy(target);
        }

        void LogHalf(GameObject half)
        {
            var mr = half.GetComponentInChildren<MeshRenderer>();
            var mf = half.GetComponentInChildren<MeshFilter>();
            int verts = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.vertexCount : -1;
            var grab = half.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>(true);
            var col = half.GetComponentInChildren<Collider>(true);
            string grabInfo = grab == null ? "NO GRAB"
                : $"grab on '{grab.gameObject.name}' enabled={grab.enabled} colliders={grab.colliders.Count} mgr={(grab.interactionManager != null)} layer={grab.gameObject.layer}";
            string colInfo = col == null ? "NO COLLIDER"
                : $"collider={col.GetType().Name} on '{col.gameObject.name}' enabled={col.enabled} isTrigger={col.isTrigger} convex={(col is MeshCollider m ? m.convex.ToString() : "n/a")}";
            Debug.Log($"[KnifeSlicer] Half '{half.name}': active={half.activeInHierarchy} pos={half.transform.position} " +
                      $"scale={half.transform.lossyScale} renderer={(mr != null && mr.enabled)} verts={verts}\n    {grabInfo}\n    {colInfo}", half);
        }

        void SetUpHalf(GameObject halfMesh, Sliceable original, Vector3 pushDir, Vector3 worldPos, Quaternion worldRot, Vector3 worldScale)
        {
            // halfMesh is the bare sliced clone (mesh only, missing the ingredient's root
            // components when the mesh lived on a child). Build the real piece by cloning the
            // ORIGINAL ingredient root (which carries XRGrabInteractable, Cookable, Sliceable,
            // Rigidbody with their exact settings) and transplanting the sliced mesh into it.
            var piece = Instantiate(original.gameObject);
            piece.name = original.name + "_Half";
            piece.SetActive(true);
            piece.transform.SetParent(original.transform.parent, true);
            piece.transform.position = worldPos;
            piece.transform.rotation = worldRot;

            // Nudge each half a hair along the cut normal so the two halves' colliders don't
            // spawn perfectly overlapping (which makes physics violently shove them apart).
            // This is a position offset, not a force, so the pieces stay put instead of jumping.
            piece.transform.position += pushDir.normalized * 0.005f;

            // Match the original's world scale (convert to local, accounting for the parent).
            Transform p = piece.transform.parent;
            if (p != null)
            {
                Vector3 ps = p.lossyScale;
                piece.transform.localScale = new Vector3(
                    ps.x != 0f ? worldScale.x / ps.x : worldScale.x,
                    ps.y != 0f ? worldScale.y / ps.y : worldScale.y,
                    ps.z != 0f ? worldScale.z / ps.z : worldScale.z);
            }
            else piece.transform.localScale = worldScale;

            // Transplant the sliced mesh (+ its material) from the bare half onto the piece's mesh renderer.
            var slicedMf = halfMesh.GetComponentInChildren<MeshFilter>();
            var slicedMr = halfMesh.GetComponentInChildren<MeshRenderer>();
            var pieceMf = piece.GetComponentInChildren<MeshFilter>();
            var pieceMr = piece.GetComponentInChildren<MeshRenderer>();
            if (slicedMf != null && pieceMf != null)
                pieceMf.sharedMesh = slicedMf.sharedMesh;
            if (slicedMr != null && pieceMr != null)
                pieceMr.sharedMaterials = slicedMr.sharedMaterials;

            // The bare sliced clone is no longer needed.
            Destroy(halfMesh);

            // Rebuild the collider on whichever object holds the mesh, to fit the new half mesh.
            var meshGO = pieceMf != null ? pieceMf.gameObject : piece;
            foreach (var c in piece.GetComponentsInChildren<Collider>(true))
                Destroy(c);

            Collider halfCollider = null;
            if (pieceMf != null && pieceMf.sharedMesh != null)
            {
                try
                {
                    var mc = meshGO.AddComponent<MeshCollider>();
                    mc.sharedMesh = pieceMf.sharedMesh;
                    mc.convex = true;
                    halfCollider = mc;
                }
                catch { halfCollider = null; }
            }
            if (halfCollider == null)
            {
                var box = meshGO.AddComponent<BoxCollider>();
                if (pieceMf != null && pieceMf.sharedMesh != null)
                {
                    box.center = pieceMf.sharedMesh.bounds.center;
                    box.size = pieceMf.sharedMesh.bounds.size;
                }
                halfCollider = box;
            }

            // Ensure a dynamic Rigidbody (clone already has one; make sure it's set up for falling/physics).
            var rb = piece.GetComponent<Rigidbody>();
            if (rb == null) rb = piece.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            // Point the (inherited) grab interactable at the new collider so grabbing works.
            var grab = piece.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>(true);
            if (grab != null)
            {
                grab.colliders.Clear();
                grab.colliders.Add(halfCollider);
                grab.trackScale = false;

                // XRI registers an interactable's colliders with the Interaction Manager during
                // OnEnable. Since we changed the collider list after the clone was enabled, toggle
                // the component so it re-registers the new collider; otherwise the manager's
                // collider->interactable lookup misses it and the piece can't be grabbed.
                if (grab.isActiveAndEnabled)
                {
                    grab.enabled = false;
                    grab.enabled = true;
                }
            }

            // Carry the cook state forward so a half cut from a cooked item stays cooked
            // (Cookable's state is not serialized, so the clone would otherwise reset to Raw).
            var originalCookable = original.GetComponent<Cookable>();
            var pieceCookable = piece.GetComponent<Cookable>();
            if (originalCookable != null && pieceCookable != null)
                pieceCookable.AdoptState(originalCookable.State, originalCookable.HeatSeconds);

            // Carry the cut budget forward so halves can be cut again up to the limit.
            var s = piece.GetComponent<Sliceable>();
            if (s == null) s = piece.AddComponent<Sliceable>();
            s.maxCuts = original.maxCuts;
            s.cutsSoFar = original.cutsSoFar + 1;

            // Make the halves rest in place like a normal ingredient: no launch impulse, and
            // clear any inherited/spawn velocity so they don't jump when the collider appears.
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Turn a Unity Plane into three points on it, matching the MeshSlicer sample.
        static (Vector3, Vector3, Vector3) ThreePointsOnPlane(Plane p)
        {
            Vector3 n = p.normal;
            Vector3 xAxis;
            if (n.x != 0f) xAxis = new Vector3(-n.y / n.x, 1f, 0f);
            else if (n.y != 0f) xAxis = new Vector3(0f, -n.z / n.y, 1f);
            else xAxis = new Vector3(1f, 0f, -n.x / n.z);
            Vector3 yAxis = Vector3.Cross(n, xAxis);
            Vector3 origin = -p.distance * n;
            return (origin, origin + xAxis, origin + yAxis);
        }
    }
}
