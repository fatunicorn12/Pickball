using Pickleball.Sandbox.Juggle;
using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// The training stage's sandbag: something to hit, lock on to and read
    /// damage numbers off, that never stays down.
    ///
    /// It is an ordinary <see cref="Health"/>, so the sword, lock-on, health
    /// bar and damage numbers treat it exactly like the boss and need no
    /// special case. Its health refills a moment after the last hit (and after
    /// a KO), so a combo can be practised over and over against a full bar.
    ///
    /// Knockback rocks the <see cref="visual"/> on a spring, while the root stays
    /// planted. A target that actually flew off would wander away from the
    /// measuring grid it is standing on. The body collider does ride with the
    /// visual, and <see cref="Health.BodyOffset"/> tells lock-on where it went, so
    /// a target in the air is hit and aimed at where it is drawn.
    ///
    /// Hits go through a <see cref="JuggleState"/>, the same Castle-Crashers
    /// weight-class juggle an enemy would use: a launcher pops the visual up,
    /// every air hit relaunches it by the weight rule, a knock-away throws it
    /// clear (it walks back once it has recovered), and a KO taken in the air
    /// holds the refill until it lands. Pick the weight class in
    /// <see cref="juggle"/> (Medium by default) and use the component's
    /// <i>Apply Weight Preset</i> menu to restore that class's numbers.
    ///
    /// A <see cref="RevengeGauge"/> sits in front of the juggle: past its limit
    /// the dummy breaks out of the combo, pulses, and takes hits without reacting
    /// for a moment. It has no counterattack; <see cref="BrokeOut"/> is the hook an
    /// enemy's AI would use.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class TrainingDummy : MonoBehaviour
    {
        [Tooltip("Rocked and popped by hits. Keep the collider off it, on the root.")]
        [SerializeField] private Transform visual;

        [Header("Refill")]
        [Tooltip("Seconds after the last hit before health snaps back to full.")]
        [SerializeField] private float refillDelay = 2.5f;

        [Header("Reaction")]
        [Tooltip("Degrees of lean per metre/second of knockback.")]
        [SerializeField] private float leanPerKnockback = 6f;
        [SerializeField] private float maxLean = 40f;
        [SerializeField] private float springStiffness = 140f;
        [SerializeField] private float springDamping = 9f;

        [Header("Juggle")]
        [Tooltip("Weight class and juggle tuning. Live: edits apply on the next hit.")]
        [SerializeField] private JuggleSettings juggle = JuggleSettings.For(JuggleWeight.Medium);

        [Tooltip("How much damage in one stretch before it breaks out of the combo. Live: " +
                 "edits apply on the next hit.")]
        [SerializeField] private RevengeSettings revenge = RevengeSettings.For(JuggleWeight.Medium);

        [Tooltip("How fast a thrown dummy walks back to its mark once it has recovered, as a " +
                 "smoothing rate per second. Higher snaps back sooner.")]
        [Min(0f)] [SerializeField] private float returnRate = 4f;

        [Tooltip("Extra scale of the visual at the instant it breaks out of a combo (0.25 = " +
                 "25% bigger), fading over about a quarter of a second.")]
        [Min(0f)] [SerializeField] private float breakOutPulse = 0.25f;

        /// <summary>It has taken too much and broken out of the combo. Raised once per
        /// break-out; an enemy's AI would start its counterattack here.</summary>
        public event System.Action BrokeOut;

        const float PulseFadePerSecond = 4f;

        Health _health;
        CapsuleCollider _body;
        Vector3 _bodyRestCentre;
        Vector3 _visualRest;
        Vector3 _visualRestScale = Vector3.one;
        Quaternion _visualRestRotation;
        float _lastHit = -999f;

        Vector3 _leanAxis = Vector3.right;
        float _lean, _leanVelocity;
        Vector3 _knockDirection;
        Vector3 _knockOffset;
        float _pulse;
        JuggleState _juggle;
        RevengeGauge _revenge;

        /// <summary>The dummy's juggle: height, air hits, hitstun. For reading;
        /// the dummy drives it from its own hits. Built lazily because plain C#
        /// state comes back null after a hot reload.</summary>
        public JuggleState Juggle => _juggle ??= new JuggleState(juggle);

        /// <summary>The dummy's break-out tally. For reading, and lazy, like
        /// <see cref="Juggle"/>.</summary>
        public RevengeGauge Revenge => _revenge ??= new RevengeGauge(revenge);

        void Awake()
        {
            _health = GetComponent<Health>();
            if (visual != null)
            {
                _visualRest = visual.localPosition;
                _visualRestRotation = visual.localRotation;
                _visualRestScale = visual.localScale;
            }
            if (TryGetComponent(out _body)) _bodyRestCentre = _body.center;
        }

        void OnEnable()
        {
            _health.Hurt += OnHurt;
            _health.Impacted += OnImpacted;
            _health.Died += OnDied;
        }

        void OnDisable()
        {
            _health.Hurt -= OnHurt;
            _health.Impacted -= OnImpacted;
            _health.Died -= OnDied;
        }

        [ContextMenu("Apply Weight Preset")]
        void ApplyWeightPreset()
        {
            juggle = JuggleSettings.For(juggle.Weight);
            revenge = RevengeSettings.For(juggle.Weight);
            if (_juggle != null) _juggle.Settings = juggle;
            if (_revenge != null) _revenge.Settings = revenge;
        }

        void OnHurt(float amount, Vector3 at) => _lastHit = Time.time;

        // A KO in the air waits for the ground; the dummy's version of dying is
        // the refill, which Update holds while the defeat is pending.
        void OnDied(Health _) => Juggle.Defeat();

        void OnImpacted(HitImpact impact)
        {
            var gauge = Revenge;
            gauge.Settings = revenge;
            if (gauge.Add(impact.Damage))
            {
                Juggle.BreakOut();
                _pulse = 1f;
                BrokeOut?.Invoke();
                return;
            }
            if (gauge.IsArmored) return;    // it hurts, but it no longer flinches

            Vector3 flat = Vector3.ProjectOnPlane(impact.Knockback, Vector3.up);
            float flatSpeed = flat.magnitude;
            if (flatSpeed > 1e-2f)
            {
                // A body already flying keeps the line it was thrown on: a finisher's
                // ring arriving just after its blade must not bend the arc.
                if (!Juggle.IsKnockedAway) _knockDirection = flat / flatSpeed;
                // Lean away from the hit: rotate about the axis across it.
                _leanAxis = Vector3.Cross(Vector3.up, flat / flatSpeed);
                // A kick of lean * natural frequency peaks at about that lean
                // with this little damping.
                float lean = Mathf.Min(flatSpeed * leanPerKnockback, maxLean);
                _leanVelocity = lean * Mathf.Sqrt(springStiffness) * 1.4f;
            }
            Juggle.Settings = juggle;
            Juggle.Hit(impact.Reaction, flatSpeed);
        }

        void Update()
        {
            float dt = Time.deltaTime;

            var juggleState = Juggle;
            juggleState.Settings = juggle;
            // Touching down with a KO is when the dummy "dies": the refill
            // timer starts from the landing, not from the last hit.
            if (juggleState.Tick(dt) == JuggleLanding.LandedDefeated)
                _lastHit = Time.time;

            var gauge = Revenge;
            gauge.Settings = revenge;
            gauge.Tick(dt);

            bool hurt = _health.IsDead || _health.Current < _health.Max;
            if (hurt && !juggleState.DefeatPending && Time.time - _lastHit >= refillDelay)
                _health.ResetToFull();

            // Thrown, it flies; once it can act again it walks back to its mark.
            _knockOffset += _knockDirection * (juggleState.KnockSpeed * dt);
            if (juggleState.CanAct) _knockOffset *= Mathf.Exp(-returnRate * dt);
            Vector3 bodyOffset = _knockOffset + Vector3.up * juggleState.Height;
            _health.BodyOffset = bodyOffset;
            Vector3 localOffset = transform.InverseTransformDirection(bodyOffset);
            if (_body != null) _body.center = _bodyRestCentre + localOffset;

            if (visual == null) return;

            // Damped spring back to upright.
            _leanVelocity += (-springStiffness * _lean - springDamping * _leanVelocity) * dt;
            _lean += _leanVelocity * dt;
            _pulse = Mathf.Max(0f, _pulse - PulseFadePerSecond * dt);

            Vector3 localAxis = transform.InverseTransformDirection(_leanAxis);
            visual.localRotation = Quaternion.AngleAxis(_lean, localAxis) * _visualRestRotation;
            visual.localPosition = _visualRest + localOffset;
            visual.localScale = _visualRestScale * (1f + breakOutPulse * _pulse);
        }
    }
}
