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
    /// the chain can be extended, when the lunge pushes) needs frame-accurate
    /// timing. Clip durations are written in by the setup script from the real
    /// clips, so the timer and the animation stay in step.
    /// </summary>
    public class MeleeCombat : MonoBehaviour
    {
        [System.Serializable]
        public class Attack
        {
            [Tooltip("Animator trigger that starts this attack.")]
            public string trigger = "Attack1";
            [Tooltip("Clip length. Filled in by the setup from the real clip.")]
            public float duration = 0.7f;

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
            [Tooltip("Forward step during the swing. Root motion is off, so the " +
                     "lunge is scripted — it is most of what gives melee weight.")]
            public float lungeSpeed = 3.5f;
            public float knockback = 4f;
            [Tooltip("Pops the target upward so following hits can juggle it.")]
            public bool launcher;
            [Tooltip("Seconds of freeze on contact. Bigger for heavier hits.")]
            public float hitstop = 0.05f;
        }

        [Header("Chain")]
        [SerializeField] private Attack[] chain =
        {
            new() { trigger = "Attack1", damage = 10f, lungeSpeed = 3.5f },
            new() { trigger = "Attack2", damage = 12f, lungeSpeed = 3.5f },
            new() { trigger = "Attack3", damage = 14f, lungeSpeed = 2.5f,
                    launcher = true, knockback = 3f, hitstop = 0.07f },
            new() { trigger = "Attack4", damage = 20f, lungeSpeed = 4.5f,
                    knockback = 8f, hitstop = 0.1f },
        };
        [SerializeField] private Attack airAttack =
            new() { trigger = "AirAttack", damage = 12f, lungeSpeed = 1.5f, knockback = 3f };

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

        static readonly int ComboHash = Animator.StringToHash("Combo");

        InputAction _attack;
        readonly HashSet<Object> _alreadyHit = new();

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

            if (def.lungeSpeed > 0f && t < def.activeEnd && locomotion != null)
                locomotion.AddMomentum(transform.forward * (def.lungeSpeed * dt * 6f));

            if (_queued && t >= def.comboOpen && !_airborneAttack && _index + 1 < chain.Length)
            {
                Begin(_index + 1, false);
                return;
            }
            if (t >= def.comboClose || t >= 1f) End();
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
            if (locomotion != null) locomotion.MovementLocked = true;
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
            if (locomotion != null) locomotion.MovementLocked = false;
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
