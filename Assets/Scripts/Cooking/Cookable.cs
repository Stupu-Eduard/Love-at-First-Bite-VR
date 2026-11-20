using System;
using UnityEngine;

/// <summary>
/// Accumulates heat and moves through Raw, AlmostDone, Cooked, Burned using a CookingProfile.
/// Heat is measured in heat-seconds (burner heat multiplied by seconds).
/// The state only moves forward. The material is swapped with sharedMaterial, so no instances are created.
/// </summary>
public class Cookable : MonoBehaviour
{
    [SerializeField] IngredientKind m_Kind = IngredientKind.Steak;
    [SerializeField] CookingProfile m_Profile;
    [SerializeField] Renderer m_TargetRenderer;

    float m_HeatSeconds;
    CookState m_State = CookState.Raw;
    bool m_Initialized;

    /// <summary>Raised once per state change, with the new state.</summary>
    public event Action<CookState> OnStateChanged;

    public IngredientKind Kind => m_Kind;
    public CookState State => m_State;
    public float HeatSeconds => m_HeatSeconds;

    void Awake() => Initialize();

    /// <summary>Sets up profile, kind and renderer from code (editor builders and tests).</summary>
    public void Configure(IngredientKind kind, CookingProfile profile, Renderer targetRenderer = null)
    {
        m_Kind = kind;
        m_Profile = profile;
        m_TargetRenderer = targetRenderer;
        m_Initialized = false;
        Initialize();
    }

    void Initialize()
    {
        if (m_Initialized) return;
        m_Initialized = true;

        if (m_TargetRenderer == null) m_TargetRenderer = GetComponentInChildren<Renderer>();
        ApplyMaterial();
    }

    /// <summary>Adds heat * dt heat-seconds. Zero or negative values are ignored.</summary>
    public void AddHeat(float heat, float dt)
    {
        Initialize();

        float amount = heat * dt;
        if (amount <= 0f) return;

        m_HeatSeconds += amount;

        if (!TryGetEntry(out var entry)) return;

        var next = entry.StateAt(m_HeatSeconds);
        if (next <= m_State) return;

        m_State = next;
        ApplyMaterial();
        OnStateChanged?.Invoke(m_State);
    }

    bool TryGetEntry(out CookingProfile.Entry entry)
    {
        entry = null;
        return m_Profile != null && m_Profile.TryGetEntry(m_Kind, out entry);
    }

    void ApplyMaterial()
    {
        if (m_TargetRenderer == null || !TryGetEntry(out var entry)) return;

        var material = entry.MaterialFor(m_State);
        if (material != null) m_TargetRenderer.sharedMaterial = material;
    }

    [ContextMenu("Debug: Add 10 heat-seconds")]
    void DebugAddHeat() => AddHeat(10f, 1f);
}
