namespace Pickleball.Sandbox.Movement.Logic
{
    /// <summary>
    /// Sunshine's single → double → triple jump chain.
    ///
    /// Jump again within a short window of landing and the next jump in the
    /// chain comes out higher; the third also needs real speed, so it is a
    /// reward for a running rhythm rather than something you get standing
    /// still. Landing from anything that is not a chain jump (a fall, a dive,
    /// a spin) breaks the chain, as does waiting out the window.
    ///
    /// Plain C# so the rules can be tested without a scene. Time is passed in
    /// rather than read, for the same reason.
    /// </summary>
    public sealed class JumpChain
    {
        /// <summary>Step of the last chain jump that landed (0 = none, 1–3).</summary>
        int _landedStep;
        float _landedAt = float.NegativeInfinity;

        /// <summary>Which chain step a ground jump launched now would be (1–3).</summary>
        public int NextStep(float now, float speed, float window, float tripleMinSpeed)
        {
            if (_landedStep <= 0 || now - _landedAt > window) return 1;
            if (_landedStep == 1) return 2;
            if (_landedStep == 2) return speed >= tripleMinSpeed ? 3 : 1;
            return 1;   // a triple restarts the chain
        }

        /// <summary>Records a landing. <paramref name="step"/> is the chain step of
        /// the jump that just ended, or 0 if the airtime was not a chain jump.</summary>
        public void Landed(int step, float now)
        {
            _landedStep = step;
            _landedAt = now;
        }

        /// <summary>Breaks the chain (a dive, a skid, a crouch …).</summary>
        public void Reset()
        {
            _landedStep = 0;
            _landedAt = float.NegativeInfinity;
        }
    }
}
