namespace Pickleball.Cutscene.Timeline.Plan
{
    /// <summary>
    /// One shot from the shot list, as data: its id, the framing note and lens the camera
    /// workstream reads, the subject the placeholder camera frames, the ordered duration beats, and
    /// the action, blocking and look cues inside it. Built by <see cref="ShotPlanBuilder"/>; the
    /// opening's shots live in <see cref="OpeningTimingPlan"/>.
    /// </summary>
    public sealed class ShotPlan
    {
        internal ShotPlan(string id, int scene, string framing, float lensStart, float lensEnd, string subject,
            float placeholderDistanceH, Beat[] beats, ActionCue[] actions, MarkCue[] marks, CurveKey[] keys)
        {
            Id = id;
            Scene = scene;
            Framing = framing;
            LensStart = lensStart;
            LensEnd = lensEnd;
            Subject = subject;
            PlaceholderDistanceH = placeholderDistanceH;
            Beats = beats;
            Actions = actions;
            Marks = marks;
            Keys = keys;
        }

        /// <summary>Shot id, e.g. <c>S2_SH05</c>.</summary>
        public string Id { get; }

        /// <summary>Scene number.</summary>
        public int Scene { get; }

        /// <summary>The shot list's framing, condensed: what the frame must show.</summary>
        public string Framing { get; }

        /// <summary>Vertical FOV at the start of the shot, in degrees.</summary>
        public float LensStart { get; }

        /// <summary>Vertical FOV at the end of the shot, in degrees (equal to the start unless it zooms).</summary>
        public float LensEnd { get; }

        /// <summary>The subject anchor (<c>@MC.Head</c>, <c>@BIRDY.Head</c>, <c>@MC.Blade</c>, <c>@PROPELLER</c>, ...).</summary>
        public string Subject { get; }

        /// <summary>Placeholder camera distance from the subject, in units of H (used only before cameras exist).</summary>
        public float PlaceholderDistanceH { get; }

        /// <summary>Duration breakdown in order.</summary>
        public Beat[] Beats { get; }

        /// <summary>Action verbs that start in this shot.</summary>
        public ActionCue[] Actions { get; }

        /// <summary>Blocking moves that start in this shot.</summary>
        public MarkCue[] Marks { get; }

        /// <summary>Look/VFX keys in this shot.</summary>
        public CurveKey[] Keys { get; }
    }
}
