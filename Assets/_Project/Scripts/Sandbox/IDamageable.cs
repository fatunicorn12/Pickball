using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Anything a weapon can hit. Keeps the weapon ignorant of what it struck —
    /// the boss, a training dummy and a breakable crate all look the same to it.
    /// </summary>
    public interface IDamageable
    {
        /// <param name="amount">Damage dealt.</param>
        /// <param name="hitPoint">World point of contact, for VFX placement.</param>
        /// <param name="knockback">World-space push, already scaled by the attack.</param>
        /// <param name="launch">True for a launcher — the hit should pop the target
        /// into the air so following attacks can juggle it.</param>
        /// <param name="kind">What dealt it, so the receiver's own rules can tell
        /// a spell from a blade. Defaults to <see cref="DamageKind.Physical"/>.</param>
        void TakeHit(float amount, Vector3 hitPoint, Vector3 knockback, bool launch,
                     DamageKind kind = DamageKind.Physical);
    }

    /// <summary>What dealt a hit. Only the receiver interprets it — e.g. Newton's
    /// inverse-square range rule skips spells (<c>NewtonBoss.rangeScalesSpells</c>).</summary>
    public enum DamageKind
    {
        /// <summary>Everything not marked otherwise: sword, boss attacks, hazards.</summary>
        Physical = 0,
        /// <summary>Spell damage: orbs, blasts, burn ticks, lightning and its chains.</summary>
        Spell = 1,
    }
}
