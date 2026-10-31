using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Pickleball.Cutscene.Editor.Actors
{
    /// <summary>
    /// Bakes a <see cref="PoseClipSpec"/> into a Humanoid <c>.anim</c>: every frame (30 fps) it
    /// samples the base clip on a hidden copy of the model, reads the human pose, applies the
    /// keys, optionally pulls the lowest skin point onto the ground, and records all 95 muscles
    /// plus the body position and rotation (RootT / RootQ).
    /// </summary>
    /// <remarks>
    /// <para>Root rotation, height and XZ are all baked into the pose ("based upon Original"),
    /// like Birdy's clips: the actor's transform is the blocking mark and the timeline moves it;
    /// the clip never travels. That is what lets a clip lie the body down (lie still) or turn
    /// it round (turn away) while the slot's Feet anchor stays put.</para>
    /// <para>Deterministic: the same spec and model give the same file, so re-running the setup
    /// menu leaves no diff. An existing asset is overwritten in place (GUID kept).</para>
    /// </remarks>
    public static class PoseClipBaker
    {
        /// <summary>Frames per second of every baked clip.</summary>
        public const float Fps = 30f;

        /// <summary>Bake a spec and write it to <c>Folder/Name.anim</c>. Returns the saved clip.</summary>
        public static AnimationClip BakeAndSave(PoseClipSpec spec)
        {
            AnimationClip clip = Bake(spec);
            string path = spec.Folder + "/" + spec.Name + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null)
            {
                // Create an empty asset and copy into it, exactly as a re-run does: a clip saved
                // straight from CreateAsset serializes its binding cache, a copied one doesn't, so
                // creating directly made the second run rewrite every new clip.
                existing = new AnimationClip { name = spec.Name };
                AssetDatabase.CreateAsset(existing, path);
            }
            EditorUtility.CopySerialized(clip, existing);
            existing.name = spec.Name;
            Object.DestroyImmediate(clip);
            AssetDatabase.SaveAssetIfDirty(existing);
            return existing;
        }

        /// <summary>Bake a spec into a new, unsaved clip, keyframe-reduced (<see cref="ClipReduction"/>).</summary>
        public static AnimationClip Bake(PoseClipSpec spec) => Bake(spec, true);

        /// <summary>
        /// The curve-writing options of one bake: whether to reduce, the model's human scale (for
        /// metre tolerances) and the loop seams that must stay keys.
        /// </summary>
        private sealed class Output
        {
            public bool Reduce;
            public float Scale;
            public bool[] Seams;
        }

        /// <summary>
        /// Bake a spec into a new, unsaved clip. <paramref name="reduce"/> false writes a key every
        /// frame on every curve (the reference the reduction is tested against).
        /// </summary>
        public static AnimationClip Bake(PoseClipSpec spec, bool reduce)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
            if (model == null) throw new InvalidOperationException($"No model at {spec.ModelPath}");
            AnimationClip baseClip = LoadClip(spec.BaseClip);
            if (baseClip == null) throw new InvalidOperationException($"No base clip at {spec.BaseClip}");

            var go = (GameObject)Object.Instantiate(model);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = go.GetComponent<Animator>();
            var handler = new HumanPoseHandler(animator.avatar, go.transform);
            var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>();
            var baked = new Mesh();
            try
            {
                int frames = Mathf.Max(2, Mathf.RoundToInt(spec.Duration * Fps) + 1);
                int mc = HumanTrait.MuscleCount;
                var muscles = new float[frames, mc];
                var rootT = new Vector3[frames];
                var rootQ = new Quaternion[frames];
                var pose = new HumanPose { muscles = new float[mc] };
                var frame = new PoseFrame();
                var basePose = new HumanClipCurves(baseClip);
                float scale = animator.humanScale;

                // Ground to the base clip's own lowest skin point (never above 0): the gameplay idle
                // stands with its soles 5 cm below the root, so grounding at 0 put every hand-keyed
                // MC clip 5 cm higher than the idle gameplay resumes in, a pop at each hand-off.
                float groundTarget = 0f;
                if (spec.Ground > 0f || spec.Keys.Exists(k => (k.Ground ?? 0f) > 0f))
                {
                    basePose.Read(spec.BaseTime >= 0f ? spec.BaseTime : 0f, spec.BaseTime < 0f, ref pose);
                    handler.SetHumanPose(ref pose);
                    groundTarget = Mathf.Min(0f, MinSkinY(smrs, baked));
                }

                int[] rightLeg = null, leftLeg = null;
                var mirror = new HumanPose { muscles = new float[mc] };
                if (spec.SymmetricLegs) LegMuscles(out leftLeg, out rightLeg);

                var setChannels = Channels(spec, k => k.Set, false);
                var addChannels = Channels(spec, k => k.Add, true);

                var output = new Output { Reduce = reduce, Scale = scale, Seams = new bool[frames] };
                float previousTb = float.NegativeInfinity;
                for (int f = 0; f < frames; f++)
                {
                    float t = f == frames - 1 ? spec.Duration : f / Fps;
                    // A looping clip wraps its base (the last frame equals the first); a one-shot
                    // plays it through and ends on its last frame.
                    float tb = spec.BaseTime >= 0f
                        ? spec.BaseTime
                        : spec.Loop
                            ? Mathf.Repeat(t * spec.BaseSpeed, Mathf.Max(baseClip.length, 1e-3f))
                            : Mathf.Min(t * spec.BaseSpeed, baseClip.length);
                    // Where the base wraps inside the clip: a loop seam, kept as a key on every curve.
                    if (tb < previousTb) output.Seams[f] = true;
                    previousTb = tb;
                    basePose.Read(tb, spec.BaseTime < 0f, ref pose);
                    if (rightLeg != null)
                    {
                        float half = Mathf.Repeat(tb + baseClip.length * 0.5f, Mathf.Max(baseClip.length, 1e-3f));
                        basePose.Read(half, spec.BaseTime < 0f, ref mirror);
                        // Left and right muscles share a sign convention, so a straight copy mirrors.
                        for (int m = 0; m < rightLeg.Length; m++) pose.muscles[rightLeg[m]] = mirror.muscles[leftLeg[m]];
                    }

                    float start = spec.Keys.Count > 0 ? spec.Keys[0].Time : 0f;
                    foreach (var ch in setChannels)
                    {
                        int i = HumanMuscles.Index(ch.Key);
                        if (i < 0) continue;
                        // A muscle first set after the clip's first key eases in from the base.
                        float first = ch.Value[0].Key;
                        if (t < first && first > start + 1e-4f)
                            pose.muscles[i] = Mathf.Lerp(pose.muscles[i], ch.Value[0].Value, Ease(Mathf.Clamp01((t - start) / (first - start))));
                        else
                            pose.muscles[i] = Eval(ch.Value, t);
                    }
                    foreach (var ch in addChannels)
                    {
                        int i = HumanMuscles.Index(ch.Key);
                        if (i >= 0) pose.muscles[i] += Eval(ch.Value, t);
                    }

                    Quaternion body = EvalBody(spec, t, pose.bodyRotation);
                    frame.Muscles = pose.muscles;
                    frame.Turn = EvalVec(spec, t, k => k.Turn);
                    frame.Offset = EvalVec(spec, t, k => k.Offset);
                    spec.Procedural?.Invoke(t, frame);
                    pose.bodyRotation = Quaternion.Euler(frame.Turn) * body;

                    float ground = EvalGround(spec, t);
                    if (ground > 0f)
                    {
                        for (int it = 0; it < 2; it++)
                        {
                            handler.SetHumanPose(ref pose);
                            float minY = MinSkinY(smrs, baked);
                            pose.bodyPosition.y -= ground * (minY - groundTarget) / scale;
                        }
                    }
                    pose.bodyPosition += frame.Offset / scale;

                    for (int i = 0; i < mc; i++) muscles[f, i] = pose.muscles[i];
                    rootT[f] = pose.bodyPosition;
                    Quaternion q = pose.bodyRotation;
                    if (f > 0 && Quaternion.Dot(q, rootQ[f - 1]) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    rootQ[f] = q;
                }

                AnimationClip clip = BuildClip(spec, frames, muscles, rootT, rootQ, output);
                BakeFootGoals(clip, spec, frames, animator, handler, output);
                return clip;
            }
            finally
            {
                handler.Dispose();
                Object.DestroyImmediate(baked);
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// The left leg's swing muscles (front-back, knee stretch, foot and toes up-down) and the
        /// matching right-leg ones. In-out and twist stay the right leg's own: on the chibi's avatar
        /// the same in-out value swings the right knee across the body.
        /// </summary>
        private static void LegMuscles(out int[] left, out int[] right)
        {
            var l = new List<int>();
            var r = new List<int>();
            for (int i = 0; i < HumanTrait.MuscleCount; i++)
            {
                string n = HumanTrait.MuscleName[i];
                if (!(n.StartsWith("Left Upper Leg") || n.StartsWith("Left Lower Leg") || n.StartsWith("Left Foot") || n.StartsWith("Left Toes"))) continue;
                if (!(n.EndsWith("Front-Back") || n.EndsWith("Stretch") || n.EndsWith("Up-Down"))) continue;
                int j = Array.IndexOf(HumanTrait.MuscleName, "Right" + n.Substring(4));
                if (j < 0) continue;
                l.Add(i);
                r.Add(j);
            }
            left = l.ToArray();
            right = r.ToArray();
        }

        /// <summary>The first take of an FBX ("path" or "path#take"), or an .anim.</summary>
        public static AnimationClip LoadClip(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string take = null;
            int hash = path.IndexOf('#');
            if (hash >= 0)
            {
                take = path.Substring(hash + 1);
                path = path.Substring(0, hash);
            }
            if (path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (o is AnimationClip c && !c.name.StartsWith("__preview__", StringComparison.Ordinal) &&
                    (take == null || c.name == take))
                    return c;
            }
            return null;
        }

        private static float MinSkinY(SkinnedMeshRenderer[] smrs, Mesh baked)
        {
            float min = float.PositiveInfinity;
            foreach (var smr in smrs)
            {
                if (smr == null || smr.sharedMesh == null) continue;
                smr.BakeMesh(baked, true);
                var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                var verts = baked.vertices;
                for (int v = 0; v < verts.Length; v++)
                {
                    float y = m.MultiplyPoint3x4(verts[v]).y;
                    if (y < min) min = y;
                }
            }
            return float.IsInfinity(min) ? 0f : min;
        }

        private static Dictionary<string, List<KeyValuePair<float, float>>> Channels(
            PoseClipSpec spec, Func<PoseKey, Dictionary<string, float>> pick, bool additive)
        {
            var map = new Dictionary<string, List<KeyValuePair<float, float>>>();
            foreach (PoseKey k in spec.Keys)
                foreach (var m in pick(k))
                    if (!map.ContainsKey(m.Key)) map[m.Key] = new List<KeyValuePair<float, float>>();
            foreach (var ch in map)
            {
                foreach (PoseKey k in spec.Keys)
                {
                    var d = pick(k);
                    if (d.TryGetValue(ch.Key, out float v)) ch.Value.Add(new KeyValuePair<float, float>(k.Time, v));
                    else if (additive) ch.Value.Add(new KeyValuePair<float, float>(k.Time, 0f));
                }
                ch.Value.Sort((a, b) => a.Key.CompareTo(b.Key));
            }
            return map;
        }

        private static float Ease(float u) => u * u * (3f - 2f * u);

        private static bool Segment<T>(List<KeyValuePair<float, T>> keys, float t, out T a, out T b, out float u)
        {
            a = keys[0].Value;
            b = a;
            u = 0f;
            if (t <= keys[0].Key) return false;
            if (t >= keys[keys.Count - 1].Key)
            {
                a = b = keys[keys.Count - 1].Value;
                return false;
            }
            for (int i = 0; i < keys.Count - 1; i++)
            {
                if (t < keys[i].Key || t > keys[i + 1].Key) continue;
                float span = keys[i + 1].Key - keys[i].Key;
                a = keys[i].Value;
                b = keys[i + 1].Value;
                u = span > 1e-6f ? Ease((t - keys[i].Key) / span) : 1f;
                return true;
            }
            return false;
        }

        private static float Eval(List<KeyValuePair<float, float>> keys, float t)
        {
            return Segment(keys, t, out float a, out float b, out float u) ? Mathf.Lerp(a, b, u) : a;
        }

        private static Quaternion EvalBody(PoseClipSpec spec, float t, Quaternion baseRotation)
        {
            var keys = new List<KeyValuePair<float, Quaternion>>();
            foreach (PoseKey k in spec.Keys)
                if (k.Body.HasValue) keys.Add(new KeyValuePair<float, Quaternion>(k.Time, k.Body.Value));
            if (keys.Count == 0) return baseRotation;
            float start = spec.Keys[0].Time, first = keys[0].Key;
            if (t < first && first > start + 1e-4f)
                return Quaternion.Slerp(baseRotation, keys[0].Value, Ease(Mathf.Clamp01((t - start) / (first - start))));
            return Segment(keys, t, out Quaternion a, out Quaternion b, out float u) ? Quaternion.Slerp(a, b, u) : a;
        }

        private static Vector3 EvalVec(PoseClipSpec spec, float t, Func<PoseKey, Vector3> pick)
        {
            if (spec.Keys.Count == 0) return Vector3.zero;
            var keys = new List<KeyValuePair<float, Vector3>>();
            foreach (PoseKey k in spec.Keys) keys.Add(new KeyValuePair<float, Vector3>(k.Time, pick(k)));
            return Segment(keys, t, out Vector3 a, out Vector3 b, out float u) ? Vector3.Lerp(a, b, u) : a;
        }

        private static float EvalGround(PoseClipSpec spec, float t)
        {
            if (spec.Keys.Count == 0) return spec.Ground;
            var keys = new List<KeyValuePair<float, float>>();
            foreach (PoseKey k in spec.Keys) keys.Add(new KeyValuePair<float, float>(k.Time, k.Ground ?? spec.Ground));
            return Mathf.Clamp01(Eval(keys, t));
        }

        /// <summary>Foot IK goal solve passes (each one halves the error many times over; 3 is sub-millimetre).</summary>
        private const int GoalPasses = 3;

        static readonly HumanBodyBones[] Feet = { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
        static readonly string[] GoalNames = { "LeftFoot", "RightFoot" };

        /// <summary>
        /// Writes the clip's foot IK goals (<c>LeftFootT/Q</c>, <c>RightFootT/Q</c>) so that foot IK
        /// puts each foot exactly where the clip's own muscles do.
        /// </summary>
        /// <remarks>
        /// A muscle-only clip has no goals, and any foot IK then pulls both feet to the empty goal
        /// at the body: the legs fold up to hip height. Timeline's foot IK is off on every action
        /// clip, but the Player's locomotion controller keeps running under the timeline and its
        /// idle state has Foot IK on, which applies to the mixed pose; in Play every cutscene
        /// clip showed MC with his feet at his hips under the cloak (review round 2, N1: "boot
        /// frozen behind at hip height, legs don't cycle"). The goal convention is internal to
        /// Mecanim, so it is solved rather than assumed: start from the feet relative to the body,
        /// evaluate the clip with foot IK on, move each goal by the miss, and repeat.
        /// </remarks>
        private static void BakeFootGoals(AnimationClip clip, PoseClipSpec spec, int frames, Animator animator, HumanPoseHandler handler,
                                          Output output)
        {
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
            float scale = animator.humanScale;
            var fkPos = new Vector3[frames, 2];
            var fkRot = new Quaternion[frames, 2];
            var bodyQ = new Quaternion[frames];
            var goalT = new Vector3[frames, 2];
            var goalQ = new Quaternion[frames, 2];
            var pose = new HumanPose { muscles = new float[HumanTrait.MuscleCount] };

            float At(int f) => f == frames - 1 ? spec.Duration : f / Fps;

            // The clip's own feet (no IK) and its body, per frame.
            Sample(clip, animator, false, frames, At, (f, p, r) =>
            {
                handler.GetHumanPose(ref pose);
                bodyQ[f] = pose.bodyRotation;
                for (int s = 0; s < 2; s++)
                {
                    fkPos[f, s] = p[s];
                    fkRot[f, s] = r[s];
                    goalT[f, s] = Quaternion.Inverse(pose.bodyRotation) * (p[s] / scale - pose.bodyPosition);
                    goalQ[f, s] = Quaternion.Inverse(pose.bodyRotation) * r[s];
                }
            });

            for (int pass = 0; pass < GoalPasses; pass++)
            {
                WriteGoals(clip, spec, frames, goalT, goalQ, null);
                Sample(clip, animator, true, frames, At, (f, p, r) =>
                {
                    Quaternion inv = Quaternion.Inverse(bodyQ[f]);
                    for (int s = 0; s < 2; s++)
                    {
                        goalT[f, s] += inv * (fkPos[f, s] - p[s]) / scale;
                        Quaternion miss = fkRot[f, s] * Quaternion.Inverse(r[s]);
                        goalQ[f, s] = Quaternion.Normalize(inv * miss * bodyQ[f] * goalQ[f, s]);
                    }
                });
            }
            // The solve passes use per-frame goals; only the final write is reduced.
            WriteGoals(clip, spec, frames, goalT, goalQ, output);
        }

        private static void Sample(AnimationClip clip, Animator animator, bool footIK, int frames, Func<int, float> time,
                                   Action<int, Vector3[], Quaternion[]> read)
        {
            Transform root = animator.transform;
            var feet = new[] { animator.GetBoneTransform(Feet[0]), animator.GetBoneTransform(Feet[1]) };
            var pos = new Vector3[2];
            var rot = new Quaternion[2];
            var graph = PlayableGraph.Create("PoseClipBaker.Goals");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(footIK);
                var output = AnimationPlayableOutput.Create(graph, "Goals", animator);
                output.SetSourcePlayable(playable);
                for (int f = 0; f < frames; f++)
                {
                    root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    playable.SetTime(time(f));
                    graph.Evaluate();
                    for (int s = 0; s < 2; s++)
                    {
                        pos[s] = root.InverseTransformPoint(feet[s].position);
                        rot[s] = Quaternion.Inverse(root.rotation) * feet[s].rotation;
                    }
                    read(f, pos, rot);
                }
            }
            finally
            {
                graph.Destroy();
            }
        }

        private static void WriteGoals(AnimationClip clip, PoseClipSpec spec, int frames, Vector3[,] goalT, Quaternion[,] goalQ,
                                       Output output)
        {
            var bindings = new List<EditorCurveBinding>();
            var curves = new List<AnimationCurve>();
            var values = new float[frames];
            for (int s = 0; s < 2; s++)
            {
                for (int f = 1; f < frames; f++)
                    if (Quaternion.Dot(goalQ[f, s], goalQ[f - 1, s]) < 0f)
                        goalQ[f, s] = new Quaternion(-goalQ[f, s].x, -goalQ[f, s].y, -goalQ[f, s].z, -goalQ[f, s].w);
                for (int c = 0; c < 3; c++)
                {
                    for (int f = 0; f < frames; f++) values[f] = goalT[f, s][c];
                    bindings.Add(EditorCurveBinding.FloatCurve("", typeof(Animator), GoalNames[s] + "T." + "xyz"[c]));
                    curves.Add(Curve(values, frames, spec, output, output != null ? ClipReduction.PositionTolerance(output.Scale) : 0f));
                }
                for (int c = 0; c < 4; c++)
                {
                    for (int f = 0; f < frames; f++) values[f] = goalQ[f, s][c];
                    bindings.Add(EditorCurveBinding.FloatCurve("", typeof(Animator), GoalNames[s] + "Q." + "xyzw"[c]));
                    curves.Add(Curve(values, frames, spec, output, ClipReduction.QuaternionComponentTolerance));
                }
            }
            AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
        }

        private static AnimationClip BuildClip(PoseClipSpec spec, int frames, float[,] muscles, Vector3[] rootT, Quaternion[] rootQ,
                                               Output output)
        {
            var clip = new AnimationClip { name = spec.Name, frameRate = Fps };
            int mc = HumanTrait.MuscleCount;
            var bindings = new List<EditorCurveBinding>();
            var curves = new List<AnimationCurve>();
            var values = new float[frames];

            void Add(string attribute, float tolerance)
            {
                bindings.Add(EditorCurveBinding.FloatCurve("", typeof(Animator), attribute));
                curves.Add(Curve(values, frames, spec, output, tolerance));
            }

            for (int c = 0; c < 3; c++)
            {
                for (int f = 0; f < frames; f++) values[f] = rootT[f][c];
                Add("RootT." + "xyz"[c], ClipReduction.BodyShare * ClipReduction.PositionTolerance(output.Scale));
            }
            for (int c = 0; c < 4; c++)
            {
                for (int f = 0; f < frames; f++) values[f] = rootQ[f][c];
                Add("RootQ." + "xyzw"[c], ClipReduction.BodyShare * ClipReduction.QuaternionComponentTolerance);
            }
            for (int i = 0; i < mc; i++)
            {
                for (int f = 0; f < frames; f++) values[f] = muscles[f, i];
                Add(HumanMuscles.ClipAttribute(i), ClipReduction.MuscleToleranceFor(HumanTrait.MuscleName[i]));
            }
            AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());

            var s = AnimationUtility.GetAnimationClipSettings(clip);
            s.loopTime = spec.Loop;
            s.loopBlend = spec.Loop;
            s.loopBlendOrientation = true;
            s.loopBlendPositionY = true;
            s.loopBlendPositionXZ = true;
            s.keepOriginalOrientation = true;
            s.keepOriginalPositionY = true;
            s.keepOriginalPositionXZ = true;
            s.heightFromFeet = false;
            s.mirror = false;
            s.startTime = 0f;
            s.stopTime = spec.Duration;
            AnimationUtility.SetAnimationClipSettings(clip, s);
            return clip;
        }

        /// <summary>
        /// A baked curve: reduced to the keys interpolation can't reproduce within
        /// <paramref name="tolerance"/> when the output reduces, else a key every frame.
        /// </summary>
        private static AnimationCurve Curve(float[] values, int frames, PoseClipSpec spec, Output output, float tolerance)
        {
            if (output == null || !output.Reduce) return Curve(values, frames, spec.Duration, spec.Loop);
            var times = new float[frames];
            for (int f = 0; f < frames; f++) times[f] = f == frames - 1 ? spec.Duration : f / Fps;
            var samples = new float[frames];
            Array.Copy(values, samples, frames);
            CurveReducer.Key[] reduced = CurveReducer.Reduce(times, samples, tolerance, output.Seams, spec.Loop);
            var keys = new Keyframe[reduced.Length];
            for (int k = 0; k < reduced.Length; k++)
                keys[k] = new Keyframe(reduced[k].Time,
                    ClipReduction.Quantise(reduced[k].Value, ClipReduction.ValueQuantum),
                    ClipReduction.Quantise(reduced[k].InSlope, ClipReduction.SlopeQuantum),
                    ClipReduction.Quantise(reduced[k].OutSlope, ClipReduction.SlopeQuantum));
            return new AnimationCurve(keys);
        }

        private static AnimationCurve Curve(float[] values, int frames, float duration, bool loop)
        {
            var keys = new Keyframe[frames];
            float dt = 1f / Fps;
            for (int f = 0; f < frames; f++)
            {
                float t = f == frames - 1 ? duration : f * dt;
                float prev = f > 0 ? values[f - 1] : (loop && frames > 2 ? values[frames - 2] : values[f]);
                float next = f < frames - 1 ? values[f + 1] : (loop && frames > 2 ? values[1] : values[f]);
                float span = (f > 0 && f < frames - 1) || loop ? 2f * dt : dt;
                float tan = (next - prev) / span;
                keys[f] = new Keyframe(t, values[f], tan, tan);
            }
            return new AnimationCurve(keys);
        }
    }
}
