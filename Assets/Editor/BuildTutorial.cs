#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Injects the guided tutorial overlay into the gameplay scene (SampleScene).
///
/// Builds, under a single "Tutorial" root:
///  - a TutorialManager with the ordered step list,
///  - a TutorialHighlighter (pulsing halo + arrow) that points at the step's target,
///  - a compact world-space panel that floats in front of the player: short step text, a progress
///    counter, a Continue button (for intro/outro steps), a Skip button, and a 4-colour cook-state
///    legend shown only on the "watch the states" step,
///  - an EventSystem with an XR UI Input Module if the scene has none (needed for VR UI).
///
/// Targets are found by name in the open scene. Anything not found is logged; assign it by hand on the
/// TutorialManager steps. The overlay stays dormant unless the menu's Tutorial button was pressed
/// (TutorialSession.StartInTutorial), so normal Play Game is unaffected.
///
/// Menu: Tools > Love at First Bite > Build Tutorial  (open SampleScene first)
/// </summary>
public static class BuildTutorial
{
    const string RootName = "Tutorial";
    const string GameplayScenePath = "Assets/Scenes/SampleScene.unity";

    static readonly Color PanelColor = new Color(0.08f, 0.07f, 0.09f, 0.90f);
    static readonly Color TextColor = new Color(0.96f, 0.93f, 0.88f);
    static readonly Color AccentColor = new Color(1f, 0.74f, 0.42f);
    static readonly Color BtnNormal = new Color(0.82f, 0.36f, 0.30f);
    static readonly Color BtnHighlight = new Color(0.94f, 0.47f, 0.40f);
    static readonly Color BtnPressed = new Color(0.62f, 0.25f, 0.22f);
    static readonly Color SkipNormal = new Color(0.26f, 0.24f, 0.28f);
    static readonly Color SkipHighlight = new Color(0.36f, 0.34f, 0.40f);
    static readonly Color SkipPressed = new Color(0.18f, 0.16f, 0.20f);

    // The four cook-state swatch colours (pink -> light brown -> dark brown -> black).
    static readonly (string label, Color color)[] CookStates =
    {
        ("RAW", new Color(0.86f, 0.45f, 0.48f)),
        ("ALMOST", new Color(0.72f, 0.46f, 0.28f)),
        ("COOKED", new Color(0.45f, 0.28f, 0.16f)),
        ("BURNED", new Color(0.09f, 0.08f, 0.07f)),
    };

    [MenuItem("Tools/Love at First Bite/Build Tutorial")]
    public static void Build()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != GameplayScenePath)
        {
            // In batch mode no scene is open; open the gameplay scene directly.
            if (Application.isBatchMode)
            {
                scene = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Single);
            }
            else if (!EditorUtility.DisplayDialog("Build Tutorial",
                $"The active scene is '{scene.name}', not SampleScene. The tutorial is meant for the gameplay " +
                "scene. Build it into the current scene anyway?", "Build here", "Cancel"))
                return;
        }

        var old = GameObject.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build Tutorial");

        EnsureEventSystem();

        var cam = Camera.main != null ? Camera.main.transform : null;
        var highlighter = BuildHighlighter(root.transform);
        var ui = BuildCanvas(root.transform, cam);
        var steps = BuildSteps();

        var manager = root.AddComponent<TutorialManager>();
        manager.Configure(steps, ui.canvas, ui.stepText, ui.progressText,
            ui.continueButton, ui.legend, highlighter, cam);

        // Continue advances; Skip ends the tutorial.
        UnityEventTools.AddPersistentListener(ui.continueBtn.onClick, manager.Advance);
        UnityEventTools.AddPersistentListener(ui.skipBtn.onClick, manager.Finish);

        EditorSceneManager.MarkSceneDirty(scene);
        if (Application.isBatchMode) EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = root;
        Debug.Log("[Tutorial] Built the tutorial overlay in this scene. It stays hidden during normal " +
                  "Play Game and runs only when launched from the menu's Tutorial button. Save with Ctrl+S.");
    }

    // ---------- pieces ----------

    static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
        var go = new GameObject("EventSystem");
        Undo.RegisterCreatedObjectUndo(go, "Tutorial EventSystem");
        go.AddComponent<UnityEngine.EventSystems.EventSystem>();
        go.AddComponent<XRUIInputModule>();
    }

    static TutorialHighlighter BuildHighlighter(Transform parent)
    {
        var go = new GameObject("Highlighter");
        go.transform.SetParent(parent, false);
        return go.AddComponent<TutorialHighlighter>();
    }

    struct Ui
    {
        public Canvas canvas;
        public TextMeshProUGUI stepText, progressText;
        public GameObject continueButton, legend;
        public Button continueBtn, skipBtn;
    }

    static Ui BuildCanvas(Transform parent, Transform cam)
    {
        var ui = new Ui();

        var canvasGo = new GameObject("TutorialCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
        canvasGo.transform.SetParent(parent, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rt = canvasGo.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(620f, 360f);
        rt.localScale = Vector3.one * 0.0013f;
        rt.position = cam != null ? cam.position + cam.forward * 0.9f : new Vector3(0f, 1.4f, 0.9f);
        ui.canvas = canvas;

        var panel = CreatePanel("Panel", rt, new Vector2(600f, 340f), Vector2.zero, PanelColor);

        ui.progressText = CreateText("Progress", panel, "1/8", 28, AccentColor,
            FontStyles.Bold, TextAlignmentOptions.TopRight, new Vector2(-24f, -16f), new Vector2(200f, 44f),
            new Vector2(1f, 1f), new Vector2(1f, 1f));

        ui.stepText = CreateText("StepText", panel, "", 40, TextColor,
            FontStyles.Normal, TextAlignmentOptions.Center, new Vector2(0f, 30f), new Vector2(540f, 180f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));

        // Cook-state legend (hidden unless the relevant step is active).
        ui.legend = BuildLegend(panel);
        ui.legend.SetActive(false);

        // Continue button (shown only on Manual steps).
        ui.continueBtn = CreateButton("ContinueButton", panel, "CONTINUE",
            new Vector2(130f, -130f), new Vector2(230f, 72f), BtnNormal, BtnHighlight, BtnPressed, 34);
        ui.continueButton = ui.continueBtn.gameObject;

        // Skip button (always available).
        ui.skipBtn = CreateButton("SkipButton", panel, "SKIP",
            new Vector2(-150f, -130f), new Vector2(170f, 72f), SkipNormal, SkipHighlight, SkipPressed, 30);

        return ui;
    }

    static GameObject BuildLegend(RectTransform panel)
    {
        var legend = new GameObject("Legend", typeof(RectTransform));
        var lrt = legend.GetComponent<RectTransform>();
        lrt.SetParent(panel, false);
        lrt.anchoredPosition = new Vector2(0f, -60f);
        lrt.sizeDelta = new Vector2(560f, 90f);

        float step = 140f;
        float startX = -(step * (CookStates.Length - 1)) * 0.5f;
        for (int i = 0; i < CookStates.Length; i++)
        {
            float x = startX + i * step;
            var swatch = CreatePanel($"Swatch{i}", lrt, new Vector2(60f, 40f),
                new Vector2(x, 14f), CookStates[i].color);
            swatch.anchorMin = swatch.anchorMax = new Vector2(0.5f, 0.5f);
            CreateText($"Label{i}", lrt, CookStates[i].label, 20, TextColor,
                FontStyles.Bold, TextAlignmentOptions.Center, new Vector2(x, -26f), new Vector2(130f, 30f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        }
        return legend;
    }

    static List<TutorialManager.Step> BuildSteps()
    {
        var steps = new List<TutorialManager.Step>();

        void Add(string text, TutorialManager.Trigger trigger, string targetName)
        {
            steps.Add(new TutorialManager.Step
            {
                Text = text,
                Trigger = trigger,
                Target = FindTarget(targetName)
            });
        }

        // The eight agreed steps, anchored on the real scene objects.
        Add("Apuca un obiect cu triggerul controllerului. Incearca sa iei si sa muti ceva.",
            TutorialManager.Trigger.TargetGrabbed, "Knife");
        Add("Deschide frigiderul si ia o bucata de carne. Carnea reapare mereu aici.",
            TutorialManager.Trigger.TargetGrabbed, "Steak_Raw");
        Add("Pune carnea pe tocator si taie-o cu cutitul, cu o miscare ferma.",
            TutorialManager.Trigger.ItemSliced, "Board");
        Add("Ia tigaia si aseaz-o pe aragaz.",
            TutorialManager.Trigger.PanOnHeat, "Frying_pan");
        Add("Pune carnea in tigaie. Se lipeste singura de fund.",
            TutorialManager.Trigger.ItemInPan, "Frying_pan");
        Add("Roteste butonul aragazului ca sa aprinzi focul (mic sau mare).",
            TutorialManager.Trigger.BurnerOn, null);
        Add("Urmareste culoarea carnii: trece prin 4 stari. Scoate-o cand e COOKED, inainte sa se arda.",
            TutorialManager.Trigger.SteakCooked, "Frying_pan");
        Add("Gata! Pune mancarea in farfurie ca sa termini preparatul.",
            TutorialManager.Trigger.ItemOnPlate, "Plate_A");

        return steps;
    }

    static Transform FindTarget(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var go = GameObject.Find(name);
        if (go == null)
            Debug.LogWarning($"[Tutorial] Target '{name}' not found in the scene. Assign it by hand on " +
                             "the TutorialManager step, or rename the object.");
        return go != null ? go.transform : null;
    }

    // ---------- UI helpers ----------

    static RectTransform CreatePanel(string name, Transform parent, Vector2 size, Vector2 pos, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        go.GetComponent<Image>().color = color;
        return rt;
    }

    static TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, Color color,
        FontStyles style, TextAlignmentOptions align, Vector2 pos, Vector2 sizeDelta,
        Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.sizeDelta = sizeDelta;
        rt.anchoredPosition = pos;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        return tmp;
    }

    static Button CreateButton(string name, Transform parent, string label, Vector2 pos, Vector2 size,
        Color normal, Color highlight, Color pressed, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;

        var image = go.GetComponent<Image>();
        image.color = Color.white;
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = Image.Type.Sliced;

        var button = go.GetComponent<Button>();
        var colors = button.colors;
        colors.normalColor = normal;
        colors.highlightedColor = highlight;
        colors.pressedColor = pressed;
        colors.selectedColor = highlight;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        CreateText("Label", rt, label, fontSize, Color.white, FontStyles.Bold,
            TextAlignmentOptions.Center, Vector2.zero, size, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        return button;
    }
}
#endif
