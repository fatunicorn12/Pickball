namespace Pickleball.Cutscene.Timeline.Plan
{
    /// <summary>
    /// "From here, move to this mark" for one actor or prop: a cut when <see cref="MoveSeconds"/>
    /// and <see cref="Speed"/> are 0, a move over <see cref="MoveSeconds"/>, or a walk at
    /// <see cref="Speed"/> m/s (scene 4's 1.04 m/s path walk), whose time the builder works out from
    /// the real marker distance. Timed like <see cref="ActionCue"/>.
    /// </summary>
    public readonly struct MarkCue
    {
        /// <summary>Actor id (or <see cref="PlanActors.Propeller"/>).</summary>
        public readonly string Actor;

        /// <summary>The mark to reach.</summary>
        public readonly PlanMark Target;

        /// <summary>Beat index the time is measured from (the beat count means the shot's end).</summary>
        public readonly int Beat;

        /// <summary>Seconds after that beat starts.</summary>
        public readonly float At;

        /// <summary>Travel time in seconds; 0 with no speed is a cut.</summary>
        public readonly float MoveSeconds;

        /// <summary>Walk speed in m/s; when above 0 it sets the travel time instead.</summary>
        public readonly float Speed;

        /// <summary>Easing of the travel.</summary>
        public readonly MarkEase Ease;

        /// <summary>Creates a cue.</summary>
        public MarkCue(string actor, PlanMark target, int beat, float at, float moveSeconds, float speed, MarkEase ease)
        {
            Actor = actor;
            Target = target;
            Beat = beat;
            At = at;
            MoveSeconds = moveSeconds;
            Speed = speed;
            Ease = ease;
        }
    }
}
