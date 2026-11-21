using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// PlayMode tests for FridgeStockSlot. A test interactor grabs and releases items through the XRInteractionManager,
/// so no input devices are needed. The "shelf" is a static box under the slot. The respawn delay is 0.3 s so the
/// tests run in real time without waiting 5 s.
/// </summary>
public class FridgeStockSlotTests
{
    /// <summary>Interactor with no input: it stays selecting until the test releases it.</summary>
    class TestInteractor : XRBaseInteractor { }

    /// <summary>Marks the stock items made by this test so they can be counted.</summary>
    class StockMarker : MonoBehaviour { }

    const float k_Delay = 0.3f;
    const float k_LeaveDistance = 0.12f;

    static readonly Vector3 k_SlotPosition = new Vector3(0f, 1f, 0f);

    readonly List<GameObject> m_Created = new List<GameObject>();

    XRInteractionManager m_Manager;
    TestInteractor m_Interactor;

    [SetUp]
    public void SetUp()
    {
        m_Manager = Track(new GameObject("Manager")).AddComponent<XRInteractionManager>();

        var interactorObject = Track(new GameObject("Hand"));
        interactorObject.SetActive(false);
        m_Interactor = interactorObject.AddComponent<TestInteractor>();
        m_Interactor.interactionManager = m_Manager;
        interactorObject.SetActive(true);
        interactorObject.transform.position = k_SlotPosition;
    }

    [TearDown]
    public void TearDown()
    {
        // Items spawned by the slot are not tracked, so remove them first (the manager must still exist).
        foreach (var marker in Object.FindObjectsByType<StockMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (marker != null) Object.DestroyImmediate(marker.gameObject);

        for (int i = m_Created.Count - 1; i >= 0; i--)
            if (m_Created[i] != null) Object.DestroyImmediate(m_Created[i]);
        m_Created.Clear();
    }

    GameObject Track(GameObject go)
    {
        m_Created.Add(go);
        return go;
    }

    // Static shelf under the slot, an inactive item template and the slot itself.
    FridgeStockSlot MakeSlot()
    {
        var shelf = Track(new GameObject("Shelf"));
        shelf.transform.position = k_SlotPosition + Vector3.down * 0.05f;
        shelf.AddComponent<BoxCollider>().size = new Vector3(2f, 0.1f, 2f);

        // Inactive, so the template itself never runs; the slot activates its clones.
        var template = Track(new GameObject("StockItem"));
        template.SetActive(false);
        var box = template.AddComponent<BoxCollider>();
        box.size = new Vector3(0.1f, 0.1f, 0.1f);
        box.center = new Vector3(0f, 0.05f, 0f);
        template.AddComponent<Rigidbody>();
        var grab = template.AddComponent<XRGrabInteractable>();
        grab.interactionManager = m_Manager;
        grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        template.AddComponent<StockMarker>();

        var slotObject = Track(new GameObject("Slot"));
        slotObject.SetActive(false);
        slotObject.transform.position = k_SlotPosition;
        var slot = slotObject.AddComponent<FridgeStockSlot>();
        slot.Configure(template, k_Delay, 0.1f, k_LeaveDistance);
        slotObject.SetActive(true);
        return slot;
    }

    static int CountItems() =>
        Object.FindObjectsByType<StockMarker>(FindObjectsSortMode.None).Length;

    static IEnumerator Steps(int count)
    {
        for (int i = 0; i < count; i++) yield return new WaitForFixedUpdate();
    }

    void Select(XRGrabInteractable grab) =>
        m_Manager.SelectEnter((IXRSelectInteractor)m_Interactor, (IXRSelectInteractable)grab);

    void MoveHand(Vector3 position) => m_Interactor.transform.position = position;

    [UnityTest]
    public IEnumerator Start_SpawnsGrabbableItemAtSlotPose()
    {
        var slot = MakeSlot();
        yield return null;
        yield return Steps(3);

        Assert.IsNotNull(slot.CurrentItem, "The slot must hold an item right after start.");
        Assert.AreEqual(1, CountItems());
        Assert.Less(Vector3.Distance(slot.CurrentItem.transform.position, k_SlotPosition), 0.05f);
        Assert.IsNotNull(slot.CurrentItem.GetComponent<XRGrabInteractable>());
        Assert.IsNotNull(slot.CurrentItem.GetComponent<HeldObjectPhysics>(), "Spawned items must get HeldObjectPhysics.");
        Assert.IsNull(slot.CurrentItem.transform.parent, "Items are spawned without a parent.");
        Assert.IsFalse(slot.IsWaitingToRespawn);
    }

    [Test]
    public void DefaultRespawnDelay_IsFiveSeconds()
    {
        var go = Track(new GameObject("Slot"));
        go.SetActive(false); // never activated, so Start does not run
        var slot = go.AddComponent<FridgeStockSlot>();

        Assert.AreEqual(5f, slot.RespawnDelay);
    }

    [UnityTest]
    public IEnumerator TakingItem_RespawnsOneItemAtSamePose_AfterDelay()
    {
        var slot = MakeSlot();
        yield return null;
        yield return Steps(3);

        var first = slot.CurrentItem;
        Assert.IsNotNull(first);
        Select(first.GetComponent<XRGrabInteractable>());
        MoveHand(k_SlotPosition + Vector3.up * 1f);

        yield return new WaitForSeconds(0.1f);
        Assert.IsTrue(slot.IsWaitingToRespawn, "Grabbing the item must start the respawn timer.");
        Assert.AreEqual(1, CountItems(), "Nothing may spawn before the delay is over.");

        yield return new WaitForSeconds(0.7f);

        Assert.IsFalse(slot.IsWaitingToRespawn);
        Assert.AreEqual(2, CountItems(), "Exactly one replacement must spawn.");
        Assert.AreNotSame(first, slot.CurrentItem);
        Assert.Less(Vector3.Distance(slot.CurrentItem.transform.position, k_SlotPosition), 0.05f,
            "The replacement must appear at the slot, not anywhere else.");
        Assert.Greater(Vector3.Distance(first.transform.position, k_SlotPosition), k_LeaveDistance,
            "The taken item was moved away from the slot.");
    }

    [UnityTest]
    public IEnumerator ItemHeldOnTheSpot_BlocksRespawn_UntilMovedAway()
    {
        var slot = MakeSlot();
        yield return null;
        yield return Steps(3);

        var first = slot.CurrentItem;
        Select(first.GetComponent<XRGrabInteractable>()); // the hand sits on the slot, the item stays there

        yield return new WaitForSeconds(k_Delay + 0.5f);
        Assert.IsTrue(slot.IsWaitingToRespawn, "The slot waits while a body occupies the spot.");
        Assert.AreEqual(1, CountItems(), "No duplicate may be spawned on top of the held item.");

        MoveHand(k_SlotPosition + Vector3.up * 1f);
        yield return new WaitForSeconds(0.5f);

        Assert.IsFalse(slot.IsWaitingToRespawn);
        Assert.AreEqual(2, CountItems(), "Exactly one item spawns once the spot is free.");

        yield return new WaitForSeconds(0.8f);
        Assert.AreEqual(2, CountItems());
    }

    [UnityTest]
    public IEnumerator DestroyedItem_TriggersOneRespawn()
    {
        var slot = MakeSlot();
        yield return null;
        yield return Steps(3);

        var first = slot.CurrentItem;
        Object.Destroy(first); // what the knife slicer does to the original
        yield return null;
        yield return null;
        Assert.IsTrue(slot.IsWaitingToRespawn);

        yield return new WaitForSeconds(k_Delay + 0.5f);

        Assert.IsFalse(slot.IsWaitingToRespawn);
        Assert.IsNotNull(slot.CurrentItem);
        Assert.AreEqual(1, CountItems());
        Assert.Less(Vector3.Distance(slot.CurrentItem.transform.position, k_SlotPosition), 0.05f);
    }

    [UnityTest]
    public IEnumerator GrabbedClone_DoesNotCauseExtraSpawn()
    {
        var slot = MakeSlot();
        yield return null;
        yield return Steps(3);

        var first = slot.CurrentItem;
        var clonePosition = k_SlotPosition + Vector3.right * 0.5f;
        var clone = Object.Instantiate(first, clonePosition, first.transform.rotation); // like a slicer half
        yield return Steps(3);

        MoveHand(clonePosition);
        Select(clone.GetComponent<XRGrabInteractable>());
        yield return new WaitForSeconds(k_Delay + 0.5f);

        Assert.IsFalse(slot.IsWaitingToRespawn, "Only the item spawned by the slot may start the timer.");
        Assert.AreSame(first, slot.CurrentItem);
        Assert.AreEqual(2, CountItems(), "The original and the clone, nothing more.");
    }

    [UnityTest]
    public IEnumerator ItemMovedFartherThanLeaveDistance_TriggersRespawn()
    {
        var slot = MakeSlot();
        yield return null;
        yield return Steps(3);

        var first = slot.CurrentItem;
        var moved = k_SlotPosition + Vector3.right * 0.5f;
        first.GetComponent<Rigidbody>().position = moved;
        first.transform.position = moved;

        yield return new WaitForSeconds(0.1f);
        Assert.IsTrue(slot.IsWaitingToRespawn, "An item pushed out of the slot counts as taken.");

        yield return new WaitForSeconds(k_Delay + 0.5f);

        Assert.IsFalse(slot.IsWaitingToRespawn);
        Assert.AreEqual(2, CountItems());
        Assert.AreNotSame(first, slot.CurrentItem);
        Assert.Less(Vector3.Distance(slot.CurrentItem.transform.position, k_SlotPosition), 0.05f);
    }

    [UnityTest]
    public IEnumerator OnlyOneRespawn_PerTaking_EvenOverLongWait()
    {
        var slot = MakeSlot();
        yield return null;
        yield return Steps(3);

        Select(slot.CurrentItem.GetComponent<XRGrabInteractable>());
        MoveHand(k_SlotPosition + Vector3.up * 1f);

        yield return new WaitForSeconds(1.5f);
        Assert.AreEqual(2, CountItems());

        yield return new WaitForSeconds(1.5f);
        Assert.AreEqual(2, CountItems(), "A second item must not appear without a second taking.");
    }
}
