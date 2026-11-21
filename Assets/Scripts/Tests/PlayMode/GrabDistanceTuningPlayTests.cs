using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Attachment;

/// <summary>
/// PlayMode tests for GrabDistanceTuning. The real InteractionAttachController is driven through its public
/// IInteractionAttachController interface: a "hand" is pulled back at the simulator keyboard speed (0.2 m/s) while
/// an anchor sits 3 m in front of it, then the hand stops. A tuned and a default controller run side by side in the
/// same frames. XRI measures the hand velocity with real time (Time.unscaledTime), so the loops run in real frames.
/// </summary>
public class GrabDistanceTuningPlayTests
{
    const float k_StartDistance = 3f;
    const float k_HandSpeed = 0.2f;
    const float k_PullSeconds = 1.5f;

    class Rig
    {
        public Transform Hand;
        public IInteractionAttachController Controller;
        public Transform Anchor;

        public float Distance => (Anchor.position - Hand.position).magnitude;
    }

    readonly List<GameObject> m_Created = new List<GameObject>();

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

    // A bare XR Origin with a camera, which the attach controller needs for its velocity tracking.
    void MakeOrigin()
    {
        var originObject = Track(new GameObject("XR Origin"));
        originObject.SetActive(false);
        var cameraObject = new GameObject("Camera");
        cameraObject.transform.SetParent(originObject.transform, false);
        var origin = originObject.AddComponent<XROrigin>();
        origin.Origin = originObject;
        origin.Camera = cameraObject.AddComponent<Camera>();
        originObject.SetActive(true);
    }

    // The controller object is created inactive and enabled after the origin exists (OnEnable looks it up).
    Rig MakeRig(string name, bool tuned, Vector3 handPosition)
    {
        var hand = Track(new GameObject(name + " Hand")).transform;
        hand.position = handPosition;

        var controllerObject = Track(new GameObject(name + " Controller"));
        controllerObject.SetActive(false);
        var controller = controllerObject.AddComponent<InteractionAttachController>();
        controller.transformToFollow = hand;
        controller.motionStabilizationMode = MotionStabilizationMode.Never;
        if (tuned) GrabDistanceTuning.Apply(controller);
        controllerObject.SetActive(true);

        var rig = new Rig { Hand = hand, Controller = controller };
        rig.Anchor = rig.Controller.GetOrCreateAnchorTransform(true);
        rig.Controller.MoveTo(hand.position + hand.forward * k_StartDistance);
        return rig;
    }

    // Pulls the hands back along their forward axis at k_HandSpeed for k_PullSeconds.
    static IEnumerator Pull(params Rig[] rigs)
    {
        float elapsed = 0f;
        while (elapsed < k_PullSeconds)
        {
            yield return null;
            float dt = Time.unscaledDeltaTime;
            elapsed += dt;
            foreach (var rig in rigs)
            {
                rig.Hand.position -= rig.Hand.forward * (k_HandSpeed * dt);
                rig.Controller.DoUpdate(dt);
            }
        }
    }

    // Keeps the hands still and updates the controllers for the given number of frames.
    static IEnumerator Hold(int frames, params Rig[] rigs)
    {
        for (int i = 0; i < frames; i++)
        {
            yield return null;
            foreach (var rig in rigs) rig.Controller.DoUpdate(Time.unscaledDeltaTime);
        }
    }

    [UnityTest]
    public IEnumerator Tuned_PullIsSlowAndGentle()
    {
        MakeOrigin();
        var tuned = MakeRig("Tuned", true, new Vector3(0f, 0f, 0f));
        Assert.IsTrue(tuned.Controller.hasOffset);
        Assert.AreEqual(k_StartDistance, tuned.Distance, 1e-3f);

        float start = Time.unscaledTime;
        yield return Pull(tuned);
        float elapsed = Time.unscaledTime - start;

        float closed = k_StartDistance - tuned.Distance;
        Assert.Greater(closed, 0.1f, "the object must come closer while the hand is pulled back");
        Assert.Greater(tuned.Distance, 1f, "the object must not collapse into the hand");
        Assert.Less(closed / elapsed, 1f, "closing speed must stay gentle");
        Assert.IsTrue(tuned.Controller.hasOffset, "the object must not have snapped into the hand");
    }

    [UnityTest]
    public IEnumerator Tuned_StaysWhereItIsAfterThePullStops()
    {
        MakeOrigin();
        var tuned = MakeRig("Tuned", true, new Vector3(0f, 0f, 0f));

        yield return Pull(tuned);
        // The velocity tracker keeps a window of recent frames, so let it see the hand standing still first.
        yield return Hold(60, tuned);
        float before = tuned.Distance;
        yield return Hold(30, tuned);

        Assert.Less(Mathf.Abs(tuned.Distance - before), 0.001f, "no momentum: the distance must not change once the hand stops");
        Assert.Less(tuned.Distance, k_StartDistance - 0.1f);
        Assert.IsTrue(tuned.Controller.hasOffset);
    }

    [UnityTest]
    public IEnumerator Tuned_ClosesSlowerThanDefault()
    {
        MakeOrigin();
        var tuned = MakeRig("Tuned", true, new Vector3(-1f, 0f, 0f));
        var untuned = MakeRig("Default", false, new Vector3(1f, 0f, 0f));

        yield return Pull(tuned, untuned);
        yield return Hold(60, tuned, untuned);

        Assert.Greater(tuned.Distance, untuned.Distance,
            "the default controller (amplification, ramp, momentum) pulls the object closer than the tuned one");
    }
}
