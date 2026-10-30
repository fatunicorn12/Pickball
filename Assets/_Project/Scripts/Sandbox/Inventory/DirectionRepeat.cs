using UnityEngine;

namespace Pickleball.Sandbox.Inventory
{
    /// <summary>
    /// Turns a held direction (keys, d-pad, stick) into discrete menu steps:
    /// one step the moment it is pushed, then, if it is still held after a
    /// delay, one step every interval, the way every console menu scrolls.
    /// The stronger axis wins, so a diagonal stick moves one way, not both.
    /// Pure and driven by explicit time, so the menu feel is tested directly.
    /// </summary>
    public sealed class DirectionRepeat
    {
        Vector2Int _held;
        float _nextStepIn;

        /// <summary>
        /// One frame: <paramref name="direction"/> is what is held now (each
        /// axis -1..1), <paramref name="dt"/> the unscaled seconds since the
        /// last tick. Returns the step to take this frame ((0,0) for none):
        /// x is -1 / +1 for left / right, y is -1 / +1 for down / up.
        /// </summary>
        public Vector2Int Tick(Vector2 direction, float dt, float threshold, float delay, float interval)
        {
            Vector2Int now = Quantise(direction, threshold);
            if (now == Vector2Int.zero)
            {
                _held = Vector2Int.zero;
                return Vector2Int.zero;
            }
            if (now != _held)
            {
                _held = now;
                _nextStepIn = delay;
                return now;
            }
            _nextStepIn -= Mathf.Max(0f, dt);
            if (_nextStepIn > 0f) return Vector2Int.zero;
            _nextStepIn += Mathf.Max(0.01f, interval);
            return now;
        }

        /// <summary>Forgets the held direction, so the next push steps at once.</summary>
        public void Reset() => _held = Vector2Int.zero;

        static Vector2Int Quantise(Vector2 v, float threshold)
        {
            float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y);
            if (ax < threshold && ay < threshold) return Vector2Int.zero;
            return ax >= ay ? new Vector2Int(v.x > 0f ? 1 : -1, 0) : new Vector2Int(0, v.y > 0f ? 1 : -1);
        }
    }
}
