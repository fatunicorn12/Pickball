using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Simple scripted motion for trying out worldlines. Each mode draws a
    /// different kind of terrain in 4D, under an upward time axis:
    ///
    /// <list type="bullet">
    /// <item><b>Oscillate</b>: swings back and forth along an axis. Draws a
    /// zigzag ramp.</item>
    /// <item><b>Orbit</b>: circles a point. Draws a spiral staircase.</item>
    /// <item><b>Lob</b>: flies a ballistic arc to a target, rests, then jumps
    /// back and goes again. Draws a bridge along the arc. The jump back is a
    /// teleport, so each throw leaves its own separate bridge.</item>
    /// </list>
    ///
    /// Moves the transform directly, in Update, and nothing else. It is a test
    /// rig, not a pattern for gameplay movers.
    /// </summary>
    public class SpacetimeDemoMover : MonoBehaviour
    {
        public enum Mode { Oscillate, Orbit, Lob }

        [SerializeField] private Mode mode = Mode.Oscillate;
        [SerializeField] private float period = 4f;

        [Header("Oscillate")]
        [SerializeField] private Vector3 swingAxis = Vector3.right;
        [SerializeField] private float amplitude = 4f;

        [Header("Orbit")]
        [SerializeField] private float radius = 3f;

        [Header("Lob")]
        [Tooltip("Landing point, relative to where the mover starts.")]
        [SerializeField] private Vector3 lobTarget = new(0f, 0f, 8f);
        [Tooltip("Shorter is flatter. Around 1 s over 8 m gives a bridge you can " +
                 "walk. At 2 s it climbs at about 70°, past the chibi's slope " +
                 "limit, and only the very top of the arc is standable.")]
        [SerializeField] private float flightTime = 1f;
        [SerializeField] private float restTime = 0.75f;
        [SerializeField] private float gravity = -9.81f;

        Vector3 _origin;
        float _clock;

        void Awake() => _origin = transform.position;

        void Update()
        {
            _clock += Time.deltaTime;
            transform.position = mode switch
            {
                Mode.Oscillate => _origin + swingAxis.normalized * (amplitude * Mathf.Sin(Phase())),
                Mode.Orbit => _origin + new Vector3(Mathf.Cos(Phase()), 0f, Mathf.Sin(Phase())) * radius,
                _ => LobPosition(),
            };
        }

        float Phase() => _clock / Mathf.Max(period, 0.01f) * Mathf.PI * 2f;

        /// <summary>
        /// Projectile motion from the origin that lands on the target after
        /// exactly <see cref="flightTime"/>. Solving for the launch velocity,
        /// rather than tuning one, means the arc always lands where it is told
        /// to whatever the gravity and timing.
        /// </summary>
        Vector3 LobPosition()
        {
            float flight = Mathf.Max(flightTime, 0.01f);
            float cycle = flight + Mathf.Max(restTime, 0f);
            float t = Mathf.Min(_clock % cycle, flight);
            var g = new Vector3(0f, gravity, 0f);
            Vector3 launch = (lobTarget - 0.5f * flight * flight * g) / flight;
            return _origin + launch * t + 0.5f * t * t * g;
        }
    }
}
