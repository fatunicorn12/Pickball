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
    /// Knockback rocks the <see cref="visual"/> on a spring, while the root and
    /// its collider stay planted. A target that actually flew off would wander
    /// away from the measuring grid it is standing on, and lock-on would lose it.
    ///
    /// Launchers and air hits go through a <see cref="JuggleState"/>, the same
    /// Castle-Crashers weight-class juggle an enemy would use: a launcher pops
    /// the visual up, every air hit relaunches it by the weight rule, and a KO
    /// taken in the air holds the refill until it lands. Pick the weight class
    /// in <see cref="juggle"/> (Medium by default) and use the component's
    /// <i>Apply Weight Preset</i> menu to restore that class's numbers.
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

        Health _health;
        Vector3 _visualRest;
        Quaternion _visualRestRotation;
        float _lastHit = -999f;

        Vector3 _leanAxis = Vector3.right;
        float _lean, _leanVelocity;
        JuggleState _juggle;

        /// <summary>The dummy's juggle: height, air hits, hitstun. For reading;
        /// the dummy drives it from its own hits. Built lazily because plain C#
        /// state comes back null after a hot reload.</summary>
        public JuggleState Juggle => _juggle ??= new JuggleState(juggle);

        void Awake()
        {
            _health = GetComponent<Health>();
            if (visual != null)
            {
                _visualRest = visual.localPosition;
                _visualRestRotation = visual.localRotation;
            }
        }

        void OnEnable()
        {
            _health.Hurt += OnHurt;
            _health.Knocked += OnKnocked;
            _health.Died += OnDied;
        }

        void OnDisable()
        {
            _health.Hurt -= OnHurt;
            _health.Knocked -= OnKnocked;
            _health.Died -= OnDied;
        }

        [ContextMenu("Apply Weight Preset")]
        void ApplyWeightPreset()
        {
            juggle = JuggleSettings.For(juggle.Weight);
            if (_juggle != null) _juggle.Settings = juggle;
        }

        void OnHurt(float amount, Vector3 at) => _lastHit = Time.time;

        // A KO in the air waits for the ground; the dummy's version of dying is
        // the refill, which Update holds while the defeat is pending.
        void OnDied(Health _) => Juggle.Defeat();

        void OnKnocked(Vector3 knockback, bool launch)
        {
            Vector3 flat = Vector3.ProjectOnPlane(knockback, Vector3.up);
            if (flat.sqrMagnitude > 1e-4f)
            {
                // Lean away from the hit: rotate about the axis across it.
                _leanAxis = Vector3.Cross(Vector3.up, flat.normalized);
                // A kick of lean * natural frequency peaks at about that lean
                // with this little damping.
                float lean = Mathf.Min(flat.magnitude * leanPerKnockback, maxLean);
                _leanVelocity = lean * Mathf.Sqrt(springStiffness) * 1.4f;
            }
            Juggle.Settings = juggle;
            Juggle.Hit(launch);
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

            bool hurt = _health.IsDead || _health.Current < _health.Max;
            if (hurt && !juggleState.DefeatPending && Time.time - _lastHit >= refillDelay)
                _health.ResetToFull();

            if (visual == null) return;

            // Damped spring back to upright.
            _leanVelocity += (-springStiffness * _lean - springDamping * _leanVelocity) * dt;
            _lean += _leanVelocity * dt;

            Vector3 localAxis = transform.InverseTransformDirection(_leanAxis);
            visual.localRotation = Quaternion.AngleAxis(_lean, localAxis) * _visualRestRotation;
            visual.localPosition = _visualRest + Vector3.up * juggleState.Height;
        }
    }
}
