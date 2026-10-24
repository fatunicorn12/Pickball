using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// The floor telegraph under a falling apple: a fixed ring for WHERE, and a
    /// filled disc growing inside it for WHEN. The disc reaches the ring at the
    /// moment of impact, so a glance at the floor reads as a countdown — a small
    /// dot is an apple you can ignore for now, a full disc is one to leave.
    ///
    /// The ring pulses faster as the disc fills, so the urgency is also there in
    /// peripheral vision, where size changes are hard to judge.
    /// </summary>
    public class ImpactMarker : MonoBehaviour
    {
        [SerializeField] private Transform fill;
        [SerializeField] private Renderer ring;

        [Tooltip("Disc size at spawn, as a fraction of the ring.")]
        [SerializeField] private float startFill = 0.12f;

        [SerializeField] private Color ringColour = new(1f, 0.55f, 0.2f, 0.9f);

        float _start;
        float _duration = 1f;
        MaterialPropertyBlock _block;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            _start = Time.time;
            _block = new MaterialPropertyBlock();
        }

        /// <summary>Called by the barrage with the fall time it solved for.</summary>
        public void Begin(float secondsToImpact)
        {
            _start = Time.time;
            _duration = Mathf.Max(secondsToImpact, 0.01f);
        }

        void Update()
        {
            float t = Mathf.Clamp01((Time.time - _start) / _duration);

            if (fill != null)
            {
                // Eased in, so the disc spends its early life small and closes
                // the last stretch quickly — matching how a fall accelerates.
                float s = Mathf.Lerp(startFill, 1f, t * t);
                fill.localScale = new Vector3(s, s, 1f);
            }

            if (ring != null)
            {
                float hz = Mathf.Lerp(1.5f, 7f, t);
                float pulse = 0.65f + 0.35f * Mathf.Sin((Time.time - _start) * hz * Mathf.PI * 2f);
                var c = ringColour;
                c.a *= t >= 1f ? 1f : pulse;
                ring.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, c);
                ring.SetPropertyBlock(_block);
            }
        }
    }
}
