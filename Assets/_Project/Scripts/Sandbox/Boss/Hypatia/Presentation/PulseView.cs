using Pickleball.Sandbox.Boss.Hypatia.Pulse;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// Shows the knockback pulse, built in layers because no single element reads
    /// as a blast.
    ///
    /// <b>Charge</b> (the warning: back off or be thrown): an astrolabe dial lights
    /// up on the plate at exactly Pulse Reach with a clock hand sweeping it once,
    /// so the floor says both how far and how soon; motes rush in to a swelling
    /// core on her body; a light builds. <b>Release</b>: a flash, a noise-eroded
    /// shockwave racing out to exactly Pulse Reach that flares the dial as it
    /// passes, a faint dome with a glowing rim and a heat ripple, sparks, dust, a
    /// real light on the plate, and a camera kick that falls off with distance.
    /// The dome stays nearly invisible face-on: the camera is usually inside it,
    /// and a bright sphere would fill the screen.
    ///
    /// Presentation only: listens to <see cref="KnockbackPulse.PulseCharging"/>,
    /// <see cref="KnockbackPulse.PulseFired"/>,
    /// <see cref="KnockbackPulse.PulseCancelled"/> and
    /// <see cref="KnockbackPulse.PulseHitPlayer"/>. The charge follows her clock
    /// (it freezes with her); the release, already let go, plays in real time. The
    /// timing curves are in <see cref="PulseLook"/>. No per-frame allocation.
    /// </summary>
    public class PulseView : MonoBehaviour
    {
        static readonly int ChargeId = Shader.PropertyToID("_Charge");
        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int SpinId = Shader.PropertyToID("_Spin");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");
        static readonly int ExtentId = Shader.PropertyToID("_Extent");
        static readonly int ReachId = Shader.PropertyToID("_Reach");
        static readonly int WaveRadiusId = Shader.PropertyToID("_WaveRadius");
        static readonly int WaveFadeId = Shader.PropertyToID("_WaveFade");
        static readonly int SigilAlphaId = Shader.PropertyToID("_SigilAlpha");
        static readonly int SeedId = Shader.PropertyToID("_Seed");
        static readonly int BrassColorId = Shader.PropertyToID("_BrassColor");
        static readonly int WaveColorId = Shader.PropertyToID("_WaveColor");
        static readonly int FadeId = Shader.PropertyToID("_Fade");
        static readonly int ScrollId = Shader.PropertyToID("_Scroll");
        static readonly int OpaqueTextureId = Shader.PropertyToID("_CameraOpaqueTexture");

        [Header("Source")]
        [Tooltip("The pulse attack on her root.")]
        [SerializeField] private KnockbackPulse pulse;
        [Tooltip("Her brain: her position, the floor height and her clock.")]
        [SerializeField] private HypatiaBoss boss;

        [Header("Parts")]
        [Tooltip("Camera-facing quad on her body (carries LockOnIgnore): the charge, then the flash.")]
        [SerializeField] private Renderer core;
        [Tooltip("Motes rushing in during the charge; moved to the core each frame, on her clock.")]
        [SerializeField] private ParticleSystem gather;
        [Tooltip("Flat quad on the plate: the dial and the shockwave. Sized from Pulse Reach.")]
        [SerializeField] private Renderer floorQuad;
        [Tooltip("Translucent sphere, scaled to the shockwave (flattened by Dome Height).")]
        [SerializeField] private Transform dome;
        [Tooltip("The dome's glowing rim.")]
        [SerializeField] private Renderer domeRenderer;
        [Tooltip("The heat ripple, a child of the dome. Stays off where the render settings make no opaque texture.")]
        [SerializeField] private Renderer rippleRenderer;
        [Tooltip("Sparks thrown out across the plate on release.")]
        [SerializeField] private ParticleSystem sparks;
        [Tooltip("Dust kicked outward on release.")]
        [SerializeField] private ParticleSystem dust;
        [Tooltip("Point light: builds over the charge, peaks at the flash.")]
        [SerializeField] private Light flashLight;
        [Tooltip("The chase camera, for the kick on release. Optional.")]
        [SerializeField] private SandboxOrbitCamera cameraRig;

        [Header("Charge")]
        [ColorUsage(false, true)]
        [Tooltip("Core colour while charging. Values over 1 bloom.")]
        [SerializeField] private Color chargeColour = new(0.9f, 1.25f, 2.6f);
        [Tooltip("Core quad size at the moment it fires, m.")]
        [SerializeField, Min(0.1f)] private float chargeCoreSize = 4f;
        [ColorUsage(false, true)]
        [Tooltip("Dial colour on the plate. Values over 1 bloom.")]
        [SerializeField] private Color dialColour = new(2.6f, 1.7f, 0.6f);
        [Tooltip("Dial spin at the start of the charge, °/s.")]
        [SerializeField] private float dialSpinStart = 25f;
        [Tooltip("Dial spin as it fires, °/s. Higher = more frantic wind-up.")]
        [SerializeField] private float dialSpinPeak = 280f;
        [Tooltip("Seconds the dial takes to fade out if the charge is cancelled.")]
        [SerializeField, Min(0.01f)] private float cancelFadeSeconds = 0.15f;
        [Tooltip("Light intensity at the end of the charge.")]
        [SerializeField, Min(0f)] private float chargeLight = 14f;

        [Header("Release")]
        [ColorUsage(false, true)]
        [Tooltip("Flash colour on her body. Values over 1 bloom.")]
        [SerializeField] private Color flashColour = new(3.2f, 3.8f, 6f);
        [Tooltip("Flash quad size, m.")]
        [SerializeField, Min(0.1f)] private float flashCoreSize = 8.5f;
        [Tooltip("Seconds the flash takes to die (real time). Short: it should feel like one blown-out frame.")]
        [SerializeField, Min(0.01f)] private float flashSeconds = 0.12f;
        [ColorUsage(false, true)]
        [Tooltip("Shockwave colour. Values over 1 bloom; keep one channel well above the others or it clips to flat white.")]
        [SerializeField] private Color waveColour = new(0.7f, 1.25f, 3.4f);
        [ColorUsage(false, true)]
        [Tooltip("Dome rim colour; keep it dim, the camera is usually inside the dome.")]
        [SerializeField] private Color domeColour = new(0.25f, 0.38f, 1f);
        [Tooltip("Dome height as a fraction of its radius (1 = a hemisphere).")]
        [SerializeField, Range(0.05f, 1f)] private float domeHeight = 0.3f;
        [Tooltip("Seconds the shockwave takes to reach Pulse Reach (real time).")]
        [SerializeField, Min(0.01f)] private float expandSeconds = 0.25f;
        [Tooltip("Seconds it then takes to fade out (real time).")]
        [SerializeField, Min(0.01f)] private float fadeSeconds = 0.45f;
        [Tooltip("Light intensity at the flash's peak.")]
        [SerializeField, Min(0f)] private float flashLightPeak = 90f;
        [Tooltip("Seconds the light takes to die after the flash (real time).")]
        [SerializeField, Min(0.01f)] private float flashLightSeconds = 0.4f;
        [Tooltip("Spark launch speed, slowest to fastest, m/s, at Particle Reference Reach.")]
        [SerializeField] private Vector2 sparkSpeed = new(12f, 36f);
        [Tooltip("Dust launch speed, slowest to fastest, m/s, at Particle Reference Reach. With drag 5 a puff settles at speed ÷ 5 m out.")]
        [SerializeField] private Vector2 dustSpeed = new(30f, 55f);
        [Tooltip("Pulse Reach the spark and dust speeds were tuned for, m: a longer reach throws them proportionally further.")]
        [SerializeField, Min(0.1f)] private float particleReferenceReach = 10f;
        [Tooltip("The floor quad is this many times Pulse Reach, so the wobbling front never meets its edge.")]
        [SerializeField, Min(1f)] private float floorMargin = 1.2f;
        [Tooltip("Lift off the plate, m, so the floor quad does not z-fight it.")]
        [SerializeField, Min(0f)] private float floorLift = 0.06f;

        [Header("Camera")]
        [Tooltip("Shake for a camera right beside her, m. Sword hits are about 0.15.")]
        [SerializeField, Min(0f)] private float shakeAtCentre = 0.42f;
        [Tooltip("Shake for a camera at Pulse Reach, m; it dies away to nothing at twice the reach.")]
        [SerializeField, Min(0f)] private float shakeAtEdge = 0.14f;
        [Tooltip("Shake when the pulse actually catches the player, m. Being hit should feel worse than watching.")]
        [SerializeField, Min(0f)] private float shakeOnHit = 0.5f;
        [Tooltip("Seconds the shake lasts.")]
        [SerializeField, Min(0.01f)] private float shakeSeconds = 0.38f;
        [Tooltip("Field-of-view kick on release, °, springing back over Fov Punch Seconds. 0 = none.")]
        [SerializeField] private float fovPunch = 5f;
        [Tooltip("Seconds the field-of-view kick takes to spring back.")]
        [SerializeField, Min(0.01f)] private float fovPunchSeconds = 0.32f;

        KnockbackPulse _subscribed;
        MaterialPropertyBlock _block;
        bool _charging, _waving, _flashing;
        float _chargeFor, _charged, _waveAge, _flashAge, _reach;
        float _dialAlpha, _dialCharge, _spin, _seed;

        /// <summary>True while the shockwave is showing.</summary>
        public bool IsShowingWave => _waving;

        /// <summary>Current shockwave radius, m (0 when not showing).</summary>
        public float WaveRadius => _waving ? PulseLook.WaveRadius(_waveAge, expandSeconds, _reach) : 0f;

        void OnEnable()
        {
            _block ??= new MaterialPropertyBlock();
            _subscribed = pulse;
            if (_subscribed != null)
            {
                _subscribed.PulseCharging += OnCharging;
                _subscribed.PulseFired += OnFired;
                _subscribed.PulseCancelled += OnCancelled;
                _subscribed.PulseHitPlayer += OnHitPlayer;
            }
            _charging = _subscribed != null && _subscribed.IsCharging;
            _waving = _flashing = false;
            _dialAlpha = 0f;
            _reach = ConfigReach();
            if (_charging)
            {
                // Rebound mid-charge after a script reload: pick the charge up from what is left.
                _chargeFor = Mathf.Max(0.01f, _subscribed.ChargeRemaining);
                _charged = 0f;
                StartEmitting();
            }
            else if (gather != null) gather.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ShowCore(_charging);
            ShowFloor(_charging);
            ShowDome(false);
            if (flashLight != null) flashLight.enabled = false;
        }

        void OnDisable()
        {
            if (_subscribed != null)
            {
                _subscribed.PulseCharging -= OnCharging;
                _subscribed.PulseFired -= OnFired;
                _subscribed.PulseCancelled -= OnCancelled;
                _subscribed.PulseHitPlayer -= OnHitPlayer;
            }
            _subscribed = null;
        }

        void OnCharging(float seconds)
        {
            _charging = true;
            _chargeFor = Mathf.Max(0.01f, seconds);
            _charged = 0f;
            _dialCharge = 0f;
            _reach = ConfigReach();
            _seed = Mathf.Repeat(_seed + 0.618034f, 1f);   // a different noise pattern each pulse
            ShowCore(true);
            ShowFloor(true);
            PlaceAtHer();
            StartEmitting();
        }

        void OnCancelled()
        {
            _charging = false;
            ShowCore(false);
            if (gather != null) gather.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            // The dial fades over Cancel Fade Seconds in LateUpdate.
        }

        void OnFired(float reach)
        {
            _charging = false;
            _reach = Mathf.Max(0.01f, reach);
            _dialCharge = 1f;
            _dialAlpha = 1f;
            _waveAge = _flashAge = 0f;
            _waving = _flashing = true;

            PlaceAtHer();
            ShowCore(true);
            ShowFloor(true);
            ShowDome(true);
            if (gather != null) gather.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Burst(sparks, sparkSpeed);
            Burst(dust, dustSpeed);

            if (cameraRig != null)
            {
                float distance = Vector3.Distance(cameraRig.transform.position, FloorOrigin());
                cameraRig.Shake(PulseLook.ShakeAmount(distance, _reach, shakeAtCentre, shakeAtEdge), shakeSeconds);
                if (Mathf.Abs(fovPunch) > 0.01f) cameraRig.PunchFov(fovPunch, fovPunchSeconds);
            }
            Apply();
        }

        void OnHitPlayer(Vector3 push, float damage)
        {
            // The camera's Shake keeps the larger of two amounts, so this only ever adds.
            if (cameraRig != null) cameraRig.Shake(shakeOnHit, shakeSeconds);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (_charging)
            {
                float scale = boss != null && boss.Context != null ? boss.Context.BossTimeScale : 1f;
                float herDt = dt * scale;
                _charged += herDt;
                _dialCharge = Mathf.Clamp01(_charged / _chargeFor);
                _spin += PulseLook.SpinRate(_dialCharge, dialSpinStart, dialSpinPeak) * Mathf.Deg2Rad * herDt;
                _dialAlpha = 1f;
                if (gather != null)
                {
                    var main = gather.main;
                    main.simulationSpeed = scale;
                    if (core != null) gather.transform.position = core.transform.position;
                }
            }
            else
            {
                if (_flashing)
                {
                    _flashAge += dt;
                    if (_flashAge >= Mathf.Max(flashSeconds, flashLightSeconds)) _flashing = false;
                }
                if (_waving)
                {
                    _waveAge += dt;
                    _dialAlpha = PulseLook.WaveFade(_waveAge, expandSeconds, fadeSeconds);
                    if (PulseLook.WaveFinished(_waveAge, expandSeconds, fadeSeconds))
                    {
                        _waving = false;
                        _dialAlpha = 0f;
                        ShowDome(false);
                    }
                }
                else if (_dialAlpha > 0f)
                {
                    _dialAlpha = Mathf.MoveTowards(_dialAlpha, 0f, dt / cancelFadeSeconds);
                }
            }

            bool coreUp = _charging || _flashAge < flashSeconds && _flashing;
            ShowCore(coreUp);
            if (!_charging && !_waving && _dialAlpha <= 0f) ShowFloor(false);
            Apply();
        }

        /// <summary>Push the current state to every part. Allocation-free.</summary>
        void Apply()
        {
            float swell = PulseLook.Swell(_dialCharge);
            float flash = _flashing ? PulseLook.Flash(_flashAge, flashSeconds) : 0f;

            if (core != null && core.gameObject.activeSelf)
            {
                float size = flash > 0f
                    ? Mathf.Lerp(chargeCoreSize, flashCoreSize, flash)
                    : Mathf.Lerp(1.2f, chargeCoreSize, swell);
                core.transform.localScale = new Vector3(size, size, 1f);
                _block.Clear();
                _block.SetFloat(ChargeId, Mathf.Lerp(0.05f, 1f, _dialCharge));
                _block.SetFloat(FlashId, flash);
                _block.SetFloat(SpinId, _spin);
                _block.SetColor(ColorId, chargeColour);
                _block.SetColor(FlashColorId, flashColour);
                core.SetPropertyBlock(_block);
            }

            float waveFade = _waving ? PulseLook.WaveFade(_waveAge, expandSeconds, fadeSeconds) : 0f;
            float radius = WaveRadius;

            if (floorQuad != null && floorQuad.gameObject.activeSelf)
            {
                float extent = Mathf.Max(0.01f, _reach) * floorMargin;
                floorQuad.transform.localScale = new Vector3(extent * 2f, extent * 2f, 1f);
                _block.Clear();
                _block.SetFloat(ExtentId, extent);
                _block.SetFloat(ReachId, _reach);
                _block.SetFloat(WaveRadiusId, radius);
                _block.SetFloat(WaveFadeId, waveFade);
                _block.SetFloat(ChargeId, _dialCharge);
                _block.SetFloat(SigilAlphaId, _dialAlpha);
                _block.SetFloat(SpinId, _spin);
                _block.SetFloat(FlashId, flash);
                _block.SetFloat(SeedId, _seed);
                _block.SetColor(BrassColorId, dialColour);
                _block.SetColor(WaveColorId, waveColour);
                floorQuad.SetPropertyBlock(_block);
            }

            if (_waving && dome != null)
            {
                float d = Mathf.Max(0.01f, 2f * radius);
                dome.localScale = new Vector3(d, d * domeHeight, d);
                float scroll = _seed + _waveAge * 0.8f;
                _block.Clear();
                _block.SetColor(ColorId, domeColour);
                _block.SetFloat(FadeId, waveFade);
                _block.SetFloat(ScrollId, scroll);
                if (domeRenderer != null) domeRenderer.SetPropertyBlock(_block);
                if (rippleRenderer != null) rippleRenderer.SetPropertyBlock(_block);
            }

            if (flashLight != null)
            {
                float intensity = _charging ? chargeLight * swell : 0f;
                if (_flashing)
                    intensity = Mathf.Max(intensity, flashLightPeak * PulseLook.Flash(_flashAge, flashLightSeconds));
                bool lit = intensity > 0.01f;
                if (flashLight.enabled != lit) flashLight.enabled = lit;
                flashLight.intensity = intensity;
            }
        }

        float ConfigReach()
        {
            var config = boss != null && boss.Context != null ? boss.Context.Config : null;
            return config != null ? Mathf.Max(0.01f, config.PulseReach) : Mathf.Max(0.01f, _reach);
        }

        Vector3 FloorOrigin()
        {
            Vector3 her = boss != null ? boss.transform.position : transform.position;
            float floor = boss != null && boss.Context != null ? boss.Context.FloorY : her.y;
            return new Vector3(her.x, floor + floorLift, her.z);
        }

        void PlaceAtHer()
        {
            Vector3 origin = FloorOrigin();
            if (floorQuad != null) floorQuad.transform.position = origin;
            if (dome != null) dome.position = origin;
            if (flashLight != null) flashLight.transform.position = origin + Vector3.up * 2.5f;
            if (sparks != null) sparks.transform.position = origin + Vector3.up * 0.25f;
            if (dust != null) dust.transform.position = origin + Vector3.up * 0.35f;
            if (gather != null && core != null) gather.transform.position = core.transform.position;
        }

        void StartEmitting()
        {
            if (gather == null) return;
            gather.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            gather.Play(true);
        }

        void Burst(ParticleSystem system, Vector2 speed)
        {
            if (system == null) return;
            var main = system.main;
            // The whole range, not startSpeedMultiplier: on a two-constant curve
            // that overwrites the maximum instead of scaling it.
            float scale = _reach / particleReferenceReach;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x * scale, speed.y * scale);
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.Play(true);
        }

        void ShowCore(bool on)
        {
            if (core != null && core.gameObject.activeSelf != on) core.gameObject.SetActive(on);
        }

        void ShowFloor(bool on)
        {
            if (floorQuad != null && floorQuad.gameObject.activeSelf != on) floorQuad.gameObject.SetActive(on);
        }

        void ShowDome(bool on)
        {
            if (dome != null && dome.gameObject.activeSelf != on) dome.gameObject.SetActive(on);
            // No opaque texture (the mobile render settings) means nothing to bend:
            // the ripple would draw grey, so it simply stays off.
            if (rippleRenderer != null) rippleRenderer.enabled = on && Shader.GetGlobalTexture(OpaqueTextureId) != null;
        }
    }
}
