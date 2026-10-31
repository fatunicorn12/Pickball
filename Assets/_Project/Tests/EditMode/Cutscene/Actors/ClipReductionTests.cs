using System;
using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Cutscene.Editor.Actors;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Pickleball.Tests.Cutscene.Actors
{
    /// <summary>
    /// The baked clips are keyframe-reduced (they were 166 MB of YAML with a key every frame on all
    /// 116 curves). The reducer keeps what interpolation can't reproduce, and a reduced clip moves
    /// every humanoid bone within 2 mm and 0.5° of an unreduced bake of the same spec.
    /// </summary>
    public class ClipReductionTests
    {
        static float[] Times(int n, float dt = 1f / 30f)
        {
            var t = new float[n];
            for (int i = 0; i < n; i++) t[i] = i * dt;
            return t;
        }

        [Test]
        public void AStraightLineCollapsesToTwoKeys()
        {
            var t = Times(61);
            var v = new float[61];
            for (int i = 0; i < v.Length; i++) v[i] = 0.3f + 0.7f * t[i];
            var keys = CurveReducer.Reduce(t, v, 0.002f);
            Assert.AreEqual(2, keys.Length);
            Assert.AreEqual(0f, keys[0].Time, 1e-6f);
            Assert.AreEqual(t[60], keys[1].Time, 1e-6f);
        }

        [Test]
        public void ASineStaysWithinTolerance()
        {
            const float tol = 0.002f;
            var t = Times(121);
            var v = new float[121];
            for (int i = 0; i < v.Length; i++) v[i] = 0.4f * Mathf.Sin(t[i] * 2.7f) + 0.1f * Mathf.Sin(t[i] * 9.1f);
            var keys = CurveReducer.Reduce(t, v, tol);
            Assert.That(keys.Length, Is.LessThan(v.Length / 2), "a smooth curve loses most of its keys");
            for (int i = 0; i < v.Length; i++)
                Assert.That(Mathf.Abs(CurveReducer.Evaluate(keys, t[i]) - v[i]), Is.LessThanOrEqualTo(tol + 1e-6f), $"sample {i}");
            // Unity evaluates the same Hermite form.
            var curve = new AnimationCurve(Array.ConvertAll(keys, k => new Keyframe(k.Time, k.Value, k.InSlope, k.OutSlope)));
            for (int i = 0; i < v.Length; i++)
                Assert.That(Mathf.Abs(curve.Evaluate(t[i]) - v[i]), Is.LessThanOrEqualTo(tol + 1e-5f), $"Unity at sample {i}");
        }

        [Test]
        public void AConstantCurveIsOneKey()
        {
            var t = Times(40);
            var v = new float[40];
            for (int i = 0; i < v.Length; i++) v[i] = 0.25f + (i % 2 == 0 ? 0.0004f : -0.0004f);
            var keys = CurveReducer.Reduce(t, v, 0.002f);
            Assert.AreEqual(1, keys.Length);
            Assert.AreEqual(0.25f, keys[0].Value, 0.001f);
        }

        [Test]
        public void LoopSeamsAreKept()
        {
            var t = Times(61);
            var v = new float[61];
            var keep = new bool[61];
            for (int i = 0; i < v.Length; i++) v[i] = (i % 30) / 30f; // a sawtooth: the base wraps at 30
            keep[30] = true;
            var keys = CurveReducer.Reduce(t, v, 0.002f, keep, true);
            Assert.That(Array.Exists(keys, k => Mathf.Abs(k.Time - t[30]) < 1e-6f), "the seam is a key");
            Assert.AreEqual(0f, keys[0].Time, 1e-6f);
            Assert.AreEqual(t[60], keys[keys.Length - 1].Time, 1e-6f, "first and last keys stay");
        }

        /// <summary>Every spec the actor setup bakes, with its library folder.</summary>
        static IEnumerable<PoseClipSpec> AllSpecs()
        {
            foreach (var s in McClips.All()) yield return s;
            foreach (var s in BirdyClips.All()) yield return s;
            var seen = new HashSet<string>();
            foreach (var r in CutsceneClipCatalog.McReuse())
                if (!string.IsNullOrEmpty(r.CopyName) && seen.Add(r.CopyName))
                    yield return CutsceneClipCatalog.InPlaceCopy(r, CutsceneActorPaths.ChibiModel, CutsceneActorPaths.McClipDir);
            foreach (var r in CutsceneClipCatalog.BirdyReuse())
                if (!string.IsNullOrEmpty(r.CopyName) && seen.Add(r.CopyName))
                    yield return CutsceneClipCatalog.InPlaceCopy(r, CutsceneActorPaths.BirdyModel, CutsceneActorPaths.BirdyCutsceneClipDir);
        }

        [Test]
        [Category("Assets")]
        public void ReducedClipsMoveEveryBoneWithin2mmAndHalfADegreeOfAFullBake()
        {
            int checkedClips = 0;
            float worstMm = 0f, worstDeg = 0f;
            string worstWhere = "";
            foreach (var spec in AllSpecs())
            {
                var reduced = AssetDatabase.LoadAssetAtPath<AnimationClip>(spec.Folder + "/" + spec.Name + ".anim");
                Assert.That(reduced, Is.Not.Null, spec.Name);
                AnimationClip full = PoseClipBaker.Bake(spec, false);
                var model = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath));
                try
                {
                    var animator = model.GetComponentInChildren<Animator>();
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.applyRootMotion = false;
                    var bones = new List<Transform>();
                    for (int b = 0; b < (int)HumanBodyBones.LastBone; b++)
                    {
                        var t = animator.GetBoneTransform((HumanBodyBones)b);
                        if (t != null) bones.Add(t);
                    }
                    int samples = Mathf.Max(2, Mathf.CeilToInt(spec.Duration * 60f) + 1);
                    var pa = new Vector3[bones.Count];
                    var ra = new Quaternion[bones.Count];
                    for (int i = 0; i < samples; i++)
                    {
                        float time = Mathf.Min(i / 60f, spec.Duration);
                        Pose(animator, full, time, bones, pa, ra);
                        var pb = new Vector3[bones.Count];
                        var rb = new Quaternion[bones.Count];
                        Pose(animator, reduced, time, bones, pb, rb);
                        for (int b = 0; b < bones.Count; b++)
                        {
                            float mm = Vector3.Distance(pa[b], pb[b]) * 1000f;
                            float deg = Quaternion.Angle(ra[b], rb[b]);
                            if (mm > worstMm || deg > worstDeg) worstWhere = $"{spec.Name} @ {time:0.000}s {bones[b].name}";
                            worstMm = Mathf.Max(worstMm, mm);
                            worstDeg = Mathf.Max(worstDeg, deg);
                            Assert.That(mm, Is.LessThanOrEqualTo(2f), $"{spec.Name} @ {time:0.000}s: {bones[b].name} moved {mm:0.00} mm");
                            Assert.That(deg, Is.LessThanOrEqualTo(0.5f), $"{spec.Name} @ {time:0.000}s: {bones[b].name} turned {deg:0.00}°");
                        }
                    }
                    checkedClips++;
                }
                finally
                {
                    Object.DestroyImmediate(model);
                    Object.DestroyImmediate(full);
                }
            }
            Assert.That(checkedClips, Is.GreaterThan(50));
            TestContext.Out.WriteLine($"{checkedClips} clips; worst {worstMm:0.00} mm / {worstDeg:0.00}° ({worstWhere})");
        }

        static void Pose(Animator animator, AnimationClip clip, float time, List<Transform> bones, Vector3[] pos, Quaternion[] rot)
        {
            var graph = PlayableGraph.Create("ClipReductionTests");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false);
                AnimationPlayableOutput.Create(graph, "Pose", animator).SetSourcePlayable(playable);
                playable.SetTime(time);
                graph.Evaluate();
                Transform root = animator.transform;
                for (int b = 0; b < bones.Count; b++)
                {
                    pos[b] = root.InverseTransformPoint(bones[b].position);
                    rot[b] = Quaternion.Inverse(root.rotation) * bones[b].rotation;
                }
            }
            finally
            {
                graph.Destroy();
            }
        }
    }
}
