using System.Collections.Generic;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// Where the option cursor goes. It only ever rests on an enabled option:
    /// stepping skips greyed-out ones and wraps round the ends, so with two or
    /// three options any of them is one or two presses away in either
    /// direction. Pure, for tests.
    /// </summary>
    public static class ChoiceSelection
    {
        /// <summary>The first enabled option, or -1 when none is.</summary>
        public static int FirstEnabled(IReadOnlyList<PresentedOption> options)
        {
            if (options == null) return -1;
            for (int i = 0; i < options.Count; i++)
                if (options[i].IsEnabled) return i;
            return -1;
        }

        /// <summary>
        /// The option <paramref name="step"/> enabled options away from
        /// <paramref name="current"/>: negative moves up the list (toward the
        /// first option), positive down, wrapping. Returns
        /// <paramref name="current"/> unchanged for a zero step or when no other
        /// option is enabled; a <paramref name="current"/> that isn't valid
        /// counts as just before the first option (or after the last, moving up).
        /// </summary>
        public static int Step(IReadOnlyList<PresentedOption> options, int current, int step)
        {
            int count = options != null ? options.Count : 0;
            if (count == 0 || step == 0) return current;
            int direction = step > 0 ? 1 : -1;
            int moves = step > 0 ? step : -step;
            int at = current >= 0 && current < count ? current : direction > 0 ? -1 : count;
            for (int m = 0; m < moves; m++)
            {
                int found = -1;
                for (int k = 1; k <= count; k++)
                {
                    int i = ((at + direction * k) % count + count) % count;
                    if (!options[i].IsEnabled) continue;
                    found = i;
                    break;
                }
                if (found < 0) return current;
                at = found;
            }
            return at;
        }
    }
}
