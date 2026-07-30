using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Castle-Crashers-style combo melee. Left click swings; clicking again
    /// inside a window near the end of the current swing chains into the next
    /// attack instead of restarting the first.
    ///
    /// The whole thing is driven from one timer rather than read back off the
    /// Animator. Animator state info is awkward to query reliably the frame a
    /// transition happens, and every window here (when the blade is live, when
    /// the chain can be extended) needs frame-accurate timing. Clip durations
    /// are written in by the setup script from the real clips — divided by each
    /// state's playback speed — so the timer and the animation stay in step.
    ///
    /// Attacking neither moves nor stops the character. Two separate things had
    /// to go for that:
    ///  - the scripted forward lunge, applied every frame of the active window,
    ///    accumulated far faster than momentum drag could shed it and slid the
    ///    player metres per swing (compounding across a chain, since nothing
    ///    reset it between hits);
    ///  - the attacks were full-body states on the base layer, so a swing
    ///    replaced the run outright and froze the legs mid-stride.
    /// The combo now lives on an upper-body-masked layer that this component
    /// fades in for the duration of a swing, so the legs carry on running and
    /// steering is never taken away.
    /// </summary>
    public class MeleeCombat : MonoBehaviour
    {
        [System.Serializable]
        public class Attack
        {
            [Tooltip("Animator trigger that starts this attack.")]
            public string trigger = "Attack1";
            [Tooltip("How long the swing lasts on screen. Filled in by the setup " +
                     "as the real clip's length divided by the state's playback " +
                     "speed, so it tracks the sped-up animation.")]
            public float duration = 0.6f;

            [Header("Windows (fraction of duration)")]
            [Tooltip("When the blade goes live.")]
            [Range(0f, 1f)] public float activeStart = 0.25f;
            [Tooltip("When the blade stops hitting.")]
            [Range(0f, 1f)] public float activeEnd = 0.6f;
            [Tooltip("Earliest point a click can chain to the next attack. " +
                     "Opening this before the blade finishes is what makes the " +
                     "chain feel responsive rather than turn-based.")]
            [Range(0f, 1f)] public float comboOpen = 0.35f;
            [Tooltip("After this the chain is dropped and we return to idle.")]
            [Range(0f, 1f)] public float comboClose = 0.95f;

            [Header("Feel")]
            public float damage = 12f;
            public float knockback = 4f;
            [Tooltip("Pops the target upward so following hits can juggle it.")]
            public bool launcher;
            [Tooltip("Seconds of freeze on contact. Bigger for heavier hits.")]
            public float hitstop = 0.05f;
        }

        [Header("Chain")]
        [SerializeField] private Attack[] chain =
        {
            new() { trigger = "Attack1", damage = 10f },
            new() { trigger = "Attack2", damage = 12f },
            new() { trigger = "Attack3", damage = 14f,
                    launcher = true, knockback = 3f, hitstop = 0.07f },
            new() { trigger = "Attack4", damage = 20f,
                    knockback = 8f, hitstop = 0.1f },
        };
        [SerializeField] private Attack airAttack =
            new() { trigger = "AirAttack", damage = 12f, knockback = 3f };

        [Header("Blade")]
        [Tooltip("Base of the blade — the pivot end.")]
        [SerializeField] private Transform bladeBase;
        [Tooltip("Tip of the blade. The swept volume is measured between the " +
                 "two, across frames.")]
        [SerializeField] private Transform bladeTip;
        [SerializeField] private float bladeRadius = 0.18f;
        [SerializeField] private LayerMask hittable = ~0;

        [Header("Wiring")]
        [SerializeField] private Animator animator;
        [SerializeField] private ChibiLocomotion locomotion;
        [SerializeField] private BladeTrail trail;
        [SerializeField] private SandboxOrbitCamera cameraRig;
        [SerializeField] private ParticleSystem impactEffect;

        [Header("Layer")]
        [Tooltip("Masked upper-body layer the combo states live on. Must match " +
                 "WeaponSetup.AttackLayer.")]
        [SerializeField] private string attackLayer = "Attack";
        [Tooltip("Layer weight fade, in weight per second. Fast enough that the " +
                 "swing does not visibly ramp in, slow enough not to pop.")]
        [SerializeField] private float layerFadeSpeed = 12f;

        static readonly int ComboHash = Animator.StringToHash("Combo");

        InputAction _attack;
        readonly HashSet<Object> _alreadyHit = new();

        int _attackLayerIndex = -1;
        bool _layerResolved;
        float _layerWeight;

        int _index = -1;              // -1 = not attacking
        bool _airborneAttack;
        float _time;
        bool _queued;
        float _hitstopLeft;
        Vector3 _prevTip, _prevBase;
        bool _hasPrev;

        public bool IsAttacking => _index >= 0 || _airborneAttack;

        Attack Current => _airborneAttack ? airAttack
                        : (_index >= 0 && _index < chain.Length ? chain[_index] : null);

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (locomotion == null) locomotion = GetComponent<ChibiLocomotion>();
            if (cameraRig == null && Camera.main != null)
                cameraRig = Camera.main.GetComponent<SandboxOrbitCamera>();
        }

        /// <summary>
        /// Index of the masked attack layer, resolved on first use.
        ///
        /// Deliberately NOT read in Awake: the Animator is on the model child
        /// and this component is on the root, so the root wakes first and the
        /// Animator has not built its playable graph yet. GetLayerIndex would
        /// log "Animator is not playing an AnimatorController", return -1, and
        /// leave the layer stuck at zero weight — the swing would play in the
        /// state machine but never blend in, so nothing visible would happen.
        /// Waiting on runtimeAnimatorController keeps that off the console.
        /// </summary>
        int AttackLayerIndex()
        {
            if (_layerResolved) return _attackLayerIndex;
            if (animator == null || animator.runtimeAnimatorController == null) return -1;

            _layerResolved = true;
            _attackLayerIndex = animator.GetLayerIndex(attackLayer);
            if (_attackLayerIndex < 0)
                Debug.LogWarning($"[MeleeCombat] No '{attackLayer}' layer on the " +
                                 "Animator — swings will not play. Re-run " +
                                 "Tools ▸ Pickleball ▸ Setup Chibi Sandbox.");
            return _attackLayerIndex;
        }

        void OnEnable()
        {
            _attack = new InputAction("Attack", InputActionType.Button);
            _attack.AddBinding("<Mouse>/leftButton");
            _attack.AddBinding("<Gamepad>/buttonWest");
            _attack.Enable();
        }

        void OnDisable()
        {
            _attack?.Disable();
            _attack?.Dispose();
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // Before any early-out below: the fade-out has to keep running for a
            // few frames after the swing ends, when IsAttacking is already false.
            UpdateLayerWeight(dt);

            // Hitstop: freeze the animation and hold movement for a few frames
            // so a connecting hit reads as impact rather than a pass-through.
            if (_hitstopLeft > 0f)
            {
                _hitstopLeft -= dt;
                if (animator != null) animator.speed = 0f;
                if (_hitstopLeft <= 0f && animator != null) animator.speed = 1f;
                return;
            }

            bool pressed = _attack.WasPressedThisFrame() && !CursorJustRelocked();

            if (!IsAttacking)
            {
                if (pressed && CanStart()) Begin(0, !IsGrounded());
                return;
            }

            var def = Current;
            if (def == null) { End(); return; }

            _time += dt;
            float t = Mathf.Clamp01(_time / Mathf.Max(def.duration, 0.01f));

            // Buffer the click; it is consumed when the window opens. Requiring
            // the press to land inside the window would punish clicking early,
            // which is exactly what players do.
            if (pressed) _queued = true;

            bool live = t >= def.activeStart && t <= def.activeEnd;
            if (trail != null) trail.SetEmitting(live);
            if (live) SweepForHits(def);
            else ClearSweep();

            if (_queued && t >= def.comboOpen && !_airborneAttack && _index + 1 < chain.Length)
            {
                Begin(_index + 1, false);
                return;
            }
            if (t >= def.comboClose || t >= 1f) End();
        }

        /// <summary>Fades the masked attack layer in for a swing and back out
        /// afterwards. At zero weight the layer contributes nothing, which is
        /// what keeps its empty default state from forcing the arms into the
        /// bind pose the rest of the time.</summary>
        void UpdateLayerWeight(float dt)
        {
            int layer = AttackLayerIndex();
            if (layer < 0) return;
            float wanted = IsAttacking ? 1f : 0f;
            if (Mathf.Approximately(_layerWeight, wanted)) return;
            _layerWeight = Mathf.MoveTowards(_layerWeight, wanted, layerFadeSpeed * dt);
            animator.SetLayerWeight(layer, _layerWeight);
        }

        bool CanStart()
        {
            if (locomotion != null && locomotion.IsBusy) return false;   // swinging, skating, dashing
            return true;
        }

        bool IsGrounded() => locomotion == null || locomotion.IsGrounded;

        /// <summary>The orbit camera uses left click to re-capture the cursor
        /// after Escape. Without this, that same click also swings the weapon.</summary>
        bool CursorJustRelocked() => cameraRig != null && cameraRig.JustRelocked;

        void Begin(int index, bool airborne)
        {
            _index = airborne ? -1 : index;
            _airborneAttack = airborne;
            _time = 0f;
            _queued = false;
            _alreadyHit.Clear();
            _hasPrev = false;

            var def = Current;
            if (def == null) { End(); return; }

            if (animator != null)
            {
                animator.SetInteger(ComboHash, airborne ? 0 : index + 1);
                animator.SetTrigger(def.trigger);
            }
            // Nothing is done to locomotion here, and that is the point: the
            // swing adds no displacement of its own and takes none away, so you
            // keep running, steering and jumping straight through a combo.
        }

        void End()
        {
            _index = -1;
            _airborneAttack = false;
            _queued = false;
            _alreadyHit.Clear();
            _hasPrev = false;
            if (trail != null) trail.SetEmitting(false);
            if (animator != null) animator.SetInteger(ComboHash, 0);
        }

        void ClearSweep() => _hasPrev = false;

        /// <summary>
        /// Sweep the volume the blade covered since last frame.
        ///
        /// A collider on the blade would tunnel: at swing speed the tip crosses
        /// metres per frame and can be on both sides of a target without ever
        /// overlapping it. Testing the capsule between last frame's blade and
        /// this frame's catches the whole arc.
        /// </summary>
        void SweepForHits(Attack def)
        {
            if (bladeBase == null || bladeTip == null) return;
            Vector3 b = bladeBase.position, tip = bladeTip.position;

            if (_hasPrev)
            {
                // Two capsules: the blade now, and the ground it covered.
                TestCapsule(b, tip, def);
                TestCapsule(_prevTip, tip, def);
                TestCapsule(_prevBase, b, def);
            }
            _prevBase = b;
            _prevTip = tip;
            _hasPrev = true;
        }

        void TestCapsule(Vector3 a, Vector3 b, Attack def)
        {
            var hits = Physics.OverlapCapsule(a, b, bladeRadius, hittable,
                                              QueryTriggerInteraction.Ignore);
            foreach (var col in hits)
            {
                if (col.transform.IsChildOf(transform)) continue;
                var target = col.GetComponentInParent<IDamageable>();
                if (target == null) continue;
                var key = target as Object;
                if (key != null && !_alreadyHit.Add(key)) continue;   // once per swing

                Vector3 point = col.ClosestPoint((a + b) * 0.5f);
                Vector3 push = transform.forward * def.knockback;
                if (def.launcher) push += Vector3.up * (def.knockback * 1.6f);

                target.TakeHit(def.damage, point, push, def.launcher);
                OnHitLanded(def, point);
            }
        }

        void OnHitLanded(Attack def, Vector3 point)
        {
            _hitstopLeft = Mathf.Max(_hitstopLeft, def.hitstop);
            if (impactEffect != null)
            {
                impactEffect.transform.position = point;
                impactEffect.Play();
            }
            if (cameraRig != null) cameraRig.Shake(def.launcher ? 0.25f : 0.15f, 0.18f);
        }

        /// <summary>Editor hook: the setup script writes measured clip lengths in.</summary>
        public void ConfigureDurations(float[] chainDurations, float airDuration)
        {
            for (int i = 0; i < chain.Length && i < chainDurations.Length; i++)
                if (chainDurations[i] > 0f) chain[i].duration = chainDurations[i];
            if (airDuration > 0f) airAttack.duration = airDuration;
        }

        public Attack[] Chain => chain;
        public Attack Air => airAttack;
    }
}
