#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Builds the start-menu scene from code (Love at First Bite can't edit scenes by hand here).
///
/// Creates Assets/Scenes/MainMenu.unity with:
///  - the XRI Starter Assets "XR Origin (XR Rig)" so the player can look around and point with the controllers,
///  - an XR Interaction Manager and an EventSystem with an XR UI Input Module (needed for VR UI),
///  - a warm-lit backdrop so the empty scene doesn't look flat,
///  - a world-space canvas (with a TrackedDeviceGraphicRaycaster) holding a styled title and two buttons:
///      Play Game  -> loads the gameplay scene (SampleScene),
///      Tutorial   -> shows an empty placeholder panel for now (filled in later),
///  - a hidden tutorial placeholder panel with a Back button.
///
/// Also registers MainMenu as the first scene in Build Settings, with SampleScene after it.
///
/// Menu: Tools > Love at First Bite > Build Main Menu
/// </summary>
public static class BuildMainMenu
{
    const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
    const string GameplaySceneName = "SampleScene";
    const string GameplayScenePath = "Assets/Scenes/SampleScene.unity";

    // XR Origin (XR Rig) prefab from the XRI 3.3.2 Starter Assets (resolved by GUID).
    const string XrRigGuid = "f6336ac4ac8b4d34bc5072418cdc62a0";

    // Palette (warm, cozy-kitchen feel).
    static readonly Color PanelColor = new Color(0.10f, 0.08f, 0.09f, 0.92f);
    static readonly Color TitleColor = new Color(1f, 0.74f, 0.42f);          // warm amber
    static readonly Color SubtitleColor = new Color(0.86f, 0.80f, 0.74f);
    static readonly Color PlayNormal = new Color(0.82f, 0.36f, 0.30f);       // warm red
    static readonly Color PlayHighlight = new Color(0.94f, 0.47f, 0.40f);
    static readonly Color PlayPressed = new Color(0.62f, 0.25f, 0.22f);
    static readonly Color TutNormal = new Color(0.24f, 0.22f, 0.26f);
    static readonly Color TutHighlight = new Color(0.34f, 0.32f, 0.38f);
    static readonly Color TutPressed = new Color(0.18f, 0.16f, 0.20f);
    static readonly Color ButtonText = Color.white;

    [MenuItem("Tools/Love at First Bite/Build Main Menu")]
    public static void Build()
    {
        if (!File.Confirm()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildEnvironment();
        BuildXrRig();
        BuildEventSystem();
        BuildCanvas();

        EditorSceneManager.SaveScene(scene, MenuScenePath);
        RegisterInBuildSettings();

        Debug.Log($"[MainMenu] Built '{MenuScenePath}'. It is now the first scene in Build Settings. " +
                  "Press Play (with the XR Device Simulator) or build to try it. Save if prompted.");
    }

    // --- helper so the destructive overwrite is explicit ---
    static class File
    {
        public static bool Confirm()
        {
            if (!System.IO.File.Exists(MenuScenePath)) return true;
            return EditorUtility.DisplayDialog(
                "Rebuild Main Menu",
                $"{MenuScenePath} already exists and will be overwritten. Continue?",
                "Rebuild", "Cancel");
        }
    }

    static void BuildEnvironment()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.14f, 0.11f, 0.12f);

        // A soft key light so the backdrop and canvas read well.
        var lightGo = new GameObject("Menu_Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.82f, 0.62f);
        light.intensity = 1.1f;
        lightGo.transform.rotation = Quaternion.Euler(45f, 160f, 0f);

        // A dark backdrop quad behind the canvas so the player isn't floating in void.
        var backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
        backdrop.name = "Menu_Backdrop";
        backdrop.transform.position = new Vector3(0f, 1.6f, 4f);
        backdrop.transform.localScale = new Vector3(14f, 9f, 1f);
        Object.DestroyImmediate(backdrop.GetComponent<Collider>());
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.07f, 0.06f, 0.08f) };
        backdrop.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static void BuildXrRig()
    {
        var path = AssetDatabase.GUIDToAssetPath(XrRigGuid);
        var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab != null)
        {
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            rig.transform.position = Vector3.zero;
        }
        else
        {
            // Fallback: a plain camera so the scene still renders if the rig prefab is missing.
            Debug.LogWarning("[MainMenu] XR Origin rig prefab not found; adding a plain camera instead.");
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 1.6f, 0f);
            camGo.AddComponent<Camera>();
        }

        // The rig expects an interaction manager in the scene.
        if (Object.FindFirstObjectByType<XRInteractionManager>() == null)
            new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
    }

    static void BuildEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<XRUIInputModule>(); // drives world-space UI from the VR controllers
    }

    static void BuildCanvas()
    {
        // World-space canvas, ~1.9 m wide at 0.0015 scale, 2 m in front at eye height.
        var canvasGo = new GameObject("MainMenuCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var canvasRt = canvasGo.GetComponent<RectTransform>();
        canvasRt.sizeDelta = new Vector2(1280f, 800f);
        canvasRt.position = new Vector3(0f, 1.6f, 2f);
        canvasRt.localScale = Vector3.one * 0.0015f;

        // The MainMenuController lives on the canvas.
        var controller = canvasGo.AddComponent<MainMenuController>();

        // Background panel.
        var panel = CreatePanel("Panel", canvasRt, new Vector2(1180f, 720f), Vector2.zero, PanelColor);

        // --- Menu root (title + subtitle + buttons) ---
        var menuRoot = new GameObject("MenuRoot", typeof(RectTransform));
        var menuRt = menuRoot.GetComponent<RectTransform>();
        menuRt.SetParent(panel, false);
        Stretch(menuRt);

        CreateText("Title", menuRt, "LOVE AT FIRST BITE", 96, TitleColor,
            FontStyles.Bold, new Vector2(0f, 230f), new Vector2(1080f, 160f));
        CreateText("Subtitle", menuRt, "VR Cooking · Date Night", 40, SubtitleColor,
            FontStyles.Normal, new Vector2(0f, 120f), new Vector2(1080f, 70f));

        var playBtn = CreateButton("PlayButton", menuRt, "PLAY GAME",
            new Vector2(0f, -30f), new Vector2(620f, 130f), PlayNormal, PlayHighlight, PlayPressed);
        var tutBtn = CreateButton("TutorialButton", menuRt, "TUTORIAL",
            new Vector2(0f, -200f), new Vector2(620f, 130f), TutNormal, TutHighlight, TutPressed);

        // --- Tutorial placeholder panel (hidden until Tutorial is pressed) ---
        var tutorialPanel = new GameObject("TutorialPanel", typeof(RectTransform));
        var tutRt = tutorialPanel.GetComponent<RectTransform>();
        tutRt.SetParent(panel, false);
        Stretch(tutRt);

        CreateText("TutorialTitle", tutRt, "TUTORIAL", 80, TitleColor,
            FontStyles.Bold, new Vector2(0f, 220f), new Vector2(1080f, 140f));
        CreateText("TutorialBody", tutRt, "Coming soon.", 44, SubtitleColor,
            FontStyles.Normal, new Vector2(0f, 20f), new Vector2(1000f, 200f));
        var backBtn = CreateButton("BackButton", tutRt, "BACK",
            new Vector2(0f, -220f), new Vector2(460f, 120f), TutNormal, TutHighlight, TutPressed);

        tutorialPanel.SetActive(false);

        // Wire the controller and the buttons.
        controller.Configure(GameplaySceneName, menuRoot, tutorialPanel);
        UnityEventTools.AddPersistentListener(playBtn.onClick, controller.PlayGame);
        UnityEventTools.AddPersistentListener(tutBtn.onClick, controller.OpenTutorial);
        UnityEventTools.AddPersistentListener(backBtn.onClick, controller.BackToMenu);
    }

    // ---------- UI building blocks ----------

    static RectTransform CreatePanel(string name, Transform parent, Vector2 size, Vector2 anchoredPos, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;
        go.GetComponent<Image>().color = color;
        return rt;
    }

    static TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, Color color,
        FontStyles style, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.sizeDelta = sizeDelta;
        rt.anchoredPosition = anchoredPos;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        return tmp;
    }

    static Button CreateButton(string name, Transform parent, string label, Vector2 anchoredPos, Vector2 size,
        Color normal, Color highlight, Color pressed)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        var image = go.GetComponent<Image>();
        image.color = Color.white; // tinted by the button color block
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = Image.Type.Sliced;

        var button = go.GetComponent<Button>();
        var colors = button.colors;
        colors.normalColor = normal;
        colors.highlightedColor = highlight;
        colors.pressedColor = pressed;
        colors.selectedColor = highlight;
        colors.disabledColor = normal * 0.5f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        CreateText("Label", rt, label, 52, ButtonText, FontStyles.Bold, Vector2.zero, size);
        return button;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void RegisterInBuildSettings()
    {
        var scenes = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(MenuScenePath, true)
        };
        if (System.IO.File.Exists(GameplayScenePath))
            scenes.Add(new EditorBuildSettingsScene(GameplayScenePath, true));

        // Keep any other scenes already in the list (except duplicates of these two).
        foreach (var s in EditorBuildSettings.scenes)
        {
            if (s.path == MenuScenePath || s.path == GameplayScenePath) continue;
            scenes.Add(s);
        }
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
#endif
