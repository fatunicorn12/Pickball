using Pickleball.Sandbox.Juggle;
using UnityEngine;

namespace Pickleball.Sandbox.Enemies
{
    /// <summary>
    /// Makes an unrigged enemy look alive by moving its whole body: a hopping waddle
    /// while it runs, a lunge when it strikes, a recoil and a shiver when it is hit, a
    /// swell when it breaks out of a combo, and a topple when it dies. The Tripo models
    /// these enemies use are static meshes, so this is all the animation they have.
    ///
    /// Presentation only: it listens to <see cref="Enemy"/> and poses
    /// <see cref="visual"/>, which must have no colliders on it. Replace this with an
    /// Animator driver once a rigged model exists. The visual's rest pose is identity,
    /// which is why a hot reload loses nothing here.
    /// </summary>
    public class EnemyWaddleView : MonoBehaviour
    {
        [Tooltip("The enemy to read. Set by the setup menu.")]
        [SerializeField] private Enemy enemy;

        [Tooltip("The body to pose: a child at identity, with the model under it.")]
        [SerializeField] private Transform visual;

        [Header("Run")]
        [Tooltip("Hops per second at full run speed. Higher looks frantic.")]
        [Min(0f)] [SerializeField] private float hopsPerSecond = 3f;
        [Tooltip("Metres the body leaves the ground per hop.")]
        [Min(0f)] [SerializeField] private float hopHeight = 0.14f;
        [Tooltip("Degrees it rocks side to side per hop.")]
        [Min(0f)] [SerializeField] private float rockDegrees = 11f;
        [Tooltip("Degrees it leans into the run.")]
        [Min(0f)] [SerializeField] private float leanDegrees = 10f;
        [Tooltip("How fast the waddle fades in and out, as a smoothing rate per second.")]
        [Min(0f)] [SerializeField] private float runBlendRate = 10f;

        [Header("Strike")]
        [Tooltip("Metres the body lunges forward on a strike.")]
        [Min(0f)] [SerializeField] private float lungeDistance = 0.45f;
        [Tooltip("Degrees it pitches forward on a strike.")]
        [Min(0f)] [SerializeField] private float lungeDegrees = 24f;
        [Tooltip("How fast a lunge settles, per second. Keep the settle under the attack " +
                 "interval (0.5 s), or strikes blur together.")]
        [Min(0f)] [SerializeField] private float lungeDecay = 11f;

        [Header("Hit")]
        [Tooltip("Degrees it rears back from a hit.")]
        [Min(0f)] [SerializeField] private float recoilDegrees = 18f;
        [Tooltip("How fast a recoil settles, per second.")]
        [Min(0f)] [SerializeField] private float recoilDecay = 9f;
        [Tooltip("Degrees it shivers side to side while stunned.")]
        [Min(0f)] [SerializeField] private float stunShiverDegrees = 4f;
        [Tooltip("Shiver speed while stunned, radians per second.")]
        [Min(0f)] [SerializeField] private float stunShiverRate = 40f;
        [Tooltip("Extra scale at the instant it breaks out of a combo (0.25 = 25% bigger).")]
        [Min(0f)] [SerializeField] private float breakOutSwell = 0.25f;
        [Tooltip("How fast the swell fades, per second.")]
        [Min(0f)] [SerializeField] private float breakOutDecay = 4f;

        [Header("Death")]
        [Tooltip("Seconds to topple over backward.")]
        [Min(0.01f)] [SerializeField] private float toppleSeconds = 0.45f;
        [Tooltip("Seconds of the corpse time, at its end, spent shrinking away.")]
        [Min(0.01f)] [SerializeField] private float shrinkSeconds = 0.5f;

        float _phase, _run, _lunge, _recoil, _swell, _topple, _deadFor;
        bool _subscribed;

        void OnEnable() => Subscribe();

        void OnDisable() => Unsubscribe();

        void Subscribe()
        {
            if (_subscribed || enemy == null) return;
            _subscribed = true;
            enemy.Struck += OnStruck;
            enemy.Flinched += OnFlinched;
            enemy.BrokeOut += OnBrokeOut;
        }

        void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            if (enemy == null) return;
            enemy.Struck -= OnStruck;
            enemy.Flinched -= OnFlinched;
            enemy.BrokeOut -= OnBrokeOut;
        }

        void OnStruck() => _lunge = 1f;
        void OnFlinched(HitImpact _) => _recoil = 1f;
        void OnBrokeOut() => _swell = 1f;

        void LateUpdate()
        {
            if (enemy == null || visual == null) return;
            float dt = Time.deltaTime;

            float runSpeed = enemy.Config.RunSpeed;
            float wanted = runSpeed > 0f ? Mathf.Clamp01(enemy.Motor.PlanarSpeed / runSpeed) : 0f;
            _run = Mathf.Lerp(_run, wanted, 1f - Mathf.Exp(-runBlendRate * dt));
            // Half a turn per hop: |sin| lifts once and sin rocks one way each half turn.
            _phase = Mathf.Repeat(_phase + hopsPerSecond * Mathf.PI * dt * _run, Mathf.PI * 2f);

            _lunge *= Mathf.Exp(-lungeDecay * dt);
            _recoil *= Mathf.Exp(-recoilDecay * dt);
            _swell *= Mathf.Exp(-breakOutDecay * dt);

            float pitch = leanDegrees * _run + lungeDegrees * _lunge - recoilDegrees * _recoil;
            float rock = Mathf.Sin(_phase) * rockDegrees * _run;
            if (enemy.State == EnemyState.Stunned)
                rock += Mathf.Sin(Time.time * stunShiverRate) * stunShiverDegrees;

            float scale = 1f + breakOutSwell * _swell;
            if (enemy.State == EnemyState.Dead)
            {
                _deadFor += dt;
                _topple = Mathf.Clamp01(_deadFor / toppleSeconds);
                // Ease out: it falls fast and settles on the ground.
                pitch = -90f * (1f - (1f - _topple) * (1f - _topple));
                rock = 0f;
                float leftAlive = enemy.Config.CorpseSeconds - _deadFor;
                scale *= Mathf.Clamp01(leftAlive / shrinkSeconds);
            }

            float lift = Mathf.Abs(Mathf.Sin(_phase)) * hopHeight * _run;
            visual.localPosition = new Vector3(0f, lift, lungeDistance * _lunge);
            visual.localRotation = Quaternion.Euler(pitch, 0f, rock);
            visual.localScale = Vector3.one * scale;
        }
    }
}
