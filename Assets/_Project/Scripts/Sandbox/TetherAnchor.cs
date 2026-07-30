using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// A point the tether can latch onto. Only objects carrying this component
    /// are valid targets — the hook is reactive to placed anchors, not to world
    /// geometry, so swing routes are authored rather than emergent.
    ///
    /// The attach point is a child transform (the hook), not the object origin,
    /// so the rope meets the model where it visually should.
    /// </summary>
    public class TetherAnchor : MonoBehaviour
    {
        [Tooltip("Where the rope actually attaches. Defaults to this transform.")]
        [SerializeField] private Transform attachPoint;

        [Tooltip("Beyond this the anchor cannot be latched at all. Set by the " +
                 "setup script from AnchorRange so every anchor agrees.")]
        [SerializeField] private float maxRange = 9f;

        [Header("Feedback")]
        [SerializeField] private Renderer highlightRenderer;
        [Tooltip("Out of range: dark and inert, so distance is readable at a glance.")]
        [SerializeField] private Color dormantColor = new(0.22f, 0.3f, 0.36f, 1f);
        [Tooltip("In range but not aimed at: lit and breathing — 'you could use me'.")]
        [SerializeField] private Color inRangeColor = new(0.35f, 0.75f, 0.9f, 1f);
        [Tooltip("Aimed at: the one that will actually be grabbed.")]
        [SerializeField] private Color targetedColor = new(1f, 0.95f, 0.55f, 1f);
        [SerializeField] private Light glow;
        [SerializeField] private float dormantGlow = 0f;
        [SerializeField] private float inRangeGlow = 2f;
        [SerializeField] private float targetedGlow = 5.5f;
        [Tooltip("Pulse rate while in range. Movement is what catches the eye " +
                 "in a busy scene; a static glow gets lost against the academy.")]
        [SerializeField] private float pulseCycleSeconds = 1.1f;
        [SerializeField] private float pulseDepth = 0.35f;

        static readonly System.Collections.Generic.List<TetherAnchor> All = new();
        public static System.Collections.Generic.IReadOnlyList<TetherAnchor> Anchors => All;

        /// <summary>How an anchor is currently presented to the player.</summary>
        public enum State { Dormant, InRange, Targeted }

        Material _mat;
        float _rangeBlend, _targetBlend, _phase;

        public Vector3 Position => attachPoint != null ? attachPoint.position : transform.position;
        public float MaxRange => maxRange;

        void Awake()
        {
            if (attachPoint == null) attachPoint = transform;
            if (highlightRenderer != null)
            {
                _mat = new Material(highlightRenderer.sharedMaterial);
                highlightRenderer.sharedMaterial = _mat;
            }
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>
        /// Driven every frame by the player's tether system. Three states rather
        /// than two: "in range" has to be legible on its own, otherwise the only
        /// way to discover whether an anchor is reachable is to aim at it and
        /// see whether anything lights up.
        /// </summary>
        public void SetState(State state)
        {
            float dt = Time.deltaTime;
            _rangeBlend = Mathf.MoveTowards(_rangeBlend, state == State.Dormant ? 0f : 1f, dt * 4f);
            _targetBlend = Mathf.MoveTowards(_targetBlend, state == State.Targeted ? 1f : 0f, dt * 8f);
            _phase += dt;

            // Breathe while reachable; hold steady once actually targeted, so
            // the pulse stopping is itself the "locked on" signal.
            float pulse = 1f;
            if (pulseCycleSeconds > 0.01f)
            {
                float s = (Mathf.Sin(_phase * Mathf.PI * 2f / pulseCycleSeconds) + 1f) * 0.5f;
                pulse = Mathf.Lerp(1f - pulseDepth, 1f, s);
                pulse = Mathf.Lerp(pulse, 1f, _targetBlend);
            }

            var c = Color.Lerp(dormantColor, inRangeColor, _rangeBlend);
            c = Color.Lerp(c, targetedColor, _targetBlend);

            if (_mat != null)
            {
                if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", c);
                if (_mat.HasProperty("_EmissionColor"))
                {
                    _mat.EnableKeyword("_EMISSION");
                    _mat.SetColor("_EmissionColor", c * (2f * _rangeBlend * pulse + 3f * _targetBlend));
                }
            }
            if (glow != null)
            {
                float g = Mathf.Lerp(dormantGlow, inRangeGlow, _rangeBlend);
                g = Mathf.Lerp(g, targetedGlow, _targetBlend);
                glow.intensity = g * pulse;
                glow.enabled = g > 0.01f;
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.9f);
            Vector3 p = attachPoint != null ? attachPoint.position : transform.position;
            Gizmos.DrawWireSphere(p, 0.25f);
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.12f);
            Gizmos.DrawWireSphere(p, maxRange);
        }
    }
}
