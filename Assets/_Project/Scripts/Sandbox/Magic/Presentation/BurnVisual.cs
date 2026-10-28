using UnityEngine;

namespace Pickleball.Sandbox.Magic.Presentation
{
    /// <summary>
    /// Draws a <see cref="BurnStatus"/>: flames licking up the burning body,
    /// rising embers and a flickering orange light, sized to the body's
    /// collider. Each damage tick puffs the flames and kicks the light, so the
    /// rhythm of the damage is visible.
    ///
    /// Presentation only, on a CHILD of the burn (which destroys itself when the
    /// burn ends): it detaches in <see cref="BurnStatus.Extinguished"/> and
    /// lets the last flames and embers die over <see cref="lingerSeconds"/>.
    /// </summary>
    public class BurnVisual : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("The burn drawn. Found on a parent when empty.")]
        [SerializeField] private BurnStatus burn;

        [Header("Parts")]
        [Tooltip("Looping flames. Shape radius and emission are sized to the body.")]
        [SerializeField] private ParticleSystem flames;
        [Tooltip("Optional looping embers drifting up.")]
        [SerializeField] private ParticleSystem embers;
        [Tooltip("Optional flickering light at the body's centre.")]
        [SerializeField] private Light glow;

        [Header("Look")]
        [Tooltip("Light intensity at rest; it flickers around this.")]
        [Min(0f)] [SerializeField] private float lightIntensity = 1.8f;
        [Tooltip("Extra light on each damage tick, as a multiple of rest (x).")]
        [Min(0f)] [SerializeField] private float tickLightBoost = 1.5f;
        [Tooltip("Flames emitted at once on each damage tick.")]
        [Min(0)] [SerializeField] private int tickPuff = 10;
        [Tooltip("Flames per second per square metre of body footprint.")]
        [Min(0f)] [SerializeField] private float flameDensity = 110f;
        [Tooltip("Seconds the detached visual lives after the burn ends.")]
        [Min(0.1f)] [SerializeField] private float lingerSeconds = 0.8f;

        float _kick, _fadeAge, _seed;
        bool _ended;

        void OnEnable()
        {
            if (burn == null) burn = GetComponentInParent<BurnStatus>();
            if (burn == null)
            {
                Destroy(gameObject);
                return;
            }
            burn.Ignited += OnIgnited;
            burn.Ticked += OnTicked;
            burn.Extinguished += OnExtinguished;
            _ended = false;
            _seed = Random.value * 10f;
        }

        void OnDisable() => Unsubscribe();

        void Unsubscribe()
        {
            if (burn == null) return;
            burn.Ignited -= OnIgnited;
            burn.Ticked -= OnTicked;
            burn.Extinguished -= OnExtinguished;
        }

        void OnIgnited(BurnStatus source)
        {
            Vector3 extents = source.BodyExtents;
            // Just outside the body: flames inside an opaque body are never seen.
            float radius = Mathf.Max(0.15f, Mathf.Max(extents.x, extents.z) * 1.05f);
            if (flames != null)
            {
                var shape = flames.shape;
                shape.radius = radius;
                var emission = flames.emission;
                emission.rateOverTime = flameDensity * Mathf.PI * radius * radius + 20f;
                if (!flames.isPlaying) flames.Play(true);
            }
            if (embers != null)
            {
                var shape = embers.shape;
                shape.radius = radius;
                if (!embers.isPlaying) embers.Play(true);
            }
            if (glow != null) glow.range = Mathf.Max(2.5f, extents.y * 3f);
            _kick = 1f;
            Follow(source);
        }

        void OnTicked(BurnStatus source, float damage)
        {
            if (flames != null && tickPuff > 0) flames.Emit(tickPuff);
            _kick = 1f;
        }

        void OnExtinguished(BurnStatus source)
        {
            Follow(source);
            Unsubscribe();
            burn = null;
            transform.SetParent(null, true);
            _ended = true;
            _fadeAge = 0f;
            if (flames != null) flames.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (embers != null) embers.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            _kick = Mathf.MoveTowards(_kick, 0f, dt * 4f);
            if (!_ended)
            {
                if (burn != null) Follow(burn);
                if (glow != null)
                {
                    float flicker = 0.75f + 0.5f * Mathf.PerlinNoise(Time.time * 9f, _seed);
                    glow.intensity = lightIntensity * flicker * (1f + tickLightBoost * _kick);
                }
                return;
            }

            _fadeAge += dt;
            if (glow != null) glow.intensity = lightIntensity * Mathf.Clamp01(1f - _fadeAge / (lingerSeconds * 0.5f));
            if (_fadeAge >= lingerSeconds) Destroy(gameObject);
        }

        /// <summary>The burn's root sits at the target's pivot; the fire belongs
        /// at the bottom of its body, rising over it.</summary>
        void Follow(BurnStatus source)
        {
            Vector3 centre = source.BodyCentre;
            Vector3 extents = source.BodyExtents;
            transform.position = centre - Vector3.up * extents.y * 0.6f;
        }
    }
}
