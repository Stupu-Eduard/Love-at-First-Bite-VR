#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Dorm-room lighting (Sprint W7, Task 5): warm point lights (~3000K), no skybox, dark brown-grey ambient.
/// Room is about x -2..2, z -2.2..1.5, ceiling at y 2.55.
/// Menu: Tools > Love at First Bite > Setup Warm Lighting
/// </summary>
public static class SetupWarmLighting
{
    const string RootName = "Lighting";

    // ~3000K as an RGB colour.
    static readonly Color Warm = new Color(1f, 0.71f, 0.42f);

    struct LightDef
    {
        public string Name; public Vector3 Pos; public float Range; public float Intensity;
        public LightDef(string n, Vector3 p, float r, float i) { Name = n; Pos = p; Range = r; Intensity = i; }
    }

    static readonly LightDef[] Lights =
    {
        new LightDef("Light_Kitchen", new Vector3( 0.0f, 2.25f,  0.9f), 5.0f, 2.2f), // over the counter / stove
        new LightDef("Light_Table",   new Vector3( 0.0f, 2.25f, -0.6f), 4.5f, 1.6f), // over the table
        new LightDef("Light_Bed",     new Vector3(-1.5f, 2.2f,  -1.6f), 3.5f, 1.0f), // dim corner by the bed
    };

    [MenuItem("Tools/Love at First Bite/Setup Warm Lighting")]
    public static void Setup()
    {
        // 1) Environment: no skybox, flat dark brown-grey ambient.
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.20f, 0.16f, 0.14f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.reflectionIntensity = 0.3f;

        // 2) Lights, replacing a previous run.
        var old = GameObject.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Setup warm lighting");

        foreach (var def in Lights)
        {
            var go = new GameObject(def.Name);
            go.transform.SetParent(root.transform, false);
            go.transform.position = def.Pos;

            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = Warm;
            l.range = def.Range;
            l.intensity = def.Intensity;
            l.shadows = LightShadows.None; // cheap on Quest 2
        }

        DynamicGI.UpdateEnvironment();
        var scene = root.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root;
        Debug.Log($"[Lighting] Created {Lights.Length} warm point lights, skybox removed. Save the scene with Ctrl+S.");
    }
}
#endif
