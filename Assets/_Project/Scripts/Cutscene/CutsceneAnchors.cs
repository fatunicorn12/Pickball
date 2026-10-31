namespace Pickleball.Cutscene
{
    /// <summary>
    /// Name and count lookups for <see cref="CutsceneAnchor"/>. Setup menus use the names to find
    /// or create the anchor children; the names are the swap contract a replacement model follows.
    /// </summary>
    public static class CutsceneAnchors
    {
        /// <summary>Number of anchor slots, the length of <see cref="CutsceneActor"/>'s array.</summary>
        public const int Count = 6;

        /// <summary>The child transform name for an anchor ("Head", "Eyes", "Hand_R", ...).</summary>
        public static string TransformName(CutsceneAnchor anchor)
        {
            switch (anchor)
            {
                case CutsceneAnchor.Head: return "Head";
                case CutsceneAnchor.Eyes: return "Eyes";
                case CutsceneAnchor.HandR: return "Hand_R";
                case CutsceneAnchor.Feet: return "Feet";
                case CutsceneAnchor.PropellerSocket: return "PropellerSocket";
                case CutsceneAnchor.Blade: return "Blade";
                default: return null;
            }
        }
    }
}
