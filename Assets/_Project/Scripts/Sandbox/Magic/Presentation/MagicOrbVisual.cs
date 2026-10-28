using UnityEngine;

namespace Pickleball.Sandbox.Magic.Presentation
{
    /// <summary>
    /// Draws a <see cref="MagicOrb"/>: a pulsing white-cyan core inside an
    /// additive halo, violet motes circling it, a streak trail, sparkle dust
    /// and a point light that washes the arena as it passes. On launch the
    /// halo and light flare (the muzzle flash); on a burst an additive shell
    /// grows to the real blast radius with a shock ring and a light flash; on
    /// a fizzle the core swells and pops.
    ///
    /// Presentation only: it subscribes to the orb's events and never touches
    /// gameplay. It must sit on a CHILD of the orb, because the orb destroys
    /// its own GameObject the moment it bursts or fizzles (so an orb without a
    /// visual can never leak). Inside the burst/fizzle handler, which the orb
    /// raises just before that Destroy, this component unparents itself and
    /// lives on for <see cref="lingerSeconds"/> so the flash, the fading trail
    /// and the sparks can finish, then destroys itself. Presentation owns
    /// presentation's lifetime; gameplay never waits on it.
    ///
    /// Hot-reload safe: the property block is rebuilt in OnEnable, and a
    /// detached visual whose orb is gone (a script reload mid-burst drops the
    /// flash timers) simply removes itself rather than freezing on screen.
    /// </summary>
    public class MagicOrbVisual : MonoBehaviour
    {
        enum Phase { Flying, Bursting, Fizzling }

        const float TwoPi = Mathf.PI * 2f;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Vector3 MoteAxis = new Vector3(0.35f, 1f, 0.2f).normalized;

        [Header("Source")]
        [Tooltip("The orb drawn. Found on a parent when empty. This visual must be on " +
                 "a child of the orb so it can detach and finish its burst after the " +
                 "orb is destroyed.")]
        [SerializeField] private MagicOrb orb;

        [Header("Parts")]
        [Tooltip("Bright unlit sphere at the heart of the orb. Pulses in size and brightness.")]
        [SerializeField] private Renderer core;
        [Tooltip("Camera-facing additive glow around the core.")]
        [SerializeField] private Renderer halo;
        [Tooltip("Optional. Pivot of the small motes circling the core; spun here.")]
        [SerializeField] private Transform motes;
        [Tooltip("Optional. Streak left behind the orb. Stops emitting at the burst " +
                 "and fades out over its own time.")]
        [SerializeField] private TrailRenderer trail;
        [Tooltip("Optional. Point light carried by the orb; flares at launch and burst.")]
        [SerializeField] private Light glowLight;
        [Tooltip("Optional. Looping world-space sparkle dust shed behind the orb; " +
                 "also puffed at launch and fizzle.")]
        [SerializeField] private ParticleSystem sparkles;
        [Tooltip("Optional. One-shot spark spray played at the burst.")]
        [SerializeField] private ParticleSystem burstSparks;
        [Tooltip("Additive sphere shown at the burst. It grows to exactly the spell's " +
                 "blast radius, so the edge you see is the edge that hits.")]
        [SerializeField] private Renderer burstShell;
        [Tooltip("Camera-facing shock ring shown at the burst.")]
        [SerializeField] private Renderer burstRing;

        [Header("Colour")]
        [Tooltip("Core tint (alpha unused: the core is opaque).")]
        [SerializeField] private Color coreColour = new(0.78f, 1f, 1f, 1f);
        [Tooltip("Halo, ring and trail-side glow tint; alpha is its opacity.")]
        [SerializeField] private Color glowColour = new(0.3f, 0.88f, 1f, 0.85f);
        [Tooltip("Violet accent: the burst shell and the fizzle halo; alpha is opacity.")]
        [SerializeField] private Color accentColour = new(0.66f, 0.42f, 1f, 0.7f);
        [Tooltip("HDR brightness multiplier on every tint (x). Above ~1 the bloom " +
                 "from WorldLook catches it; much higher washes the colour out to white.")]
        [Min(0f)] [SerializeField] private float intensity = 2.5f;

        [Header("Flight")]
        [Tooltip("Core diameter, in metres. Keep it a little under twice the spell's " +
                 "orb radius so what you see is not bigger than what hits.")]
        [Min(0.01f)] [SerializeField] private float coreDiameter = 0.4f;
        [Tooltip("Halo diameter, in metres.")]
        [Min(0.01f)] [SerializeField] private float haloDiameter = 1.5f;
        [Tooltip("Pulse rate, in beats per second.")]
        [Min(0f)] [SerializeField] private float pulseHz = 2.4f;
        [Tooltip("Pulse depth as a fraction of size (0-0.5). 0 is a steady glow.")]
        [Range(0f, 0.5f)] [SerializeField] private float pulseAmount = 0.14f;
        [Tooltip("Mote orbit speed, in degrees per second.")]
        [SerializeField] private float moteSpinDegreesPerSecond = 240f;
        [Tooltip("Point-light intensity in flight.")]
        [Min(0f)] [SerializeField] private float lightIntensity = 2.5f;
        [Tooltip("Point-light range in flight, in metres.")]
        [Min(0f)] [SerializeField] private float lightRange = 4f;

        [Header("Launch")]
        [Tooltip("Seconds the muzzle flare takes to settle. ~0.15 reads as a flash.")]
        [Min(0.01f)] [SerializeField] private float muzzleSeconds = 0.14f;
        [Tooltip("Halo size at the peak of the flare, as a multiple of normal (x).")]
        [Min(1f)] [SerializeField] private float muzzleHaloScale = 2.4f;
        [Tooltip("Extra light at the peak of the flare, as a multiple of normal (x).")]
        [Min(0f)] [SerializeField] private float muzzleLightBoost = 3f;
        [Tooltip("Sparkles puffed at the hand on launch.")]
        [Min(0)] [SerializeField] private int muzzleSparkCount = 14;

        [Header("Burst")]
        [Tooltip("Seconds the burst shell, ring and light flash take to grow and fade.")]
        [Min(0.05f)] [SerializeField] private float burstSeconds = 0.35f;
        [Tooltip("Light intensity at the instant of the burst.")]
        [Min(0f)] [SerializeField] private float burstLightIntensity = 9f;
        [Tooltip("Where the shock ring ends, as a multiple of the blast radius (x).")]
        [Min(0.1f)] [SerializeField] private float ringOvershoot = 1.25f;
        [Tooltip("Ring radius within its texture, as a fraction of the quad's half-size " +
                 "(the generated WellHalo ring sits at 0.55). Used to size the quad so " +
                 "the drawn ring lands where it should.")]
        [Min(0.05f)] [SerializeField] private float ringTextureFill = 0.55f;

        [Header("Fizzle")]
        [Tooltip("Seconds the fizzle pop takes.")]
        [Min(0.05f)] [SerializeField] private float fizzleSeconds = 0.25f;
        [Tooltip("How much the core and halo swell before popping, as a multiple (x).")]
        [Min(1f)] [SerializeField] private float fizzleSwell = 1.5f;
        [Tooltip("Sparkles thrown off by the pop.")]
        [Min(0)] [SerializeField] private int fizzleSparkCount = 10;

        [Header("Cleanup")]
        [Tooltip("Seconds the detached visual lives after the orb is gone, so the trail " +
                 "and sparks can fade. Never shorter than the burst, fizzle or trail time.")]
        [Min(0f)] [SerializeField] private float lingerSeconds = 0.6f;

        MaterialPropertyBlock _block;
        Phase _phase;
        float _age, _fxAge, _muzzleLeft, _burstRadius, _lingerTotal;

        void OnEnable()
        {
            _block = new MaterialPropertyBlock();
            if (orb == null) orb = GetComponentInParent<MagicOrb>();
            if (orb == null)
            {
                // Detached and orphaned (a reload mid-burst), or placed without
                // an orb: nothing left to draw.
                Destroy(gameObject);
                return;
            }

            orb.Launched += OnLaunched;
            orb.Burst += OnBurst;
            orb.Fizzled += OnFizzled;

            _phase = Phase.Flying;
            ShowFlight(true);
            ShowBurst(false);
        }

        void OnDisable() => Unsubscribe();

        void Unsubscribe()
        {
            if (orb == null) return;
            orb.Launched -= OnLaunched;
            orb.Burst -= OnBurst;
            orb.Fizzled -= OnFizzled;
        }

        // ------------------------------------------------------------ events

        void OnLaunched(MagicOrb source)
        {
            _phase = Phase.Flying;
            _age = 0f;
            _muzzleLeft = muzzleSeconds;
            ShowFlight(true);
            ShowBurst(false);
            // The prefab may have been spawned somewhere else first; never
            // streak in from there.
            if (trail != null)
            {
                trail.Clear();
                trail.emitting = true;
            }
            if (sparkles != null)
            {
                if (!sparkles.isPlaying) sparkles.Play();
                if (muzzleSparkCount > 0) sparkles.Emit(muzzleSparkCount);
            }
            AnimateFlight(0f);
        }

        void OnBurst(MagicOrb source, Vector3 point, int hits)
        {
            var spell = source != null ? source.Spell : null;
            _burstRadius = Mathf.Max(spell != null ? spell.BlastRadius : 0f, coreDiameter);

            Detach(point);
            _phase = Phase.Bursting;
            ShowFlight(false);
            ShowBurst(true);
            if (burstSparks != null) burstSparks.Play(true);
            if (glowLight != null)
            {
                glowLight.enabled = true;
                glowLight.range = Mathf.Max(lightRange, _burstRadius * 2.5f);
            }
            AnimateBurst();
        }

        void OnFizzled(MagicOrb source)
        {
            // Emit before Detach stops the system: particles already alive
            // survive a StopEmitting, new ones would not be made after it.
            if (sparkles != null && fizzleSparkCount > 0) sparkles.Emit(fizzleSparkCount);
            Detach(transform.position);
            _phase = Phase.Fizzling;
            if (motes != null) motes.gameObject.SetActive(false);
            ShowBurst(false);
            AnimateFizzle();
        }

        /// <summary>Cut loose from the orb (it is destroyed right after this
        /// frame's event) and become a self-timed effect at <paramref name="point"/>.</summary>
        void Detach(Vector3 point)
        {
            Unsubscribe();
            orb = null;
            _fxAge = 0f;
            _lingerTotal = Mathf.Max(lingerSeconds, Mathf.Max(burstSeconds, fizzleSeconds));
            if (trail != null)
            {
                trail.emitting = false;
                _lingerTotal = Mathf.Max(_lingerTotal, trail.time);
            }
            if (sparkles != null) sparkles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            transform.SetParent(null, true);
            transform.position = point;
        }

        // ---------------------------------------------------------- animation

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            _age += dt;
            if (_phase == Phase.Flying)
            {
                AnimateFlight(dt);
                return;
            }

            _fxAge += dt;
            if (_phase == Phase.Bursting) AnimateBurst();
            else AnimateFizzle();
            if (_fxAge >= _lingerTotal) Destroy(gameObject);
        }

        void AnimateFlight(float dt)
        {
            float wave = Mathf.Sin(_age * pulseHz * TwoPi);
            float flare = 0f;
            if (_muzzleLeft > 0f)
            {
                _muzzleLeft = Mathf.Max(0f, _muzzleLeft - dt);
                flare = _muzzleLeft / muzzleSeconds;
                flare *= flare;   // fast drop-off: a flash, not a fade
            }

            if (core != null)
            {
                core.transform.localScale = Vector3.one * (coreDiameter * (1f + pulseAmount * wave));
                Tint(core, coreColour, intensity * (1f + 0.3f * wave + 2f * flare), 1f);
            }
            if (halo != null)
            {
                float size = haloDiameter * (1f + 0.5f * pulseAmount * wave) * Mathf.Lerp(1f, muzzleHaloScale, flare);
                halo.transform.localScale = new Vector3(size, size, 1f);
                Tint(halo, glowColour, intensity * (1f + flare), 1f);
            }
            if (motes != null)
                motes.localRotation = Quaternion.AngleAxis(_age * moteSpinDegreesPerSecond, MoteAxis);
            if (glowLight != null)
            {
                glowLight.range = lightRange;
                glowLight.intensity = lightIntensity * (1f + 0.2f * wave) * (1f + muzzleLightBoost * flare);
            }
        }

        void AnimateBurst()
        {
            float t = Mathf.Clamp01(_fxAge / burstSeconds);
            float rest = 1f - t;
            float fade = rest * rest;
            bool live = t < 1f;

            if (burstShell != null)
            {
                burstShell.enabled = live;
                if (live)
                {
                    float grow = 1f - rest * rest * rest;                 // ease-out
                    float diameter = Mathf.Lerp(coreDiameter, _burstRadius * 2f, grow);
                    burstShell.transform.localScale = Vector3.one * diameter;
                    Tint(burstShell, accentColour, intensity, fade);
                }
            }
            if (burstRing != null)
            {
                burstRing.enabled = live;
                if (live)
                {
                    float grow = 1f - rest * rest * rest * rest;          // runs ahead of the shell
                    float quad = _burstRadius * 2f * ringOvershoot * grow / ringTextureFill;
                    burstRing.transform.localScale = new Vector3(quad, quad, 1f);
                    Tint(burstRing, glowColour, intensity * 1.5f, fade);
                }
            }
            if (glowLight != null)
            {
                glowLight.enabled = live;
                glowLight.intensity = burstLightIntensity * fade;
            }
        }

        void AnimateFizzle()
        {
            float t = Mathf.Clamp01(_fxAge / fizzleSeconds);
            // Swell over the first 30%, then collapse to nothing.
            float scale = t < 0.3f
                ? Mathf.Lerp(1f, fizzleSwell, t / 0.3f)
                : Mathf.Lerp(fizzleSwell, 0f, (t - 0.3f) / 0.7f);
            bool live = t < 1f;

            if (core != null)
            {
                core.enabled = live;
                // Brightness held, not faded: the core is opaque, and an unlit
                // sphere dimmed toward black reads as a hole, not a pop.
                if (live)
                {
                    core.transform.localScale = Vector3.one * (coreDiameter * 0.8f * scale);
                    Tint(core, coreColour, intensity, 1f);
                }
            }
            if (halo != null)
            {
                halo.enabled = live;
                if (live)
                {
                    float size = haloDiameter * scale;
                    halo.transform.localScale = new Vector3(size, size, 1f);
                    Tint(halo, accentColour, intensity, 1f - t);
                }
            }
            if (glowLight != null)
            {
                glowLight.enabled = live;
                glowLight.intensity = lightIntensity * (1f - t);
            }
        }

        // ------------------------------------------------------------ helpers

        void ShowFlight(bool on)
        {
            if (core != null) core.enabled = on;
            if (halo != null) halo.enabled = on;
            if (motes != null) motes.gameObject.SetActive(on);
            if (glowLight != null) glowLight.enabled = on;
        }

        void ShowBurst(bool on)
        {
            if (burstShell != null) burstShell.enabled = on;
            if (burstRing != null) burstRing.enabled = on;
        }

        /// <summary>HDR tint through the shared property block: rgb scaled by
        /// <paramref name="brightness"/>, alpha by <paramref name="alpha"/>.</summary>
        void Tint(Renderer target, Color colour, float brightness, float alpha)
        {
            _block.SetColor(BaseColorId, new Color(colour.r * brightness, colour.g * brightness,
                                                   colour.b * brightness, colour.a * alpha));
            target.SetPropertyBlock(_block);
        }
    }
}
