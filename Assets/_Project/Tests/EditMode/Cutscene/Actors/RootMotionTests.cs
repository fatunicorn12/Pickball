using NUnit.Framework;
using Pickleball.Cutscene;
using Pickleball.Cutscene.Editor.Actors;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Actors
{
    /// <summary>
    /// No clip a library plays may move the actor's root: the timeline's marks track owns the
    /// root, and a clip with root motion stacks its travel on top (the integration bug where MC's
    /// walk ran twice as far as his marks in OC_S4). Sampled on the character's own model:
    /// AnimationClip.SampleAnimation writes any unbaked root motion into the root transform, so a
    /// root that stays at the origin at t = 0 and t = end means the clip is in place.
    /// </summary>
    [Category("Assets")]
    public class RootMotionTests
    {
        [Test]
        public void McLibrary_NoClipMovesTheRoot()
        {
            Check(CutsceneActorPaths.McLibrary, CutsceneActorPaths.ChibiModel);
        }

        [Test]
        public void BirdyLibrary_NoClipMovesTheRoot()
        {
            Check(CutsceneActorPaths.BirdyLibrary, CutsceneActorPaths.BirdyModel);
        }

        private static void Check(string libraryPath, string modelPath)
        {
            var lib = AssetDatabase.LoadAssetAtPath<CutsceneActionLibrary>(libraryPath);
            Assert.NotNull(lib, $"{libraryPath} missing: run 1 Build Actors");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var go = (GameObject)Object.Instantiate(model);
            go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                for (int i = 0; i < lib.Count; i++)
                {
                    AnimationClip clip = lib.Get(i).clip;
                    string what = $"'{lib.Get(i).verb}' ({clip.name})";

                    // Every clip the libraries play is a cutscene-owned asset, never a gameplay source.
                    StringAssert.StartsWith(CutsceneActorPaths.AnimDir + "/", AssetDatabase.GetAssetPath(clip),
                        $"{what} is not a cutscene-owned copy");

                    var s = AnimationUtility.GetAnimationClipSettings(clip);
                    Assert.IsTrue(s.loopBlendPositionXZ && s.loopBlendOrientation && s.loopBlendPositionY,
                        $"{what}: root XZ, rotation and Y must be baked into the pose");
                    Assert.Less(clip.averageSpeed.magnitude, 1e-3f, $"{what}: average root speed {clip.averageSpeed}");

                    foreach (float t in new[] { 0f, clip.length })
                    {
                        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                        clip.SampleAnimation(go, t);
                        Assert.Less(go.transform.position.magnitude, 0.001f, $"{what}: root moved to {go.transform.position} at t = {t}");
                        Assert.Less(Quaternion.Angle(Quaternion.identity, go.transform.rotation), 0.1f,
                            $"{what}: root turned at t = {t}");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
