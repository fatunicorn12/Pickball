using UnityEngine;

namespace Pickleball.Sandbox.Combat.Presentation
{
    /// <summary>
    /// Draws an area burst: a ground ring that grows on exactly the curve the
    /// damage uses (<see cref="AreaBurst.GrowthFraction"/>), so the edge you see
    /// is the edge that hits.
    ///
    /// Optional dressing, all built into the prefab by the setup script:
    ///  - <b>Bars</b> standing on the ring with heights 0.2 + 0.4·|sin 3θ| — the
    ///    rectangles of a Riemann sum, for Area Under the Curve;
    ///  - a <b>radius line</b> sweeping once round, like θ on the unit circle,
    ///    for Unit Circle;
    ///  - a faint <b>fill disc</b> for the area itself.
    /// </summary>
    public class RingBurstEffect : PooledEffect
    {
        [SerializeField] private LineRenderer ring;
        [Tooltip("Optional. Two-point line from the centre to the ring edge.")]
        [SerializeField] private LineRenderer radiusLine;
        [Tooltip("Optional. Children stood around the ring; their angle is read " +
                 "from where the setup placed them.")]
        [SerializeField] private Transform[] bars = new Transform[0];
        [Tooltip("Optional. Flat unit disc scaled to the ring.")]
        [SerializeField] private Transform fill;

        [Header("Shape")]
        [Tooltip("Ring segments. 48 is smooth at 3 m.")]
        [Range(8, 128)] [SerializeField] private int segments = 48;
        [Tooltip("Height of the shortest bar, in metres.")]
        [Min(0f)] [SerializeField] private float barBaseHeight = 0.2f;
        [Tooltip("Extra height of the tallest bar, in metres (the sine's amplitude).")]
        [Min(0f)] [SerializeField] private float barWaveHeight = 0.4f;
        [Tooltip("Lobes of |sin(nθ)| round the ring. 3 = the house Riemann look.")]
        [Min(1)] [SerializeField] private int barLobes = 3;
        [Tooltip("Turns the radius line makes while the ring grows.")]
        [SerializeField] private float radiusTurns = 1f;

        [Header("Timing")]
        [Tooltip("Seconds to grow when the cue gives none (instant bursts).")]
        [Min(0.01f)] [SerializeField] private float defaultGrowSeconds = 0.15f;
        [Tooltip("Seconds to fade once full size.")]
        [Min(0.01f)] [SerializeField] private float fadeSeconds = 0.35f;
        [Tooltip("Brightness multiplier on the tint (×).")]
        [Min(0f)] [SerializeField] private float intensity = 2f;
        [Tooltip("Opacity of the fill disc relative to the ring (0-1).")]
        [Range(0f, 1f)] [SerializeField] private float fillOpacity = 0.25f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        MaterialPropertyBlock _block;
        Renderer[] _barRenderers;
        float[] _barAngles;
        Renderer _fillRenderer;
        float _age, _radius, _grow;
        Color _tint;

        public override void Play(in EffectCue cue)
        {
            _block ??= new MaterialPropertyBlock();
            if (_barRenderers == null)
            {
                // Angles are read once, from where the setup stood each bar:
                // re-reading them from a bar shrunk to the centre would lose them.
                _barRenderers = new Renderer[bars.Length];
                _barAngles = new float[bars.Length];
                for (int i = 0; i < bars.Length; i++)
                {
                    if (bars[i] == null) continue;
                    _barRenderers[i] = bars[i].GetComponentInChildren<Renderer>();
                    Vector3 p = bars[i].localPosition;
                    _barAngles[i] = Mathf.Atan2(p.z, p.x);
                }
                if (fill != null) _fillRenderer = fill.GetComponentInChildren<Renderer>();
            }
            if (ring != null)
            {
                ring.useWorldSpace = false;
                ring.loop = true;
                ring.positionCount = segments;
            }
            if (radiusLine != null)
            {
                radiusLine.useWorldSpace = false;
                radiusLine.positionCount = 2;
            }

            _age = 0f;
            _radius = Mathf.Max(0.05f, cue.Radius);
            _grow = cue.GrowSeconds > 0f ? cue.GrowSeconds : defaultGrowSeconds;
            _tint = cue.Color * intensity;
            Apply();
        }

        void LateUpdate()
        {
            _age += Time.deltaTime;
            if (_age >= _grow + fadeSeconds) { Finish(); return; }
            Apply();
        }

        void Apply()
        {
            float g = AreaBurst.GrowthFraction(_age, _grow);
            float r = _radius * g;
            float alpha = _age <= _grow ? 1f : 1f - Mathf.Clamp01((_age - _grow) / fadeSeconds);
            var c = new Color(_tint.r, _tint.g, _tint.b, alpha);

            if (ring != null)
            {
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    ring.SetPosition(i, new Vector3(Mathf.Cos(a) * r, 0.03f, Mathf.Sin(a) * r));
                }
                ring.startColor = ring.endColor = c;
            }

            if (radiusLine != null)
            {
                float a = g * radiusTurns * Mathf.PI * 2f;
                radiusLine.SetPosition(0, new Vector3(0f, 0.04f, 0f));
                radiusLine.SetPosition(1, new Vector3(Mathf.Cos(a) * r, 0.04f, Mathf.Sin(a) * r));
                radiusLine.startColor = radiusLine.endColor = c;
            }

            _block.SetColor(BaseColorId, c);
            for (int i = 0; i < bars.Length; i++)
            {
                var bar = bars[i];
                if (bar == null) continue;
                float theta = _barAngles[i];
                float h = (barBaseHeight + barWaveHeight * Mathf.Abs(Mathf.Sin(barLobes * theta))) * g;
                var dir = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta));
                bar.localPosition = dir * r + Vector3.up * (h * 0.5f);
                bar.localScale = new Vector3(bar.localScale.x, Mathf.Max(0.001f, h), bar.localScale.z);
                if (_barRenderers[i] != null) _barRenderers[i].SetPropertyBlock(_block);
            }

            if (fill != null)
            {
                fill.localScale = new Vector3(r * 2f, r * 2f, 1f);
                if (_fillRenderer != null)
                {
                    _block.SetColor(BaseColorId, new Color(c.r, c.g, c.b, alpha * fillOpacity));
                    _fillRenderer.SetPropertyBlock(_block);
                }
            }
        }
    }
}
