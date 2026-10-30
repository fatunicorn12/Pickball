namespace Pickleball.Sandbox.Inventory
{
    /// <summary>
    /// The weapon-switch rule, kept pure so it is tested without a scene: a
    /// switch asked for while the sword is busy (a swing playing, or a hit's
    /// hitstop) waits, and is applied on the first frame it is not. Only the
    /// latest request counts, and asking for the weapon already in hand
    /// cancels a waiting one, so cycling Compass → Ruler → Compass mid-swing
    /// ends where it started with no swap at all.
    ///
    /// Waiting rather than swapping mid-swing matters because the swap moves
    /// the blade markers: the hit sweep would jump between two blades in one
    /// frame, and the half-played step belongs to the old weapon's combo.
    /// </summary>
    public sealed class PendingEquip
    {
        /// <summary>The weapon waiting to go into the hand, or null.</summary>
        public WeaponDefinition Pending { get; private set; }

        /// <summary>True while a switch is waiting.</summary>
        public bool HasPending { get; private set; }

        /// <summary>
        /// Asks for <paramref name="weapon"/> while <paramref name="current"/> is
        /// in hand. Replaces any waiting request. Returns false (and clears the
        /// wait) when <paramref name="weapon"/> is already in hand.
        /// </summary>
        public bool Request(WeaponDefinition weapon, WeaponDefinition current)
        {
            if (weapon == current)
            {
                Clear();
                return false;
            }
            Pending = weapon;
            HasPending = true;
            return true;
        }

        /// <summary>
        /// The switch to apply now: true, with the weapon, when one is waiting
        /// and <paramref name="busy"/> is false; the wait is then cleared. False
        /// while busy or with nothing waiting.
        /// </summary>
        public bool TryTake(bool busy, out WeaponDefinition weapon)
        {
            weapon = null;
            if (!HasPending || busy) return false;
            weapon = Pending;
            Clear();
            return true;
        }

        /// <summary>Forgets any waiting switch.</summary>
        public void Clear()
        {
            Pending = null;
            HasPending = false;
        }
    }
}
