using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Settles the pan onto a burner when the player lets go of it close to one.
///
/// When the pan is not held, nearly still, roughly upright and within reach of a BurnerZone, it glides to the
/// burner in a short time: its base ends up centered on the burner, level, resting on the real surface.
/// The yaw (which way the handle points) is kept. Grabbing the pan cancels the move at once.
///
/// Only the Rigidbody pose is changed. isKinematic is not touched, so XRGrabInteractable cannot undo anything.
/// The pan origin must be the center of its base, as in Pan.prefab.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class PanSnap : MonoBehaviour
{
    const int MaxRayHits = 16;

    [Header("When to snap")]
    [Tooltip("Max horizontal distance (m) between the pan center and the burner center.")]
    [SerializeField] float m_CaptureRadius = 0.15f;

    [Tooltip("Max height (m) of the pan base above the burner rest point.")]
    [SerializeField] float m_MaxHeightAbove = 0.25f;

    [Tooltip("Max depth (m) of the pan base below the burner rest point.")]
    [SerializeField] float m_MaxHeightBelow = 0.05f;

    [Tooltip("The pan only snaps when slower than this (m/s). A thrown pan is left alone.")]
    [SerializeField] float m_MaxSpeed = 1.5f;

    [Tooltip("The pan only snaps when it spins slower than this (rad/s).")]
    [SerializeField] float m_MaxAngularSpeed = 6f;

    [Tooltip("The pan only snaps when tilted less than this many degrees from upright.")]
    [SerializeField] float m_MaxTilt = 40f;

    [Tooltip("Seconds the pan must have been released before it can snap. Lets the throw velocity of the grab settle.")]
    [SerializeField] float m_SettleDelay = 0.1f;

    [Header("How it snaps")]
    [Tooltip("Seconds the move takes.")]
    [SerializeField] float m_Duration = 0.15f;

    [Tooltip("Colliders this far (m) from the burner rest height count as the burner surface.")]
    [SerializeField] float m_SurfaceTolerance = 0.06f;

    [Tooltip("Gap (m) left between the pan base and the surface so the two colliders do not start overlapping.")]
    [SerializeField] float m_Clearance = 0.002f;

    Rigidbody m_Body;
    XRBaseInteractable m_Interactable;
    readonly RaycastHit[] m_RayHits = new RaycastHit[MaxRayHits];

    float m_ReleasedFor;
    float m_Elapsed;
    Vector3 m_StartPosition;
    Quaternion m_StartRotation;
    Vector3 m_TargetPosition;
    Quaternion m_TargetRotation;

    bool m_Resting;
    Vector3 m_RestingPosition;
    Quaternion m_RestingRotation;

    /// <summary>True while the pan is gliding to a burner.</summary>
    public bool IsSnapping { get; private set; }

    /// <summary>The burner the pan last settled on, until it is grabbed or knocked away. Null otherwise.</summary>
    public BurnerZone SnappedZone { get; private set; }

    Rigidbody Body
    {
        get
        {
            if (m_Body == null) m_Body = GetComponent<Rigidbody>();
            return m_Body;
        }
    }

    void Awake()
    {
        m_Interactable = GetComponent<XRBaseInteractable>();
    }

    void OnDisable() => ResetState();

    void FixedUpdate()
    {
        if (m_Interactable != null && m_Interactable.isSelected)
        {
            ResetState();
            return;
        }

        m_ReleasedFor += Time.fixedDeltaTime;

        if (IsSnapping)
        {
            Glide();
            return;
        }

        if (m_ReleasedFor < m_SettleDelay || Body.isKinematic) return;

        if (m_Resting)
        {
            // Stay put until somebody grabs or knocks the pan.
            bool moved = Vector3.Distance(Body.position, m_RestingPosition) > 0.02f
                         || Quaternion.Angle(Body.rotation, m_RestingRotation) > 5f;
            if (!moved) return;
            m_Resting = false;
            SnappedZone = null;
        }

        if (!IsCalm()) return;
        if (!TryFindTarget(out var zone, out var position, out var rotation)) return;

        Begin(zone, position, rotation);
    }

    void Begin(BurnerZone zone, Vector3 position, Quaternion rotation)
    {
        m_StartPosition = Body.position;
        m_StartRotation = Body.rotation;
        m_TargetPosition = position;
        m_TargetRotation = rotation;
        m_Elapsed = 0f;
        SnappedZone = zone;
        IsSnapping = true;
        Glide();
    }

    void Glide()
    {
        m_Elapsed += Time.fixedDeltaTime;
        float t = m_Duration <= 0f ? 1f : Mathf.Clamp01(m_Elapsed / m_Duration);
        float eased = t * t * (3f - 2f * t);

        Body.MovePosition(Vector3.Lerp(m_StartPosition, m_TargetPosition, eased));
        Body.MoveRotation(Quaternion.Slerp(m_StartRotation, m_TargetRotation, eased));
        Body.linearVelocity = Vector3.zero;
        Body.angularVelocity = Vector3.zero;

        if (t < 1f) return;

        IsSnapping = false;
        m_Resting = true;
        m_RestingPosition = m_TargetPosition;
        m_RestingRotation = m_TargetRotation;
    }

    void ResetState()
    {
        IsSnapping = false;
        m_Resting = false;
        SnappedZone = null;
        m_ReleasedFor = 0f;
    }

    bool IsCalm()
    {
        return Body.linearVelocity.magnitude <= m_MaxSpeed
               && Body.angularVelocity.magnitude <= m_MaxAngularSpeed
               && Vector3.Angle(Body.rotation * Vector3.up, Vector3.up) <= m_MaxTilt;
    }

    bool TryFindTarget(out BurnerZone zone, out Vector3 position, out Quaternion rotation)
    {
        zone = null;
        position = default;
        rotation = default;

        var origin = Body.position;
        float bestDistance = float.MaxValue;

        var zones = BurnerZone.Active;
        for (int i = 0; i < zones.Count; i++)
        {
            var candidate = zones[i];
            if (candidate == null) continue;

            var offset = origin - candidate.RestPosition;
            if (offset.y > m_MaxHeightAbove || offset.y < -m_MaxHeightBelow) continue;

            float distance = new Vector2(offset.x, offset.z).magnitude;
            if (distance > m_CaptureRadius || distance >= bestDistance) continue;

            bestDistance = distance;
            zone = candidate;
        }

        if (zone == null) return false;

        var rest = zone.RestPosition;
        position = new Vector3(rest.x, FindSurfaceHeight(rest) + m_Clearance, rest.z);
        rotation = LevelRotation(Body.rotation);
        return true;
    }

    // Height of the real surface under the burner. Falls back to the rest height of the zone when no collider is found.
    // Free rigidbodies (this pan, food, the knife) are not a surface.
    float FindSurfaceHeight(Vector3 rest)
    {
        float reach = 0.3f;
        int count = Physics.RaycastNonAlloc(
            rest + Vector3.up * reach, Vector3.down, m_RayHits, reach + m_SurfaceTolerance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        float best = rest.y;
        float bestError = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var hit = m_RayHits[i];
            if (hit.rigidbody != null && !hit.rigidbody.isKinematic) continue;

            float error = Mathf.Abs(hit.point.y - rest.y);
            if (error > m_SurfaceTolerance || error >= bestError) continue;

            bestError = error;
            best = hit.point.y;
        }
        return best;
    }

    // Upright rotation that keeps the direction the handle points to.
    static Quaternion LevelRotation(Quaternion current)
    {
        var forward = Vector3.ProjectOnPlane(current * Vector3.forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(current * Vector3.up, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f) return Quaternion.identity;
        return Quaternion.LookRotation(forward.normalized, Vector3.up);
    }
}
