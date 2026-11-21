using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode physics tests for PanSnap. Plain primitives, no XR, so the "grabbed" case is not covered here.
/// Layout: a static slab whose top is the stove surface, and burner zones whose rest point is on that surface
/// (BurnerZone default rest offset is 0.016 m below the zone object).
/// </summary>
public class PanSnapTests
{
    const float RestOffset = 0.016f; // keep in sync with the BurnerZone default

    readonly List<GameObject> m_Created = new List<GameObject>();

    GameObject Track(GameObject go)
    {
        m_Created.Add(go);
        return go;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var go in m_Created) if (go != null) Object.Destroy(go);
        m_Created.Clear();
    }

    GameObject MakeSurface(float topY)
    {
        var slab = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        slab.transform.localScale = new Vector3(1f, 0.2f, 1f);
        slab.transform.position = new Vector3(0f, topY - 0.1f, 0f);
        return slab;
    }

    // The zone object sits above its rest point, like the real zones sit above the burner ring.
    BurnerZone MakeZone(Vector3 restPoint)
    {
        var go = Track(new GameObject("Zone"));
        go.transform.position = restPoint + new Vector3(0f, RestOffset, 0f);
        return go.AddComponent<BurnerZone>();
    }

    PanSnap MakePan(Vector3 position, Quaternion rotation, bool useGravity = true)
    {
        var root = Track(new GameObject("TestPan"));
        root.transform.SetPositionAndRotation(position, rotation);

        var body = root.AddComponent<Rigidbody>();
        body.useGravity = useGravity;

        var baseCollider = root.AddComponent<BoxCollider>();
        baseCollider.size = new Vector3(0.24f, 0.02f, 0.24f);
        baseCollider.center = new Vector3(0f, 0.01f, 0f);

        return root.AddComponent<PanSnap>();
    }

    static IEnumerator Steps(int count)
    {
        for (int i = 0; i < count; i++) yield return new WaitForFixedUpdate();
    }

    static Vector3 Flat(Vector3 v) => Vector3.ProjectOnPlane(v, Vector3.up);

    [UnityTest]
    public IEnumerator ReleasedNearBurner_EndsCenteredLevelAndOnTheSurface()
    {
        MakeSurface(1f);
        var zone = MakeZone(new Vector3(0f, 1f, 0f));
        var pan = MakePan(new Vector3(0.06f, 1.15f, -0.05f), Quaternion.Euler(10f, 30f, -8f));
        var handleBefore = Flat(pan.transform.forward);

        yield return Steps(45);

        var position = pan.transform.position;
        Assert.AreSame(zone, pan.SnappedZone);
        Assert.IsFalse(pan.IsSnapping);
        Assert.Less(new Vector2(position.x, position.z).magnitude, 0.005f, "Centered on the burner.");
        Assert.AreEqual(1f, position.y, 0.006f, "Base rests on the surface.");
        Assert.Less(Vector3.Angle(pan.transform.up, Vector3.up), 1f, "Level.");
        Assert.Less(Vector3.Angle(Flat(pan.transform.forward), handleBefore), 1f, "The handle keeps pointing the same way.");
    }

    [UnityTest]
    public IEnumerator ReleasedFarFromBurner_IsNotMoved()
    {
        MakeSurface(1f);
        MakeZone(new Vector3(0f, 1f, 0f));
        var pan = MakePan(new Vector3(0.4f, 1.02f, 0f), Quaternion.identity);

        yield return Steps(30);

        Assert.IsNull(pan.SnappedZone);
        Assert.IsFalse(pan.IsSnapping);
        Assert.AreEqual(0.4f, pan.transform.position.x, 0.01f);
    }

    [UnityTest]
    public IEnumerator FastPan_FlyingOverBurner_IsNotCaught()
    {
        MakeSurface(1f);
        MakeZone(new Vector3(0f, 1f, 0f));
        var pan = MakePan(new Vector3(-0.5f, 1.03f, 0f), Quaternion.identity, useGravity: false);
        pan.GetComponent<Rigidbody>().linearVelocity = new Vector3(4f, 0f, 0f);

        for (int i = 0; i < 20; i++)
        {
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(pan.IsSnapping, "Step " + i);
            Assert.IsNull(pan.SnappedZone, "Step " + i);
        }
    }

    [UnityTest]
    public IEnumerator PanTiltedTooMuch_IsNotSnapped()
    {
        MakeSurface(1f);
        MakeZone(new Vector3(0f, 1f, 0f));
        var pan = MakePan(new Vector3(0f, 1.05f, 0f), Quaternion.Euler(60f, 0f, 0f), useGravity: false);

        yield return Steps(30);

        Assert.IsNull(pan.SnappedZone);
        Assert.Greater(Vector3.Angle(pan.transform.up, Vector3.up), 50f);
    }

    [UnityTest]
    public IEnumerator BetweenTwoBurners_GoesToTheNearestOne()
    {
        MakeSurface(1f);
        MakeZone(new Vector3(0f, 1f, 0f));
        var right = MakeZone(new Vector3(0.2f, 1f, 0f));
        var pan = MakePan(new Vector3(0.14f, 1.03f, 0f), Quaternion.identity);

        yield return Steps(45);

        Assert.AreSame(right, pan.SnappedZone);
        Assert.AreEqual(0.2f, pan.transform.position.x, 0.005f);
    }

    [UnityTest]
    public IEnumerator SurfaceHigherThanBurnerRestPoint_UsesTheRealSurface()
    {
        MakeSurface(1.02f);
        MakeZone(new Vector3(0f, 1f, 0f));
        var pan = MakePan(new Vector3(0f, 1.1f, 0f), Quaternion.identity);

        yield return Steps(45);

        Assert.IsNotNull(pan.SnappedZone);
        Assert.AreEqual(1.02f, pan.transform.position.y, 0.006f, "Sits on the collider, not inside it.");
    }

    [UnityTest]
    public IEnumerator PanKnockedAwayAfterSnapping_ForgetsTheBurner()
    {
        MakeSurface(1f);
        var zone = MakeZone(new Vector3(0f, 1f, 0f));
        var pan = MakePan(new Vector3(0.03f, 1.04f, 0f), Quaternion.identity);
        yield return Steps(45);
        Assert.AreSame(zone, pan.SnappedZone);

        pan.GetComponent<Rigidbody>().position = new Vector3(0.4f, 1.0f, 0f);
        yield return Steps(10);

        Assert.IsNull(pan.SnappedZone);
    }
}
