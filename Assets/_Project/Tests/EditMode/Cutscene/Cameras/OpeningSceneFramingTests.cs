using NUnit.Framework;
using Pickleball.Cutscene.Cameras.Editor;
using Pickleball.Cutscene.Editor.Pipeline;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Cameras
{
    /// <summary>
    /// The whole opening, validated: opens Scenes/Cutscene/OpeningPlayable.unity (when the lead has
    /// committed it), runs Validate Framing and wants zero flags. Ignored when the scene or its
    /// cameras are absent.
    /// </summary>
    public class OpeningSceneFramingTests
    {
        [Test]
        [Category("CutsceneScene")]
        public void OpeningSceneHasNoFramingFlags()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PipelinePaths.Scene) == null)
                Assert.Ignore($"{PipelinePaths.Scene} is not in the project (it is generated; the lead commits it at integration).");
            EditorSceneManager.OpenScene(PipelinePaths.Scene, OpenSceneMode.Single);
            try
            {
                if (GameObject.Find(CameraAssetPaths.RootName) == null)
                    Assert.Ignore("The opening scene has no cutscene cameras (run " + Pickleball.Cutscene.CutsceneMenus.BuildCameras + ").");
                var report = FramingValidator.Run(false);
                Assert.IsNotNull(report);
                Assert.AreEqual(0, report.Flags.Count, string.Join("\n", report.Flags));
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
    }
}
