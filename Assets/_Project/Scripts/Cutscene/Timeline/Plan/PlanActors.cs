namespace Pickleball.Cutscene.Timeline.Plan
{
    /// <summary>
    /// Actor ids and frame references the timing plan uses. Speaker ids match
    /// <see cref="CutsceneSpeakers"/>; the propeller is a prop, not a speaker. A frame written
    /// <c>@MC</c> is that actor's root, <c>@MC.HandR</c> one of its anchors (the
    /// <see cref="CutsceneAnchor"/> enum name); anything else is a marker id from
    /// <see cref="CutsceneMarkerIds"/>.
    /// </summary>
    public static class PlanActors
    {
        /// <summary>The player character.</summary>
        public const string MC = CutsceneSpeakers.MC;

        /// <summary>Birdy.</summary>
        public const string Birdy = CutsceneSpeakers.Birdy;

        /// <summary>The propeller prop.</summary>
        public const string Propeller = "PROPELLER";

        /// <summary>Prefix that marks a frame or subject as an actor reference rather than a marker id.</summary>
        public const char ActorRefPrefix = '@';

        /// <summary>MC's root.</summary>
        public const string AtMC = "@MC";

        /// <summary>Birdy's root.</summary>
        public const string AtBirdy = "@BIRDY";

        /// <summary>The propeller's root.</summary>
        public const string AtPropeller = "@PROPELLER";

        /// <summary>MC's right palm.</summary>
        public const string AtMCHandR = "@MC.HandR";

        /// <summary>MC's head.</summary>
        public const string AtMCHead = "@MC.Head";

        /// <summary>MC's compass-blade guard.</summary>
        public const string AtMCBlade = "@MC.Blade";

        /// <summary>Birdy's head.</summary>
        public const string AtBirdyHead = "@BIRDY.Head";

        /// <summary>Birdy's right wing tip.</summary>
        public const string AtBirdyHandR = "@BIRDY.HandR";

        /// <summary>Top of Birdy's helmet.</summary>
        public const string AtBirdySocket = "@BIRDY.PropellerSocket";

        /// <summary>
        /// Splits <c>@ACTOR.Anchor</c> into its parts. False for a plain marker id. The anchor is
        /// null for a bare actor root.
        /// </summary>
        public static bool TryParseActorRef(string frame, out string actor, out string anchor)
        {
            actor = null;
            anchor = null;
            if (string.IsNullOrEmpty(frame) || frame[0] != ActorRefPrefix) return false;
            int dot = frame.IndexOf('.');
            if (dot < 0)
            {
                actor = frame.Substring(1);
                return true;
            }
            actor = frame.Substring(1, dot - 1);
            anchor = frame.Substring(dot + 1);
            return true;
        }
    }
}
