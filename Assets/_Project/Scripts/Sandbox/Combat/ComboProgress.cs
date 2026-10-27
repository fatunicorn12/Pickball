namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// How far into each chain the player is. The two counters run on
    /// different clocks, which is the whole reason this is its own type:
    ///
    /// <list type="bullet">
    /// <item><b>Ground</b> resets whenever the chain is dropped — the swing ended
    /// without a follow-up press — or the player leaves the ground.</item>
    /// <item><b>Air</b> resets only on landing. Dropping an air chain and
    /// pressing again carries on from where it stopped, so one jump buys one air
    /// combo and Air Combo Plus is worth learning.</item>
    /// </list>
    ///
    /// Pure state, no Unity types: MeleeCombat reports what happened, the
    /// resolver reads the counts.
    /// </summary>
    public sealed class ComboProgress
    {
        /// <summary>Steps played in the current ground chain.</summary>
        public int GroundHits { get; private set; }

        /// <summary>Air steps played since the player last stood on something.</summary>
        public int AirHits { get; private set; }

        /// <summary>The count the resolver needs for <paramref name="track"/>.</summary>
        public int HitsIn(ComboTrack track) => track == ComboTrack.Ground ? GroundHits : AirHits;

        /// <summary>A step on <paramref name="track"/> has started. Starting an
        /// air step abandons any ground chain, so landing begins afresh.</summary>
        public void RecordStep(ComboTrack track)
        {
            if (track == ComboTrack.Ground)
            {
                GroundHits++;
            }
            else
            {
                GroundHits = 0;
                AirHits++;
            }
        }

        /// <summary>The swing ended without being chained. Only the ground chain
        /// is dropped; the air chain is spent per airtime, not per press.</summary>
        public void DropChain() => GroundHits = 0;

        /// <summary>The player touched down: the air chain refills, and any
        /// ground chain from before the jump is long gone.</summary>
        public void Land()
        {
            AirHits = 0;
            GroundHits = 0;
        }

        /// <summary>The player left the ground. A ground chain cannot continue in
        /// the air — the next press starts the air chain.</summary>
        public void LeaveGround() => GroundHits = 0;

        /// <summary>Back to a clean slate (disable, respawn).</summary>
        public void Reset()
        {
            GroundHits = 0;
            AirHits = 0;
        }
    }
}
