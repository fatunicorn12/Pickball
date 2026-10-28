namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// When the player is free to talk. Pure, so the rules are tested rather
    /// than found by jumping at an NPC.
    ///
    /// Time stops for the whole conversation, so starting one mid-air, mid-dash,
    /// mid-swing or in 4D would hang the player in that state. Two questions,
    /// asked at two moments:
    /// <list type="bullet">
    /// <item><see cref="MayOffer"/>, every frame, for the prompt (and the E claim
    /// that goes with it). A short airborne grace keeps the prompt from
    /// flickering off on a one-frame ground-check miss at a step.</item>
    /// <item><see cref="MayStart"/>, on the press itself. No grace: the first
    /// fraction of a second of a jump or a fall must not start a talk.</item>
    /// </list>
    /// With the rule switched off (<c>requireFree</c> false) both always answer yes.
    /// </summary>
    public static class TalkEligibility
    {
        /// <summary>Whether the prompt may show: free, and grounded within the grace.</summary>
        public static bool MayOffer(bool requireFree, bool grounded, float airborneSeconds, float graceSeconds,
                                    bool busy, bool attacking, bool in4D)
            => !requireFree || (!busy && !attacking && !in4D && (grounded || airborneSeconds <= graceSeconds));

        /// <summary>Whether a press may start the talk now: free, and really grounded.</summary>
        public static bool MayStart(bool requireFree, bool grounded, bool busy, bool attacking, bool in4D)
            => !requireFree || (grounded && !busy && !attacking && !in4D);
    }
}
