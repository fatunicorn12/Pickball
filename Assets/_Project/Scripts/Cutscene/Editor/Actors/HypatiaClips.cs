using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using static Pickleball.Cutscene.Editor.Actors.PoseKit;
using M = Pickleball.Cutscene.Editor.Actors.HumanMuscles;

namespace Pickleball.Cutscene.Editor.Actors
{
    /// <summary>
    /// Hypatia's two baked boss-fight clips (Docs/Systems/HypatiaBoss.md). It lives here because
    /// <see cref="PoseClipBaker"/> does, and that assembly can reach the fight's editor code but
    /// not the other way round. The results are assets the fight's setup only references, so
    /// re-bake by hand after her model changes shape.
    /// </summary>
    public static class HypatiaClips
    {
        const string ClipDir = "Assets/_Project/Animation/Hypatia/Clips";
        const string DeathPath = ClipDir + "/Hypatia_Death.anim";
        const float DeathFps = 30f;

        /// <summary>Humanoid copy of Mixamo's "sword and shield death (2)": the repo's FBX is a Generic import.</summary>
        public const string DeathSource = ClipDir + "/Source/Hypatia_DeathSource.anim";

        /// <summary>Bake both clips onto her current model.</summary>
        [MenuItem("Tools/Pickleball/Bake Hypatia Clips")]
        public static void BakeAll()
        {
            PoseClipBaker.BakeAndSave(Burning());
            BakeDeath();
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Alexandria's Burning, keyed by hand because no clip in the repo drives both hands at the
        /// floor. Arms rise and open, head up, then drive down and out exactly at the release (2 s,
        /// the band's warning lead), with the head level throughout so she stays looking at the
        /// player. It plays on the upper-body layer, so it needs no grounding or body compensation.
        /// </summary>
        public static PoseClipSpec Burning()
        {
            var spec = new PoseClipSpec
            {
                Name = "Hypatia_Burning",
                ModelPath = HypatiaFightModel.ModelPath,
                Folder = ClipDir,
                BaseClip = HypatiaFightModel.IdleClipPath,
                BaseTime = 1f,
                Duration = 2.7f,
                Ground = 0f,
                Note = "Both arms rise and open, then drive down and out toward the floor at 2 s.",
            };
            spec.K(
                Pose(0f, armUp: -0.42f, armFront: 0.05f, forearm: 0.85f, torso: 0f, nod: 0f, spread: 0.1f),
                Pose(0.8f, armUp: 0.45f, armFront: -0.25f, forearm: 0.6f, torso: -0.05f, nod: 0.08f, spread: 0.2f),
                Pose(1.45f, armUp: 0.9f, armFront: -0.12f, forearm: 0.85f, torso: -0.11f, nod: 0.15f, spread: 0.3f),
                Pose(1.68f, armUp: 0.95f, armFront: -0.1f, forearm: 0.9f, torso: -0.12f, nod: 0.15f, spread: 0.4f),
                Pose(2f, armUp: -0.22f, armFront: -0.12f, forearm: 1f, torso: 0.16f, nod: 0.24f, spread: 0.6f),
                Pose(2.7f, armUp: -0.18f, armFront: -0.1f, forearm: 0.95f, torso: 0.13f, nod: 0.2f, spread: 0.5f));
            return spec;
        }

        /// <summary>
        /// She crumples to her knees and falls forward, so she lands with her head toward the
        /// player rather than her feet. Mocap kneels through a wide skirt, so the body is lifted
        /// wherever a vertex of hers would go below the plate. Sampled through a graph, the way it
        /// plays in the fight. Overwrites the asset in place (GUID kept).
        /// </summary>
        public static void BakeDeath()
        {
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(DeathSource);
            var copy = Object.Instantiate(source);
            copy.name = "Hypatia_Death";
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = false;
            settings.loopBlendOrientation = settings.loopBlendPositionY = settings.loopBlendPositionXZ = true;
            settings.keepOriginalOrientation = settings.keepOriginalPositionY = settings.keepOriginalPositionXZ = true;
            settings.heightFromFeet = false;
            AnimationUtility.SetAnimationClipSettings(copy, settings);

            // Twice, because lifting a frame changes nothing but its height, while blending can
            // leave a small residue at the fastest moments of the fall.
            for (int pass = 0; pass < 2; pass++) LiftAbovePlate(copy);

            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(DeathPath);
            if (existing == null)
            {
                existing = new AnimationClip { name = "Hypatia_Death" };
                AssetDatabase.CreateAsset(existing, DeathPath);
            }
            EditorUtility.CopySerialized(copy, existing);
            existing.name = "Hypatia_Death";
            Object.DestroyImmediate(copy);
            AssetDatabase.SaveAssetIfDirty(existing);
        }

        static void LiftAbovePlate(AnimationClip clip)
        {
            var go = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HypatiaFightModel.ModelPath));
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = go.GetComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            var baked = new Mesh();
            var verts = new List<Vector3>();
            var graph = PlayableGraph.Create("HypatiaDeathLift");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "o", animator);
                var play = AnimationClipPlayable.Create(graph, clip);
                play.SetApplyFootIK(false);
                play.Pause();
                output.SetSourcePlayable(play);
                animator.Rebind();

                int frames = Mathf.CeilToInt(clip.length * DeathFps) + 1;
                var below = new float[frames];
                for (int f = 0; f < frames; f++)
                {
                    play.SetTime(Mathf.Min(f / DeathFps, clip.length));
                    graph.Evaluate(0f);
                    smr.BakeMesh(baked, true);
                    baked.GetVertices(verts);
                    float lowest = verts.Min(v => (smr.transform.position + smr.transform.rotation * v).y);
                    below[f] = Mathf.Max(0f, -lowest);
                }

                // Hold each lift for a few frames around it, then soften it, so she is carried over
                // the contact rather than popped up at it.
                const int hold = 4, soften = 4;
                var held = new float[frames];
                for (int f = 0; f < frames; f++)
                    for (int o = -hold; o <= hold; o++)
                        held[f] = Mathf.Max(held[f], below[Mathf.Clamp(f + o, 0, frames - 1)]);
                var lift = new float[frames];
                for (int f = 0; f < frames; f++)
                {
                    float sum = 0f;
                    for (int o = -soften; o <= soften; o++) sum += held[Mathf.Clamp(f + o, 0, frames - 1)];
                    lift[f] = sum / (2 * soften + 1);
                }

                EditorCurveBinding binding = AnimationUtility.GetCurveBindings(clip).First(b => b.propertyName == "RootT.y");
                AnimationCurve original = AnimationUtility.GetEditorCurve(clip, binding);
                float scale = animator.humanScale;
                var keys = new Keyframe[frames];
                for (int f = 0; f < frames; f++)
                {
                    float t = Mathf.Min(f / DeathFps, clip.length);
                    keys[f] = new Keyframe(t, original.Evaluate(t) + lift[f] / scale);
                }
                var curve = new AnimationCurve(keys);
                for (int f = 0; f < frames; f++) curve.SmoothTangents(f, 0f);
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(baked);
                Object.DestroyImmediate(go);
            }
        }

        static PoseKey Pose(float t, float armUp, float armFront, float forearm, float torso, float nod, float spread)
        {
            return K(t)
                .S(M.Arm(true, armUp, armFront, forearm)).S(M.Arm(false, armUp, armFront, forearm))
                .S(M.OpenHand(true, spread)).S(M.OpenHand(false, spread))
                .S(M.Torso(torso)).Look(nod, 0f);
        }
    }
}
