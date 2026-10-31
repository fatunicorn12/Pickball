namespace Pickleball.Cutscene.Timeline.Plan
{
    /// <summary>
    /// The float channels the look/VFX tracks animate, and the component type and field each one
    /// drives. The sets workstream owns the components; the builder finds them by type name at
    /// editor time and skips the track with a warning when they are absent, so this assembly never
    /// compiles against them.
    /// </summary>
    public static class LookChannels
    {
        /// <summary>
        /// <c>CutsceneLook</c>'s blend: 0 = the white void, 1 = golden-hour day. S1_SH07 floods the
        /// colour in over 4.0–6.0 s; every later scene holds 1.
        /// </summary>
        public const string LookBlend = "look.blend";

        /// <summary>
        /// <c>ConstructionLineResolve</c>'s progress: 0 = no lines, 1 = the shrine fully drawn. S1_SH07
        /// draws them over 1.0–4.0 s; every later scene holds 1.
        /// </summary>
        public const string LinesProgress = "lines.progress";

        /// <summary>Every channel, in track order.</summary>
        public static readonly string[] All = { LookBlend, LinesProgress };

        /// <summary>Component type name for a channel.</summary>
        public static string ComponentType(string channel) =>
            channel == LookBlend ? "CutsceneLook" : channel == LinesProgress ? "ConstructionLineResolve" : null;

        /// <summary>Serialized float field for a channel (matched case-insensitively, a leading underscore allowed).</summary>
        public static string FieldName(string channel) =>
            channel == LookBlend ? "blend" : channel == LinesProgress ? "progress" : null;
    }
}
