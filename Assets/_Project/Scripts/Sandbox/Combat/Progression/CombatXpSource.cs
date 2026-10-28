using UnityEngine;

namespace Pickleball.Sandbox.Combat.Progression
{
    /// <summary>
    /// Pays Lemmas for sword work: a rate per HP of damage actually dealt, and
    /// a bonus for the blow that defeats something. The rules — and why a
    /// target only ever pays its worth once — are in <see cref="XpLedger"/>.
    ///
    /// Listens to MeleeCombat's <c>HitConnecting</c> (to read the target's
    /// health just before the hit) and <c>HitLanded</c> (just after); the sword
    /// has no idea XP exists. Only <see cref="Health"/> targets pay, because
    /// that is what "defeated" means in this project — the sword itself only
    /// ever sees an <see cref="IDamageable"/>, which is as it should be.
    /// </summary>
    public class CombatXpSource : MonoBehaviour
    {
        [SerializeField] private MeleeCombat combat;
        [SerializeField] private PlayerLevel level;

        // Rebuilt in OnEnable rather than initialised readonly (script-reload
        // safety). A reload in Play mode therefore forgets who has paid out.
        XpLedger _ledger;

        // The one hit in flight: HitConnecting and HitLanded are raised as a
        // synchronous pair around the target's TakeHit.
        Health _pending;
        float _pendingBefore;

        void Awake()
        {
            if (combat == null) combat = GetComponent<MeleeCombat>();
            if (level == null) level = GetComponent<PlayerLevel>();
        }

        void OnEnable()
        {
            _ledger ??= new XpLedger();
            if (combat == null) return;
            combat.HitConnecting += OnHitConnecting;
            combat.HitLanded += OnHitLanded;
        }

        void OnDisable()
        {
            if (combat == null) return;
            combat.HitConnecting -= OnHitConnecting;
            combat.HitLanded -= OnHitLanded;
        }

        void OnHitConnecting(MeleeHitReport hit)
        {
            _pending = HealthOf(hit);
            _pendingBefore = _pending != null ? _pending.Current : 0f;
        }

        void OnHitLanded(MeleeHitReport hit)
        {
            var health = HealthOf(hit);
            var before = _pendingBefore;
            bool paired = health != null && health == _pending;
            _pending = null;
            if (!paired || level == null) return;

            var config = level.Config;
            float perDamage = config != null ? config.XpPerDamage : 1f;
            int perDefeat = config != null ? config.XpPerDefeat : 0;
            level.AddXp(_ledger.Credit(health, before, health.Current, health.Max, perDamage, perDefeat));
        }

        static Health HealthOf(MeleeHitReport hit)
            => hit.Collider != null ? hit.Collider.GetComponentInParent<Health>() : null;
    }
}
