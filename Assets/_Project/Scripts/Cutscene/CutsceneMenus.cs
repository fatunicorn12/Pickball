namespace Pickleball.Cutscene
{
    /// <summary>
    /// The setup-menu paths for the cutscene build, in run order. Each workstream owns one step and
    /// its menu must be idempotent. The "all" menu runs the steps in order through
    /// <c>EditorApplication.ExecuteMenuItem</c>, so no builder compiles against another's code.
    /// Steps 5 and 6 work on the scene step 4 leaves open, finding what they need by component.
    /// </summary>
    public static class CutsceneMenus
    {
        /// <summary>Menu root.</summary>
        public const string Root = "Tools/Pickleball/Cutscene/";

        /// <summary>Step 1 (characters): actor prefabs, action libraries, controllers.</summary>
        public const string BuildActors = Root + "1 Build Actors";

        /// <summary>Step 2 (sets): set prefabs with their markers.</summary>
        public const string BuildSets = Root + "2 Build Sets";

        /// <summary>Step 3 (pipeline): import Docs/Story/OpeningCutscenes.md into the script asset.</summary>
        public const string ImportScript = Root + "3 Import Script";

        /// <summary>Step 4 (pipeline): build Scenes/Cutscene/OpeningPlayable.unity, place sets and actors, build the timelines.</summary>
        public const string BuildScene = Root + "4 Build Opening Scene";

        /// <summary>
        /// Step 4b (characters): in the open scene, give the gameplay Player and every placed actor
        /// prefab their CutsceneActor slot: anchors from humanoid bones, measured size, voice
        /// source, action library, mouth flap. Model-agnostic, so it is also the swap path.
        /// </summary>
        public const string RigSceneActors = Root + "4b Rig Scene Actors";

        /// <summary>Step 5 (ui): build and wire the subtitle HUD and skip indicator in the open scene.</summary>
        public const string BuildSubtitleHud = Root + "5 Build Subtitle HUD";

        /// <summary>Step 6 (cameras): build the virtual cameras and camera tracks in the open scene.</summary>
        public const string BuildCameras = Root + "6 Build Cameras";

        /// <summary>Runs steps 1, 2, 3, 4, 4b, 5, 6 in order (owned by the pipeline).</summary>
        public const string SetupAll = Root + "Setup Opening Cutscenes";
    }
}
