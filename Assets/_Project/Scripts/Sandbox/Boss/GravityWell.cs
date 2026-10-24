using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// A point mass Newton drops on the arena. Pulls anything that cares about
    /// gravity toward it by his own law:
    ///
    ///     F = G·m / r²
    ///
    /// Since everything here is treated as unit mass, the force is also the
    /// acceleration, and it is handed to <see cref="ChibiLocomotion.AddAcceleration"/>
    /// every frame the well is alive.
    ///
    /// The r² is the whole design. Pull is nearly nothing at the rim and vicious
    /// at the centre, so wells do not read as circles you must avoid — they read
    /// as terrain with a gradient, and the corridors between them are navigable
    /// if you respect the falloff. That is the phase-one loop: read the field,
    /// find the shallow line, close the distance.
    ///
    /// <b>The singularity is clamped, deliberately.</b> Literal 1/r² goes to
    /// infinity at r=0 and a single frame at the centre would fling the player
    /// out of the level at whatever speed the timestep happened to allow. Inside
    /// <see cref="coreRadius"/> the force is held flat at its rim value, which is
    /// also where the well switches from pulling to holding: chip damage and a
    /// movement lock, so being caught costs you tempo rather than instantly
    /// killing you.
    ///
    /// <b>What the player actually feels is a drift speed, not an acceleration.</b>
    /// ChibiLocomotion damps field velocity proportionally, so a steady pull
    /// settles at (acceleration / fieldDamping) rather than building forever. At
    /// the shipped tuning — strength 90, damping 2.5 — that ladder is:
    ///
    ///     14 m (rim)  0.18 m/s     a hint that the ground is tilted
    ///      8 m        0.56 m/s     a drift you correct for
    ///      5 m        1.44 m/s     a current you have to lean against
    ///      3 m        4.00 m/s     most of a sprint, spent just holding station
    ///    1.8 m (core) 11.1 m/s     you are not walking out
    ///
    /// (It shipped at 55, where everything outside 5 m — i.e. everywhere a well
    /// is allowed to spawn relative to you — was under 0.9 m/s and barely felt.)
    ///
    /// against a 4.84 m/s sprint. That ladder IS the gradient the fight is built
    /// to be read off, so <see cref="strength"/> and the locomotion's damping are
    /// one tuning decision in two files, not two independent knobs.
    /// </summary>
    public class GravityWell : MonoBehaviour
    {
        [Header("Field")]
        [Tooltip("G·m in the equation above — the pull one metre out, in m/s². " +
                 "Everything else follows from the inverse square.")]
        [SerializeField] private float strength = 90f;

        [Tooltip("Beyond this the well is ignored entirely. Keeps the arena " +
                 "readable and the per-frame cost bounded.")]
        [SerializeField] private float influenceRadius = 14f;

        [Tooltip("Inside this the pull stops climbing and the well starts holding " +
                 "you. Also the visual core.")]
        [SerializeField] private float coreRadius = 1.8f;

        [Header("Core")]
        [Tooltip("Damage per second while inside the core. Chip, not a kill — the " +
                 "cost of a bad line, and it is recoverable.")]
        [SerializeField] private float coreDamagePerSecond = 9f;

        [Tooltip("Movement lock refreshed each frame inside the core. Short: it is " +
                 "re-applied continuously while held, and expires quickly on exit.")]
        [SerializeField] private float coreMoveLock = 0.25f;

        [Header("Life")]
        [Tooltip("Seconds before the well collapses. Zero or less means it stays " +
                 "until something destroys it.")]
        [SerializeField] private float lifetime = 9f;

        [Tooltip("Seconds spent fading in. The field ramps over this window so a " +
                 "well cannot spawn on top of you and yank instantly.")]
        [SerializeField] private float spawnRamp = 0.6f;

        [Header("Presentation")]
        [SerializeField] private Transform core;
        [SerializeField] private ParticleSystem debris;

        /// <summary>Every live well. Apples and any other field-sensitive body read
        /// this rather than each finding wells for itself.</summary>
        public static readonly List<GravityWell> Active = new();

        public Vector3 Centre => transform.position;
        public float InfluenceRadius => influenceRadius;
        public float CoreRadius => coreRadius;

        /// <summary>0..1 ramp while the well is establishing itself.</summary>
        public float Intensity { get; private set; }

        float _age;
        ChibiLocomotion _player;
        Health _playerHealth;

        /// <summary>Latched by <see cref="Collapse"/>. Destroy is deferred to the
        /// end of the frame, so without this the object keeps ticking for the rest
        /// of it — still pulling, and still chipping anyone stood in its core after
        /// the boss that cast it is already dead.</summary>
        bool _collapsing;

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        void Start()
        {
            // Found once. FindFirstObjectByType per-well per-frame is the kind of
            // thing that is invisible with one well and ruinous with twelve.
            _player = FindFirstObjectByType<ChibiLocomotion>();
            if (_player != null) _playerHealth = _player.GetComponent<Health>();
        }

        void Update()
        {
            // Already collapsed, just not yet destroyed. See _collapsing.
            if (_collapsing) return;

            float dt = Time.deltaTime;
            _age += dt;

            Intensity = spawnRamp > 0f ? Mathf.Clamp01(_age / spawnRamp) : 1f;

            if (lifetime > 0f && _age >= lifetime)
            {
                Collapse();
                return;
            }

            if (core != null)
            {
                // Breathe, so a live well never reads as scenery.
                float pulse = 1f + Mathf.Sin(Time.time * 4f) * 0.06f;
                core.localScale = Vector3.one * (coreRadius * 2f * Intensity * pulse);
            }

            if (_player == null) return;

            Vector3 toCentre = transform.position - _player.transform.position;
            float distance = toCentre.magnitude;
            if (distance > influenceRadius || distance < 0.0001f) return;

            _player.AddAcceleration(toCentre / distance * AccelerationAt(distance));

            if (distance <= coreRadius)
            {
                _player.LockMovement(coreMoveLock);
                if (_playerHealth != null)
                    _playerHealth.Damage(coreDamagePerSecond * dt * Intensity,
                                         _player.transform.position);
            }
        }

        /// <summary>
        /// Pull at a given distance, in m/s². Public so the apples — and anything
        /// else that wants to fall correctly — get the identical falloff instead
        /// of a second, subtly different copy of the same equation.
        /// </summary>
        public float AccelerationAt(float distance)
        {
            if (distance > influenceRadius) return 0f;
            // Clamped at the core rim: see the class comment. r is never allowed
            // below coreRadius, so the returned force has a finite ceiling of
            // strength / coreRadius².
            float r = Mathf.Max(distance, coreRadius);
            return strength / (r * r) * Intensity;
        }

        /// <summary>Acceleration vector acting on a point in world space.</summary>
        public Vector3 AccelerationAt(Vector3 worldPoint)
        {
            Vector3 toCentre = transform.position - worldPoint;
            float distance = toCentre.magnitude;
            if (distance < 0.0001f || distance > influenceRadius) return Vector3.zero;
            return toCentre / distance * AccelerationAt(distance);
        }

        /// <summary>Sum of every live well acting on a point — the resultant field.
        ///
        /// Entries are null-checked. The registry is maintained by OnEnable/OnDisable
        /// and pruned early by <see cref="Collapse"/>, but it is public static state
        /// that the EditMode suite writes to directly, and a destroyed MonoBehaviour
        /// compares equal to null while still sitting in a List. One apple hitting a
        /// null here would throw inside its own Update every frame.</summary>
        public static Vector3 FieldAt(Vector3 worldPoint)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < Active.Count; i++)
            {
                GravityWell well = Active[i];
                if (well == null) continue;
                sum += well.AccelerationAt(worldPoint);
            }
            return sum;
        }

        /// <summary>
        /// How hard the field pulls at a point, ignoring direction.
        ///
        /// <b>Nothing in the fight calls this.</b> The comment here used to say the
        /// boss used it to avoid dropping a well onto the player and the barrage
        /// used it to find convergence points. Neither was ever true:
        /// NewtonBoss.DropWell rejects candidates on plain distance to the player,
        /// and AppleBarrage scatters spawns without consulting the field at all.
        /// Corrected rather than deleted because the EditMode suite pins it as the
        /// scalar companion to <see cref="FieldAt"/> — but it is dead weight in the
        /// game, and if a real caller never turns up it should go, test and all.
        /// </summary>
        public static float FieldStrengthAt(Vector3 worldPoint)
            => FieldAt(worldPoint).magnitude;

        /// <summary>
        /// Overwrite the tunables a prefab would normally carry.
        ///
        /// <b>Nothing in the game calls this either.</b> NewtonBoss instantiates the
        /// authored prefab and takes its values as they stand. It survives because
        /// the EditMode suite needs these four set before the first tick, and they
        /// are private serialized fields. Said plainly so nobody wires a spawner
        /// through it believing it to be the supported way to make a well.
        /// </summary>
        public void Configure(float wellStrength, float influence, float core, float life)
        {
            strength = wellStrength;
            influenceRadius = influence;
            coreRadius = core;
            lifetime = life;
        }

        /// <summary>
        /// End the well now.
        ///
        /// <b>Leaving <see cref="Active"/> happens here, not in OnDisable.</b>
        /// Destroy is deferred to the end of the frame, so between this call and
        /// that point the object is neither null nor pruned: it still holds a slot
        /// in the registry, still counts against NewtonBoss's live-well budget, and
        /// still contributes to <see cref="FieldAt"/>. Nothing outside this class
        /// can see through that — a caller counting Active gets a stale answer and
        /// has no way to tell. OnDisable will try to remove it again on the way
        /// out; List.Remove of an absent item returns false and does nothing.
        ///
        /// Removal alone is not enough, which is why <see cref="_collapsing"/>
        /// exists too: leaving the registry stops other systems reading this well,
        /// but the well's own Update is still scheduled for the rest of the frame
        /// and would go on pulling the player and chipping his health. The two are
        /// related but neither substitutes for the other.
        ///
        /// Safe to call twice — NewtonBoss sweeps every live well when he dies,
        /// and a well can collapse of old age in the same frame.
        /// </summary>
        public void Collapse()
        {
            if (_collapsing) return;
            _collapsing = true;
            Active.Remove(this);

            if (debris != null)
            {
                debris.transform.SetParent(null, true);
                debris.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(debris.gameObject, 2f);
            }
            Destroy(gameObject);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.5f, 0.3f, 1f, 0.85f);
            Gizmos.DrawWireSphere(transform.position, coreRadius);
            Gizmos.color = new Color(0.5f, 0.3f, 1f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, influenceRadius);
        }
    }
}
