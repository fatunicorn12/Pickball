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

        [Header("Hit flash")]
        [Tooltip("Seconds the struck target's renderers flash white. 0 = off.")]
        [Range(0f, 0.3f)] [SerializeField] private float hitFlashSeconds = 0.08f;

        [Header("Audio (procedural placeholder)")]
        [Tooltip("Play generated swing/hit sounds. Cached once; replace with real clips later.")]
        [SerializeField] private bool proceduralAudio = true;
        [Range(0f, 1f)] [SerializeField] private float audioVolume = 0.5f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        const int MaxFlashes = 8;

        sealed class Flash
        {
            public Renderer[] Renderers;
            public FlashTimer Timer;
            public bool Active;
        }

        EffectPool _pool;
        Transform _poolRoot;
        Flash[] _flashes;
        MaterialPropertyBlock _block;
        readonly System.Collections.Generic.Dictionary<Transform, Renderer[]> _rendererCache = new();
        AudioSource _audio;
        static AudioClip _swingClip, _hitClip;

        void Update()
        {
            if (_flashes == null) return;
            float dt = Time.unscaledDeltaTime;
            foreach (var f in _flashes)
            {
                if (!f.Active) continue;
                if (f.Timer.Tick(dt)) ApplyFlash(f, f.Timer.Strength);
                else { ClearFlash(f); f.Active = false; }
            }
        }

        void ApplyFlash(Flash f, float _)
        {
            _block.SetColor(BaseColorId, Color.white);
            _block.SetColor(ColorId, Color.white);
            foreach (var r in f.Renderers) if (r != null) r.SetPropertyBlock(_block);
        }

        static void ClearFlash(Flash f)
        {
            foreach (var r in f.Renderers) if (r != null) r.SetPropertyBlock(null);
        }

        void StartFlash(Collider col)
        {
            if (hitFlashSeconds <= 0f || col == null) return;
            var root = col.attachedRigidbody != null ? col.attachedRigidbody.transform : col.transform.root;
            if (!_rendererCache.TryGetValue(root, out var renderers) || renderers == null)
                _rendererCache[root] = renderers = root.GetComponentsInChildren<Renderer>();
            Flash slot = null;
            foreach (var f in _flashes)
            {
                if (f.Active && f.Renderers == renderers) { slot = f; break; }
                if (slot == null && !f.Active) slot = f;
            }
            if (slot == null) return;
            slot.Renderers = renderers;
            slot.Active = true;
            slot.Timer.Trigger(hitFlashSeconds);
            ApplyFlash(slot, 1f);
        }

        void PlaySound(AudioClip clip, float pitch, float volume)
        {
            if (!proceduralAudio || _audio == null || clip == null) return;
            _audio.pitch = pitch;
            _audio.PlayOneShot(clip, volume * audioVolume);
        }

        /// <summary>Filtered-noise whoosh (swing) and noise+thump (hit), built once.</summary>
        static void BuildClips()
        {
            if (_swingClip != null) return;
            const int rate = 22050;
            int n = (int)(rate * 0.16f);
            var s = new float[n];
            var rng = new System.Random(7);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float env = Mathf.Sin(t * Mathf.PI);
                float noise = (float)(rng.NextDouble() * 2 - 1);
                float a = Mathf.Lerp(0.05f, 0.5f, t);        // rising cutoff: a swish
                lp += a * (noise - lp);
                s[i] = lp * env * 1.6f;
            }
            _swingClip = AudioClip.Create("proc_swing", n, 1, rate, false);
            _swingClip.SetData(s, 0);

            n = (int)(rate * 0.14f);
            var h = new float[n];
            lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float env = Mathf.Exp(-t * 7f);
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp += 0.35f * (noise - lp);
                float thump = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(160f, 60f, t) * i / rate);
                h[i] = (lp * 0.7f + thump * 0.9f) * env;
            }
            _hitClip = AudioClip.Create("proc_hit", n, 1, rate, false);
            _hitClip.SetData(h, 0);
        }

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

            _block = new MaterialPropertyBlock();
            _flashes = new Flash[MaxFlashes];
            for (int i = 0; i < _flashes.Length; i++) _flashes[i] = new Flash();
            if (proceduralAudio)
            {
                BuildClips();
                _audio = _poolRoot.gameObject.AddComponent<AudioSource>();
                _audio.playOnAwake = false;
                _audio.spatialBlend = 0f;
            }

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
            if (_flashes != null)
                foreach (var f in _flashes) if (f.Active) ClearFlash(f);
            _flashes = null;
            _rendererCache.Clear();
            _audio = null;
            _pool?.Clear();
            _pool = null;
            if (_poolRoot != null) Destroy(_poolRoot.gameObject);
            _poolRoot = null;
            if (trail != null) trail.SetEmitting(false);
        }

        void OnSwingStarted(ResolvedStep placement)
        {
            var look = placement.Step.Presentation;
            PlaySound(_swingClip, placement.IsLast ? 0.75f : Random.Range(0.95f, 1.15f), 0.6f);
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

            StartFlash(hit.Collider);
            PlaySound(_hitClip, hit.Placement.IsLast ? 0.8f : Random.Range(0.9f, 1.1f),
                      hit.Placement.IsLast ? 1f : 0.8f);

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
