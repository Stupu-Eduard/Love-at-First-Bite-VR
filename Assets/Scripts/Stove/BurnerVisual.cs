using UnityEngine;

/// <summary>
/// Shows the state of a Burner: a glowing ring (URP emission, via MaterialPropertyBlock so no material is copied)
/// and a flame object that is hidden when off and scales with the heat.
/// The ring material must have the _EMISSION keyword enabled (the editor builder does this).
/// </summary>
public class BurnerVisual : MonoBehaviour
{
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [SerializeField] Burner m_Burner;
    [SerializeField] Renderer m_Ring;
    [SerializeField] Transform m_Flame;
    [SerializeField, ColorUsage(false, true)] Color m_RingColor = new Color(1f, 0.3f, 0.05f);
    [SerializeField] float m_HighEmission = 3f;
    [SerializeField] float m_FlickerAmount = 0.06f;
    [SerializeField] float m_FlickerSpeed = 18f;

    MaterialPropertyBlock m_Block;
    Vector3 m_FlameBaseScale = Vector3.one;
    bool m_HasFlameBase;
    float m_Heat;

    /// <summary>Emission multiplier for a heat value. Off gives 0, High gives highEmission.</summary>
    public static float EmissionFor(float heat, float highEmission) => Mathf.Clamp01(heat) * highEmission;

    /// <summary>Flame size factor for a heat value: 0 when off, 0.5 on Low, 1 on High.</summary>
    public static float FlameScaleFor(float heat) => Mathf.Clamp01(heat);

    /// <summary>
    /// Links the visual to its parts from code (tests). Outside Play Mode it only stores the references,
    /// so it never changes scene objects (hiding the flame would be saved into prefabs).
    /// </summary>
    public void Configure(Burner burner, Renderer ring, Transform flame)
    {
        bool live = Application.isPlaying && isActiveAndEnabled;
        if (live) Unsubscribe();
        m_Burner = burner;
        m_Ring = ring;
        m_Flame = flame;
        m_HasFlameBase = false;
        if (live) { Subscribe(); Refresh(); }
    }

    void OnEnable()
    {
        Subscribe();
        Refresh();
    }

    void OnDisable() => Unsubscribe();

    void Subscribe()
    {
        if (m_Burner != null) m_Burner.OnPositionChanged += OnPositionChanged;
    }

    void Unsubscribe()
    {
        if (m_Burner != null) m_Burner.OnPositionChanged -= OnPositionChanged;
    }

    void OnPositionChanged(KnobPosition position) => Refresh();

    void Refresh()
    {
        m_Heat = m_Burner != null ? m_Burner.Heat : 0f;

        if (m_Ring != null)
        {
            m_Ring.enabled = m_Heat > 0f; // an unlit ring would otherwise cover the hob with a dark disc
            if (m_Block == null) m_Block = new MaterialPropertyBlock();
            m_Ring.GetPropertyBlock(m_Block);
            m_Block.SetColor(EmissionColorId, m_RingColor * EmissionFor(m_Heat, m_HighEmission));
            m_Ring.SetPropertyBlock(m_Block);
        }

        if (m_Flame != null)
        {
            if (!m_HasFlameBase)
            {
                m_FlameBaseScale = m_Flame.localScale;
                m_HasFlameBase = true;
            }
            m_Flame.gameObject.SetActive(m_Heat > 0f);
            m_Flame.localScale = m_FlameBaseScale * FlameScaleFor(m_Heat);
        }
    }

    // Small flicker so the flame does not look frozen. Only runs while the burner is on.
    void Update()
    {
        if (m_Flame == null || m_Heat <= 0f || !m_HasFlameBase) return;

        float flicker = 1f + m_FlickerAmount * Mathf.Sin(Time.time * m_FlickerSpeed);
        m_Flame.localScale = m_FlameBaseScale * (FlameScaleFor(m_Heat) * flicker);
    }
}
