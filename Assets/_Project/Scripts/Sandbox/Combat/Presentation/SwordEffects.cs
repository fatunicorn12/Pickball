using UnityEngine;

namespace Pickleball.Sandbox.Combat.Presentation
{
    /// <summary>
    /// Everything the sword shows rather than does: the blade ribbon, impact
    /// sparks, per-step hit pops, finisher rings, camera shake and FOV kick.
    ///
    /// Listens to MeleeCombat and reads each step's
    /// <see cref="SwingPresentation"/>; gameplay never calls in here. That is
    /// what lets a slash be restyled, or this whole component removed, without
    /// touching damage or timing.
    /// </summary>
    public class SwordEffects : MonoBehaviour
    {
        [SerializeField] private MeleeCombat combat;
        [SerializeField] private BladeTrail trail;
        [SerializeField] private SandboxOrbitCamera cameraRig;

        [Tooltip("Optional sparks played at every blade hit, whatever the step.")]
        [SerializeField] private ParticleSystem impactEffect;

        [Header("Finishers")]
        [Tooltip("Extra camera shake multiplier on a chain's last hit (×). " +
                 "Sells the finisher without every step authoring it.")]
        [Min(1f)] [SerializeField] private float lastHitShakeScale = 1.4f;

        [Tooltip("Seconds an FOV punch takes to spring back.")]
        [Min(0.01f)] [SerializeField] private float fovPunchSeconds = 0.25f;

        EffectPool _pool;
        Transform _poolRoot;

        void Awake()
        {
            if (combat == null) combat = GetComponent<MeleeCombat>();
            if (cameraRig == null && Camera.main != null)
                cameraRig = Camera.main.GetComponent<SandboxOrbitCamera>();
        }

        void OnEnable()
        {
            // A world-space root, so pops and rings stay where they were struck
            // rather than riding along with the player.
            _poolRoot = new GameObject($"{name}_SwordEffects").transform;
            _pool = new EffectPool(_poolRoot);

            if (combat == null) return;
            combat.SwingStarted += OnSwingStarted;
            combat.BladeLiveChanged += OnBladeLive;
            combat.HitLanded += OnHitLanded;
            combat.BurstReleased += OnBurstReleased;
            combat.ChainEnded += OnChainEnded;
        }

        void OnDisable()
        {
            if (combat != null)
            {
                combat.SwingStarted -= OnSwingStarted;
                combat.BladeLiveChanged -= OnBladeLive;
                combat.HitLanded -= OnHitLanded;
                combat.BurstReleased -= OnBurstReleased;
                combat.ChainEnded -= OnChainEnded;
            }
            _pool?.Clear();
            _pool = null;
            if (_poolRoot != null) Destroy(_poolRoot.gameObject);
            _poolRoot = null;
            if (trail != null) trail.SetEmitting(false);
        }

        void OnSwingStarted(ResolvedStep placement)
        {
            var look = placement.Step.Presentation;
            if (trail != null) trail.SetTint(look.TrailColor);
            if (cameraRig != null && Mathf.Abs(look.FovPunch) > 0.01f)
                cameraRig.PunchFov(look.FovPunch, fovPunchSeconds);
        }

        void OnBladeLive(bool live)
        {
            if (trail != null) trail.SetEmitting(live);
        }

        void OnChainEnded(ResolvedStep _)
        {
            if (trail != null) trail.SetEmitting(false);
        }

        void OnHitLanded(MeleeHitReport hit)
        {
            var step = hit.Placement.Step;
            if (step == null) return;
            var look = step.Presentation;

            if (!hit.FromBurst)
            {
                if (impactEffect != null)
                {
                    impactEffect.transform.position = hit.Point;
                    impactEffect.Play();
                }
                _pool?.Play(look.HitEffect, new EffectCue(hit.Point, transform.rotation, 0.5f, 0f,
                                                          look.TrailColor));
            }

            if (cameraRig != null && look.ShakeAmount > 0f)
            {
                float scale = hit.Placement.IsLast ? lastHitShakeScale : 1f;
                cameraRig.Shake(look.ShakeAmount * scale, look.ShakeSeconds);
            }
        }

        void OnBurstReleased(BurstReport burst)
        {
            var look = burst.Placement.Step.Presentation;
            _pool?.Play(look.BurstEffect, new EffectCue(burst.Centre, Quaternion.identity,
                                                        burst.Burst.Radius, burst.Burst.ExpandSeconds,
                                                        look.TrailColor));
        }
    }
}
