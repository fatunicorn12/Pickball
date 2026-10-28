using UnityEngine;

namespace Pickleball.Sandbox.Magic.Presentation
{
    /// <summary>
    /// Draws the caster's channel (Heal) round the player's feet, so how far
    /// along it is and whether it broke never needs the HUD:
    ///  - a ring that fills clockwise from the front as the channel progresses,
    ///    over a faint full track;
    ///  - a rune turning under the feet and a soft glow round the body, both
    ///    brightening with progress, with a green light and rising motes;
    ///  - on completion, a burst: a ring spreading out, a spray of motes and a
    ///    light flash;
    ///  - on a cancel, everything flushes to the fail colour and drains away;
    ///  - a <b>halo</b> over the head repeats the ring (track and progress arc).
    ///    Heal is cast from the aim stance, whose over-the-shoulder camera
    ///    frames the head and cuts off the feet, so the halo is the part of the
    ///    channel the player actually sees (QA F-3); the feet ring serves every
    ///    other view.
    ///
    /// Presentation only: it listens to <see cref="SpellCaster.ChannelStarted"/>,
    /// <see cref="SpellCaster.ChannelCompleted"/> and
    /// <see cref="SpellCaster.ChannelCancelled"/>, and polls
    /// <see cref="SpellCaster.ChannelProgress"/>. It lives on a child of the
    /// player built by Setup Magic; its lines are in local space, so it follows
    /// the player for free.
    ///
    /// <b>Optional model:</b> the channelled spell's
    /// <see cref="SpellDefinition.ModelPrefab"/> (a heal rune) replaces the rune
    /// quad, scaled to the ring's diameter times
    /// <see cref="SpellDefinition.ModelScale"/>; author it for a 1 m diameter.
    /// </summary>
    public class HealChannelVisual : MonoBehaviour
    {
        enum Phase { Idle, Channeling, Completing, Cancelling }

        const float TwoPi = Mathf.PI * 2f;
        const int ArcSegments = 64;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("Source")]
        [Tooltip("The caster whose channel this draws. Found on a parent when empty.")]
        [SerializeField] private SpellCaster caster;

        [Header("Parts")]
        [Tooltip("Local-space line: the progress arc round the feet.")]
        [SerializeField] private LineRenderer progressRing;
        [Tooltip("Local-space looped line: the faint full track under the arc.")]
        [SerializeField] private LineRenderer trackRing;
        [Tooltip("Flat rune quad under the feet; turned and brightened with progress.")]
        [SerializeField] private Renderer rune;
        [Tooltip("Camera-facing glow round the body.")]
        [SerializeField] private Renderer glow;
        [Tooltip("Green point light at the chest.")]
        [SerializeField] private Light glowLight;
        [Tooltip("Looping motes rising round the body while channelling.")]
        [SerializeField] private ParticleSystem motes;
        [Tooltip("One-shot spray on completion.")]
        [SerializeField] private ParticleSystem burst;
        [Tooltip("Flat ring that spreads out on completion.")]
        [SerializeField] private Renderer burstRing;
        [Tooltip("Optional. Local-space line: the progress arc of the halo over the head.")]
        [SerializeField] private LineRenderer haloRing;
        [Tooltip("Optional. Local-space looped line: the halo's faint full track.")]
        [SerializeField] private LineRenderer haloTrack;

        [Header("Colour")]
        [Tooltip("Channel tint: ring, rune, glow, light; alpha is opacity. The ring and rune " +
                 "blend normally, so a saturated green reads on any floor.")]
        [SerializeField] private Color healColour = new(0.18f, 0.88f, 0.34f, 1f);
        [Tooltip("Completion flash tint.")]
        [SerializeField] private Color burstColour = new(1f, 0.95f, 0.55f, 1f);
        [Tooltip("Tint everything drains to on a cancel.")]
        [SerializeField] private Color failColour = new(0.85f, 0.3f, 0.3f, 1f);
        [Tooltip("HDR brightness multiplier (x). Bloom catches above ~1.")]
        [Min(0f)] [SerializeField] private float intensity = 2.5f;

        [Header("Shape")]
        [Tooltip("Radius of the ring round the feet, in metres.")]
        [Min(0.1f)] [SerializeField] private float ringRadius = 0.95f;
        [Tooltip("Height of the ring above the feet, in metres.")]
        [SerializeField] private float ringHeight = 0.06f;
        [Tooltip("Radius of the halo over the head, in metres.")]
        [Min(0.05f)] [SerializeField] private float haloRadius = 0.42f;
        [Tooltip("Height of the halo above the feet, in metres: just over the chibi's head, " +
                 "inside the aim camera's frame.")]
        [SerializeField] private float haloHeight = 1.8f;
        [Tooltip("Rune turn rate, in degrees per second.")]
        [SerializeField] private float runeSpinDegreesPerSecond = 45f;
        [Tooltip("Glow diameter at full progress, in metres.")]
        [Min(0.1f)] [SerializeField] private float glowSize = 2.6f;
        [Tooltip("Burst ring end radius, in metres.")]
        [Min(0.1f)] [SerializeField] private float burstRadius = 2.6f;
        [Tooltip("Ring radius within the burst texture as a fraction of the quad's half-size.")]
        [Min(0.05f)] [SerializeField] private float ringTextureFill = 0.55f;

        [Header("Timing")]
        [Tooltip("Seconds the completion burst lasts.")]
        [Min(0.05f)] [SerializeField] private float burstSeconds = 0.6f;
        [Tooltip("Seconds a cancel takes to drain away.")]
        [Min(0.05f)] [SerializeField] private float cancelSeconds = 0.35f;
        [Tooltip("Motes per second at full progress.")]
        [Min(0f)] [SerializeField] private float moteRate = 45f;
        [Tooltip("Light intensity at full progress.")]
        [Min(0f)] [SerializeField] private float lightIntensity = 3f;

        MaterialPropertyBlock _block;
        Vector3[] _arc;
        Phase _phase;
        float _fxAge, _progress, _brokeAt, _runeAngle;
        SpellCaster _subscribed;
        GameObject _model;
        GameObject _modelSource;

        void OnEnable()
        {
            _block = new MaterialPropertyBlock();
            _arc = new Vector3[ArcSegments + 1];
            if (caster == null) caster = GetComponentInParent<SpellCaster>();
            Subscribe(caster);
            _phase = Phase.Idle;
            ShowAll(false);
        }

        void OnDisable()
        {
            Subscribe(null);
            _phase = Phase.Idle;
            ShowAll(false);
        }

        void Subscribe(SpellCaster next)
        {
            if (_subscribed != null)
            {
                _subscribed.ChannelStarted -= OnStarted;
                _subscribed.ChannelCompleted -= OnCompleted;
                _subscribed.ChannelCancelled -= OnCancelled;
            }
            _subscribed = next;
            if (_subscribed != null)
            {
                _subscribed.ChannelStarted += OnStarted;
                _subscribed.ChannelCompleted += OnCompleted;
                _subscribed.ChannelCancelled += OnCancelled;
            }
        }

        // ------------------------------------------------------------ events

        void OnStarted(SpellDefinition spell)
        {
            EnsureModel(spell);
            _phase = Phase.Channeling;
            _fxAge = 0f;
            _progress = 0f;
            ShowAll(true);
            if (burstRing != null) burstRing.enabled = false;
            if (motes != null) motes.Play(true);
        }

        void OnCompleted(SpellDefinition spell)
        {
            _phase = Phase.Completing;
            _fxAge = 0f;
            _progress = 1f;
            if (burst != null) burst.Play(true);
            if (motes != null) motes.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (burstRing != null) burstRing.enabled = true;
        }

        void OnCancelled(SpellDefinition spell, ChannelStopReason reason)
        {
            if (_phase != Phase.Channeling) return;   // a refusal: the HUD says why
            _phase = Phase.Cancelling;
            _fxAge = 0f;
            _brokeAt = _progress;
            if (motes != null) motes.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        // --------------------------------------------------------- animation

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            _fxAge += dt;
            _runeAngle += runeSpinDegreesPerSecond * dt * (_phase == Phase.Channeling ? 1f + 2f * _progress : 1f);

            switch (_phase)
            {
                case Phase.Channeling:
                    _progress = caster != null ? caster.ChannelProgress : 0f;
                    Draw(_progress, healColour, Mathf.Clamp01(_fxAge / 0.15f), _progress);
                    break;

                case Phase.Completing:
                {
                    float t = Mathf.Clamp01(_fxAge / burstSeconds);
                    float fade = 1f - t;
                    Draw(1f, Color.Lerp(burstColour, healColour, t), fade, 1f + fade);
                    if (burstRing != null)
                    {
                        float grow = 1f - fade * fade * fade;
                        float q = burstRadius * 2f * grow / ringTextureFill;
                        burstRing.transform.localScale = new Vector3(q, q, 1f);
                        Tint(burstRing, burstColour, 1.1f, fade * fade);
                    }
                    if (t >= 1f) Finish();
                    break;
                }

                case Phase.Cancelling:
                {
                    float t = Mathf.Clamp01(_fxAge / cancelSeconds);
                    Draw(_brokeAt * (1f - t), failColour, 1f - t, _brokeAt * (1f - t));
                    if (t >= 1f) Finish();
                    break;
                }
            }
        }

        /// <summary>Everything at <paramref name="fill"/> of the arc, in
        /// <paramref name="colour"/>, at <paramref name="alpha"/> opacity and
        /// <paramref name="strength"/> of full brightness.</summary>
        void Draw(float fill, Color colour, float alpha, float strength)
        {
            strength = Mathf.Max(0f, strength);
            if (trackRing != null)
            {
                DrawArc(trackRing, 1f, loop: true, ringRadius, ringHeight);
                Tint(trackRing, colour, 1f, 0.3f * alpha);
            }
            if (progressRing != null)
            {
                DrawArc(progressRing, fill, loop: false, ringRadius, ringHeight);
                progressRing.enabled = fill > 0.005f;
                Tint(progressRing, colour, 1f + 0.15f * strength, alpha);
            }
            if (haloTrack != null)
            {
                DrawArc(haloTrack, 1f, loop: true, haloRadius, haloHeight);
                Tint(haloTrack, colour, 1f, 0.45f * alpha);
            }
            if (haloRing != null)
            {
                DrawArc(haloRing, fill, loop: false, haloRadius, haloHeight);
                haloRing.enabled = fill > 0.005f;
                Tint(haloRing, colour, 1f + 0.15f * strength, alpha);
            }
            if (rune != null && _model == null)
            {
                rune.transform.localRotation = Quaternion.Euler(90f, _runeAngle, 0f);
                float size = ringRadius * 2f * Mathf.Lerp(0.85f, 1f, Mathf.Clamp01(strength));
                rune.transform.localScale = new Vector3(size, size, 1f);
                Tint(rune, colour, 1f, alpha * Mathf.Lerp(0.3f, 0.9f, Mathf.Clamp01(strength)));
            }
            if (_model != null)
                _model.transform.localRotation = Quaternion.Euler(0f, _runeAngle, 0f);
            if (glow != null)
            {
                float size = glowSize * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(strength));
                glow.transform.localScale = new Vector3(size, size, 1f);
                Tint(glow, colour, intensity * 0.6f, alpha * 0.55f * Mathf.Clamp01(strength));
            }
            if (glowLight != null)
            {
                glowLight.color = colour;
                glowLight.intensity = lightIntensity * Mathf.Clamp01(strength) * alpha * (strength > 1f ? strength : 1f);
            }
            if (motes != null)
            {
                var emission = motes.emission;
                emission.rateOverTime = _phase == Phase.Channeling ? 8f + moteRate * _progress : 0f;
            }
        }

        void Finish()
        {
            _phase = Phase.Idle;
            ShowAll(false);
        }

        void ShowAll(bool on)
        {
            if (progressRing != null) progressRing.enabled = on;
            if (trackRing != null) trackRing.enabled = on;
            if (haloRing != null) haloRing.enabled = on;
            if (haloTrack != null) haloTrack.enabled = on;
            if (rune != null) rune.enabled = on && _model == null;
            if (_model != null) _model.SetActive(on);
            if (glow != null) glow.enabled = on;
            if (glowLight != null) glowLight.enabled = on;
            if (burstRing != null && !on) burstRing.enabled = false;
            if (!on && motes != null) motes.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void EnsureModel(SpellDefinition spell)
        {
            var prefab = spell != null ? spell.ModelPrefab : null;
            if (prefab == _modelSource) return;
            if (_model != null) Destroy(_model);
            _model = null;
            _modelSource = prefab;
            if (prefab == null) return;
            var parent = rune != null ? rune.transform.parent : transform;
            _model = SpellModel.Spawn(prefab, parent, ringRadius * 2f * spell.ModelScale);
            if (_model != null) _model.transform.localPosition = new Vector3(0f, ringHeight, 0f);
        }

        /// <summary>An arc of <paramref name="fraction"/> of a turn of
        /// <paramref name="radius"/> at <paramref name="height"/>, in local space,
        /// starting at the front and running clockwise.</summary>
        void DrawArc(LineRenderer line, float fraction, bool loop, float radius, float height)
        {
            fraction = Mathf.Clamp01(fraction);
            int count = loop ? ArcSegments : Mathf.Max(2, Mathf.CeilToInt(fraction * ArcSegments) + 1);
            float span = loop ? TwoPi * (ArcSegments - 1) / ArcSegments : fraction * TwoPi;
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.PI * 0.5f - span * i / (count - 1);
                _arc[i] = new Vector3(Mathf.Cos(a) * radius, height, Mathf.Sin(a) * radius);
            }
            line.loop = loop;
            line.positionCount = count;
            line.SetPositions(_arc);
        }

        void Tint(Renderer target, Color colour, float brightness, float alpha)
        {
            _block.SetColor(BaseColorId, new Color(colour.r * brightness, colour.g * brightness,
                                                   colour.b * brightness, colour.a * Mathf.Clamp01(alpha)));
            target.SetPropertyBlock(_block);
        }
    }
}
