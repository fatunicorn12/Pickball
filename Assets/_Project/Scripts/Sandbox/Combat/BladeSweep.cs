using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// Which capsules the blade covered since the last frame — what MeleeCombat
    /// overlap-tests for hits. Pure: fed blade positions, it returns segments.
    ///
    /// A collider on the blade would tunnel: at swing speed the tip crosses
    /// metres per frame and can be on both sides of a target without ever
    /// overlapping it. So each frame tests the blade where it is now plus the
    /// two capsules its tip and base swept since the previous frame.
    ///
    /// Two rules fix the misses the old in-line version had (QA CB1):
    ///  - The blade is sampled on EVERY frame of a swing, live or not, so the
    ///    first live frame already has a previous position to sweep from.
    ///    Previously the first live frame only recorded the blade and tested
    ///    nothing.
    ///  - A frame that tests always includes the blade where it is now, so even
    ///    with no history a live frame tests something. A long frame that jumps
    ///    the whole active window is tested once, across the whole arc.
    /// </summary>
    public sealed class BladeSweep
    {
        /// <summary>Most segments one <see cref="Sample"/> can produce.</summary>
        public const int MaxSegments = 3;

        Vector3 _prevBase, _prevTip;
        bool _hasPrev;

        /// <summary>True once a blade position has been recorded since the last
        /// <see cref="Reset"/>.</summary>
        public bool HasHistory => _hasPrev;

        /// <summary>Forget the previous blade, so the next sample does not sweep
        /// from a stale pose (call when no swing is playing).</summary>
        public void Reset() => _hasPrev = false;

        /// <summary>
        /// Whether this frame's interval overlapped the active window and so must
        /// be tested: the blade is live now, or the timeline reports it just
        /// went live or just stopped. A frame that jumps the whole window reports
        /// both edges, so it is tested once across its whole arc.
        /// </summary>
        public static bool ShouldTest(bool bladeLive, SwingSignal signal)
            => bladeLive || (signal & (SwingSignal.BladeOn | SwingSignal.BladeOff)) != 0;

        /// <summary>
        /// Record this frame's blade. When <paramref name="test"/> is true, write
        /// the capsules to overlap-test into <paramref name="into"/> (at least
        /// <see cref="MaxSegments"/> long) and return how many; otherwise return 0.
        /// </summary>
        public int Sample(Vector3 bladeBase, Vector3 bladeTip, bool test, (Vector3 from, Vector3 to)[] into)
        {
            int count = 0;
            if (test)
            {
                into[count++] = (bladeBase, bladeTip);          // the blade now
                if (_hasPrev)
                {
                    into[count++] = (_prevTip, bladeTip);       // ground the tip covered
                    into[count++] = (_prevBase, bladeBase);     // and the base
                }
            }
            _prevBase = bladeBase;
            _prevTip = bladeTip;
            _hasPrev = true;
            return count;
        }
    }
}
