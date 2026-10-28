namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// The index arithmetic of the spell selector: which slot a press lands on
    /// and where a cycle goes, skipping empty slots and wrapping at the ends.
    /// Pure, so <see cref="SpellSelector"/>'s rules are tested without input.
    /// </summary>
    public static class SpellSelection
    {
        /// <summary>True when <paramref name="index"/> names a filled slot.</summary>
        public static bool IsUsable(SpellDefinition[] spells, int index)
            => spells != null && index >= 0 && index < spells.Length && spells[index] != null;

        /// <summary>The first filled slot, or -1 when there is none.</summary>
        public static int FirstUsable(SpellDefinition[] spells)
        {
            if (spells == null) return -1;
            for (int i = 0; i < spells.Length; i++)
                if (spells[i] != null) return i;
            return -1;
        }

        /// <summary>
        /// The next filled slot from <paramref name="current"/> in the direction of
        /// <paramref name="direction"/>'s sign, wrapping round. A single filled
        /// slot cycles onto itself. A <paramref name="current"/> that is out of
        /// range or empty starts from just before the first slot (forward) or
        /// just after the last (backward). Direction 0 keeps a usable
        /// <paramref name="current"/>, else falls back to <see cref="FirstUsable"/>.
        /// Returns -1 only when no slot is filled.
        /// </summary>
        public static int Cycle(SpellDefinition[] spells, int current, int direction)
        {
            if (spells == null || spells.Length == 0) return -1;
            if (direction == 0) return IsUsable(spells, current) ? current : FirstUsable(spells);

            int step = direction > 0 ? 1 : -1;
            int n = spells.Length;
            int start = current >= 0 && current < n ? current : (step > 0 ? -1 : n);
            for (int k = 1; k <= n; k++)
            {
                int i = ((start + step * k) % n + n) % n;
                if (spells[i] != null) return i;
            }
            return -1;
        }
    }
}
