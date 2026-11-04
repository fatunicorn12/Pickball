using System;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// Deals the indices 0..count-1 in random order, every one before any repeats, and
    /// never the same index twice in a row across a reshuffle. Pure random picks repeat
    /// a sound often enough for a player mashing a combo to notice; this cannot.
    ///
    /// Allocates only in the constructor, so a swing can draw from it freely.
    /// </summary>
    public sealed class ShuffleBag
    {
        readonly int[] _order;
        readonly Random _random;
        int _next;

        /// <summary>Number of indices in the bag.</summary>
        public int Count => _order.Length;

        /// <param name="count">How many indices to deal; 0 makes an empty bag.</param>
        /// <param name="random">The source of randomness; tests pass a seeded one.</param>
        public ShuffleBag(int count, Random random)
        {
            _order = new int[Math.Max(count, 0)];
            for (int i = 0; i < _order.Length; i++) _order[i] = i;
            _random = random;
            _next = _order.Length;    // so the first draw shuffles
        }

        /// <summary>The next index, or -1 from an empty bag.</summary>
        public int Next()
        {
            if (_order.Length == 0) return -1;
            if (_next >= _order.Length) Reshuffle();
            return _order[_next++];
        }

        void Reshuffle()
        {
            int last = _order[^1];    // what the previous round dealt last
            for (int i = _order.Length - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (_order[i], _order[j]) = (_order[j], _order[i]);
            }
            // Moved out of first place rather than reshuffled again, so this always ends.
            if (_order.Length > 1 && _order[0] == last)
            {
                int j = 1 + _random.Next(_order.Length - 1);
                (_order[0], _order[j]) = (_order[j], _order[0]);
            }
            _next = 0;
        }
    }
}
