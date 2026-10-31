namespace Pickleball.Cutscene.Actors
{
    /// <summary>
    /// Chooses a painted mouth shape (0 closed, 1 half, 2 open) from the voice openness
    /// <see cref="MouthFlap"/> already smooths. Two guards stop it flickering on a noisy level:
    /// hysteresis (a shape is entered above one threshold and left only below a lower one) and a
    /// minimum hold (a shape stays on screen at least that long once it appears). Plain C#, so the
    /// rules are tested without audio.
    /// </summary>
    public sealed class MouthShapePicker
    {
        /// <summary>Closed mouth (the model's own texture).</summary>
        public const int Closed = 0;

        /// <summary>Half-open mouth.</summary>
        public const int Half = 1;

        /// <summary>Open mouth.</summary>
        public const int Open = 2;

        // Starts "held forever", so the first syllable after silence opens the mouth at once.
        private float _held = float.PositiveInfinity;

        /// <summary>The shape on screen.</summary>
        public int Shape { get; private set; }

        /// <summary>Back to closed (a new line, a re-enable).</summary>
        public void Reset()
        {
            Shape = Closed;
            _held = float.PositiveInfinity;
        }

        /// <summary>
        /// The shape the thresholds ask for from <paramref name="current"/>, before the hold time.
        /// </summary>
        public static int Target(int current, float openness, float halfOpenAt, float openAt, float closeBelow, float halfBelow)
        {
            switch (current)
            {
                case Open:
                    if (openness < closeBelow) return Closed;
                    return openness < halfBelow ? Half : Open;
                case Half:
                    if (openness >= openAt) return Open;
                    return openness < closeBelow ? Closed : Half;
                default:
                    if (openness >= openAt) return Open;
                    return openness >= halfOpenAt ? Half : Closed;
            }
        }

        /// <summary>Advance one frame and return the shape to show.</summary>
        public int Step(float openness, float dt, MouthSwapConfig config)
        {
            return Step(openness, dt, config.HalfOpenAt, config.OpenAt, config.CloseBelow, config.HalfBelow, config.MinHoldSeconds);
        }

        /// <summary>Advance one frame with explicit thresholds (the test seam).</summary>
        public int Step(float openness, float dt, float halfOpenAt, float openAt, float closeBelow, float halfBelow, float minHold)
        {
            _held += dt;
            int want = Target(Shape, openness, halfOpenAt, openAt, closeBelow, halfBelow);
            if (want != Shape && _held >= minHold)
            {
                Shape = want;
                _held = 0f;
            }
            return Shape;
        }
    }
}
