using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Combat.Progression
{
    /// <summary>
    /// What a hit is worth in Lemmas. Pure, keyed by target, so the rules are
    /// tests rather than something to check by playing:
    ///
    /// <list type="bullet">
    /// <item>Only damage actually <b>applied</b> pays (health before minus after),
    /// so hitting something already at zero pays nothing, and a weak-point hit
    /// that does double damage earns double.</item>
    /// <item>The defeat bonus is paid once, for the blow that took the target
    /// from above zero to zero. Hitting a corpse someone else downed is worth
    /// nothing.</item>
    /// <item>Each target pays out its worth — max health in damage, plus one
    /// defeat bonus — <b>once per session</b>. Damage beyond that pays nothing,
    /// however the target's health came back.</item>
    /// </list>
    ///
    /// The last rule is what stops farming (QA CB5). The training dummy refills
    /// a moment after any hit, KO or not, so a "re-arm on refill" rule paid
    /// ~400 Lemmas per KO and levels 2–5 fell in about 1.3 KOs; capping damage
    /// alone would still have let a player chip, wait, refill and repeat. The
    /// cost is that a boss rematch pays nothing new.
    /// </summary>
    public sealed class XpLedger
    {
        sealed class Account
        {
            public float PaidHp;
            public bool DefeatPaid;
        }

        readonly Dictionary<object, Account> _accounts = new();
        float _carry;

        /// <summary>
        /// Lemmas earned by one hit on <paramref name="target"/>, whose health
        /// went from <paramref name="healthBefore"/> to <paramref name="healthAfter"/>
        /// out of <paramref name="maxHealth"/>. Fractions carry over between
        /// hits (1.5 per HP over a 13 HP hit pays 19 then 20, not 19 forever).
        /// </summary>
        public int Credit(object target, float healthBefore, float healthAfter, float maxHealth,
                          float xpPerDamage, int xpPerDefeat)
        {
            if (target == null || maxHealth <= 0f) return 0;
            if (!_accounts.TryGetValue(target, out var account))
                _accounts[target] = account = new Account();

            float applied = Mathf.Max(0f, healthBefore - healthAfter);
            float payable = Mathf.Min(applied, Mathf.Max(0f, maxHealth - account.PaidHp));
            account.PaidHp += payable;

            int bonus = 0;
            if (!account.DefeatPaid && healthBefore > 0f && healthAfter <= 0f)
            {
                account.DefeatPaid = true;
                bonus = Mathf.Max(0, xpPerDefeat);
            }

            float earned = payable * Mathf.Max(0f, xpPerDamage) + _carry;
            int whole = Mathf.FloorToInt(earned);
            _carry = earned - whole;
            return whole + bonus;
        }

        /// <summary>HP of damage on <paramref name="target"/> that can still earn
        /// Lemmas before it is spent (its full max health if never hit).</summary>
        public float RemainingHp(object target, float maxHealth)
            => target != null && _accounts.TryGetValue(target, out var a)
                ? Mathf.Max(0f, maxHealth - a.PaidHp)
                : Mathf.Max(0f, maxHealth);
    }
}
