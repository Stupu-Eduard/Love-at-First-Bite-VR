using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Attachment;

/// <summary>
/// Makes the distance of a far-grabbed object (the pan) controllable: it moves closer slowly while the hand is
/// pulled back (S in the XR Device Simulator) and stays exactly where it is when the hand stops.
///
/// Cause of the old behavior: the rig's NearFarInteractor creates a default <see cref="InteractionAttachController"/>
/// at runtime (nothing is serialized in the XR Origin prefab). Its defaults amplify the hand motion by
/// (1 + movementScale), where movementScale grows with the object distance (maxAdditionalVelocityScalar 1.5 times the
/// distance, so up to 5.5x at 3 m), it attenuates slow motion with zVelocityRampThreshold 0.3 and, most of all, it
/// keeps pulling after the input stops (useMomentum). The result was two states only: far when grabbed and glued to
/// the hand after a short S press.
///
/// Derivation from InteractionAttachController.ComputeAmplifiedOffset (XRI 3.3.2), per frame:
///   movement = zMotion * rampAmount * (1 + movementScale) * dt
///   movementScale = Bezier lerp(min * pivot, max * pivot, t) with 0 &lt;= t &lt;= 1 and pivot about the current distance
///   rampAmount = 1 when zVelocityRampThreshold is 0, otherwise clamp(|zMotion| / threshold, 0, 1)
///   newOffset = offset + movement (+ momentum, only when useMomentum is on)
/// With the values below: rampAmount = 1 (a slow 0.2 m/s pull is not attenuated), momentum = 0 (the offset does not
/// change when the hand stops), and movementScale is at most 0.3 * distance, so the object closes at no more than
/// hand speed * (1 + 0.3 * distance). At 3 m and the simulator speed of 0.2 m/s that is at most about 0.38 m/s,
/// and it gets slower as the object comes closer (at 1 m: at most 0.26 m/s).
///
/// Snap into the hand: when the pull reaches the hand (new offset length &lt;= 0) XRI calls ResetOffset and the object
/// attaches to the hand pose. The offset is already about 0 at that moment, so the jump is at most one frame of
/// movement and not visible. It cannot be avoided without editing the package, and it only happens if S is held
/// until the object reaches the hand.
///
/// Where to retune: the constants below (applied to every controller in the loaded scenes) and the simulator's
/// own controller speed, XRDeviceSimulator keyboardZTranslateSpeed (0.2 m/s in the sample prefab).
///
/// The tuning is runtime-only, so the rig prefab and the scene are not edited. <see cref="ApplyAll"/> runs after
/// every scene load. A controller created later than that is not covered (the NearFarInteractor creates its
/// controller in Awake, before AfterSceneLoad), the same limitation as HeldObjectPhysics.InstallAll.
/// </summary>
public static class GrabDistanceTuning
{
    /// <summary>Nothing keeps moving the object after the hand stops.</summary>
    public const bool UseMomentum = false;

    /// <summary>0 = slow hand motion is not attenuated by the ramp factor.</summary>
    public const float ZVelocityRampThreshold = 0f;

    /// <summary>Extra speed scale at distance 0 (none).</summary>
    public const float MinAdditionalVelocityScalar = 0f;

    /// <summary>Extra speed scale per meter of distance. Default XRI is 1.5; 0.3 keeps the pull gentle.</summary>
    public const float MaxAdditionalVelocityScalar = 0.3f;

    /// <summary>Speed for thumbstick manipulation, in case useManipulationInput is enabled later (XRI default 1).</summary>
    public const float ManipulationTranslateSpeed = 0.4f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void ApplyOnSceneLoad()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded; // stays safe with Domain Reload disabled
        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyAll();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyAll();

    /// <summary>Tunes every InteractionAttachController in the loaded scenes (including inactive ones).</summary>
    /// <returns>How many controllers were tuned.</returns>
    public static int ApplyAll()
    {
        var controllers = Object.FindObjectsByType<InteractionAttachController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++) Apply(controllers[i]);
        return controllers.Length;
    }

    /// <summary>Applies the constants to one controller. Safe to call repeatedly.</summary>
    public static void Apply(InteractionAttachController controller)
    {
        if (controller == null) return;

        controller.useMomentum = UseMomentum;
        controller.zVelocityRampThreshold = ZVelocityRampThreshold;
        // Set min first: OnValidate orders min/max and the setters clamp each value separately.
        controller.minAdditionalVelocityScalar = MinAdditionalVelocityScalar;
        controller.maxAdditionalVelocityScalar = MaxAdditionalVelocityScalar;
        controller.manipulationTranslateSpeed = ManipulationTranslateSpeed;
    }
}
