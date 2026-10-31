namespace Pickleball.Cutscene.Editor.Pipeline
{
    /// <summary>
    /// Every path the pipeline menus read or write, in one place. The script markdown and the
    /// user's audio folder are inputs; the scene, timelines and build report are generated.
    /// </summary>
    public static class PipelinePaths
    {
        /// <summary>The script, relative to the project root (outside Assets).</summary>
        public const string ScriptMarkdown = "Docs/Story/OpeningCutscenes.md";

        /// <summary>Folder for the imported script asset.</summary>
        public const string ScriptDir = "Assets/_Project/ScriptableObjects/Cutscene/Script";

        /// <summary>The imported script asset.</summary>
        public const string ScriptAsset = ScriptDir + "/OpeningScript.asset";

        /// <summary>Folder for playback assets.</summary>
        public const string PlaybackDir = "Assets/_Project/ScriptableObjects/Cutscene/Playback";

        /// <summary>The playback tunables.</summary>
        public const string PlaybackConfig = PlaybackDir + "/CutscenePlaybackConfig.asset";

        /// <summary>The set prefabs menu 4 places (the swap point for a set).</summary>
        public const string SetList = PlaybackDir + "/CutsceneSetList.asset";

        /// <summary>
        /// Where the user records voice lines by hand, named by line id (<c>OC_S3_07.wav</c>). The
        /// importer binds clips from here into empty slots. The user owns this folder: the pipeline
        /// never creates it or writes to it.
        /// </summary>
        public const string AudioDir = "Assets/_Project/Audio/Cutscene/Opening";

        /// <summary>Generated timelines (not committed; the lead generates them at integration).</summary>
        public const string TimelineDir = "Assets/_Project/Timeline/Cutscene";

        /// <summary>The scene the opening is built from.</summary>
        public const string SourceScene = "Assets/_Project/Scenes/AcademyGreybox.unity";

        /// <summary>Generated scene folder.</summary>
        public const string SceneDir = "Assets/_Project/Scenes/Cutscene";

        /// <summary>The generated playable scene.</summary>
        public const string Scene = SceneDir + "/OpeningPlayable.unity";

        /// <summary>The sets workstream's prefabs.</summary>
        public const string SetPrefabDir = "Assets/_Project/Prefabs/Cutscene/Sets";

        /// <summary>The characters workstream's actor prefabs.</summary>
        public const string ActorPrefabDir = "Assets/_Project/Prefabs/Cutscene/Actors";

        /// <summary>The characters workstream's action libraries.</summary>
        public const string ActionLibraryDir = "Assets/_Project/ScriptableObjects/Cutscene/Actors";

        /// <summary>Build report, under the gitignored Logs folder of the project.</summary>
        public const string BuildReport = "Logs/CutsceneBuildReport.txt";

        /// <summary>Report of the last Rebuild Timelines run (kept apart so it doesn't hide the scene build's).</summary>
        public const string TimelineReport = "Logs/CutsceneTimelineReport.txt";

        /// <summary>The set prefab ids, in placement order: what a missing <see cref="SetList"/> is seeded with.</summary>
        public static string[] SetIds => new[] { "Void", "OuterShrine", "OlivePath", "SouthCrest", "AcademyVista" };

        /// <summary>The timeline asset path for a scene.</summary>
        public static string Timeline(int scene) => TimelineDir + "/OC_S" + scene + ".playable";

        /// <summary>A set prefab path.</summary>
        public static string SetPrefab(string setId) => SetPrefabDir + "/" + setId + ".prefab";

        /// <summary>An actor prefab path (Birdy_Actor, MC_Actor, Propeller).</summary>
        public static string ActorPrefab(string name) => ActorPrefabDir + "/" + name + ".prefab";

        /// <summary>An action library path (MC, Birdy).</summary>
        public static string ActionLibrary(string name) => ActionLibraryDir + "/" + name + "_ActionLibrary.asset";
    }
}
