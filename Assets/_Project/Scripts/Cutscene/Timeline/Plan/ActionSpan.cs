namespace Pickleball.Cutscene.Timeline.Plan
{
    /// <summary>One verb laid out: it plays from its cue until the actor's next cue or the scene's end.</summary>
    public readonly struct ActionSpan
    {
        /// <summary>Actor id.</summary>
        public readonly string Actor;

        /// <summary>Verb.</summary>
        public readonly string Verb;

        /// <summary>Shot the cue is in.</summary>
        public readonly string ShotId;

        /// <summary>Start.</summary>
        public readonly double Start;

        /// <summary>End.</summary>
        public readonly double End;

        /// <summary>Creates a span.</summary>
        public ActionSpan(string actor, string verb, string shotId, double start, double end)
        {
            Actor = actor;
            Verb = verb;
            ShotId = shotId;
            Start = start;
            End = end;
        }
    }
}
