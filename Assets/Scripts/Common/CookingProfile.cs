using System;
using UnityEngine;

/// <summary>
/// Cooking thresholds and materials per ingredient.
/// Thresholds are in "heat-seconds": time on the burner multiplied by burner heat
/// (Low = 0.5, High = 1). Low heat therefore cooks twice as slowly as High.
/// </summary>
[CreateAssetMenu(fileName = "CookingProfile", menuName = "Love at First Bite/Cooking Profile")]
public class CookingProfile : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public IngredientKind kind = IngredientKind.Steak;

        [Header("Heat-seconds needed to reach each state")]
        public float almostDoneAt = 10f;
        public float cookedAt = 20f;
        public float burnedAt = 40f;

        [Header("Material for each state")]
        public Material raw;
        public Material almostDone;
        public Material cooked;
        public Material burned;

        /// <summary>State reached after the given amount of accumulated heat-seconds.</summary>
        public CookState StateAt(float heatSeconds)
        {
            if (heatSeconds >= burnedAt) return CookState.Burned;
            if (heatSeconds >= cookedAt) return CookState.Cooked;
            if (heatSeconds >= almostDoneAt) return CookState.AlmostDone;
            return CookState.Raw;
        }

        public Material MaterialFor(CookState state)
        {
            switch (state)
            {
                case CookState.AlmostDone: return almostDone;
                case CookState.Cooked: return cooked;
                case CookState.Burned: return burned;
                default: return raw;
            }
        }

        /// <summary>True when thresholds are positive and strictly increasing and all 4 materials are set.</summary>
        public bool IsValid(out string error)
        {
            if (almostDoneAt <= 0f || cookedAt <= almostDoneAt || burnedAt <= cookedAt)
            {
                error = $"{kind}: thresholds must satisfy 0 < almostDoneAt < cookedAt < burnedAt " +
                        $"(got {almostDoneAt}, {cookedAt}, {burnedAt}).";
                return false;
            }
            if (raw == null || almostDone == null || cooked == null || burned == null)
            {
                error = $"{kind}: all 4 materials must be assigned.";
                return false;
            }
            error = null;
            return true;
        }
    }

    [SerializeField] Entry[] m_Entries = Array.Empty<Entry>();

    public Entry[] Entries => m_Entries;

    public bool TryGetEntry(IngredientKind kind, out Entry entry)
    {
        if (m_Entries != null)
        {
            foreach (var e in m_Entries)
            {
                if (e != null && e.kind == kind)
                {
                    entry = e;
                    return true;
                }
            }
        }
        entry = null;
        return false;
    }

    /// <summary>Replaces all entries. Used by editor builders and tests.</summary>
    public void SetEntries(Entry[] entries) => m_Entries = entries ?? Array.Empty<Entry>();

    /// <summary>Validates every entry. Returns false and a message on the first problem.</summary>
    public bool Validate(out string error)
    {
        if (m_Entries == null || m_Entries.Length == 0)
        {
            error = "Profile has no entries.";
            return false;
        }
        foreach (var e in m_Entries)
        {
            if (e == null)
            {
                error = "Profile contains an empty entry.";
                return false;
            }
            if (!e.IsValid(out error)) return false;
        }
        error = null;
        return true;
    }
}
