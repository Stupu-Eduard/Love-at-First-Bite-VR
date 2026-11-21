using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Attachment;

/// <summary>EditMode tests for GrabDistanceTuning: the settings written to an InteractionAttachController.</summary>
public class GrabDistanceTuningTests
{
    GameObject m_Object;

    [TearDown]
    public void TearDown()
    {
        if (m_Object != null) Object.DestroyImmediate(m_Object);
    }

    InteractionAttachController MakeController()
    {
        m_Object = new GameObject("Controller");
        return m_Object.AddComponent<InteractionAttachController>();
    }

    [Test]
    public void Defaults_DifferFromTuning()
    {
        var controller = MakeController();

        Assert.IsTrue(controller.useMomentum);
        Assert.Greater(controller.zVelocityRampThreshold, 0f);
        Assert.Greater(controller.maxAdditionalVelocityScalar, GrabDistanceTuning.MaxAdditionalVelocityScalar);
    }

    [Test]
    public void Apply_DisablesMomentumAndRamp()
    {
        var controller = MakeController();

        GrabDistanceTuning.Apply(controller);

        Assert.IsFalse(controller.useMomentum);
        Assert.AreEqual(0f, controller.zVelocityRampThreshold);
    }

    [Test]
    public void Apply_BoundsDistanceAmplification()
    {
        var controller = MakeController();

        GrabDistanceTuning.Apply(controller);

        Assert.AreEqual(0f, controller.minAdditionalVelocityScalar);
        Assert.AreEqual(0.3f, controller.maxAdditionalVelocityScalar, 1e-6f);
        Assert.LessOrEqual(controller.minAdditionalVelocityScalar, controller.maxAdditionalVelocityScalar);
        Assert.AreEqual(0.4f, controller.manipulationTranslateSpeed, 1e-6f);
        Assert.IsTrue(controller.useDistanceBasedVelocityScaling);
    }

    [Test]
    public void Apply_IsIdempotent()
    {
        var controller = MakeController();

        GrabDistanceTuning.Apply(controller);
        GrabDistanceTuning.Apply(controller);

        Assert.IsFalse(controller.useMomentum);
        Assert.AreEqual(0.3f, controller.maxAdditionalVelocityScalar, 1e-6f);
    }

    [Test]
    public void Apply_NullIsIgnored()
    {
        Assert.DoesNotThrow(() => GrabDistanceTuning.Apply(null));
    }

    [Test]
    public void ApplyAll_TunesControllersInTheScene()
    {
        var controller = MakeController();

        int count = GrabDistanceTuning.ApplyAll();

        Assert.GreaterOrEqual(count, 1);
        Assert.IsFalse(controller.useMomentum);
    }
}
