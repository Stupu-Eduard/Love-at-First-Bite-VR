using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode physics tests for Pan. They use plain primitives (no XR) and a kinematic pan that the test moves.
/// Local layout of the test pan (meters): base box y 0..0.02, Contents y 0.01..0.15, HeatSensor y -0.03..0.03.
/// </summary>
public class PanTests
{
    readonly List<GameObject> m_Created = new List<GameObject>();

    Pan m_Pan;
    Rigidbody m_PanBody;

    [SetUp]
    public void SetUp()
    {
        var root = Track(new GameObject("TestPan"));
        m_PanBody = root.AddComponent<Rigidbody>();
        m_PanBody.isKinematic = true;

        var baseCollider = root.AddComponent<BoxCollider>();
        baseCollider.size = new Vector3(0.3f, 0.02f, 0.3f);
        baseCollider.center = new Vector3(0f, 0.01f, 0f);

        var contents = MakeVolume(root.transform, "Contents", new Vector3(0f, 0.08f, 0f), new Vector3(0.26f, 0.14f, 0.26f));
        var sensor = MakeVolume(root.transform, "HeatSensor", Vector3.zero, new Vector3(0.2f, 0.06f, 0.2f));

        m_Pan = root.AddComponent<Pan>();
        m_Pan.Configure(contents, sensor);
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

    static BoxCollider MakeVolume(Transform parent, string name, Vector3 center, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var box = go.AddComponent<BoxCollider>();
        box.center = center;
        box.size = size;
        box.enabled = false; // used only as a shape
        return box;
    }

    Cookable MakeSteak(Vector3 position)
    {
        var go = Track(GameObject.CreatePrimitive(PrimitiveType.Sphere));
        go.transform.localScale = Vector3.one * 0.1f;
        go.transform.position = position;

        var body = go.AddComponent<Rigidbody>();
        body.useGravity = false;

        return go.AddComponent<Cookable>();
    }

    BurnerZone MakeZone(KnobPosition position)
    {
        var go = Track(new GameObject("Zone"));
        go.transform.position = m_PanBody.position;
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(0.3f, 0.1f, 0.3f);

        var burner = go.AddComponent<Burner>();
        burner.SetPosition(position);

        var zone = go.AddComponent<BurnerZone>();
        zone.Bind(burner);
        return zone;
    }

    static IEnumerator Steps(int count)
    {
        for (int i = 0; i < count; i++) yield return new WaitForFixedUpdate();
    }

    [UnityTest]
    public IEnumerator Steak_InsideContents_SticksWhenAtRest()
    {
        var steak = MakeSteak(new Vector3(0f, 0.07f, 0f));

        yield return Steps(3);

        Assert.IsTrue(m_Pan.IsStuck(steak));
        Assert.AreEqual(1, m_Pan.StuckCount);
        Assert.IsNotNull(steak.GetComponent<FixedJoint>());
    }

    [UnityTest]
    public IEnumerator Steak_OutsideContents_DoesNotStick()
    {
        var steak = MakeSteak(new Vector3(1f, 0.07f, 0f));

        yield return Steps(3);

        Assert.IsFalse(m_Pan.IsStuck(steak));
        Assert.IsNull(steak.GetComponent<FixedJoint>());
    }

    [UnityTest]
    public IEnumerator Steak_MovingFast_DoesNotStick()
    {
        var steak = MakeSteak(new Vector3(0f, 0.07f, 0f));
        steak.GetComponent<Rigidbody>().linearVelocity = new Vector3(3f, 0f, 0f);

        yield return Steps(1);

        Assert.IsFalse(m_Pan.IsStuck(steak));
    }

    [UnityTest]
    public IEnumerator Detach_FreesTheSteak()
    {
        var steak = MakeSteak(new Vector3(0f, 0.07f, 0f));
        yield return Steps(3);
        Assert.IsTrue(m_Pan.IsStuck(steak));

        Assert.IsTrue(m_Pan.Detach(steak));
        yield return null; // Destroy() of the joint happens at the end of the frame

        Assert.IsFalse(m_Pan.IsStuck(steak));
        Assert.IsNull(steak.GetComponent<FixedJoint>());
        Assert.IsFalse(m_Pan.Detach(steak), "Second detach must report nothing to do.");
    }

    [UnityTest]
    public IEnumerator Steak_FollowsPanWhenItMoves()
    {
        var steak = MakeSteak(new Vector3(0f, 0.07f, 0f));
        yield return Steps(3);
        Assert.IsTrue(m_Pan.IsStuck(steak));

        var localBefore = m_PanBody.transform.InverseTransformPoint(steak.transform.position);

        for (int i = 0; i < 20; i++)
        {
            m_PanBody.MovePosition(m_PanBody.position + new Vector3(0.02f, 0f, 0.01f));
            yield return new WaitForFixedUpdate();
        }
        yield return Steps(5);

        var localAfter = m_PanBody.transform.InverseTransformPoint(steak.transform.position);
        Assert.Less(Vector3.Distance(localBefore, localAfter), 0.05f);
        Assert.IsTrue(m_Pan.IsStuck(steak));
    }

    [UnityTest]
    public IEnumerator Steak_StaysWhenPanIsFlippedUpsideDown()
    {
        var steak = MakeSteak(new Vector3(0f, 0.07f, 0f));
        yield return Steps(3);
        Assert.IsTrue(m_Pan.IsStuck(steak));

        var localBefore = m_PanBody.transform.InverseTransformPoint(steak.transform.position);

        m_PanBody.MoveRotation(Quaternion.Euler(180f, 0f, 0f));
        yield return Steps(15);

        var localAfter = m_PanBody.transform.InverseTransformPoint(steak.transform.position);
        Assert.Less(Vector3.Distance(localBefore, localAfter), 0.08f);
        Assert.IsTrue(m_Pan.IsStuck(steak));
    }

    [UnityTest]
    public IEnumerator Heat_OnHighBurner_IsPassedToStuckSteak()
    {
        MakeZone(KnobPosition.High);
        var steak = MakeSteak(new Vector3(0f, 0.07f, 0f));

        yield return Steps(15);

        Assert.AreEqual(1f, m_Pan.CurrentHeat, 0.0001f);
        Assert.Greater(steak.HeatSeconds, 0.1f);
    }

    [UnityTest]
    public IEnumerator Heat_OnLowBurner_IsHalfOfHigh()
    {
        MakeZone(KnobPosition.Low);
        var steak = MakeSteak(new Vector3(0f, 0.07f, 0f));

        yield return Steps(15);

        Assert.AreEqual(0.5f, m_Pan.CurrentHeat, 0.0001f);
        Assert.Greater(steak.HeatSeconds, 0f);
    }

    [UnityTest]
    public IEnumerator Heat_OnBurnerThatIsOff_GivesNothing()
    {
        MakeZone(KnobPosition.Off);
        var steak = MakeSteak(new Vector3(0f, 0.07f, 0f));

        yield return Steps(15);

        Assert.AreEqual(0f, m_Pan.CurrentHeat);
        Assert.AreEqual(0f, steak.HeatSeconds);
        Assert.IsTrue(m_Pan.IsStuck(steak), "The steak still sticks, it just gets no heat.");
    }

    [UnityTest]
    public IEnumerator Heat_StopsWhenPanLeavesTheBurner()
    {
        MakeZone(KnobPosition.High);
        var steak = MakeSteak(new Vector3(0f, 0.07f, 0f));
        yield return Steps(10);
        Assert.Greater(steak.HeatSeconds, 0f);

        m_PanBody.MovePosition(m_PanBody.position + Vector3.up * 1f);
        yield return Steps(5);
        float afterLeaving = steak.HeatSeconds;

        yield return Steps(10);

        Assert.AreEqual(0f, m_Pan.CurrentHeat);
        Assert.AreEqual(afterLeaving, steak.HeatSeconds, 0.0001f);
    }
}
