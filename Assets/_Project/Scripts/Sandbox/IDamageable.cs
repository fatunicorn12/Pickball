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
        void TakeHit(float amount, Vector3 hitPoint, Vector3 knockback, bool launch);
    }
}
