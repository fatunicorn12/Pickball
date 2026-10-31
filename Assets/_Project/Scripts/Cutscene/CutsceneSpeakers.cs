namespace Pickleball.Cutscene
{
    /// <summary>
    /// Speaker ids as the script writes them before the colon (<c>MC: [tag] line</c>). Actor ids,
    /// subtitle speaker colours and audio routing all key on these strings.
    /// </summary>
    public static class CutsceneSpeakers
    {
        /// <summary>The player character.</summary>
        public const string MC = "MC";

        /// <summary>Birdy, the propeller bird.</summary>
        public const string Birdy = "BIRDY";
    }
}
