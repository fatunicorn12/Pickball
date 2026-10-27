using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Combat.Progression
{
    /// <summary>
    /// Pays Lemmas for sword work: a rate per HP of damage, and a bonus for
    /// the blow that defeats something.
    ///
    /// Listens to <c>MeleeCombat.HitLanded</c>; the sword has no idea XP
    /// exists. Defeats are recognised through <see cref="Health"/> because
    /// that is what "defeated" means in this project — the sword only ever sees
    /// an <see cref="IDamageable"/>, which is as it should be.
    /// </summary>
    public class CombatXpSource : MonoBehaviour
    {
        [SerializeField] private MeleeCombat combat;
        [SerializeField] private PlayerLevel level;

        // Fractional Lemmas carried between hits, so 1.5 per HP over a 13-HP
        // hit pays 19 then 20, not 19 every time.
        float _carry;

        // Health components already paid a defeat bonus this life. Rebuilt in
        // OnEnable rather than initialised readonly (script-reload safety).
        HashSet<Health> _credited;

        void Awake()
        {
            if (combat == null) combat = GetComponent<MeleeCombat>();
            if (level == null) level = GetComponent<PlayerLevel>();
        }

        void OnEnable()
        {
            _credited ??= new HashSet<Health>();
            if (combat != null) combat.HitLanded += OnHitLanded;
        }

        void OnDisable()
        {
            if (combat != null) combat.HitLanded -= OnHitLanded;
        }

        void OnHitLanded(MeleeHitReport hit)
        {
            if (level == null) return;
            var config = level.Config;
            float perDamage = config != null ? config.XpPerDamage : 1f;
            int perDefeat = config != null ? config.XpPerDefeat : 0;

            var health = hit.Collider != null ? hit.Collider.GetComponentInParent<Health>() : null;
            int bonus = 0;
            if (health != null)
            {
                if (!health.IsDead)
                {
                    _credited.Remove(health);          // alive again: a rematch pays again
                }
                else if (_credited.Add(health))
                {
                    bonus = perDefeat;                 // this was the killing blow
                }
                else
                {
                    return;                            // flogging a defeated foe pays nothing
                }
            }

            float earned = hit.Damage * perDamage + _carry;
            int whole = Mathf.FloorToInt(earned);
            _carry = earned - whole;
            level.AddXp(whole + bonus);
        }
    }
}
