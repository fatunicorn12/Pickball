using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// A collectable that hovers, spins and glows, and grants one ability on
    /// touch. Generic on purpose: the feather grants DoubleJump, the skateboard
    /// grants Skateboard, and anything else you model later only needs a
    /// different <see cref="ability"/> and mesh.
    ///
    /// Motion is driven from the object's *starting* position, so the pickup
    /// can be dropped anywhere in the scene and will bob around wherever it was
    /// placed rather than snapping to a fixed height.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class AbilityPickup : MonoBehaviour
    {
        [Header("Grants")]
        [SerializeField] private AbilityKind ability = AbilityKind.DoubleJump;
        [Tooltip("Destroy the pickup once collected. Untick to leave it in the scene " +
                 "after collection, so it can be picked up again (useful for testing).")]
        [SerializeField] private bool consumeOnPickup = true;

        [Header("Idle motion")]
        [SerializeField] private float spinDegreesPerSecond = 75f;
        [SerializeField] private float hoverAmplitude = 0.18f;
        [SerializeField] private float hoverCycleSeconds = 2.2f;

        [Header("Glow")]
        [SerializeField] private Light glowLight;
        [SerializeField] private float glowMin = 1.4f;
        [SerializeField] private float glowMax = 3.2f;
        [SerializeField] private float glowCycleSeconds = 1.3f;

        [Header("Visuals")]
        [Tooltip("Spun and bobbed. Left separate from the root so the trigger " +
                 "collider stays still while the mesh moves.")]
        [SerializeField] private Transform visual;
        [SerializeField] private ParticleSystem collectEffect;

        Vector3 _visualStart;
        float _phase;
        bool _taken;

        void Awake()
        {
            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
            if (visual != null) _visualStart = visual.localPosition;
            GetComponent<SphereCollider>().isTrigger = true;
            // Random phase so several pickups in a scene do not pulse in lockstep.
            _phase = Random.value * 10f;
        }

        void Update()
        {
            if (_taken || visual == null) return;
            float t = Time.time + _phase;

            visual.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.Self);
            visual.localPosition = _visualStart + Vector3.up *
                (Mathf.Sin(t * Mathf.PI * 2f / Mathf.Max(hoverCycleSeconds, 0.01f)) * hoverAmplitude);

            if (glowLight != null)
            {
                float k = (Mathf.Sin(t * Mathf.PI * 2f / Mathf.Max(glowCycleSeconds, 0.01f)) + 1f) * 0.5f;
                glowLight.intensity = Mathf.Lerp(glowMin, glowMax, k);
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (_taken) return;
            var abilities = other.GetComponentInParent<PlayerAbilities>();
            if (abilities == null) return;
            if (!abilities.Grant(ability)) return;   // already owned

            _taken = true;
            if (collectEffect != null)
            {
                collectEffect.transform.SetParent(null, true);
                collectEffect.Play();
                Destroy(collectEffect.gameObject, 3f);
            }

            if (consumeOnPickup) Destroy(gameObject);
            else _taken = false;
        }
    }
}
