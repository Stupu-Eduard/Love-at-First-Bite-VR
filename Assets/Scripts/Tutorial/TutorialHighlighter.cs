using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws attention to one scene object without touching its own materials: it spawns a pulsing,
/// unlit halo (a scaled copy of the target's renderer bounds) and an optional bobbing arrow above it.
/// Enable it to point at the current tutorial target, disable it to hide. One instance is reused and
/// re-pointed at each step, so nothing is created per frame.
/// </summary>
public class TutorialHighlighter : MonoBehaviour
{
    [SerializeField] Color m_Color = new Color(1f, 0.82f, 0.25f, 0.9f);
    [SerializeField] float m_PulseSpeed = 3f;
    [SerializeField] float m_PulseAmount = 0.12f;
    [SerializeField] float m_Padding = 1.15f;     // halo is this much bigger than the target bounds
    [SerializeField] float m_ArrowHeight = 0.18f; // gap above the target for the arrow
    [SerializeField] float m_ArrowBob = 0.04f;

    Transform m_Target;
    Transform m_Halo;
    Transform m_Arrow;
    Material m_Material;
    Vector3 m_HaloBaseScale = Vector3.one;
    float m_ArrowBaseY;

    void Awake()
    {
        BuildVisuals();
        Hide();
    }

    /// <summary>Points the halo at a target and shows it. Null hides the highlighter.</summary>
    public void Show(Transform target)
    {
        m_Target = target;
        if (target == null) { Hide(); return; }

        gameObject.SetActive(true);
        FitTo(target);
    }

    public void Hide()
    {
        m_Target = null;
        gameObject.SetActive(false);
    }

    void BuildVisuals()
    {
        // Unlit transparent material so the halo glows regardless of scene lighting.
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        m_Material = new Material(shader);
        SetMaterialColor(m_Color);

        // Halo: a sphere shell around the object.
        var halo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        halo.name = "Halo";
        var haloCol = halo.GetComponent<Collider>();
        if (haloCol != null) Destroy(haloCol);
        halo.GetComponent<Renderer>().sharedMaterial = m_Material;
        halo.transform.SetParent(transform, false);
        m_Halo = halo.transform;

        // Arrow: a cone (cylinder squashed to a point) pointing down at the object.
        var arrow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        arrow.name = "Arrow";
        var arrowCol = arrow.GetComponent<Collider>();
        if (arrowCol != null) Destroy(arrowCol);
        arrow.GetComponent<Renderer>().sharedMaterial = m_Material;
        arrow.transform.SetParent(transform, false);
        arrow.transform.localScale = new Vector3(0.05f, 0.08f, 0.05f);
        arrow.transform.localRotation = Quaternion.Euler(180f, 0f, 0f); // tip down
        m_Arrow = arrow.transform;
    }

    void SetMaterialColor(Color c)
    {
        if (m_Material == null) return;
        if (m_Material.HasProperty("_BaseColor")) m_Material.SetColor("_BaseColor", c);
        if (m_Material.HasProperty("_Color")) m_Material.SetColor("_Color", c);
        // URP Unlit transparency.
        m_Material.SetFloat("_Surface", 1f);
        m_Material.SetFloat("_Blend", 0f);
        m_Material.renderQueue = 3000;
    }

    void FitTo(Transform target)
    {
        var bounds = WorldBounds(target);
        transform.position = bounds.center;

        float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (size <= 0f) size = 0.1f;

        m_HaloBaseScale = Vector3.one * size * m_Padding;
        m_Halo.localScale = m_HaloBaseScale;

        float top = bounds.extents.y + m_ArrowHeight;
        m_Arrow.localPosition = new Vector3(0f, top, 0f);
        m_ArrowBaseY = top;
    }

    static Bounds WorldBounds(Transform target)
    {
        var renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(target.position, Vector3.one * 0.1f);
        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    void LateUpdate()
    {
        if (m_Target == null) return;

        // Follow the target (it may be grabbed and moved).
        FitTo(m_Target);

        float t = Time.time * m_PulseSpeed;
        float pulse = 1f + Mathf.Sin(t) * m_PulseAmount;
        m_Halo.localScale = m_HaloBaseScale * pulse;
        m_Arrow.localPosition = new Vector3(0f, m_ArrowBaseY + Mathf.Sin(t) * m_ArrowBob, 0f);
    }
}
