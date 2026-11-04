using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// Leaves a trail of twinkling stars behind whatever she throws. The particle
    /// system is a child on the prefab but detaches when the projectile starts, and
    /// follows it from then on, because her projectiles are destroyed outright when
    /// they end: a child's stars would vanish in the same frame. Detached, emission
    /// just stops and the stars finish their lives, then the system removes itself.
    ///
    /// Stars are emitted by distance, so a frozen projectile stops leaving them and
    /// a fast one leaves them further apart, never a clump. Presentation only.
    /// </summary>
    public class StarTrail : MonoBehaviour
    {
        [Tooltip("The star particles, a child on the prefab (world space, rate over distance).")]
        [SerializeField] private ParticleSystem stars;

        // Start, not Awake or OnEnable: Unity refuses to change a parent while the
        // hierarchy above it is being activated, which is what Instantiate is doing then.
        void Start()
        {
            if (stars != null && stars.transform.parent != null)
                stars.transform.SetParent(null, true);
        }

        void LateUpdate()
        {
            if (stars != null) stars.transform.position = transform.position;
        }

        void OnDestroy()
        {
            if (stars == null) return;
            // Still a child means it never started: it goes down with its parent.
            if (stars.transform.parent != null) return;
            var main = stars.main;
            main.stopAction = ParticleSystemStopAction.Destroy;
            stars.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
