namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// Turns a held up/down input (stick, d-pad or keys, combined into one
    /// axis by <see cref="DialogueChoiceInput.Axis"/>) into cursor steps, the
    /// way a menu should feel: one step the moment it is pushed, then, if it is
    /// held, a pause and a steady repeat. Pushing the other way steps at once.
    ///
    /// Time is passed in (unscaled; the game is paused), so it is tested at
    /// any frame rate without a scene. At most one repeat per call: a long
    /// hitch moves the cursor one option, not five.
    /// </summary>
    public sealed class ChoiceNavigator
    {
        // Share of the press threshold the axis must fall below to count as let go.
        const float ReleaseShare = 0.6f;

        int _held;      // −1 up, +1 down, 0 neither
        float _timer;   // seconds until the next repeat

        /// <summary>The direction being held: −1 up, +1 down, 0 none.</summary>
        public int Held => _held;

        /// <summary>
        /// One frame: <paramref name="axis"/> is up/down input (+1 up, −1 down),
        /// <paramref name="unscaledDeltaTime"/> the frame's length. Returns the
        /// cursor step: −1 up the list (toward the first option), +1 down, 0 none.
        /// </summary>
        public int Step(float axis, float unscaledDeltaTime, in ChoiceNavigationSettings settings)
        {
            int direction = Direction(axis, settings.Threshold);
            if (direction == 0)
            {
                _held = 0;
                return 0;
            }
            if (direction != _held)
            {
                _held = direction;
                _timer = settings.RepeatDelay;
                return direction;
            }
            if (!(settings.RepeatInterval > 0f)) return 0;

            _timer -= unscaledDeltaTime > 0f ? unscaledDeltaTime : 0f;
            if (_timer > 0f) return 0;
            // Keep the phase so repeats land on the same beat at any frame rate,
            // but never owe more than one step.
            _timer += settings.RepeatInterval;
            if (_timer <= 0f) _timer = settings.RepeatInterval;
            return direction;
        }

        /// <summary>Forgets any held direction, so the next push steps at once.</summary>
        public void Reset()
        {
            _held = 0;
            _timer = 0f;
        }

        /// <summary>
        /// For the frame the options appear: a direction already held then (the
        /// stick still tilted from walking) is taken as held, not as a push, and
        /// never repeats. It moves the cursor only once it is let go and pushed
        /// again, so the highlight always starts on the first option.
        /// </summary>
        public void Prime(float axis, in ChoiceNavigationSettings settings)
        {
            _held = 0;
            _held = Direction(axis, settings.Threshold);
            _timer = float.PositiveInfinity;
        }

        int Direction(float axis, float threshold)
        {
            float press = threshold > 0f ? threshold : 0.5f;
            float release = press * ReleaseShare;
            // Still holding: keep it until the axis falls back past the release point.
            if (_held < 0 && axis >= release) return -1;
            if (_held > 0 && axis <= -release) return 1;
            if (axis >= press) return -1;     // up: toward the first option
            if (axis <= -press) return 1;     // down
            return 0;
        }
    }
}
