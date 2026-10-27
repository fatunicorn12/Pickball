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
    /// Knockback rocks the <see cref="visual"/> on a spring and a launcher pops
    /// it up, while the root and its collider stay planted. A target that
    /// actually flew off would wander away from the measuring grid it is
    /// standing on, and lock-on would lose it.
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
        [Tooltip("How high a launcher pops the visual, in metres.")]
        [SerializeField] private float launchHeight = 0.7f;
        [SerializeField] private float springStiffness = 140f;
        [SerializeField] private float springDamping = 9f;

        Health _health;
        Vector3 _visualRest;
        Quaternion _visualRestRotation;
        float _lastHit = -999f;

        Vector3 _leanAxis = Vector3.right;
        float _lean, _leanVelocity;
        float _hop, _hopVelocity;

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
        }

        void OnDisable()
        {
            _health.Hurt -= OnHurt;
            _health.Knocked -= OnKnocked;
        }

        void OnHurt(float amount, Vector3 at) => _lastHit = Time.time;

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
            if (launch)
                _hopVelocity = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * launchHeight);
        }

        void Update()
        {
            float dt = Time.deltaTime;

            bool hurt = _health.IsDead || _health.Current < _health.Max;
            if (hurt && Time.time - _lastHit >= refillDelay)
                _health.ResetToFull();

            if (visual == null) return;

            // Damped spring back to upright.
            _leanVelocity += (-springStiffness * _lean - springDamping * _leanVelocity) * dt;
            _lean += _leanVelocity * dt;

            if (_hop > 0f || _hopVelocity > 0f)
            {
                _hopVelocity += Physics.gravity.y * dt;
                _hop += _hopVelocity * dt;
                if (_hop <= 0f) { _hop = 0f; _hopVelocity = 0f; }
            }

            Vector3 localAxis = transform.InverseTransformDirection(_leanAxis);
            visual.localRotation = Quaternion.AngleAxis(_lean, localAxis) * _visualRestRotation;
            visual.localPosition = _visualRest + Vector3.up * _hop;
        }
    }
}
