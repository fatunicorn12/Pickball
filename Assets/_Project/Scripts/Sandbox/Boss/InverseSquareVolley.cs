using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Newton's inverse-square law, as an attack.
    ///
    /// A ring of orbs expands outward from him. Each orb's damage falls off with
    /// the square of how far it has travelled from him when it touches you:
    ///
    ///     damage = baseDamage · (referenceDistance / r)²
    ///
    /// so a ring that catches you at his feet is brutal and the same ring at the
    /// far wall is a tap. That is half of the risk/reward: his weak point is also
    /// close range (<see cref="NewtonBoss"/> scales damage TO him by the same
    /// law), so the place you deal the most is the place you take the most.
    ///
    /// The orbs read the falloff too — they shrink and dim as they travel, so how
    /// big an orb looks is how much it will hurt. They fly low, at shin height,
    /// so a jump or a dash clears a ring: the answer to being close is timing,
    /// not running away.
    ///
    /// Orbs are integrated here rather than each carrying a component, for the
    /// same reason the apples avoid PhysX: a ring is one calculation, not a
    /// negotiation with the solver.
    /// </summary>
    public class InverseSquareVolley : MonoBehaviour
    {
        [Header("Ring")]
        [SerializeField] private int orbsPerRing = 16;
        [SerializeField] private float orbSpeed = 7.5f;
        [SerializeField] private float maxTravel = 20f;

        [Tooltip("Height above Newton's feet the ring flies at. Low enough that a " +
                 "running jump clears it.")]
        [SerializeField] private float flightHeight = 0.45f;

        [Tooltip("Seconds between the tell and the ring leaving him.")]
        [SerializeField] private float windUp = 0.55f;

        [Header("Inverse square")]
        [Tooltip("Damage when hit exactly referenceDistance from Newton.")]
        [SerializeField] private float baseDamage = 8f;
        [SerializeField] private float referenceDistance = 4f;
        [SerializeField] private float minDamage = 1f;
        [SerializeField] private float maxDamage = 30f;

        [Header("Hit")]
        [SerializeField] private float orbRadius = 0.35f;

        [Header("Presentation")]
        [Tooltip("Visual for one orb. A collider-free sphere; created on the fly " +
                 "if left empty.")]
        [SerializeField] private GameObject orbPrefab;
        [SerializeField] private float orbVisualSizeAtReference = 0.6f;

        const float PlayerBodyRadius = 0.45f;
        const float PlayerCentreHeight = 0.9f;

        struct Orb
        {
            public Transform Visual;
            public Vector3 Origin, Direction, Previous;
            public float Travelled;
        }

        readonly List<Orb> _orbs = new();
        readonly List<float> _pendingRings = new();
        ChibiLocomotion _player;
        Health _playerHealth;

        public bool IsWindingUp => _pendingRings.Count > 0;
        public int LiveOrbs => _orbs.Count;

        /// <summary>Damage for an orb that touches you r metres from Newton.
        /// Pure, so the falloff can be tested without a scene.</summary>
        public static float DamageAt(float r, float baseDamage, float reference,
                                     float min, float max)
        {
            float d = Mathf.Max(r, 0.01f);
            float ratio = reference / d;
            return Mathf.Clamp(baseDamage * ratio * ratio, min, max);
        }

        void Start()
        {
            _player = FindFirstObjectByType<ChibiLocomotion>();
            if (_player != null) _playerHealth = _player.GetComponent<Health>();
        }

        /// <summary>Queue a ring. <paramref name="extraDelay"/> lets the boss
        /// stagger a second ring behind the first in phase two.</summary>
        public void Fire(float extraDelay = 0f) => _pendingRings.Add(windUp + extraDelay);

        public void Clear()
        {
            _pendingRings.Clear();
            for (int i = 0; i < _orbs.Count; i++)
                if (_orbs[i].Visual != null) Destroy(_orbs[i].Visual.gameObject);
            _orbs.Clear();
        }

        void OnDisable() => Clear();

        void Update()
        {
            float dt = Time.deltaTime;

            for (int i = _pendingRings.Count - 1; i >= 0; i--)
            {
                _pendingRings[i] -= dt;
                if (_pendingRings[i] > 0f) continue;
                _pendingRings.RemoveAt(i);
                SpawnRing();
            }

            Vector3 playerCentre = _player != null
                ? _player.transform.position + Vector3.up * PlayerCentreHeight
                : Vector3.zero;

            for (int i = _orbs.Count - 1; i >= 0; i--)
            {
                Orb orb = _orbs[i];
                orb.Previous = orb.Origin + orb.Direction * orb.Travelled;
                orb.Travelled += orbSpeed * dt;
                Vector3 now = orb.Origin + orb.Direction * orb.Travelled;

                if (orb.Travelled >= maxTravel || orb.Visual == null)
                {
                    Remove(i);
                    continue;
                }

                orb.Visual.position = now;
                float falloff = Mathf.Clamp(referenceDistance / Mathf.Max(orb.Travelled, 0.5f), 0.35f, 1.6f);
                orb.Visual.localScale = Vector3.one * (orbVisualSizeAtReference * falloff);
                _orbs[i] = orb;

                if (_player != null && HitsPlayer(orb.Previous, now, playerCentre))
                {
                    if (_playerHealth != null)
                        _playerHealth.Damage(DamageAt(orb.Travelled, baseDamage,
                                                      referenceDistance, minDamage, maxDamage),
                                             now);
                    Remove(i);
                }
            }
        }

        bool HitsPlayer(Vector3 from, Vector3 to, Vector3 playerCentre)
        {
            Vector3 seg = to - from;
            float len2 = seg.sqrMagnitude;
            float t = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(playerCentre - from, seg) / len2) : 0f;
            Vector3 nearest = from + seg * t;
            float r = orbRadius + PlayerBodyRadius;
            return (playerCentre - nearest).sqrMagnitude <= r * r;
        }

        void SpawnRing()
        {
            Vector3 origin = transform.position + Vector3.up * flightHeight;
            float offset = Random.Range(0f, 360f);
            int count = Mathf.Max(orbsPerRing, 1);

            for (int i = 0; i < count; i++)
            {
                float angle = (offset + i * 360f / count) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                _orbs.Add(new Orb
                {
                    Visual = MakeVisual(origin),
                    Origin = origin,
                    Direction = dir,
                    Previous = origin,
                    Travelled = 0f,
                });
            }
        }

        Transform MakeVisual(Vector3 at)
        {
            GameObject go;
            if (orbPrefab != null)
            {
                go = Instantiate(orbPrefab, at, Quaternion.identity);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(go.GetComponent<Collider>());
                go.transform.position = at;
            }
            go.name = "InverseSquareOrb";
            return go.transform;
        }

        void Remove(int index)
        {
            if (_orbs[index].Visual != null) Destroy(_orbs[index].Visual.gameObject);
            _orbs.RemoveAt(index);
        }
    }
}
