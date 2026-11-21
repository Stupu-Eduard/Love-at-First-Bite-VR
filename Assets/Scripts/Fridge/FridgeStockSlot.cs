using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// One spot on a fridge shelf that always holds one piece of stock. At start it spawns the configured prefab at its
/// own pose. When the piece is taken it waits <see cref="RespawnDelay"/> seconds (5 by default) and spawns a fresh
/// one at the same pose, so a taken item never appears anywhere else.
///
/// A piece counts as taken when the player grabs it, when it is destroyed (sliced in place) or when it is found
/// farther than the leave distance from the slot (pushed out by hand). The new piece is only spawned when no
/// physical body (an enabled, non-trigger collider with a Rigidbody) occupies the spot, so an item still held on
/// the spot, or put back there, never gets a duplicate on top of it. Static colliders such as the shelf do not block.
///
/// Only the slot keeps state. Clones made by the knife slicer (halves) carry no slot component, so they never
/// cause extra spawns. Items are spawned without a parent, so the scale of the fridge does not leak into them.
/// </summary>
public class FridgeStockSlot : MonoBehaviour
{
    const int k_BufferSize = 16;

    [Tooltip("Prefab spawned at this slot (for example Steak or Mushroom). It should have a Rigidbody and an XRGrabInteractable.")]
    [SerializeField] GameObject m_Prefab;

    [Tooltip("Seconds between the item being taken and the new one appearing.")]
    [SerializeField, Min(0f)] float m_RespawnDelay = 5f;

    [Tooltip("Distance in meters from the slot after which an item that was not grabbed (pushed out by hand) counts as taken.")]
    [SerializeField, Min(0.01f)] float m_LeaveDistance = 0.12f;

    [Tooltip("Radius in meters of the free-spot check. A new item only spawns when no rigidbody overlaps this sphere.")]
    [SerializeField, Min(0.01f)] float m_ClearRadius = 0.1f;

    [Tooltip("Spawn the first item in Start. Turn off to spawn it from code with SpawnNow.")]
    [SerializeField] bool m_SpawnOnStart = true;

    readonly Collider[] m_Buffer = new Collider[k_BufferSize];

    GameObject m_Item;
    bool m_HasItem;
    XRGrabInteractable m_ItemGrab;
    bool m_Waiting;
    float m_RespawnAt;
    bool m_WarnedNoPrefab;

    /// <summary>The item currently sitting in the slot. Null while the slot is empty or waiting to respawn.</summary>
    public GameObject CurrentItem => m_HasItem && m_Item != null ? m_Item : null;

    /// <summary>True between the item being taken and the replacement being spawned.</summary>
    public bool IsWaitingToRespawn => m_Waiting;

    /// <summary>Seconds between taking an item and its replacement.</summary>
    public float RespawnDelay => m_RespawnDelay;

    /// <summary>Sets up the slot from code (editor builders and tests). Call before the first Start.</summary>
    public void Configure(GameObject prefab, float respawnDelay = 5f, float clearRadius = 0.1f, float leaveDistance = 0.12f)
    {
        m_Prefab = prefab;
        m_RespawnDelay = Mathf.Max(0f, respawnDelay);
        m_ClearRadius = Mathf.Max(0.01f, clearRadius);
        m_LeaveDistance = Mathf.Max(0.01f, leaveDistance);
        m_WarnedNoPrefab = false;
    }

    /// <summary>Spawns the item now if the slot is empty.</summary>
    /// <returns>The new item, or null when the slot already holds one or there is no prefab.</returns>
    public GameObject SpawnNow()
    {
        if (CurrentItem != null) return null;
        return Spawn();
    }

    void Start()
    {
        if (m_SpawnOnStart && !m_HasItem && !m_Waiting) Spawn();
    }

    void Update()
    {
        if (m_HasItem)
        {
            if (m_Item == null)
                MarkTaken(); // destroyed, for example sliced in place
            else if ((m_Item.transform.position - transform.position).sqrMagnitude > m_LeaveDistance * m_LeaveDistance)
                MarkTaken(); // pushed out of the slot without a grab
        }

        if (m_Waiting && isActiveAndEnabled && Time.time >= m_RespawnAt && IsSpotClear())
            Spawn();
    }

    void OnDestroy() => Unsubscribe();

    GameObject Spawn()
    {
        if (m_Prefab == null)
        {
            if (!m_WarnedNoPrefab)
            {
                Debug.LogWarning($"[FridgeStockSlot] '{name}' has no prefab assigned, nothing to spawn.", this);
                m_WarnedNoPrefab = true;
            }
            return null;
        }

        var item = Instantiate(m_Prefab, transform.position, transform.rotation);
        item.name = m_Prefab.name;

        var body = item.GetComponent<Rigidbody>();
        if (body != null && !body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        // Runtime spawns are not covered by the scene-load pass of HeldObjectPhysics.
        var grab = item.GetComponent<XRGrabInteractable>();
        HeldObjectPhysics.TryInstall(grab);

        // A stock item must be live even when the prefab asset itself is inactive.
        if (!item.activeSelf) item.SetActive(true);

        Unsubscribe();
        m_Item = item;
        m_HasItem = true;
        m_Waiting = false;

        if (grab != null)
        {
            m_ItemGrab = grab;
            m_ItemGrab.selectEntered.AddListener(OnItemSelected);
        }
        return item;
    }

    void OnItemSelected(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args) => MarkTaken();

    // Idempotent: the first call starts the respawn timer, later calls for the same item do nothing.
    void MarkTaken()
    {
        if (!m_HasItem) return;

        Unsubscribe();
        m_Item = null;
        m_HasItem = false;
        m_Waiting = true;
        m_RespawnAt = Time.time + m_RespawnDelay;
    }

    void Unsubscribe()
    {
        if (m_ItemGrab != null) m_ItemGrab.selectEntered.RemoveListener(OnItemSelected);
        m_ItemGrab = null;
    }

    // The spot is blocked by any enabled, non-trigger collider that belongs to a rigidbody (a held or loose item).
    // Static colliders such as the shelf and the fridge walls do not block.
    bool IsSpotClear()
    {
        int count = Physics.OverlapSphereNonAlloc(SpotCenter(), m_ClearRadius, m_Buffer, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var hit = m_Buffer[i];
            if (hit != null && hit.enabled && !hit.isTrigger && hit.attachedRigidbody != null) return false;
        }
        return true;
    }

    Vector3 SpotCenter() => transform.position + Vector3.up * m_ClearRadius;

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.9f, 0.3f, 0.6f);
        Gizmos.DrawWireSphere(SpotCenter(), m_ClearRadius);
        Gizmos.color = new Color(1f, 0.8f, 0.1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, m_LeaveDistance);
    }
}
