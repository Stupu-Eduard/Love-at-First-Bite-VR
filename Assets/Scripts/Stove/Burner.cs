using System;
using UnityEngine;

/// <summary>
/// One stove burner. The knob sets its position, the position decides the heat:
/// Off = 0, Low = 0.5, High = 1. Contract from Documentation/Demo_Plan.md, section 4 (IsOn, Heat).
/// </summary>
public class Burner : MonoBehaviour
{
    public const float LowHeat = 0.5f;
    public const float HighHeat = 1f;

    [SerializeField] KnobPosition m_Position = KnobPosition.Off;

    // Last position that listeners were told about.
    KnobPosition m_Announced;

    /// <summary>Raised when the position changes, with the new position.</summary>
    public event Action<KnobPosition> OnPositionChanged;

    public KnobPosition Position => m_Position;
    public bool IsOn => m_Position != KnobPosition.Off;
    public float Heat => HeatFor(m_Position);

    public static float HeatFor(KnobPosition position)
    {
        switch (position)
        {
            case KnobPosition.Low: return LowHeat;
            case KnobPosition.High: return HighHeat;
            default: return 0f;
        }
    }

    void Awake() => m_Announced = m_Position;

    public void SetPosition(KnobPosition position)
    {
        m_Position = position;
        Announce();
    }

    void Announce()
    {
        if (m_Announced == m_Position) return;
        m_Announced = m_Position;
        OnPositionChanged?.Invoke(m_Position);
    }

#if UNITY_EDITOR
    // Changing the position in the Inspector during Play Mode notifies listeners too.
    void OnValidate()
    {
        if (Application.isPlaying) Announce();
    }
#endif
}
