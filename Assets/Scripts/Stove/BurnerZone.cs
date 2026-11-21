using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marker for the area above a burner. Put it on an empty object with an enabled trigger collider.
/// A Pan whose HeatSensor overlaps this zone receives the heat of the linked Burner.
/// </summary>
public class BurnerZone : MonoBehaviour
{
    static readonly List<BurnerZone> s_Active = new List<BurnerZone>();

    [SerializeField] Burner m_Burner;

    [Tooltip("Where the base of a pan rests, in the local space of this zone. The default is the height of the burner ring.")]
    [SerializeField] Vector3 m_RestOffset = new Vector3(0f, -0.016f, 0f);

    /// <summary>All enabled zones in the scene. Used by PanSnap so it does not have to search the scene.</summary>
    public static IReadOnlyList<BurnerZone> Active => s_Active;

    public Burner Burner => m_Burner;

    /// <summary>World position where the center of a pan base should rest on this burner.</summary>
    public Vector3 RestPosition => transform.TransformPoint(m_RestOffset);

    void OnEnable()
    {
        if (!s_Active.Contains(this)) s_Active.Add(this);
    }

    void OnDisable() => s_Active.Remove(this);

    /// <summary>Links the zone to a burner from code (editor builders and tests).</summary>
    public void Bind(Burner burner) => m_Burner = burner;

    /// <summary>Heat of this zone, 0 when it has no burner.</summary>
    public float Heat => m_Burner != null ? m_Burner.Heat : 0f;

    /// <summary>Highest heat among the given zones. Null entries are ignored. Empty returns 0.</summary>
    public static float MaxHeat(IReadOnlyList<BurnerZone> zones)
    {
        float max = 0f;
        if (zones == null) return max;
        for (int i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];
            if (zone == null) continue;
            if (zone.Heat > max) max = zone.Heat;
        }
        return max;
    }
}
