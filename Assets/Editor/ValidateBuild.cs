#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Batch self-check for the menu + tutorial wiring. Opens both scenes and asserts the key objects and
/// references exist, so problems show up in the log instead of at runtime on the headset.
/// Menu: Tools > Love at First Bite > Validate Build (also runnable with -executeMethod ValidateBuild.Run).
/// </summary>
public static class ValidateBuild
{
    static int s_Problems;
    static readonly StringBuilder s_Log = new StringBuilder();

    [MenuItem("Tools/Love at First Bite/Validate Build")]
    public static void Run()
    {
        s_Problems = 0;
        s_Log.Clear();

        CheckBuildSettings();
        CheckMainMenu();
        CheckTutorial();

        s_Log.Insert(0, s_Problems == 0
            ? "[Validate] ALL CHECKS PASSED.\n"
            : $"[Validate] {s_Problems} PROBLEM(S) FOUND.\n");

        if (s_Problems == 0) Debug.Log(s_Log.ToString());
        else Debug.LogError(s_Log.ToString());
    }

    static void Ok(string msg) => s_Log.AppendLine("  OK   " + msg);
    static void Fail(string msg) { s_Problems++; s_Log.AppendLine("  FAIL " + msg); }

    static void CheckBuildSettings()
    {
        s_Log.AppendLine("Build Settings:");
        var scenes = EditorBuildSettings.scenes;
        if (scenes.Length < 2) { Fail("fewer than 2 scenes in Build Settings."); return; }
        if (scenes[0].path.EndsWith("MainMenu.unity") && scenes[0].enabled) Ok("MainMenu is the first scene.");
        else Fail("MainMenu is not the first enabled scene.");
        bool hasSample = false;
        foreach (var s in scenes) if (s.path.EndsWith("SampleScene.unity") && s.enabled) hasSample = true;
        if (hasSample) Ok("SampleScene is in Build Settings."); else Fail("SampleScene missing from Build Settings.");
    }

    static void CheckMainMenu()
    {
        s_Log.AppendLine("MainMenu scene:");
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);

        var controller = Object.FindFirstObjectByType<MainMenuController>();
        if (controller == null) { Fail("no MainMenuController in MainMenu."); return; }
        Ok("MainMenuController present.");

        if (Object.FindFirstObjectByType<Canvas>() != null) Ok("Canvas present."); else Fail("no Canvas.");
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) Ok("EventSystem present.");
        else Fail("no EventSystem (UI won't respond in VR).");

        int buttons = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Length;
        if (buttons >= 2) Ok($"{buttons} buttons present."); else Fail($"expected >=2 buttons, found {buttons}.");

        if (Camera.main != null) Ok("Main Camera (XR rig) present."); else Fail("no Main Camera / XR rig.");
    }

    static void CheckTutorial()
    {
        s_Log.AppendLine("SampleScene + Tutorial:");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);

        var mgr = Object.FindFirstObjectByType<TutorialManager>();
        if (mgr == null) { Fail("no TutorialManager in SampleScene."); return; }
        Ok("TutorialManager present.");

        var so = new SerializedObject(mgr);
        CheckRef(so, "m_Canvas", "tutorial canvas");
        CheckRef(so, "m_StepText", "step text");
        CheckRef(so, "m_ProgressText", "progress text");
        CheckRef(so, "m_ContinueButton", "continue button");
        CheckRef(so, "m_Legend", "cook-state legend");
        CheckRef(so, "m_Highlighter", "highlighter");
        CheckRef(so, "m_Camera", "camera");

        var steps = so.FindProperty("m_Steps");
        if (steps == null || steps.arraySize == 0) { Fail("no tutorial steps."); return; }
        Ok($"{steps.arraySize} tutorial steps.");

        int withTarget = 0, withText = 0;
        for (int i = 0; i < steps.arraySize; i++)
        {
            var e = steps.GetArrayElementAtIndex(i);
            if (!string.IsNullOrWhiteSpace(e.FindPropertyRelative("Text").stringValue)) withText++;
            if (e.FindPropertyRelative("Target").objectReferenceValue != null) withTarget++;
        }
        if (withText == steps.arraySize) Ok("every step has text."); else Fail($"{steps.arraySize - withText} step(s) with no text.");
        Ok($"{withTarget}/{steps.arraySize} steps have a highlight target (others intentionally none).");

        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) Ok("EventSystem present.");
        else Fail("no EventSystem in SampleScene (tutorial UI won't respond in VR).");

        // Mechanics presence is informational, not a failure: the tutorial has a manual fallback.
        bool hasBurner = Object.FindFirstObjectByType<Burner>() != null;
        bool hasPan = Object.FindFirstObjectByType<Pan>() != null;
        bool hasCookable = Object.FindFirstObjectByType<Cookable>() != null;
        s_Log.AppendLine($"  INFO functional cooking in scene -> Burner:{hasBurner} Pan:{hasPan} Cookable:{hasCookable} " +
                         "(false = that step needs the Continue fallback).");
    }

    static void CheckRef(SerializedObject so, string prop, string label)
    {
        var p = so.FindProperty(prop);
        if (p != null && p.objectReferenceValue != null) Ok($"{label} wired.");
        else Fail($"{label} ({prop}) not wired.");
    }
}
#endif
