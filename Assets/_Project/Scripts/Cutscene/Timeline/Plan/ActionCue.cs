namespace Pickleball.Cutscene.Timeline.Plan
{
    /// <summary>
    /// "From here, play this verb" for one actor, timed from the start of one of the shot's beats
    /// (so it moves with the line it belongs to when audio retimes the shot). The verb lasts until
    /// the actor's next cue or the end of the scene. Verbs are the shot list §6 names, lower case;
    /// <see cref="CutsceneActionLibrary"/> maps them to clips.
    /// </summary>
    public readonly struct ActionCue
    {
        /// <summary>Actor id.</summary>
        public readonly string Actor;

        /// <summary>Verb, lower case.</summary>
        public readonly string Verb;

        /// <summary>Beat index the time is measured from (the beat count means the shot's end).</summary>
        public readonly int Beat;

        /// <summary>Seconds after that beat starts.</summary>
        public readonly float At;

        /// <summary>Creates a cue.</summary>
        public ActionCue(string actor, string verb, int beat, float at)
        {
            Actor = actor;
            Verb = verb;
            Beat = beat;
            At = at;
        }
    }
}
