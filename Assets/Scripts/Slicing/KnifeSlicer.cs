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
        [Tooltip("Minimum knife speed (metres/second) to count as a cutting swipe. Slower contact is ignored.")]
        public float minSwipeSpeed = 1.2f;

        [Header("Blade orientation")]
        [Tooltip("Which local axis of the knife points along the FLAT of the blade (the slice-plane normal). For a blade lying in the knife's X-Z plane, this is usually X.")]
        public Axis bladeNormalAxis = Axis.X;

        [Header("Cut result")]
        [Tooltip("Material applied to the freshly exposed cut face. Leave empty to reuse the food's own material.")]
        public Material cutFaceMaterial;
        [Tooltip("Push the two halves apart by this impulse so the cut reads clearly.")]
        public float separationImpulse = 0.6f;

        readonly MeshSlicer m_slicer = new MeshSlicer();
        Rigidbody m_rb;
        bool m_busy; // one slice at a time; SliceAsync runs across frames

        void Awake()
        {
            m_rb = GetComponent<Rigidbody>();
        }

        void OnTriggerEnter(Collider other)
        {
            if (m_busy) return;

            // Speed gate: only a fast swipe cuts.
            if (m_rb.linearVelocity.magnitude < minSwipeSpeed) return;

            // Find the Sliceable on the thing we touched (on it or a parent).
            var sliceable = other.GetComponentInParent<Sliceable>();
            if (sliceable == null) return;
            if (sliceable.cutsSoFar >= sliceable.maxCuts) return;

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
                return;
            }
            if (!mf.sharedMesh.isReadable)
            {
                Debug.LogWarning($"[KnifeSlicer] Mesh '{mf.sharedMesh.name}' on '{target.name}' is not Read/Write enabled. " +
                                 "Run Tools > Love at First Bite > Make Ingredient Meshes Readable.", target);
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

            m_busy = true;
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

            // Remove the original uncut object.
            Destroy(target);
        }

        void SetUpHalf(GameObject half, Sliceable original, Vector3 pushDir)
        {
            half.transform.SetParent(original.transform.parent, true);

            // Fresh physics on each half.
            foreach (var c in half.GetComponentsInChildren<Collider>())
                Destroy(c);
            var mc = half.GetComponent<MeshCollider>();
            if (mc == null) mc = half.AddComponent<MeshCollider>();
            mc.convex = true; // required for a non-kinematic Rigidbody

            var rb = half.GetComponent<Rigidbody>();
            if (rb == null) rb = half.AddComponent<Rigidbody>();
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            // Carry the cut budget forward so halves can be cut again up to the limit.
            var s = half.GetComponent<Sliceable>();
            if (s == null) s = half.AddComponent<Sliceable>();
            s.maxCuts = original.maxCuts;
            s.cutsSoFar = original.cutsSoFar + 1;

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
