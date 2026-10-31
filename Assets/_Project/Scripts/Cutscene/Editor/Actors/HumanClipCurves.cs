using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Cutscene.Editor.Actors
{
    /// <summary>
    /// A humanoid clip's own curves (RootT, RootQ and the 95 muscles), read directly. This is
    /// the human pose in clip terms, so it can be handed straight to
    /// <see cref="HumanPoseHandler.SetHumanPose"/>.
    /// </summary>
    /// <remarks>
    /// Used instead of <c>AnimationClip.SampleAnimation</c> by the clip baker and the pose sheets:
    /// sampling a clip whose root motion isn't baked moves the instance's root transform, after
    /// which HumanPoseHandler reads and writes the body in different frames (the first bake came
    /// out lying in the floor). Reading the curves has no such side effect.
    /// </remarks>
    public sealed class HumanClipCurves
    {
        private readonly AnimationCurve[] _muscles = new AnimationCurve[HumanTrait.MuscleCount];
        private readonly AnimationCurve[] _t = new AnimationCurve[3];
        private readonly AnimationCurve[] _q = new AnimationCurve[4];

        /// <summary>Clip length, seconds (at least 1 ms).</summary>
        public float Length { get; }

        /// <summary>Read a clip's humanoid curves.</summary>
        public HumanClipCurves(AnimationClip clip)
        {
            Length = Mathf.Max(clip.length, 1e-3f);
            var byName = new Dictionary<string, AnimationCurve>();
            foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
                if (b.type == typeof(Animator)) byName[b.propertyName] = AnimationUtility.GetEditorCurve(clip, b);
            for (int i = 0; i < _muscles.Length; i++) byName.TryGetValue(HumanMuscles.ClipAttribute(i), out _muscles[i]);
            for (int c = 0; c < 3; c++) byName.TryGetValue("RootT." + "xyz"[c], out _t[c]);
            for (int c = 0; c < 4; c++) byName.TryGetValue("RootQ." + "xyzw"[c], out _q[c]);
        }

        /// <summary>
        /// The pose at a time into <paramref name="pose"/> (its muscle array must be allocated).
        /// With <paramref name="detrend"/>, the body's net XZ travel over the clip is removed, so a
        /// walk walks in place but keeps its sway.
        /// </summary>
        public void Read(float time, bool detrend, ref HumanPose pose)
        {
            for (int i = 0; i < _muscles.Length; i++) pose.muscles[i] = _muscles[i] != null ? _muscles[i].Evaluate(time) : 0f;
            var p = new Vector3(V(_t[0], time, 0f), V(_t[1], time, 1f), V(_t[2], time, 0f));
            if (detrend)
            {
                float u = time / Length;
                p.x -= (V(_t[0], Length, 0f) - V(_t[0], 0f, 0f)) * u;
                p.z -= (V(_t[2], Length, 0f) - V(_t[2], 0f, 0f)) * u;
            }
            pose.bodyPosition = p;
            var q = new Quaternion(V(_q[0], time, 0f), V(_q[1], time, 0f), V(_q[2], time, 0f), V(_q[3], time, 1f));
            pose.bodyRotation = Quaternion.Normalize(q);
        }

        private static float V(AnimationCurve c, float t, float fallback) => c != null ? c.Evaluate(t) : fallback;
    }
}
