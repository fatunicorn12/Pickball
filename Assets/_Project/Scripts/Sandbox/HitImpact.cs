using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// One connecting hit, as the receiver sees it. A struct rather than more
    /// arguments on <see cref="IDamageable.TakeHit(float, Vector3, Vector3, bool, DamageKind)"/>,
    /// so a new property of a hit reaches its receivers without another signature change.
    /// </summary>
    public readonly struct HitImpact
    {
        /// <summary>Damage dealt, before the receiver's own filters.</summary>
        public readonly float Damage;

        /// <summary>World point of contact, for VFX placement.</summary>
        public readonly Vector3 Point;

        /// <summary>World-space push in m/s, already scaled by the attack.</summary>
        public readonly Vector3 Knockback;

        /// <summary>What kind of blow this is; see <see cref="HitReaction"/>.</summary>
        public readonly HitReaction Reaction;

        /// <summary>What dealt it, so the receiver's rules can tell a spell from a blade.</summary>
        public readonly DamageKind Kind;

        public HitImpact(float damage, Vector3 point, Vector3 knockback,
                         HitReaction reaction = HitReaction.Flinch, DamageKind kind = DamageKind.Physical)
        {
            Damage = damage;
            Point = point;
            Knockback = knockback;
            Reaction = reaction;
            Kind = kind;
        }
    }
}
