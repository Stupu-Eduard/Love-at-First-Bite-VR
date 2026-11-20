using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Frying pan logic.
///
/// Two volumes are defined with BoxColliders. They stay DISABLED; only their size, center and
/// transform are used, so they never push or grab anything:
///  - Contents: the inside of the pan. A Cookable resting here gets stuck to the pan.
///  - HeatSensor: a thin box under the base. Any BurnerZone it overlaps gives the pan its heat.
///
/// Sticking uses a FixedJoint to the pan Rigidbody. Nothing is reparented and isKinematic is not touched,
/// so XRGrabInteractable (which restores parent and kinematic state on drop) cannot undo it.
/// The joint is removed as soon as the player grabs the item.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Pan : MonoBehaviour
{
    const int MaxOverlaps = 32;

    [Tooltip("Inside of the pan. Items whose center is inside get stuck. The collider is only used as a shape.")]
    [SerializeField] BoxCollider m_Contents;

    [Tooltip("Thin box under the pan base. Overlapping BurnerZones give the pan its heat.")]
    [SerializeField] BoxCollider m_HeatSensor;

    [Tooltip("An item sticks only when it moves slower than this (m/s) relative to the pan.")]
    [SerializeField] float m_MaxStickSpeed = 0.35f;

    class StuckItem
    {
        public FixedJoint Joint;
        public XRBaseInteractable Interactable;
        public UnityAction<SelectEnterEventArgs> OnSelected;
    }

    Rigidbody m_Body;
    readonly Collider[] m_Hits = new Collider[MaxOverlaps];
    readonly Dictionary<Cookable, StuckItem> m_Stuck = new Dictionary<Cookable, StuckItem>();
    readonly List<Cookable> m_Scratch = new List<Cookable>();
    readonly List<BurnerZone> m_Zones = new List<BurnerZone>();

    /// <summary>Heat of the burner under the pan (highest of the overlapped zones), 0 when none.</summary>
    public float CurrentHeat { get; private set; }

    /// <summary>Number of items currently stuck to the pan.</summary>
    public int StuckCount => m_Stuck.Count;

    Rigidbody Body
    {
        get
        {
            if (m_Body == null) m_Body = GetComponent<Rigidbody>();
            return m_Body;
        }
    }

    /// <summary>Sets the two volumes from code (editor builders and tests).</summary>
    public void Configure(BoxCollider contents, BoxCollider heatSensor)
    {
        m_Contents = contents;
        m_HeatSensor = heatSensor;
    }

    public bool IsStuck(Cookable cookable) => !ReferenceEquals(cookable, null) && m_Stuck.ContainsKey(cookable);

    void FixedUpdate()
    {
        ReleaseInvalid();
        StickCandidates();

        CurrentHeat = SampleHeat();
        if (CurrentHeat <= 0f || m_Stuck.Count == 0) return;

        m_Scratch.Clear();
        m_Scratch.AddRange(m_Stuck.Keys);
        foreach (var cookable in m_Scratch)
        {
            if (cookable != null) cookable.AddHeat(CurrentHeat, Time.fixedDeltaTime);
        }
    }

    void OnDisable()
    {
        m_Scratch.Clear();
        m_Scratch.AddRange(m_Stuck.Keys);
        foreach (var cookable in m_Scratch) Detach(cookable);
        CurrentHeat = 0f;
    }

    /// <summary>
    /// Sticks the item to the pan if it is inside the Contents volume, not held and nearly still
    /// relative to the pan. Returns true when it is stuck after the call.
    /// </summary>
    public bool TryStick(Cookable cookable)
    {
        if (ReferenceEquals(cookable, null) || cookable == null) return false;
        if (m_Stuck.ContainsKey(cookable)) return true;
        if (!CanStick(cookable, out var itemBody)) return false;

        var joint = cookable.gameObject.AddComponent<FixedJoint>();
        joint.connectedBody = Body;

        var item = new StuckItem { Joint = joint };

        var interactable = cookable.GetComponent<XRBaseInteractable>();
        if (interactable != null)
        {
            item.Interactable = interactable;
            item.OnSelected = _ => Detach(cookable);
            interactable.selectEntered.AddListener(item.OnSelected);
        }

        m_Stuck.Add(cookable, item);
        itemBody.WakeUp();
        return true;
    }

    /// <summary>Removes the joint and lets the item move freely. Returns false if it was not stuck.</summary>
    public bool Detach(Cookable cookable)
    {
        if (ReferenceEquals(cookable, null)) return false;
        if (!m_Stuck.TryGetValue(cookable, out var item)) return false;

        m_Stuck.Remove(cookable);

        if (item.Joint != null) Destroy(item.Joint);
        if (item.Interactable != null && item.OnSelected != null)
            item.Interactable.selectEntered.RemoveListener(item.OnSelected);

        return true;
    }

    // Safety net: drops items that were destroyed, lost their joint or were grabbed without the event reaching us.
    void ReleaseInvalid()
    {
        if (m_Stuck.Count == 0) return;

        m_Scratch.Clear();
        m_Scratch.AddRange(m_Stuck.Keys);
        foreach (var cookable in m_Scratch)
        {
            var item = m_Stuck[cookable];
            bool gone = cookable == null || item.Joint == null;
            bool held = !gone && item.Interactable != null && item.Interactable.isSelected;
            if (gone || held) Detach(cookable);
        }
    }

    void StickCandidates()
    {
        int count = OverlapVolume(m_Contents);
        for (int i = 0; i < count; i++)
        {
            var cookable = m_Hits[i].GetComponentInParent<Cookable>();
            if (cookable == null || m_Stuck.ContainsKey(cookable)) continue;
            TryStick(cookable);
        }
    }

    bool CanStick(Cookable cookable, out Rigidbody itemBody)
    {
        itemBody = cookable.GetComponent<Rigidbody>();
        if (itemBody == null || itemBody.isKinematic) return false;
        if (m_Contents == null) return false;

        var interactable = cookable.GetComponent<XRBaseInteractable>();
        if (interactable != null && interactable.isSelected) return false;

        var center = itemBody.worldCenterOfMass;
        if (!InsideContents(center)) return false;

        var relative = itemBody.linearVelocity - Body.GetPointVelocity(center);
        return relative.magnitude <= m_MaxStickSpeed;
    }

    bool InsideContents(Vector3 worldPoint)
    {
        var local = m_Contents.transform.InverseTransformPoint(worldPoint) - m_Contents.center;
        var half = m_Contents.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    float SampleHeat()
    {
        m_Zones.Clear();
        int count = OverlapVolume(m_HeatSensor);
        for (int i = 0; i < count; i++)
        {
            var zone = m_Hits[i].GetComponent<BurnerZone>();
            if (zone != null) m_Zones.Add(zone);
        }
        return BurnerZone.MaxHeat(m_Zones);
    }

    // Overlap test using a (disabled) BoxCollider purely as a box definition. Triggers are included.
    int OverlapVolume(BoxCollider volume)
    {
        if (volume == null) return 0;

        var t = volume.transform;
        var scale = t.lossyScale;
        var halfExtents = Vector3.Scale(volume.size * 0.5f,
            new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));

        return Physics.OverlapBoxNonAlloc(
            t.TransformPoint(volume.center), halfExtents, m_Hits, t.rotation,
            ~0, QueryTriggerInteraction.Collide);
    }
}
