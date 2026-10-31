using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Cutscene.Editor.Pipeline
{
    /// <summary>
    /// <c>Tools ▸ Pickleball ▸ Cutscene ▸ Setup Opening Cutscenes</c>: runs every workstream's step
    /// in order through <see cref="EditorApplication.ExecuteMenuItem"/> — 1 actors, 2 sets, 3 script,
    /// 4 scene, 4b rig, then a timeline rebuild (so bindings pick up whatever 4b changed on the
    /// actors), 5 subtitle HUD, 6 cameras. A step not in this build is skipped with a warning.
    /// Idempotent because every step is.
    /// </summary>
    public static class SetupAllMenu
    {
        /// <summary>The steps, in order.</summary>
        public static string[] Steps => new[]
        {
            CutsceneMenus.BuildActors,
            CutsceneMenus.BuildSets,
            CutsceneMenus.ImportScript,
            CutsceneMenus.BuildScene,
            CutsceneMenus.RigSceneActors,
            OpeningSceneBuilder.RebuildTimelinesMenu,
            CutsceneMenus.BuildSubtitleHud,
            CutsceneMenus.BuildCameras,
        };

        /// <summary>Menu entry.</summary>
        [MenuItem(CutsceneMenus.SetupAll, false, 0)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Leave Play mode before running the cutscene setup.");
                return;
            }
            var log = new List<string>();
            int skipped = 0;
            foreach (string step in Steps)
                if (!MenuItemLookup.TryRun(step, log)) skipped++;
            string summary = "Setup Opening Cutscenes:\n  " + string.Join("\n  ", log);
            if (skipped > 0) Debug.LogWarning(summary);
            else Debug.Log(summary);
        }
    }
}
