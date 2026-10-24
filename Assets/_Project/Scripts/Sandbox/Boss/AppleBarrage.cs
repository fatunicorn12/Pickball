using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Phase two's pressure: apples, from above, continuously — and absurdly.
    /// The design calls for a screen full of fast-falling fruit, so the rate is
    /// in the teens per second and they fall at roughly twice normal gravity.
    /// Each apple is individually cheap (low damage, recoverable) so the volume
    /// is the threat, not any single hit.
    ///
    /// Spawn points are biased toward the player but scattered, so the barrage
    /// is an area to read rather than a homing attack to dodge. What makes it
    /// interesting is that it does not act alone — every apple falls through the
    /// live gravity wells, so the wells bend them into convergence points where
    /// they collide and annihilate. See <see cref="Apple"/>: the practical result
    /// is that ground near a well becomes the safest place to stand during a
    /// barrage, exactly inverting phase one, where it was the most dangerous.
    ///
    /// The telegraph is a ground decal at the predicted landing point. It is
    /// deliberately drawn from a straight-line ballistic prediction, ignoring the
    /// wells — so the marker shows where the apple <i>would</i> land unimpeded,
    /// and watching a live apple curve away from its own marker is how the player
    /// learns the field is bending it.
    /// </summary>
    public class AppleBarrage : MonoBehaviour
    {
        [Header("Rate")]
        [SerializeField] private float applesPerSecond = 14f;
        [SerializeField] private float spawnHeight = 18f;

        [Tooltip("Radius around the player that spawns are scattered over.")]
        [SerializeField] private float spread = 10f;

        [Tooltip("Fraction of apples aimed tightly at the player rather than " +
                 "scattered, so standing still under the barrage is never safe.")]
        [Range(0f, 1f)]
        [SerializeField] private float focusedShare = 0.25f;
        [SerializeField] private float focusedSpread = 2.5f;

        [Tooltip("Sideways speed given at spawn, so apples arrive on an arc and " +
                 "the wells have a lateral component to work against.")]
        [SerializeField] private float lateralSpeed = 1.5f;

        [Header("World")]
        [Tooltip("Y the apples splat at. Sampled from the arena floor at start if " +
                 "a collider is found beneath the boss — his own colliders are " +
                 "excluded, so this is the floor and never the top of his head.")]
        [SerializeField] private float groundY;

        [Header("Prefabs")]
        [SerializeField] private Apple applePrefab;
        [SerializeField] private GameObject impactMarkerPrefab;
        [Tooltip("Extra seconds the telegraph lingers past the predicted impact.")]
        [SerializeField] private float markerLinger = 0.1f;

        /// <summary>Clearance above the boss's own bounds that the floor probe
        /// starts from. Only has to lift the origin clear of his geometry; the
        /// probe discards hits on him regardless.</summary>
        const float ProbeHeadroom = 0.5f;

        /// <summary>How far below his feet the probe will look for a floor.
        /// Generous on purpose — the alternative to finding it is a barrage that
        /// splats at the wrong height for the whole fight.</summary>
        const float ProbeDepth = 50f;

        public float GroundY => groundY;
        public bool IsRunning { get; private set; }

        float _spawnAccumulator;
        ChibiLocomotion _player;
        Health _playerHealth;

        void Start()
        {
            _player = FindFirstObjectByType<ChibiLocomotion>();
            if (_player != null) _playerHealth = _player.GetComponent<Health>();

            SampleGroundY();
        }

        /// <summary>
        /// Find the arena floor under the boss.
        ///
        /// This component lives on the boss, and his capsule spans his full
        /// height, so a probe started at some fixed height above his feet is
        /// either inside that capsule — which Unity skips, since a ray ignores
        /// colliders containing its origin — or above his head, in which case it
        /// hits him and every apple in the fight splats at head height with its
        /// telegraph floating in mid-air. Which of those you get is decided
        /// entirely by how tall someone decided to make him, and his height is a
        /// look choice with no business being load-bearing here.
        ///
        /// So both halves are made independent of it: the origin is derived from
        /// his actual bounds, and any hit on his own hierarchy is discarded even
        /// if the origin ends up somewhere unexpected.
        /// </summary>
        void SampleGroundY()
        {
            float feetY = transform.position.y;
            float topY = feetY + ProbeHeadroom;

            var own = GetComponentsInChildren<Collider>();
            for (int i = 0; i < own.Length; i++)
                topY = Mathf.Max(topY, own[i].bounds.max.y + ProbeHeadroom);

            Vector3 origin = new(transform.position.x, topY, transform.position.z);
            float reach = topY - feetY + ProbeDepth;

            var hits = Physics.RaycastAll(origin, Vector3.down, reach, ~0,
                                          QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider.transform.IsChildOf(transform)) continue;  // Newton himself
                if (hits[i].distance >= nearest) continue;
                nearest = hits[i].distance;
                groundY = hits[i].point.y;
            }

            if (float.IsPositiveInfinity(nearest))
                Debug.LogWarning($"[AppleBarrage] No floor found under {name} within " +
                                 $"{reach:0.0} m. Apples will splat at the inspector's " +
                                 $"groundY of {groundY:0.00} — check the arena has a " +
                                 "collider beneath the boss.");
        }

        void Update()
        {
            if (!IsRunning || applePrefab == null || _player == null) return;

            _spawnAccumulator += applesPerSecond * Time.deltaTime;
            while (_spawnAccumulator >= 1f)
            {
                _spawnAccumulator -= 1f;
                SpawnOne();
            }
        }

        void SpawnOne()
        {
            float radius = Random.value < focusedShare ? focusedSpread : spread;
            Vector2 offset = Random.insideUnitCircle * radius;
            Vector3 origin = _player.transform.position
                           + new Vector3(offset.x, 0f, offset.y)
                           + Vector3.up * spawnHeight;

            Vector3 lateral = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
            if (lateral.sqrMagnitude > 0.001f) lateral = lateral.normalized * lateralSpeed;

            var apple = Instantiate(applePrefab, origin, Random.rotation);
            apple.Launch(lateral, this, _player, _playerHealth);

            PlaceMarker(apple, origin, lateral);
        }

        /// <summary>
        /// Straight ballistic prediction, wells excluded on purpose — see the
        /// class comment. Solves h = ½gt² for the fall time, then offsets by the
        /// lateral drift over that time.
        /// </summary>
        void PlaceMarker(Apple apple, Vector3 origin, Vector3 lateral)
        {
            if (impactMarkerPrefab == null) return;

            // Read off the apple that was just spawned. Written in here as a
            // literal instead, retuning the apple's gravity would move every
            // telegraph off the arcs it exists to predict, with no compile error
            // and nothing else to notice.
            float gravity = Mathf.Max(apple.FallAcceleration, 0.01f);

            float fall = Mathf.Max(origin.y - groundY, 0.01f);
            float time = Mathf.Sqrt(2f * fall / gravity);
            Vector3 landing = origin + lateral * time;
            landing.y = groundY + 0.02f;

            // Unrotated. The prefab is a cube already scaled flat in XZ, so it is
            // a decal lying on the floor exactly as authored. The 90° X rotation
            // this used to apply is what a Quad needs — a Quad's face lies in XY —
            // and on a box it tipped the slab onto its edge as a 1.3 m wall, which
            // told the player nothing about where the apple was going to land.
            // Lives exactly as long as the fall, so a marker on the floor always
            // means an apple is still coming.
            var marker = Instantiate(impactMarkerPrefab, landing, Quaternion.identity);
            Destroy(marker, time + markerLinger);
        }

        public void Begin() => IsRunning = true;

        public void End()
        {
            IsRunning = false;
            _spawnAccumulator = 0f;
        }

        /// <summary>Clear the sky — used when the boss dies mid-barrage so the
        /// player is not killed by an apple after the fight is over.</summary>
        public void ClearAirborne()
        {
            for (int i = Apple.Airborne.Count - 1; i >= 0; i--)
                if (Apple.Airborne[i] != null) Apple.Airborne[i].Cancel();
        }
    }
}
