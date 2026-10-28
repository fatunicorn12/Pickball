using System;

namespace Pickleball.Sandbox.Npc
{
    /// <summary>
    /// Chooses which idle an NPC plays next. Variant 0 is the main idle; any
    /// others are fidgets. After the main idle loops it either loops again
    /// (with chance <c>mainChance</c>) or plays a random fidget, and a fidget
    /// always returns to the main idle. That reads as a character standing
    /// around with the odd stretch or look-about, rather than a shuffled
    /// playlist of fidgets.
    ///
    /// A plain class with an injected <see cref="Random"/> so tests can seed it.
    /// </summary>
    public sealed class IdleVariantPicker
    {
        readonly Random _random;

        /// <param name="random">Source of randomness; seed it for repeatable results.</param>
        public IdleVariantPicker(Random random) => _random = random ?? new Random();

        /// <summary>
        /// The variant to play after <paramref name="current"/> finishes a loop.
        /// </summary>
        /// <param name="current">The variant playing now (0 = main idle).</param>
        /// <param name="count">How many idle variants exist. 1 or fewer always gives 0.</param>
        /// <param name="mainChance">0–1: chance the main idle loops again instead of fidgeting.</param>
        public int Next(int current, int count, float mainChance)
        {
            if (count <= 1) return 0;
            if (current != 0) return 0;
            if (_random.NextDouble() < mainChance) return 0;
            return 1 + _random.Next(count - 1);
        }
    }
}
