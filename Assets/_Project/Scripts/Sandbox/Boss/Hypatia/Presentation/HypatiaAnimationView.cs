using System;
using Pickleball.Sandbox.Boss.Hypatia.Burning;
using Pickleball.Sandbox.Boss.Hypatia.Pulse;
using Pickleball.Sandbox.Boss.Hypatia.Rings;
using UnityEngine;
using ClipAction = Pickleball.Sandbox.Boss.Hypatia.Presentation.HypatiaAnimationDirector.Action;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// Plays her attack, Phase Two and death clips. Presentation only: it listens to
    /// the attacks' existing events and never calls a gameplay setter, so gameplay
    /// timing is the same with or without it. <see cref="HypatiaAnimationDirector"/>
    /// makes every decision; this turns them into Animator calls.
    ///
    /// <para><b>Layers.</b> Attacks and Phase Two play on the UpperBody layer (see
    /// <c>HypatiaFightModel.EnsureController</c>), whose mask leaves root and legs to
    /// the base layer: she hovers and rings fire often, so the long dress must not be
    /// thrown around each time. (Phase Two was tried full-body: the only power-up in
    /// the repo plants a wide fighter's stance, which is wrong for her.) Only Death is
    /// full-body. Topple stays with <see cref="ToppleView"/>, which writes the Toppled
    /// bool; this only clears the way.</para>
    ///
    /// <para><b>No root motion.</b> The clips are baked in place and
    /// <c>applyRootMotion</c> stays off: the brain owns her position.</para>
    ///
    /// <para><b>Reload.</b> The director is not serialized, so it is rebuilt in
    /// OnEnable, and the current fight state is re-read from the brain.</para>
    /// </summary>
    public class HypatiaAnimationView : MonoBehaviour
    {
        /// <summary>Name of the upper-body layer in her controller.</summary>
        public const string UpperLayer = "UpperBody";

        /// <summary>Upper-layer float scaling the attack states' speed.</summary>
        public const string SpeedParameter = "UpperSpeed";

        /// <summary>The upper layer's resting state: no motion, so the base layer shows through.</summary>
        public const string EmptyState = "Empty";

        /// <summary>Clips are named this prefix plus their state's name.</summary>
        public const string ClipPrefix = "Hypatia_";

        static readonly int SpeedId = Animator.StringToHash(SpeedParameter);
        static readonly int EmptyId = Animator.StringToHash(EmptyState);

        /// <summary>A clip's release time and speed limits. Its length is read from the
        /// clip itself, so a swapped clip cannot leave a stale number behind.</summary>
        [Serializable]
        struct ClipSetting
        {
            [Tooltip("When the release pose falls in the clip, s at speed 1. Re-measure it if the clip is " +
                     "swapped. 0 = no gameplay moment to hit: it just plays.")]
            [Min(0f)] public float release;
            [Tooltip("Slowest it may play to meet a long lead, ×. Lower is more languid; 1 keeps a sharp " +
                     "motion sharp, and she waits instead.")]
            [Min(0.01f)] public float minSpeed;
            [Tooltip("Fastest it may play to meet a short lead, ×.")]
            [Min(0.01f)] public float maxSpeed;
            [Tooltip("Blend into this clip, s. Short reads as snappy.")]
            [Min(0f)] public float fade;
            [Tooltip("Leave the clip this far in, s at speed 1, to cut a long follow-through. 0 = play it all.")]
            [Min(0f)] public float leaveAt;

            public ClipSetting(float release, float minSpeed, float maxSpeed, float fade, float leaveAt = 0f)
            {
                this.release = release;
                this.minSpeed = minSpeed;
                this.maxSpeed = maxSpeed;
                this.fade = fade;
                this.leaveAt = leaveAt;
            }
        }

        [Header("Sources")]
        [Tooltip("Her brain.")]
        [SerializeField] private HypatiaBoss boss;
        [Tooltip("Rings: the forecast starts the sweep.")]
        [SerializeField] private CelestialRingEmitter rings;
        [Tooltip("The pulse: the charge starts the gather; a cancelled charge stops it.")]
        [SerializeField] private KnockbackPulse pulse;
        [Tooltip("The floor burn: a band's warning starts the reach to the floor.")]
        [SerializeField] private ArenaBurn burn;

        [Header("Parts")]
        [Tooltip("Her animator. The brain sets its speed from her clock.")]
        [SerializeField] private Animator animator;
        [Tooltip("Her halo. It stands just behind her, so her fall backward would pass through it: it " +
                 "shrinks away as she dies.")]
        [SerializeField] private Transform halo;

        [Header("Clips (timing measured on her skeleton)")]
        [Tooltip("Sweep: the arm whips out at the ring's launch.")]
        [SerializeField] private ClipSetting ringClip = new(0.87f, 0.8f, 1.3f, 0.2f);
        [Tooltip("Upward summon: arms overhead as the first orbiter appears, opening as the rest follow.")]
        [SerializeField] private ClipSetting ellipseClip = new(0.6f, 0.5f, 1.2f, 0.25f);
        [Tooltip("Overhand lob: the arm comes over at the launch. Left early: the clip's follow-through droops.")]
        [SerializeField] private ClipSetting parabolaClip = new(0.37f, 0.8f, 1.2f, 0.15f, 0.62f);
        [Tooltip("One-arm thrust: fully extended at the shot, and sharp, so she waits rather than slowing it.")]
        [SerializeField] private ClipSetting hyperbolaClip = new(0.25f, 1f, 1.3f, 0.1f);
        [Tooltip("Gather overhead, then burst down and out on the pulse's flash.")]
        [SerializeField] private ClipSetting pulseClip = new(1f, 0.9f, 1.5f, 0.12f);
        [Tooltip("Arms rise and open, then drive down and out at the floor as the band ignites.")]
        [SerializeField] private ClipSetting burningClip = new(2f, 0.7f, 1.3f, 0.25f);
        [Tooltip("Power-up: an arm thrown to the sky, then a sweep down. No release to hit.")]
        [SerializeField] private ClipSetting phaseTwoClip = new(0f, 1f, 1f, 0.2f);
        [Tooltip("Full-body collapse. Holds its last pose.")]
        [SerializeField] private ClipSetting deathClip = new(0f, 1f, 1f, 0.15f);

        [Header("Look")]
        [Tooltip("Blend from an attack back to rest, s.")]
        [SerializeField, Min(0f)] private float restFade = 0.3f;
        [Tooltip("Blend out of the way when a topple cuts in, s.")]
        [SerializeField, Min(0f)] private float interruptFade = 0.1f;
        [Tooltip("How fast the halo shrinks away at her death, 1/s (exponential). About 3 has it gone " +
                 "before she falls.")]
        [SerializeField, Min(0.1f)] private float haloShrinkRate = 3f;

        /// <summary>A clip started: for tests and harnesses that check the fight's order.</summary>
        public event Action<ClipAction> ClipStarted;

        /// <summary>What she is shown doing now.</summary>
        public ClipAction Current => _director != null ? _director.Active : ClipAction.None;

        HypatiaAnimationDirector _director;
        HypatiaBoss _boundBoss;
        CelestialRingEmitter _boundRings;
        KnockbackPulse _boundPulse;
        ArenaBurn _boundBurn;
        int _upperLayer = -1;
        bool _haloShrinking;

        // Refreshed in OnEnable, so a clip swapped by setup cannot leave a stale length.
        float _ringLength, _ellipseLength, _parabolaLength, _hyperbolaLength, _pulseLength,
              _burningLength, _phaseTwoLength, _deathLength;

        void OnEnable()
        {
            _director = new HypatiaAnimationDirector();
            _director.Started += OnClipStarted;
            _director.Finished += OnClipFinished;

            _upperLayer = -1;
            ReadClipLengths();

            _boundBoss = boss;
            if (_boundBoss != null)
            {
                _boundBoss.ConicTelegraphed += OnConicTelegraphed;
                _boundBoss.PhaseTwoStarted += OnPhaseTwoStarted;
                _boundBoss.Defeated += OnDefeated;
                _boundBoss.Toppled += OnToppled;
                _boundBoss.Recovered += OnRecovered;
            }
            _boundRings = rings;
            if (_boundRings != null) _boundRings.GapForecast += OnGapForecast;
            _boundPulse = pulse;
            if (_boundPulse != null)
            {
                _boundPulse.PulseCharging += OnPulseCharging;
                _boundPulse.PulseCancelled += OnPulseCancelled;
            }
            _boundBurn = burn;
            if (_boundBurn != null) _boundBurn.BandSmouldering += OnBandSmouldering;

            // A reload mid-attack leaves a clip holding its last frame with no one to end it.
            CrossFadeUpper(EmptyId, 0f);
            if (boss == null) return;
            if (boss.IsDefeated) OnDefeated();
            else if (boss.IsToppled) _director.BeginTopple();
        }

        void OnDisable()
        {
            if (_boundBoss != null)
            {
                _boundBoss.ConicTelegraphed -= OnConicTelegraphed;
                _boundBoss.PhaseTwoStarted -= OnPhaseTwoStarted;
                _boundBoss.Defeated -= OnDefeated;
                _boundBoss.Toppled -= OnToppled;
                _boundBoss.Recovered -= OnRecovered;
            }
            _boundBoss = null;
            if (_boundRings != null) _boundRings.GapForecast -= OnGapForecast;
            _boundRings = null;
            if (_boundPulse != null)
            {
                _boundPulse.PulseCharging -= OnPulseCharging;
                _boundPulse.PulseCancelled -= OnPulseCancelled;
            }
            _boundPulse = null;
            if (_boundBurn != null) _boundBurn.BandSmouldering -= OnBandSmouldering;
            _boundBurn = null;
            if (_director != null)
            {
                _director.Started -= OnClipStarted;
                _director.Finished -= OnClipFinished;
            }
        }

        void Update()
        {
            float scale = boss != null && boss.Context != null ? boss.Context.BossTimeScale : 1f;
            _director.Tick(Time.deltaTime * scale);

            if (!_haloShrinking || halo == null) return;
            halo.localScale *= Mathf.Exp(-haloShrinkRate * Time.deltaTime);
            if (halo.localScale.x >= 0.02f) return;
            halo.gameObject.SetActive(false);
            _haloShrinking = false;
        }

        // ─── What the fight is doing ─────────────────────────────────────────

        void OnGapForecast(RingSnapshot forecast) =>
            Ask(ClipAction.Rings, ringClip, _ringLength, forecast.LeadSeconds);

        void OnConicTelegraphed(ConicKind kind, float eccentricity, float lead)
        {
            HypatiaConfig config = boss.Config;
            float toRelease = HypatiaAnimationDirector.ConicReleaseLead(kind, lead, config.AsymptoteLead,
                                                                        config.LobFlightTime, config.LobMarkerLead);
            switch (kind)
            {
                case ConicKind.Ellipse: Ask(ClipAction.ConicEllipse, ellipseClip, _ellipseLength, toRelease); break;
                case ConicKind.Parabola: Ask(ClipAction.ConicParabola, parabolaClip, _parabolaLength, toRelease); break;
                default: Ask(ClipAction.ConicHyperbola, hyperbolaClip, _hyperbolaLength, toRelease); break;
            }
        }

        void OnPulseCharging(float seconds) => Ask(ClipAction.Pulse, pulseClip, _pulseLength, seconds);

        void OnPulseCancelled() => _director.Cancel(ClipAction.Pulse);

        void OnBandSmouldering(int band, float inner, float outer, float lead) =>
            Ask(ClipAction.Burning, burningClip, _burningLength, lead);

        void OnPhaseTwoStarted() => Ask(ClipAction.PhaseTwo, phaseTwoClip, _phaseTwoLength, 0f);

        void OnToppled(float duration) => _director.BeginTopple();

        void OnRecovered() => _director.EndTopple();

        void OnDefeated()
        {
            _haloShrinking = halo != null && halo.gameObject.activeSelf;
            var timing = new HypatiaAnimationDirector.ClipTiming(_deathLength, 0f);
            _director.BeginDeath(HypatiaAnimationDirector.Plan(in timing, 0f));
        }

        void Ask(ClipAction action, in ClipSetting clip, float length, float lead)
        {
            if (length <= 0f) return;   // no clip for this state: the attack simply plays without one
            float playable = clip.leaveAt > 0f ? Mathf.Min(clip.leaveAt, length) : length;
            var timing = new HypatiaAnimationDirector.ClipTiming(playable, clip.release, clip.minSpeed, clip.maxSpeed);
            _director.Request(action, HypatiaAnimationDirector.Plan(in timing, lead));
        }

        // ─── What the Animator does ──────────────────────────────────────────

        void OnClipStarted(ClipAction action, HypatiaAnimationDirector.ClipPlan plan)
        {
            ClipStarted?.Invoke(action);
            if (!AnimatorReady) return;

            switch (action)
            {
                case ClipAction.Topple:
                    CrossFadeUpper(EmptyId, interruptFade);
                    return;

                case ClipAction.Death:
                    CrossFadeUpper(EmptyId, interruptFade);
                    animator.CrossFadeInFixedTime(Animator.StringToHash(StateName(action)), deathClip.fade, 0);
                    return;
            }

            if (UpperLayerIndex < 0) return;
            animator.SetFloat(SpeedId, plan.Speed);
            animator.CrossFadeInFixedTime(Animator.StringToHash(StateName(action)), FadeFor(action), UpperLayerIndex, plan.Offset);
        }

        void OnClipFinished(ClipAction action)
        {
            if (action != ClipAction.Topple) CrossFadeUpper(EmptyId, restFade);
        }

        // An animator enabled after this view has no layers yet, so nothing here may run before it is ready.
        bool AnimatorReady => animator != null && animator.runtimeAnimatorController != null
                              && animator.isActiveAndEnabled && animator.isInitialized;

        int UpperLayerIndex
        {
            get
            {
                if (_upperLayer < 0 && AnimatorReady) _upperLayer = animator.GetLayerIndex(UpperLayer);
                return _upperLayer;
            }
        }

        void CrossFadeUpper(int state, float fade)
        {
            if (AnimatorReady && UpperLayerIndex >= 0) animator.CrossFadeInFixedTime(state, fade, UpperLayerIndex);
        }

        float FadeFor(ClipAction action) => action switch
        {
            ClipAction.Rings => ringClip.fade,
            ClipAction.ConicEllipse => ellipseClip.fade,
            ClipAction.ConicParabola => parabolaClip.fade,
            ClipAction.ConicHyperbola => hyperbolaClip.fade,
            ClipAction.Pulse => pulseClip.fade,
            ClipAction.Burning => burningClip.fade,
            ClipAction.PhaseTwo => phaseTwoClip.fade,
            _ => 0.2f,
        };

        /// <summary>The animator state showing this action (also its clip's name, with
        /// <see cref="ClipPrefix"/>). The editor builds the controller from these.</summary>
        public static string StateName(ClipAction action) => action switch
        {
            ClipAction.Rings => "Rings",
            ClipAction.Burning => "Burning",
            ClipAction.ConicEllipse => "ConicEllipse",
            ClipAction.ConicParabola => "ConicParabola",
            ClipAction.ConicHyperbola => "ConicHyperbola",
            ClipAction.Pulse => "Pulse",
            ClipAction.PhaseTwo => "PhaseTwo",
            ClipAction.Death => "Death",
            _ => null,
        };

        void ReadClipLengths()
        {
            _ringLength = _ellipseLength = _parabolaLength = _hyperbolaLength = _pulseLength =
                _burningLength = _phaseTwoLength = _deathLength = 0f;
            if (animator == null || animator.runtimeAnimatorController == null) return;

            // Allocates the array once per enable; never per frame.
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip == null) continue;
                switch (clip.name)
                {
                    case ClipPrefix + "Rings": _ringLength = clip.length; break;
                    case ClipPrefix + "ConicEllipse": _ellipseLength = clip.length; break;
                    case ClipPrefix + "ConicParabola": _parabolaLength = clip.length; break;
                    case ClipPrefix + "ConicHyperbola": _hyperbolaLength = clip.length; break;
                    case ClipPrefix + "Pulse": _pulseLength = clip.length; break;
                    case ClipPrefix + "Burning": _burningLength = clip.length; break;
                    case ClipPrefix + "PhaseTwo": _phaseTwoLength = clip.length; break;
                    case ClipPrefix + "Death": _deathLength = clip.length; break;
                }
            }
        }
    }
}
