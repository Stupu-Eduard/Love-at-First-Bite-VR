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
        [Tooltip("Push the two halves apart by this impulse so the cut reads clearly.")]
        public float separationImpulse = 0.4f;

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

            SetUpHalf(result.top, sliceable, normal);
            SetUpHalf(result.bottom, sliceable, -normal);

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
            Debug.Log($"[KnifeSlicer] Half '{half.name}': active={half.activeInHierarchy} pos={half.transform.position} " +
                      $"scale={half.transform.lossyScale} renderer={(mr != null && mr.enabled)} verts={verts} parent={(half.transform.parent ? half.transform.parent.name : "none")}", half);
        }

        void SetUpHalf(GameObject half, Sliceable original, Vector3 pushDir)
        {
            half.name = original.name + "_Half";
            half.SetActive(true);

            // Keep the half in the same place in the hierarchy as the original.
            half.transform.SetParent(original.transform.parent, true);

            // The half is a CLONE of the original, so it already has the Cookable and
            // XRGrabInteractable. We keep those so halves stay grabbable and cookable.
            // Only the collider must be rebuilt, because the old one doesn't match the new mesh.
            foreach (var c in half.GetComponentsInChildren<Collider>(true))
                Destroy(c);

            // Build a convex collider that fits the new half mesh (box fallback if cooking fails).
            var newMf = half.GetComponentInChildren<MeshFilter>();
            Collider halfCollider = null;
            if (newMf != null && newMf.sharedMesh != null)
            {
                var mc = half.AddComponent<MeshCollider>();
                mc.sharedMesh = newMf.sharedMesh;
                mc.convex = true;
                halfCollider = mc;
            }
            if (halfCollider == null)
                halfCollider = half.AddComponent<BoxCollider>();

            var rb = half.GetComponent<Rigidbody>();
            if (rb == null) rb = half.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            // Re-point the grab interactable's collider list at the new collider so grabbing works.
            var grab = half.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if (grab != null)
            {
                grab.colliders.Clear();
                grab.colliders.Add(halfCollider);
                grab.trackScale = false;
            }

            // Carry the cut budget forward so halves can be cut again up to the limit.
            var s = half.GetComponent<Sliceable>();
            if (s == null) s = half.AddComponent<Sliceable>();
            s.maxCuts = original.maxCuts;
            s.cutsSoFar = original.cutsSoFar + 1;

            // Nudge the halves apart so the cut reads and they don't overlap-jam.
            rb.AddForce(pushDir * separationImpulse, ForceMode.Impulse);
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
