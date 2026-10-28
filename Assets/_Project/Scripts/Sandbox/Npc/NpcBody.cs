using UnityEngine;

namespace Pickleball.Sandbox.Npc
{
    /// <summary>
    /// An NPC's physical presence: the solid collider the player bumps into
    /// and the sword hits, for as long as the NPC is alive.
    ///
    /// The collider has to be solid (not a trigger) because MeleeCombat's
    /// blade sweep and lock-on's line-of-sight test both ignore triggers. That
    /// is fine while the NPC stands in it, but a death clip lays the body down
    /// a metre away, which would leave an invisible upright pillar where it
    /// stood. So on death the collider is switched off. Lock-on already drops
    /// dead targets, and a dead Health takes no more damage, so nothing needs it.
    ///
    /// Gameplay, not presentation: it changes collision, so it is kept out of
    /// <see cref="NpcAnimatorDriver"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class NpcBody : MonoBehaviour
    {
        [Tooltip("Solid collider that blocks the player and takes sword hits while alive. " +
                 "Disabled when the NPC dies.")]
        [SerializeField] private Collider blocker;

        [Tooltip("Whose death switches the collider off. Empty: the Health on this object.")]
        [SerializeField] private Health health;

        /// <summary>The collider this body switches off on death.</summary>
        public Collider Blocker => blocker;

        void OnEnable()
        {
            if (health == null) health = GetComponent<Health>();
            if (blocker == null) blocker = GetComponent<Collider>();
            // Subscribe only. Checking IsDead here would read a Health whose
            // Awake may not have run yet (and so reads as dead); the collider's
            // enabled flag is serialized, so a script reload keeps it anyway.
            if (health != null) health.Died += OnDied;
        }

        void OnDisable()
        {
            if (health != null) health.Died -= OnDied;
        }

        void OnDied(Health _)
        {
            if (blocker != null) blocker.enabled = false;
        }
    }
}
