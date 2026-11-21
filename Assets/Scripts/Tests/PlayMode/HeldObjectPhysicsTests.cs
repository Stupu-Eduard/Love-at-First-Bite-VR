using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// PlayMode tests for HeldObjectPhysics. A test interactor is selected and released by hand through the
/// XRInteractionManager, so no input devices are needed. The player rig is a bare XROrigin with a CharacterController.
/// </summary>
public class HeldObjectPhysicsTests
{
    /// <summary>Interactor with no input: it stays selecting until the test releases it.</summary>
    class TestInteractor : XRBaseInteractor { }

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
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var go in m_Created) if (go != null) Object.Destroy(go);
        m_Created.Clear();
    }

    GameObject Track(GameObject go)
    {
        m_Created.Add(go);
        return go;
    }

    // A small dynamic box that moves Instantaneous, like every grabbable in the scene.
    XRGrabInteractable MakeGrabbable(Vector3 position, bool kinematic = false)
    {
        var go = Track(new GameObject("Grabbable"));
        go.SetActive(false);
        go.transform.position = position;

        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(0.3f, 0.1f, 0.3f);

        var body = go.AddComponent<Rigidbody>();
        body.isKinematic = kinematic;

        var grab = go.AddComponent<XRGrabInteractable>();
        grab.interactionManager = m_Manager;
        grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        grab.throwOnDetach = true;

        go.SetActive(true);
        return grab;
    }

    // A player rig with a CharacterController (radius 0.5, height 2) centered on its position.
    CharacterController MakeRig(Vector3 position)
    {
        var go = Track(new GameObject("XR Origin"));
        go.SetActive(false);
        go.transform.position = position;

        var controller = go.AddComponent<CharacterController>();
        controller.radius = 0.5f;
        controller.height = 2f;
        controller.center = Vector3.zero;

        go.AddComponent<XROrigin>();
        go.SetActive(true);
        return controller;
    }

    HeldObjectPhysics Install(XRGrabInteractable grab)
    {
        HeldObjectPhysics.InstallAll();
        return grab.GetComponent<HeldObjectPhysics>();
    }

    void Select(XRGrabInteractable grab) =>
        m_Manager.SelectEnter((IXRSelectInteractor)m_Interactor, (IXRSelectInteractable)grab);

    void Release(XRGrabInteractable grab) =>
        m_Manager.SelectExit((IXRSelectInteractor)m_Interactor, (IXRSelectInteractable)grab);

    static IEnumerator Steps(int count)
    {
        for (int i = 0; i < count; i++) yield return new WaitForFixedUpdate();
    }

    // Moves the test hand toward the target at a constant speed, one step per rendered frame.
    IEnumerator MoveHand(Vector3 target, float speed)
    {
        var hand = m_Interactor.transform;
        while ((target - hand.position).sqrMagnitude > 1e-6f)
        {
            hand.position = Vector3.MoveTowards(hand.position, target, speed * Time.deltaTime);
            yield return null;
        }
    }

    // A static box, like a wall or a counter.
    GameObject MakeStaticBox(Vector3 center, Vector3 size)
    {
        var go = Track(new GameObject("StaticBox"));
        go.transform.position = center;
        go.AddComponent<BoxCollider>().size = size;
        return go;
    }

    // A pan-like grabbable: origin at the center of its base, with a PanSnap, moving Instantaneous.
    XRGrabInteractable MakePanGrabbable(Vector3 position)
    {
        var go = Track(new GameObject("TestPan"));
        go.SetActive(false);
        go.transform.position = position;

        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(0.24f, 0.02f, 0.24f);
        box.center = new Vector3(0f, 0.01f, 0f);

        go.AddComponent<Rigidbody>();
        go.AddComponent<PanSnap>();

        var grab = go.AddComponent<XRGrabInteractable>();
        grab.interactionManager = m_Manager;
        grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        grab.throwOnDetach = true;

        go.SetActive(true);
        return grab;
    }

    [UnityTest]
    public IEnumerator Installer_AddsComponent_ToInstantaneousGrabbable()
    {
        var grab = MakeGrabbable(Vector3.zero);
        yield return null;

        Assert.IsNull(grab.GetComponent<HeldObjectPhysics>());
        int added = HeldObjectPhysics.InstallAll();

        Assert.GreaterOrEqual(added, 1);
        Assert.IsNotNull(grab.GetComponent<HeldObjectPhysics>());

        // Installing twice must not add a second component.
        Assert.AreEqual(0, HeldObjectPhysics.InstallAll());
    }

    [UnityTest]
    public IEnumerator Installer_SkipsKinematicBody()
    {
        var grab = MakeGrabbable(Vector3.zero, kinematic: true);
        yield return null;

        HeldObjectPhysics.InstallAll();

        Assert.IsNull(grab.GetComponent<HeldObjectPhysics>());
    }

    [UnityTest]
    public IEnumerator Selecting_SwitchesToVelocityTracking_AndReleaseRestoresMovementType()
    {
        var grab = MakeGrabbable(Vector3.zero);
        var body = grab.GetComponent<Rigidbody>();
        var physics = Install(grab);
        Assert.IsNotNull(physics);
        yield return null;

        Select(grab);
        yield return Steps(2);

        Assert.IsTrue(grab.isSelected);
        Assert.IsTrue(physics.IsHeld);
        Assert.AreEqual(XRBaseInteractable.MovementType.VelocityTracking, grab.movementType);
        Assert.IsFalse(body.isKinematic, "A held object must stay dynamic so walls can stop it.");

        Release(grab);
        yield return Steps(2);

        Assert.IsFalse(grab.isSelected);
        Assert.IsFalse(physics.IsHeld);
        Assert.AreEqual(XRBaseInteractable.MovementType.Instantaneous, grab.movementType);
        Assert.IsFalse(body.isKinematic);
        Assert.IsTrue(grab.throwOnDetach, "ThrowOnDetach must be left as configured.");
    }

    [UnityTest]
    public IEnumerator Selecting_IgnoresRigCollisions_AndReleaseRestoresThem()
    {
        var rig = MakeRig(new Vector3(10f, 0f, 0f)); // far away: no overlap, so restore is immediate
        var grab = MakeGrabbable(Vector3.zero);
        var heldCollider = grab.GetComponent<BoxCollider>();
        Install(grab);
        yield return null;

        Assert.IsFalse(Physics.GetIgnoreCollision(heldCollider, rig));

        Select(grab);
        yield return Steps(2);

        Assert.IsTrue(Physics.GetIgnoreCollision(heldCollider, rig), "Held object must ignore the player capsule.");

        Release(grab);
        yield return Steps(2);

        Assert.IsFalse(Physics.GetIgnoreCollision(heldCollider, rig), "Collision must come back after release.");
    }

    [UnityTest]
    public IEnumerator Release_KeepsRigCollisionIgnored_UntilObjectLeavesThePlayer()
    {
        var rig = MakeRig(Vector3.zero); // overlaps the grabbable
        var grab = MakeGrabbable(Vector3.zero);
        var heldCollider = grab.GetComponent<BoxCollider>();
        Install(grab);
        yield return null;

        Select(grab);
        yield return Steps(2);
        Release(grab);
        yield return Steps(2);

        Assert.IsTrue(Physics.GetIgnoreCollision(heldCollider, rig), "Still overlapping the player: keep ignoring so it is not shoved.");

        rig.transform.position = new Vector3(10f, 0f, 0f);
        yield return Steps(3);

        Assert.IsFalse(Physics.GetIgnoreCollision(heldCollider, rig), "Once clear of the player, collision is restored.");
    }

    [UnityTest]
    public IEnumerator HeldObject_IsStoppedByWall_WhenHandPushesThroughIt()
    {
        MakeStaticBox(new Vector3(1.5f, 0f, 0f), new Vector3(1f, 2f, 2f)); // face at x = 1
        var grab = MakeGrabbable(Vector3.zero);
        var body = grab.GetComponent<Rigidbody>();
        Install(grab);
        yield return null;

        Select(grab);
        yield return Steps(2);

        yield return MoveHand(new Vector3(3f, 0f, 0f), 2f);
        yield return Steps(25);

        Assert.Greater(m_Interactor.transform.position.x, 2.9f, "The hand went through the wall.");
        // The box is 0.3 wide, so its center cannot pass x = 0.85.
        Assert.Less(body.position.x, 0.9f, "A held object must be blocked by static colliders.");
        Assert.IsFalse(body.isKinematic);
    }

    [UnityTest]
    public IEnumerator HeldObject_SpeedIsLimited_WhenHandJumpsAway()
    {
        var grab = MakeGrabbable(Vector3.zero);
        var body = grab.GetComponent<Rigidbody>();
        var physics = Install(grab);
        Assert.IsNotNull(physics);
        yield return null;

        Select(grab);
        yield return Steps(2);

        m_Interactor.transform.position = new Vector3(5f, 0f, 0f); // asks for a very large velocity

        // Keep in sync with the default of HeldObjectPhysics.m_MaxHeldSpeed.
        for (int i = 0; i < 15; i++)
        {
            yield return new WaitForFixedUpdate();
            Assert.LessOrEqual(body.linearVelocity.magnitude, 6.01f, "Step " + i);
        }
    }

    [UnityTest]
    public IEnumerator Release_ThrowsObject_WithTheHandVelocity()
    {
        var grab = MakeGrabbable(Vector3.zero);
        var body = grab.GetComponent<Rigidbody>();
        Install(grab);
        yield return null;

        Select(grab);
        yield return Steps(2);

        yield return MoveHand(new Vector3(1.5f, 0f, 0f), 2f);
        Release(grab);
        yield return null; // XRI applies the throw velocity in LateUpdate
        yield return new WaitForFixedUpdate();

        Assert.Greater(body.linearVelocity.x, 1f, "The object keeps the hand velocity after release.");
    }

    [UnityTest]
    public IEnumerator TwoHands_ObjectStaysPhysicsDriven_UntilTheLastHandReleases()
    {
        var grab = MakeGrabbable(Vector3.zero);
        grab.selectMode = InteractableSelectMode.Multiple;
        var physics = Install(grab);
        yield return null;

        var secondHandObject = Track(new GameObject("Second Hand"));
        secondHandObject.SetActive(false);
        var secondHand = secondHandObject.AddComponent<TestInteractor>();
        secondHand.interactionManager = m_Manager;
        secondHandObject.SetActive(true);
        yield return null;

        Select(grab);
        m_Manager.SelectEnter((IXRSelectInteractor)secondHand, (IXRSelectInteractable)grab);
        yield return Steps(2);

        Assert.IsTrue(physics.IsHeld);
        Assert.AreEqual(XRBaseInteractable.MovementType.VelocityTracking, grab.movementType);

        Release(grab);
        yield return Steps(2);

        Assert.IsTrue(grab.isSelected);
        Assert.IsTrue(physics.IsHeld, "Still held by the second hand.");
        Assert.AreEqual(XRBaseInteractable.MovementType.VelocityTracking, grab.movementType);

        m_Manager.SelectExit((IXRSelectInteractor)secondHand, (IXRSelectInteractable)grab);
        yield return Steps(2);

        Assert.IsFalse(physics.IsHeld);
        Assert.AreEqual(XRBaseInteractable.MovementType.Instantaneous, grab.movementType);
    }

    [UnityTest]
    public IEnumerator CloneMadeWhileHeld_StartsWithTheOriginalMovementType()
    {
        var grab = MakeGrabbable(Vector3.zero);
        Install(grab);
        yield return null;

        Select(grab);
        yield return Steps(2);
        Assert.AreEqual(XRBaseInteractable.MovementType.VelocityTracking, grab.movementType);

        var clone = Track(Object.Instantiate(grab.gameObject));
        yield return null;

        var cloneGrab = clone.GetComponent<XRGrabInteractable>();
        Assert.IsFalse(cloneGrab.isSelected);
        Assert.AreEqual(XRBaseInteractable.MovementType.Instantaneous, cloneGrab.movementType,
            "A half sliced from a held object must not keep the hold-only movement type.");
        Assert.AreEqual(XRBaseInteractable.MovementType.VelocityTracking, grab.movementType, "The original is still held.");
    }

    [UnityTest]
    public IEnumerator PanHeldDownOntoStove_AndReleased_SnapsToTheBurnerSurface()
    {
        // Stove slab with its top at y = 1 and a burner whose rest point is on it.
        MakeStaticBox(new Vector3(0f, 0.9f, 0f), new Vector3(1f, 0.2f, 1f));
        var zoneObject = Track(new GameObject("Zone"));
        zoneObject.transform.position = new Vector3(0f, 1f + 0.016f, 0f); // BurnerZone rest offset is 0.016
        var zone = zoneObject.AddComponent<BurnerZone>();

        var start = new Vector3(0.06f, 1.2f, -0.05f);
        m_Interactor.transform.position = start;
        var grab = MakePanGrabbable(start);
        var snap = grab.GetComponent<PanSnap>();
        var body = grab.GetComponent<Rigidbody>();
        Install(grab);
        yield return null;

        Select(grab);
        yield return Steps(2);

        // Push the hand well below the stove surface: the pan must stop on the slab instead of going through it.
        yield return MoveHand(new Vector3(start.x, 0.9f, start.z), 1f);
        yield return Steps(20);
        Assert.GreaterOrEqual(body.position.y, 0.99f, "The held pan went into the stove.");

        Release(grab);
        yield return Steps(60);

        var position = grab.transform.position;
        Assert.IsFalse(grab.isSelected);
        Assert.IsFalse(body.isKinematic);
        Assert.AreSame(zone, snap.SnappedZone);
        Assert.IsFalse(snap.IsSnapping);
        Assert.Less(new Vector2(position.x, position.z).magnitude, 0.005f, "Centered on the burner.");
        Assert.AreEqual(1f, position.y, 0.006f, "Base rests on the stove surface.");
    }
}
