using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Rotary stove knob. The player grabs it and twists it around an axis. It snaps to three detents:
/// Off (0 deg), Low (60 deg), High (120 deg), with hysteresis so it does not flicker at the borders.
/// XRI 3.3.2 has no ready-made knob, so this uses an XRSimpleInteractable and reads the hand position.
///
/// Setup: put this on an object with a collider (the grab area) that does NOT turn. Pivot is the visible
/// knob that turns (it can be a different object, for example a knob mesh that is part of the stove model).
/// Axis is the turning axis in the pivot's local space.
/// "Rotate around visual center" turns the pivot around the center of its mesh instead of its transform origin.
/// Use it for imported models whose knob origin is not in the middle of the knob.
/// </summary>
[RequireComponent(typeof(XRSimpleInteractable))]
public class Knob : MonoBehaviour
{
    public const float OffAngle = 0f;
    public const float LowAngle = 60f;
    public const float HighAngle = 120f;

    const float OffLowBorder = 30f;   // halfway between Off and Low
    const float LowHighBorder = 90f;  // halfway between Low and High

    [SerializeField] Burner m_Burner;
    [SerializeField] Transform m_Pivot;
    [SerializeField] Vector3 m_LocalAxis = Vector3.up;
    [SerializeField] bool m_RotateAroundVisualCenter;
    [Tooltip("Flip if the knob turns the wrong way when you twist your hand.")]
    [SerializeField] bool m_Reverse;
    [SerializeField] float m_Hysteresis = 8f;
    [Tooltip("Speed of the snap to a detent after release, in degrees per second.")]
    [SerializeField] float m_SnapSpeed = 540f;
    [SerializeField, Range(0f, 1f)] float m_HapticAmplitude = 0.35f;
    [SerializeField] float m_HapticDuration = 0.05f;

    XRSimpleInteractable m_Interactable;
    Quaternion m_BaseRotation;
    Vector3 m_BasePosition;
    Vector3 m_CenterInParent;      // point (in the pivot's parent space) the knob turns around
    float m_Angle;                 // angle currently shown
    KnobPosition m_Position;
    bool m_WasSelected;
    Vector3 m_PreviousHandDirection;

    public KnobPosition Position => m_Position;

    /// <summary>Angle of a detent in degrees.</summary>
    public static float AngleFor(KnobPosition position)
    {
        switch (position)
        {
            case KnobPosition.Low: return LowAngle;
            case KnobPosition.High: return HighAngle;
            default: return OffAngle;
        }
    }

    /// <summary>
    /// Position for an angle, given the current position. Angles are clamped to 0..120.
    /// Leaving the current position needs the angle to pass its border by <paramref name="hysteresis"/> degrees.
    /// </summary>
    public static KnobPosition PositionFor(float angle, KnobPosition current, float hysteresis)
    {
        angle = Mathf.Clamp(angle, OffAngle, HighAngle);
        hysteresis = Mathf.Max(0f, hysteresis);

        var plain = angle < OffLowBorder ? KnobPosition.Off
                  : angle < LowHighBorder ? KnobPosition.Low
                  : KnobPosition.High;

        switch (current)
        {
            case KnobPosition.Off:
                return angle >= OffLowBorder + hysteresis ? plain : KnobPosition.Off;
            case KnobPosition.Low:
                return angle >= OffLowBorder - hysteresis && angle < LowHighBorder + hysteresis
                    ? KnobPosition.Low : plain;
            default:
                return angle >= LowHighBorder - hysteresis ? KnobPosition.High : plain;
        }
    }

    /// <summary>
    /// Links the knob to its burner and pivot from code (editor builder and tests).
    /// Outside Play Mode it only stores the references.
    /// </summary>
    public void Configure(Burner burner, Transform pivot, Vector3 localAxis, bool reverse, bool rotateAroundVisualCenter = false)
    {
        bool live = Application.isPlaying && isActiveAndEnabled;
        if (live) Unsubscribe();
        m_Burner = burner;
        m_Pivot = pivot;
        m_LocalAxis = localAxis;
        m_Reverse = reverse;
        m_RotateAroundVisualCenter = rotateAroundVisualCenter;
        if (live)
        {
            if (m_Pivot == null) m_Pivot = transform;
            CaptureBasePose();
            Subscribe();
            SyncFromBurner();
        }
    }

    void Awake()
    {
        m_Interactable = GetComponent<XRSimpleInteractable>();
        if (m_Pivot == null) m_Pivot = transform;
        CaptureBasePose();
    }

    void CaptureBasePose()
    {
        m_BaseRotation = m_Pivot.localRotation;
        m_BasePosition = m_Pivot.localPosition;
        m_CenterInParent = m_BasePosition;

        if (!m_RotateAroundVisualCenter) return;

        var visual = m_Pivot.GetComponentInChildren<Renderer>();
        if (visual == null) return;

        var parent = m_Pivot.parent;
        var center = visual.bounds.center;
        m_CenterInParent = parent != null ? parent.InverseTransformPoint(center) : center;
    }

    void OnEnable()
    {
        Subscribe();
        SyncFromBurner();
    }

    void OnDisable() => Unsubscribe();

    void Subscribe()
    {
        if (m_Burner != null) m_Burner.OnPositionChanged += OnBurnerChanged;
    }

    void Unsubscribe()
    {
        if (m_Burner != null) m_Burner.OnPositionChanged -= OnBurnerChanged;
    }

    // The burner was changed from elsewhere (Inspector, another script). Follow it unless the player is turning.
    void OnBurnerChanged(KnobPosition position)
    {
        if (m_Interactable != null && m_Interactable.isSelected) return;
        m_Position = position;
    }

    void SyncFromBurner()
    {
        if (m_Burner == null || m_Pivot == null) return;
        m_Position = m_Burner.Position;
        m_Angle = AngleFor(m_Position);
        ApplyRotation();
    }

    void Update()
    {
        if (m_Interactable == null) return;

        bool selected = m_Interactable.isSelected;
        if (selected && !m_WasSelected) BeginTurn();
        m_WasSelected = selected;

        if (selected) TurnWithHand();
        else SnapToDetent();
    }

    Vector3 WorldAxis => m_Pivot.TransformDirection(m_LocalAxis).normalized;

    // The point the knob turns around, in world space. It does not move while the knob turns.
    Vector3 WorldCenter
    {
        get
        {
            var parent = m_Pivot.parent;
            return parent != null ? parent.TransformPoint(m_CenterInParent) : m_CenterInParent;
        }
    }

    bool TryGetHandDirection(out Vector3 direction)
    {
        direction = Vector3.zero;
        var interactor = m_Interactable.firstInteractorSelecting;
        if (interactor == null) return false;

        var attach = interactor.GetAttachTransform(m_Interactable);
        if (attach == null) return false;

        var toHand = attach.position - WorldCenter;
        direction = Vector3.ProjectOnPlane(toHand, WorldAxis);
        return direction.sqrMagnitude > 1e-6f;
    }

    void BeginTurn()
    {
        if (TryGetHandDirection(out var direction)) m_PreviousHandDirection = direction;
        else m_PreviousHandDirection = Vector3.zero;
    }

    void TurnWithHand()
    {
        if (!TryGetHandDirection(out var direction)) return;

        if (m_PreviousHandDirection.sqrMagnitude > 1e-6f)
        {
            float delta = Vector3.SignedAngle(m_PreviousHandDirection, direction, WorldAxis);
            if (m_Reverse) delta = -delta;
            m_Angle = Mathf.Clamp(m_Angle + delta, OffAngle, HighAngle);
            ApplyRotation();
        }
        m_PreviousHandDirection = direction;

        var next = PositionFor(m_Angle, m_Position, m_Hysteresis);
        if (next == m_Position) return;

        m_Position = next;
        if (m_Burner != null) m_Burner.SetPosition(m_Position);
        PlayHaptic();
    }

    void SnapToDetent()
    {
        float target = AngleFor(m_Position);
        if (Mathf.Approximately(m_Angle, target)) return;

        m_Angle = Mathf.MoveTowards(m_Angle, target, m_SnapSpeed * Time.deltaTime);
        ApplyRotation();
    }

    // Turns the pivot by m_Angle around the axis through the turning center.
    void ApplyRotation()
    {
        if (m_Pivot == null) return;

        var axisInParent = (m_BaseRotation * m_LocalAxis).normalized;
        var delta = Quaternion.AngleAxis(m_Angle, axisInParent);

        m_Pivot.localRotation = delta * m_BaseRotation;
        m_Pivot.localPosition = m_CenterInParent + delta * (m_BasePosition - m_CenterInParent);
    }

    void PlayHaptic()
    {
        if (m_HapticAmplitude <= 0f) return;
        if (m_Interactable.firstInteractorSelecting is XRBaseInputInteractor input)
            input.SendHapticImpulse(m_HapticAmplitude, m_HapticDuration);
    }
}
