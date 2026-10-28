using UnityEngine;

namespace Pickleball.Sandbox.Magic.Presentation
{
    /// <summary>
    /// Draws a <see cref="FreezeZone"/>: a disc of frost exactly the zone's
    /// radius, a bright rim that drains round like a clock as the zone runs out
    /// (so its time left reads at a glance), ice crystals that shoot up round
    /// the edge, drifting snow and a cold light. On placement a puff of frost
    /// bursts outward; on expiry the crystals sink, the frost fades and the
    /// snow stops.
    ///
    /// Presentation only, on a CHILD of the zone (which destroys itself on
    /// expiry): it detaches in the <see cref="FreezeZone.Expired"/> handler and
    /// fades out over <see cref="fadeSeconds"/>.
    ///
    /// <b>Optional model:</b> the spell's <see cref="SpellDefinition.ModelPrefab"/>
    /// (ice crystals or a frost ring) replaces the procedural crystals. It is
    /// placed at the centre and scaled by the radius times
    /// <see cref="SpellDefinition.ModelScale"/>, so author it for a 1 m radius.
    /// </summary>
    public class FreezeZoneVisual : MonoBehaviour
    {
        const float TwoPi = Mathf.PI * 2f;
        const int RimSegments = 64;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("Source")]
        [Tooltip("The zone drawn. Found on a parent when empty.")]
        [SerializeField] private FreezeZone zone;

        [Header("Parts")]
        [Tooltip("Flat frost disc, a unit quad scaled to the zone's diameter.")]
        [SerializeField] private Renderer frostDisc;
        [Tooltip("Local-space line round the rim; drawn as an arc of the time left.")]
        [SerializeField] private LineRenderer timerRim;
        [Tooltip("Flat glow ring under the rim.")]
        [SerializeField] private Renderer rimGlow;
        [Tooltip("Parent of the procedural ice crystals. Each child is placed round the rim " +
                 "in the direction it was built at.")]
        [SerializeField] private Transform crystals;
        [Tooltip("Looping snow inside the zone. Its shape radius is set to the zone's.")]
        [SerializeField] private ParticleSystem snow;
        [Tooltip("One-shot frost puff on placement.")]
        [SerializeField] private ParticleSystem placePuff;
        [Tooltip("Cold point light at the centre.")]
        [SerializeField] private Light coldLight;

        [Header("Colour")]
        [Tooltip("Frost disc tint (alpha-blended); alpha is opacity. Saturated, so it reads " +
                 "on a white floor as well as a dark one.")]
        [SerializeField] private Color frostColour = new(0.45f, 0.8f, 1f, 0.6f);
        [Tooltip("Timer rim tint (alpha-blended); alpha is opacity.")]
        [SerializeField] private Color rimColour = new(0.12f, 0.58f, 1f, 1f);
        [Tooltip("Additive glow under the rim; alpha is opacity.")]
        [SerializeField] private Color glowColour = new(0.55f, 0.95f, 1f, 1f);
        [Tooltip("HDR brightness multiplier on the rim (x).")]
        [Min(0f)] [SerializeField] private float intensity = 2.5f;

        [Header("Timing")]
        [Tooltip("Seconds the frost spreads and the crystals rise after placement.")]
        [Min(0.01f)] [SerializeField] private float growSeconds = 0.3f;
        [Tooltip("Seconds everything fades after expiry.")]
        [Min(0.05f)] [SerializeField] private float fadeSeconds = 0.5f;
        [Tooltip("Frost shimmer, in pulses per second.")]
        [Min(0f)] [SerializeField] private float shimmerHz = 0.9f;
        [Tooltip("Cold light intensity.")]
        [Min(0f)] [SerializeField] private float lightIntensity = 1.6f;
        [Tooltip("Ring radius within the glow texture as a fraction of the quad's half-size " +
                 "(the generated WellHalo ring sits at 0.55).")]
        [Min(0.05f)] [SerializeField] private float ringTextureFill = 0.55f;

        MaterialPropertyBlock _block;
        Vector3[] _rim;
        Vector3[] _crystalDirections;
        Vector3[] _crystalScales;
        GameObject _model;
        float _radius = 1f, _age, _fadeAge;
        bool _expired;

        void OnEnable()
        {
            _block = new MaterialPropertyBlock();
            _rim = new Vector3[RimSegments + 1];
            CaptureCrystals();

            if (zone == null) zone = GetComponentInParent<FreezeZone>();
            if (zone == null)
            {
                Destroy(gameObject);
                return;
            }
            zone.Placed += OnPlaced;
            zone.Expired += OnExpired;
            _expired = false;
        }

        void OnDisable() => Unsubscribe();

        void Unsubscribe()
        {
            if (zone == null) return;
            zone.Placed -= OnPlaced;
            zone.Expired -= OnExpired;
        }

        void CaptureCrystals()
        {
            int n = crystals != null ? crystals.childCount : 0;
            _crystalDirections = new Vector3[n];
            _crystalScales = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var c = crystals.GetChild(i);
                Vector3 flat = c.localPosition;
                flat.y = 0f;
                _crystalDirections[i] = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
                _crystalScales[i] = c.localScale;
            }
        }

        // ------------------------------------------------------------ events

        void OnPlaced(FreezeZone source)
        {
            _radius = Mathf.Max(0.1f, source.Radius);
            var spell = source.Spell;
            if (_model == null && spell != null && spell.ModelPrefab != null)
            {
                _model = SpellModel.Spawn(spell.ModelPrefab, transform, _radius * spell.ModelScale);
                if (_model != null && crystals != null) crystals.gameObject.SetActive(false);
            }
            if (snow != null)
            {
                var shape = snow.shape;
                shape.radius = _radius * 0.9f;
                var emission = snow.emission;
                emission.rateOverTime = 14f * _radius * _radius;
                snow.Play(true);
            }
            if (placePuff != null)
            {
                var shape = placePuff.shape;
                shape.radius = _radius * 0.3f;
                var main = placePuff.main;
                main.startSpeed = new ParticleSystem.MinMaxCurve(_radius * 1.5f, _radius * 3f);
                placePuff.Play(true);
            }
            if (coldLight != null) coldLight.range = _radius * 2.5f;
            Animate(0f, source.RemainingNormalised, 1f);
        }

        void OnExpired(FreezeZone source)
        {
            Unsubscribe();
            zone = null;
            transform.SetParent(null, true);
            _expired = true;
            _fadeAge = 0f;
            if (snow != null) snow.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        // --------------------------------------------------------- animation

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (!_expired)
            {
                if (zone == null) return;
                _age = zone.Age;
                _radius = Mathf.Max(0.1f, zone.Radius);
                Animate(_age, zone.RemainingNormalised, 1f);
                return;
            }

            _fadeAge += dt;
            float fade = 1f - Mathf.Clamp01(_fadeAge / fadeSeconds);
            Animate(_age + _fadeAge, 0f, fade);
            if (_fadeAge >= Mathf.Max(fadeSeconds, 1.2f)) Destroy(gameObject);   // snow settles
        }

        void Animate(float age, float remaining, float fade)
        {
            float grow = Mathf.Clamp01(age / growSeconds);
            grow = 1f - (1f - grow) * (1f - grow);
            float shimmer = 0.88f + 0.12f * Mathf.Sin(age * shimmerHz * TwoPi);
            float spread = grow * fade;

            if (frostDisc != null)
            {
                float d = _radius * 2f * Mathf.Lerp(0.6f, 1f, grow);
                frostDisc.transform.localScale = new Vector3(d, d, 1f);
                Tint(frostDisc, frostColour, 1f, spread * shimmer);
            }
            if (rimGlow != null)
            {
                float q = _radius * 2f / ringTextureFill * Mathf.Lerp(0.6f, 1f, grow);
                rimGlow.transform.localScale = new Vector3(q, q, 1f);
                Tint(rimGlow, glowColour, intensity * 0.6f, 0.6f * spread);
            }
            if (timerRim != null)
            {
                DrawArc(timerRim, _radius * Mathf.Lerp(0.6f, 1f, grow), _expired ? 1f : remaining);
                Tint(timerRim, rimColour, 1f, spread);
            }
            if (crystals != null && crystals.gameObject.activeSelf)
            {
                float rise = _expired ? fade : grow;
                for (int i = 0; i < _crystalDirections.Length && i < crystals.childCount; i++)
                {
                    var c = crystals.GetChild(i);
                    c.localPosition = _crystalDirections[i] * _radius * 0.94f;
                    // Each crystal rises a beat after the one before: a wave round the rim.
                    float stagger = Mathf.Clamp01(rise * 1.4f - (float)i / Mathf.Max(1, _crystalDirections.Length) * 0.4f);
                    c.localScale = _crystalScales[i] * stagger;
                }
            }
            if (_model != null) _model.transform.localScale = Vector3.one * (_radius * Mathf.Max(0.01f, spread));
            if (coldLight != null) coldLight.intensity = lightIntensity * spread * shimmer;
        }

        /// <summary>The rim as an arc of <paramref name="fraction"/> of a turn,
        /// starting at the front and draining clockwise.</summary>
        void DrawArc(LineRenderer line, float radius, float fraction)
        {
            fraction = Mathf.Clamp01(fraction);
            int count = Mathf.Max(2, Mathf.CeilToInt(fraction * RimSegments) + 1);
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.PI * 0.5f - fraction * TwoPi * i / (count - 1);
                _rim[i] = new Vector3(Mathf.Cos(a) * radius, 0.05f, Mathf.Sin(a) * radius);
            }
            line.loop = false;
            line.positionCount = count;
            line.SetPositions(_rim);
            line.enabled = fraction > 0.001f;
        }

        void Tint(Renderer target, Color colour, float brightness, float alpha)
        {
            _block.SetColor(BaseColorId, new Color(colour.r * brightness, colour.g * brightness,
                                                   colour.b * brightness, colour.a * Mathf.Clamp01(alpha)));
            target.SetPropertyBlock(_block);
        }
    }
}
