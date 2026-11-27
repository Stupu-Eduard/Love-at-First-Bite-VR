using UnityEngine;

namespace LoveAtFirstBite.Searing
{
    /// <summary>
    /// Per-side searing for an ingredient. While the ingredient rests on a hot pan, the side
    /// currently facing DOWN accumulates "cook amount" and the IngredientSear shader darkens it.
    /// Flip the ingredient and the other side starts to sear.
    ///
    /// This is additive and visual: it reads the pan's heat (from the Pan it is touching) and
    /// drives the shader's _CookDown / _CookUp. It does not replace the Cookable logic; put it on
    /// an ingredient whose renderer uses the LoveAtFirstBite/IngredientSear shader.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class SearCooking : MonoBehaviour
    {
        static readonly int CookDownId = Shader.PropertyToID("_CookDown");
        static readonly int CookUpId = Shader.PropertyToID("_CookUp");

        [Tooltip("Renderer whose material uses the IngredientSear shader. Auto-found if left empty.")]
        [SerializeField] Renderer m_Renderer;

        [Tooltip("How fast a side sears, per heat-second. 1 cook amount = cooked, 2 = burnt.")]
        [SerializeField] float m_SearRate = 0.05f;

        [Tooltip("If true, uses a per-instance material so each ingredient sears independently.")]
        [SerializeField] bool m_UseInstanceMaterial = true;

        Material m_Material;
        Rigidbody m_Body;
        float m_CookDown;
        float m_CookUp;
        Pan m_PanTouching;

        void Awake()
        {
            m_Body = GetComponent<Rigidbody>();
            if (m_Renderer == null) m_Renderer = GetComponentInChildren<Renderer>();
            if (m_Renderer != null)
                m_Material = m_UseInstanceMaterial ? m_Renderer.material : m_Renderer.sharedMaterial;
        }

        void OnCollisionStay(Collision collision)
        {
            // Remember a pan we are resting on, so we can read its heat.
            var pan = collision.rigidbody != null ? collision.rigidbody.GetComponent<Pan>() : null;
            if (pan == null) pan = collision.collider.GetComponentInParent<Pan>();
            if (pan != null) m_PanTouching = pan;
        }

        void OnCollisionExit(Collision collision)
        {
            var pan = collision.rigidbody != null ? collision.rigidbody.GetComponent<Pan>() : null;
            if (pan == null) pan = collision.collider.GetComponentInParent<Pan>();
            if (pan != null && pan == m_PanTouching) m_PanTouching = null;
        }

        void FixedUpdate()
        {
            if (m_Material == null || m_PanTouching == null) return;

            float heat = m_PanTouching.CurrentHeat;
            if (heat <= 0f) return;

            // Which world direction is this object's local "down" pointing? If the object's up
            // points up, its bottom faces the pan, so the DOWN side sears. When flipped, the
            // bottom (now facing up) stops and the other side sears.
            float facingUp = Vector3.Dot(transform.up, Vector3.up); // +1 upright, -1 flipped

            float add = heat * m_SearRate * Time.fixedDeltaTime;

            // Upright: the shader's "down" region (worldUp<0) is the side on the pan -> grow _CookDown.
            // Flipped: the shader's "up" region is now on the pan -> grow _CookUp.
            if (facingUp >= 0f)
                m_CookDown = Mathf.Min(2f, m_CookDown + add);
            else
                m_CookUp = Mathf.Min(2f, m_CookUp + add);

            m_Material.SetFloat(CookDownId, m_CookDown);
            m_Material.SetFloat(CookUpId, m_CookUp);
        }

        /// <summary>Copies sear progress onto another SearCooking (used when the ingredient is cut).</summary>
        public void CopyTo(SearCooking other)
        {
            if (other == null) return;
            other.m_CookDown = m_CookDown;
            other.m_CookUp = m_CookUp;
            if (other.m_Material != null)
            {
                other.m_Material.SetFloat(CookDownId, m_CookDown);
                other.m_Material.SetFloat(CookUpId, m_CookUp);
            }
        }
    }
}
