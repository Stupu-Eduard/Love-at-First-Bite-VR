using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Guided, mostly-visual tutorial that runs on top of the real kitchen scene.
///
/// Shows one step at a time on a small world-space panel that floats in front of the player, pulses a
/// halo on the object the step is about, and auto-advances when the step's condition is met (e.g. the
/// burner turns on, the steak reaches Cooked). A short text line and a 4-colour cook-state legend carry
/// the rest; there is no voice and almost no reading.
///
/// It only activates when TutorialSession.StartInTutorial is true (set by the menu's Tutorial button),
/// so Play Game loads the exact same scene with the tutorial dormant.
///
/// The editor builder (Tools > Love at First Bite > Build Tutorial) fills in the step list and the
/// scene references. Targets are looked up by name with the builder, but can be reassigned by hand.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    /// <summary>How a step decides it is finished.</summary>
    public enum Trigger
    {
        Manual,          // player presses Continue (intro / outro)
        TargetGrabbed,   // the target's XRGrabInteractable becomes selected
        BurnerOn,        // any burner in the scene is on
        BurnerOff,       // all burners are off (after at least one was on)
        PanOnHeat,       // the pan sits on a lit burner (CurrentHeat > 0)
        ItemInPan,       // at least one cookable is stuck in the pan
        SteakCooked,     // a steak reached Cooked (not Burned)
        ItemSliced,      // a "Sliced" ingredient exists in the scene
        ItemOnPlate      // a cookable rests on a plate collider
    }

    [Serializable]
    public class Step
    {
        [TextArea] public string Text = "";
        public Trigger Trigger = Trigger.Manual;
        [Tooltip("Scene object the halo points at. Optional.")]
        public Transform Target;
    }

    [Header("Steps (filled by the builder)")]
    [SerializeField] List<Step> m_Steps = new List<Step>();

    [Header("UI (filled by the builder)")]
    [SerializeField] Canvas m_Canvas;
    [SerializeField] TextMeshProUGUI m_StepText;
    [SerializeField] TextMeshProUGUI m_ProgressText;
    [SerializeField] GameObject m_ContinueButton;  // only shown for Manual steps
    [SerializeField] GameObject m_Legend;          // the 4-colour cook-state guide
    [SerializeField] TutorialHighlighter m_Highlighter;

    [Header("Placement")]
    [SerializeField] Transform m_Camera;           // the panel floats in front of this
    [SerializeField] float m_Distance = 0.9f;
    [SerializeField] float m_Height = -0.25f;
    [SerializeField] float m_Follow = 2.5f;

    int m_Index = -1;
    bool m_Active;
    bool m_BurnerWasOn;    // for the BurnerOff step

    // Cached scene systems, found once on start.
    Burner[] m_Burners;
    Pan m_Pan;

    /// <summary>Called by the builder to wire references without the Inspector.</summary>
    public void Configure(List<Step> steps, Canvas canvas, TextMeshProUGUI stepText,
        TextMeshProUGUI progressText, GameObject continueButton, GameObject legend,
        TutorialHighlighter highlighter, Transform cam)
    {
        m_Steps = steps;
        m_Canvas = canvas;
        m_StepText = stepText;
        m_ProgressText = progressText;
        m_ContinueButton = continueButton;
        m_Legend = legend;
        m_Highlighter = highlighter;
        m_Camera = cam;
    }

    void Start()
    {
        m_Active = TutorialSession.StartInTutorial;
        if (!m_Active)
        {
            // Normal play: keep the whole overlay out of the way.
            if (m_Canvas != null) m_Canvas.gameObject.SetActive(false);
            if (m_Highlighter != null) m_Highlighter.Hide();
            enabled = false;
            return;
        }

        if (m_Camera == null && Camera.main != null) m_Camera = Camera.main.transform;
        m_Burners = FindObjectsByType<Burner>(FindObjectsSortMode.None);
        m_Pan = FindFirstObjectByType<Pan>();

        if (m_Canvas != null) m_Canvas.gameObject.SetActive(true);
        GoTo(0);
    }

    void Update()
    {
        if (!m_Active) return;

        PlacePanel();

        if (m_Index < 0 || m_Index >= m_Steps.Count) return;
        if (IsComplete(m_Steps[m_Index].Trigger)) Advance();
    }

    // --- flow ---

    /// <summary>Hooked to the Continue button and used for Manual steps.</summary>
    public void Advance() => GoTo(m_Index + 1);

    /// <summary>Hooked to the Skip/Close button. Ends the tutorial immediately.</summary>
    public void Finish()
    {
        m_Active = false;
        TutorialSession.StartInTutorial = false;
        if (m_Highlighter != null) m_Highlighter.Hide();
        if (m_Canvas != null) m_Canvas.gameObject.SetActive(false);
        enabled = false;
    }

    void GoTo(int index)
    {
        m_Index = index;
        if (m_Index >= m_Steps.Count) { Finish(); return; }

        var step = m_Steps[m_Index];
        m_BurnerWasOn = step.Trigger == Trigger.BurnerOff && AnyBurnerOn();

        if (m_StepText != null) m_StepText.text = step.Text;
        if (m_ProgressText != null) m_ProgressText.text = $"{m_Index + 1}/{m_Steps.Count}";
        // Continue is always available as a fallback so a missing object/mechanic can't soft-lock the
        // tutorial. Auto-advance still fires first when the real action happens.
        if (m_ContinueButton != null) m_ContinueButton.SetActive(true);
        if (m_Legend != null) m_Legend.SetActive(step.Trigger == Trigger.SteakCooked);
        if (m_Highlighter != null) m_Highlighter.Show(step.Target);
    }

    // --- completion checks ---

    bool IsComplete(Trigger trigger)
    {
        switch (trigger)
        {
            case Trigger.Manual: return false; // advanced by the button
            case Trigger.TargetGrabbed: return TargetGrabbed();
            case Trigger.BurnerOn: return AnyBurnerOn();
            case Trigger.BurnerOff:
                if (AnyBurnerOn()) m_BurnerWasOn = true;
                return m_BurnerWasOn && !AnyBurnerOn();
            case Trigger.PanOnHeat: return m_Pan != null && m_Pan.CurrentHeat > 0f;
            case Trigger.ItemInPan: return m_Pan != null && m_Pan.StuckCount > 0;
            case Trigger.SteakCooked: return AnySteakCooked();
            case Trigger.ItemSliced: return AnySlicedInScene();
            case Trigger.ItemOnPlate: return AnyItemOnPlate();
            default: return false;
        }
    }

    bool TargetGrabbed()
    {
        var t = m_Steps[m_Index].Target;
        if (t == null) return false;
        var grab = t.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>();
        return grab != null && grab.isSelected;
    }

    bool AnyBurnerOn()
    {
        if (m_Burners == null) return false;
        foreach (var b in m_Burners) if (b != null && b.IsOn) return true;
        return false;
    }

    bool AnySteakCooked()
    {
        foreach (var c in FindObjectsByType<Cookable>(FindObjectsSortMode.None))
            if (c != null && c.Kind == IngredientKind.Steak && c.State == CookState.Cooked) return true;
        return false;
    }

    // "Sliced" ingredients are separate prefabs whose name contains "Sliced".
    bool AnySlicedInScene()
    {
        foreach (var c in FindObjectsByType<Cookable>(FindObjectsSortMode.None))
            if (c != null && c.name.IndexOf("Sliced", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    // A rough check: a cookable whose center is just above a plate collider.
    bool AnyItemOnPlate()
    {
        var plates = new List<Collider>();
        foreach (var go in GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            if (go.name.IndexOf("Plate", StringComparison.OrdinalIgnoreCase) < 0) continue;
            var col = go.GetComponent<Collider>();
            if (col != null) plates.Add(col);
        }
        if (plates.Count == 0) return false;

        foreach (var c in FindObjectsByType<Cookable>(FindObjectsSortMode.None))
        {
            if (c == null) continue;
            var p = c.transform.position;
            foreach (var plate in plates)
            {
                var b = plate.bounds;
                if (p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z &&
                    p.y >= b.min.y && p.y <= b.max.y + 0.15f)
                    return true;
            }
        }
        return false;
    }

    // --- panel placement ---

    void PlacePanel()
    {
        if (m_Canvas == null || m_Camera == null) return;

        var cam = m_Camera;
        Vector3 forward = cam.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 targetPos = cam.position + forward * m_Distance + Vector3.up * m_Height;
        var ct = m_Canvas.transform;
        ct.position = Vector3.Lerp(ct.position, targetPos, Time.deltaTime * m_Follow);

        Vector3 look = ct.position - cam.position;
        look.y = 0f;
        if (look.sqrMagnitude > 1e-4f) ct.rotation = Quaternion.LookRotation(look);
    }
}
