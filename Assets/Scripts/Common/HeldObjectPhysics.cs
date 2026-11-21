using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Keeps a grabbed object physical so it cannot be pushed through walls, counters, furniture or the stove.
///
/// XRGrabInteractable in Instantaneous mode makes the Rigidbody kinematic and teleports it to the hand, so nothing
/// can stop it. While this component's object is held it switches the interactable to VelocityTracking instead:
/// the Rigidbody stays dynamic, chases the hand with velocities and is blocked by static colliders. The original
/// movement type is restored on release, and ThrowOnDetach keeps working because XRI computes the throw itself.
///
/// While held, the object ignores the colliders of the player rig (CharacterController, hands) so a pan held close
/// to the body is not blocked by the player. After release, a pair is only re-enabled once the two no longer
/// overlap, so the dropped object is not shoved away by the player capsule.
///
/// <see cref="InstallAll"/> adds this component to every grabbable with a Rigidbody when a scene loads, so no
/// scene or prefab has to be edited.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(XRGrabInteractable))]
public class HeldObjectPhysics : MonoBehaviour
{
    [Header("Hand following (VelocityTracking)")]
    [Tooltip("Fraction of the current velocity removed each physics step. 1 = the hand fully drives the object (no overshoot).")]
    [SerializeField] float m_VelocityDamping = 1f;

    [Tooltip("How much of the distance to the hand is closed each physics step. 1 = reaches the hand in one step.")]
    [SerializeField] float m_VelocityScale = 1f;

    [Tooltip("Fraction of the current angular velocity removed each physics step.")]
    [SerializeField] float m_AngularVelocityDamping = 1f;

    [Tooltip("How much of the rotation to the hand is closed each physics step.")]
    [SerializeField] float m_AngularVelocityScale = 1f;

    [Tooltip("Top speed of the held body in m/s. When the hand is far from the object (it is blocked by a wall), VelocityTracking asks for a huge velocity that would launch loose objects the body touches. 0 = no limit.")]
    [SerializeField, Min(0f)] float m_MaxHeldSpeed = 6f;

    [Header("Rigidbody")]
    [Tooltip("Use ContinuousDynamic detection and Interpolate so thin objects (plates, knife, pan) do not tunnel and do not jitter.")]
    [SerializeField] bool m_RobustBody = true;

    // Original grab settings. Serialized so a clone made while the object is held (a sliced half) can restore them.
    [SerializeField, HideInInspector] bool m_HasOriginalMovement;
    [SerializeField, HideInInspector] XRBaseInteractable.MovementType m_OriginalMovement;

    struct IgnoredPair
    {
        public Collider Held;
        public Collider Rig;
    }

    XRGrabInteractable m_Grab;
    Rigidbody m_Body;
    bool m_Listening;
    bool m_Holding;
    XROrigin m_Rig;

    // Pairs ignored while held, and pairs kept ignored after release until the colliders stop overlapping.
    readonly List<IgnoredPair> m_Ignored = new List<IgnoredPair>();
    readonly List<IgnoredPair> m_PendingRestore = new List<IgnoredPair>();

    /// <summary>True while the object is selected and driven by VelocityTracking.</summary>
    public bool IsHeld => m_Holding;

    /// <summary>
    /// Adds <see cref="HeldObjectPhysics"/> to every grabbable in the loaded scenes that has a Rigidbody that is
    /// dynamic at rest and moves Instantaneous or Kinematic. Objects without a Rigidbody (knobs) are skipped.
    /// </summary>
    /// <returns>How many components were added.</returns>
    public static int InstallAll()
    {
        var grabbables = Object.FindObjectsByType<XRGrabInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int added = 0;
        for (int i = 0; i < grabbables.Length; i++)
        {
            if (TryInstall(grabbables[i])) added++;
        }
        return added;
    }

    /// <summary>
    /// Adds <see cref="HeldObjectPhysics"/> to one grabbable, using the same rules as <see cref="InstallAll"/>:
    /// it needs a Rigidbody that is dynamic at rest and an Instantaneous or Kinematic movement type, and it must
    /// not have the component yet. Used for grabbables created at runtime (for example the fridge stock slots),
    /// which the scene-load pass does not cover.
    /// </summary>
    /// <returns>True if the component was added.</returns>
    public static bool TryInstall(XRGrabInteractable grab)
    {
        if (grab == null || grab.GetComponent<HeldObjectPhysics>() != null) return false;

        var body = grab.GetComponent<Rigidbody>();
        if (body == null || body.isKinematic) return false;

        var type = grab.movementType;
        if (type != XRBaseInteractable.MovementType.Instantaneous && type != XRBaseInteractable.MovementType.Kinematic) return false;

        grab.gameObject.AddComponent<HeldObjectPhysics>();
        return true;
    }

    // Covers the first scene and every scene loaded later (additive loads, scene switches).
    // Grabbables instantiated at runtime outside a scene load are not covered; clones of installed objects are.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void InstallOnSceneLoad()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded; // stays safe with Domain Reload disabled
        SceneManager.sceneLoaded += OnSceneLoaded;
        InstallAll();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => InstallAll();

    void Awake()
    {
        m_Grab = GetComponent<XRGrabInteractable>();
        m_Body = GetComponent<Rigidbody>();

        // A clone made while the original was held starts with VelocityTracking but is not held itself.
        if (m_HasOriginalMovement && !m_Grab.isSelected)
        {
            m_Grab.movementType = m_OriginalMovement;
            m_HasOriginalMovement = false;
        }

        if (m_RobustBody && !m_Body.isKinematic)
        {
            m_Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            m_Body.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    void OnEnable()
    {
        if (m_Grab == null) m_Grab = GetComponent<XRGrabInteractable>();
        m_Grab.selectEntered.AddListener(OnSelectEntered);
        m_Grab.selectExited.AddListener(OnSelectExited);
        m_Listening = true;
    }

    void OnDisable()
    {
        if (m_Listening && m_Grab != null)
        {
            m_Grab.selectEntered.RemoveListener(OnSelectEntered);
            m_Grab.selectExited.RemoveListener(OnSelectExited);
        }
        m_Listening = false;

        if (m_Holding) EndHold();
        RestoreAllCollisions();
    }

    void FixedUpdate()
    {
        // XRInteractionManager (execution order -105) has already applied the hand-following velocity this step,
        // so limiting it here takes effect before the physics simulation runs.
        if (m_Holding && m_MaxHeldSpeed > 0f && !m_Body.isKinematic)
            m_Body.linearVelocity = Vector3.ClampMagnitude(m_Body.linearVelocity, m_MaxHeldSpeed);

        if (m_PendingRestore.Count == 0) return;

        // Re-enable a pair once the two colliders no longer overlap.
        for (int i = m_PendingRestore.Count - 1; i >= 0; i--)
        {
            var pair = m_PendingRestore[i];
            if (pair.Held == null || pair.Rig == null)
            {
                m_PendingRestore.RemoveAt(i);
                continue;
            }

            if (pair.Held.bounds.Intersects(pair.Rig.bounds)) continue;

            Physics.IgnoreCollision(pair.Held, pair.Rig, false);
            m_PendingRestore.RemoveAt(i);
        }
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (m_Holding) return; // second hand: already set up

        m_Holding = true;

        // Physics driven grab: the body stays dynamic, so static colliders stop it.
        m_OriginalMovement = m_Grab.movementType;
        m_HasOriginalMovement = true;

        m_Grab.velocityDamping = m_VelocityDamping;
        m_Grab.velocityScale = m_VelocityScale;
        m_Grab.angularVelocityDamping = m_AngularVelocityDamping;
        m_Grab.angularVelocityScale = m_AngularVelocityScale;
        m_Grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;

        if (m_RobustBody && !m_Body.isKinematic)
        {
            m_Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            m_Body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        IgnoreRig(args.interactorObject != null ? args.interactorObject.transform : null);
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        if (!m_Holding || m_Grab.isSelected) return; // still held by another hand
        EndHold();
    }

    void EndHold()
    {
        m_Holding = false;

        if (m_HasOriginalMovement)
        {
            m_Grab.movementType = m_OriginalMovement;
            m_HasOriginalMovement = false;
        }

        // Re-enable rig collisions now unless the object still overlaps the rig.
        for (int i = 0; i < m_Ignored.Count; i++)
        {
            var pair = m_Ignored[i];
            if (pair.Held == null || pair.Rig == null) continue;

            if (pair.Held.bounds.Intersects(pair.Rig.bounds))
                m_PendingRestore.Add(pair);
            else
                Physics.IgnoreCollision(pair.Held, pair.Rig, false);
        }
        m_Ignored.Clear();
    }

    void IgnoreRig(Transform interactor)
    {
        m_Rig = interactor != null ? interactor.GetComponentInParent<XROrigin>() : null;
        if (m_Rig == null) m_Rig = Object.FindFirstObjectByType<XROrigin>();
        if (m_Rig == null) return;

        var heldColliders = GetComponentsInChildren<Collider>(false);
        var rigColliders = m_Rig.GetComponentsInChildren<Collider>(false);

        for (int h = 0; h < heldColliders.Length; h++)
        {
            var held = heldColliders[h];
            if (!IsSolid(held)) continue;

            for (int r = 0; r < rigColliders.Length; r++)
            {
                var rig = rigColliders[r];
                if (!IsSolid(rig) || rig.transform.IsChildOf(transform)) continue;

                Physics.IgnoreCollision(held, rig, true);
                var pair = new IgnoredPair { Held = held, Rig = rig };
                m_Ignored.Add(pair);

                // A pair that was waiting to be restored from an earlier release is held again.
                for (int p = m_PendingRestore.Count - 1; p >= 0; p--)
                {
                    if (m_PendingRestore[p].Held == held && m_PendingRestore[p].Rig == rig)
                        m_PendingRestore.RemoveAt(p);
                }
            }
        }
    }

    // Triggers and disabled colliders never block anything, so they are left alone.
    static bool IsSolid(Collider collider) => collider != null && collider.enabled && !collider.isTrigger;

    void RestoreAllCollisions()
    {
        for (int i = 0; i < m_Ignored.Count; i++)
            if (m_Ignored[i].Held != null && m_Ignored[i].Rig != null)
                Physics.IgnoreCollision(m_Ignored[i].Held, m_Ignored[i].Rig, false);
        m_Ignored.Clear();

        for (int i = 0; i < m_PendingRestore.Count; i++)
            if (m_PendingRestore[i].Held != null && m_PendingRestore[i].Rig != null)
                Physics.IgnoreCollision(m_PendingRestore[i].Held, m_PendingRestore[i].Rig, false);
        m_PendingRestore.Clear();
    }
}
