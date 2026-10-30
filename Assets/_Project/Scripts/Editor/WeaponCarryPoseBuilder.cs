using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Pickleball.Sandbox.Hands;
using Pickleball.Sandbox.Weapon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

/// <summary>
/// Authors a weapon's carry pose: a one-frame Humanoid clip of the weapon arm holding
/// the blade where it reads well and clears the body. It also renders contact sheets
/// of a carry pose over the locomotion clips.
///
/// <para><b>How a pose is found.</b> A <see cref="Spec"/> states the pose as geometry in
/// the character's own frame (x right, y up, z forward): where the fist goes, which way
/// the blade points, and which way its steel edge faces. The builder poses
/// <c>Chibi.prefab</c> in the base clip's frame (idle by default), seats the weapon's
/// held prefab on the solved grab point, and searches the nine right-arm muscles
/// (shoulder, arm, forearm and hand) for the pose that best meets the spec. The cost
/// also counts the weapon's clearance volumes, a floor margin for the tip, and a soft
/// limit on the wrist muscles, so the blade stays clear and the wrist never reads as
/// broken. Searching in muscle space keeps every pose inside the avatar's limits: a
/// pose solved with bone rotations would lose any part the muscles can't express when
/// it is written to the clip.</para>
///
/// <para>The search is deterministic coordinate descent from the base clip's own arm, so
/// re-running it writes the same clip. The clip carries the whole body (the base pose
/// with the solved arm), so it previews as a full pose; the Carry layer's mask passes
/// only the right arm through.</para>
///
/// <para>Contact sheets: <see cref="RenderSheet"/> poses the same rig over any base clip,
/// with the carry arm laid over it in muscle space, which is what a masked Override layer
/// at full weight does. Post-Animator passes (FingerCurl, WeaponClearance) don't run here,
/// so the fist shows the clip's fingers; Play mode shows the closed fist.</para>
/// </summary>
public static class WeaponCarryPoseBuilder
{
    const string ChibiPrefab = "Assets/_Project/Prefabs/Chibi.prefab";

    /// <summary>The nine muscles a right-arm carry pose is made of, in search order.</summary>
    static readonly string[] ArmMuscles =
    {
        "Right Shoulder Down-Up", "Right Shoulder Front-Back",
        "Right Arm Down-Up", "Right Arm Front-Back", "Right Arm Twist In-Out",
        "Right Forearm Stretch", "Right Forearm Twist In-Out",
        "Right Hand Down-Up", "Right Hand In-Out",
    };

    /// <summary>A carry pose as geometry in the character's frame (x right, y up, z forward).</summary>
    [Serializable]
    public struct Spec
    {
        /// <summary>Asset path of the clip to write.</summary>
        public string ClipPath;
        /// <summary>Held prefab whose blade is posed (its grip anchor seats on the grab point).</summary>
        public string HeldPrefab;
        /// <summary>Clearance profile whose volumes the blade must stay out of. Optional.</summary>
        public string ClearanceProfile;
        /// <summary>Model file of the locomotion clip the pose is authored over. The arm's
        /// muscles are relative to the chest, so the pose lands as specified only over a
        /// chest turned the way this clip's is.</summary>
        public string BaseClip;
        /// <summary>Time in <see cref="BaseClip"/>, s.</summary>
        public float BaseTime;
        /// <summary>Where the centre of the fist goes, m, relative to the character's root.</summary>
        public Vector3 Fist;
        /// <summary>Which way the blade points from the fist (normalised by the builder).</summary>
        public Vector3 Blade;
        /// <summary>Which way the steel edge faces (made perpendicular to the blade).</summary>
        public Vector3 Edge;
        /// <summary>Lowest the blade may come to the floor, m (the feet's lowest point).</summary>
        public float FloorMargin;
        /// <summary>Extra gap kept from every clearance volume on top of the profile's own, m.</summary>
        public float ExtraClearance;
    }

    const string IdleFile = "Assets/_Project/Animation/Chibi/SwordShield/sword and shield idle (3).fbx";
    const string WalkFile = "Assets/_Project/Animation/Chibi/SwordShield/sword and shield walk.fbx";
    const string HeldRuler = "Assets/_Project/Prefabs/Weapons/Held_Ruler.prefab";
    const string RulerClearance = "Assets/_Project/ScriptableObjects/Weapon/RulerWeaponClearance.asset";

    /// <summary>
    /// The ruler's carry, standing: the low trail. The arm hangs relaxed with the fist
    /// beside the hip, and the blade runs back and a little out from it, dropping gently,
    /// edge down and out so the flat tilts up toward the game camera. Authored over the
    /// idle's first frame, whose chest is turned 58° to his right (the sword-and-shield
    /// stance); see <see cref="RulerCarryMoving"/> for why there are two.
    ///
    /// Chosen over a shoulder rest (<see cref="RulerShoulderRest"/>): on the chibi the
    /// shoulder sits right under the hair (the head sphere reaches down to 0.91 m and the
    /// shoulder joint is at 0.94 m), so a 1.05 m blade laid back over it runs through the
    /// head, and from behind the head hides it. See SwordAbilities.md, "Carry pose".
    /// </summary>
    public static readonly Spec RulerCarryStanding = new()
    {
        ClipPath = "Assets/_Project/Animation/Chibi/Carry/Carry_Ruler_Stand.anim",
        HeldPrefab = HeldRuler,
        ClearanceProfile = RulerClearance,
        BaseClip = IdleFile,
        BaseTime = 0f,
        Fist = new Vector3(0.26f, 0.57f, 0.05f),
        Blade = new Vector3(0.25f, -0.2f, -0.95f),
        Edge = new Vector3(0.6f, -0.75f, 0.1f),
        FloorMargin = 0.25f,
        ExtraClearance = 0.04f,
    };

    /// <summary>
    /// The ruler's carry, moving: the same low trail authored over the walk, whose chest
    /// faces nearly ahead (8°). The arm's muscles are relative to the chest, and the
    /// locomotion clips turn the chest anywhere from 0° to 67°, so one pose authored over
    /// the idle swung the blade straight out to his side at a walk. The Carry layer blends
    /// the two on the Animator's Speed, as the base layer blends idle and walk.
    /// </summary>
    public static readonly Spec RulerCarryMoving = new()
    {
        ClipPath = "Assets/_Project/Animation/Chibi/Carry/Carry_Ruler_Move.anim",
        HeldPrefab = HeldRuler,
        ClearanceProfile = RulerClearance,
        BaseClip = WalkFile,
        BaseTime = 0.3f,
        Fist = new Vector3(0.27f, 0.57f, 0.04f),
        Blade = new Vector3(0.55f, -0.2f, -0.8f),
        Edge = new Vector3(0.7f, -0.7f, 0.1f),
        FloorMargin = 0.25f,
        ExtraClearance = 0.04f,
    };

    /// <summary>
    /// The alternative that was tried and rejected: the flat of the blade on the right
    /// shoulder, edge up, tip trailing behind and up. Kept so the comparison can be
    /// rendered again (<see cref="Solve"/> with <c>write: false</c>).
    /// </summary>
    public static readonly Spec RulerShoulderRest = new()
    {
        ClipPath = "Assets/_Project/Animation/Chibi/Carry/Carry_Ruler_ShoulderRest.anim",
        HeldPrefab = HeldRuler,
        ClearanceProfile = RulerClearance,
        BaseClip = IdleFile,
        BaseTime = 0f,
        Fist = new Vector3(0.2f, 0.88f, 0.16f),
        Blade = new Vector3(0.3f, 0.55f, -0.78f),
        Edge = new Vector3(0f, 0.8f, 0.6f),
        FloorMargin = 0.18f,
        ExtraClearance = 0f,
    };

    /// <summary>What a solved pose achieved.</summary>
    public struct Result
    {
        public float[] Muscles;      // the nine arm muscles, in ArmMuscles order
        public float BladeError;     // degrees off the spec's blade direction
        public float EdgeError;      // degrees off the spec's edge direction
        public float FistError;      // metres off the spec's fist position
        public Vector3 Fist;         // where the fist ended up, character frame
        public Vector3 Blade;        // where the blade points, character frame
        public float WorstGap;       // smallest gap to a clearance volume, m (negative: inside)
        public string WorstVolume;
        public float TipHeight;      // lowest point of the blade above the floor, m
        public float Cost;
        public override string ToString() =>
            $"blade {BladeError:F1}° {Blade:F2}, edge {EdgeError:F1}°, fist {FistError * 100f:F1} cm {Fist:F2}, " +
            $"worst gap {WorstGap * 100f:F1} cm ({WorstVolume}), lowest blade point {TipHeight * 100f:F1} cm, " +
            $"muscles [{string.Join(", ", Muscles.Select(m => m.ToString("F2")))}]";
    }

    // ------------------------------------------------------------------ rig

    /// <summary>
    /// Chibi.prefab in the open scene, the weapon seated on its solved grab point, plus a
    /// pose handler. Dispose destroys everything it made.
    /// </summary>
    public sealed class Rig : IDisposable
    {
        public GameObject Root;
        public Animator Animator;
        public Transform Socket, BladeBase, BladeTip;
        public HumanPoseHandler Handler;
        public int[] ArmIndices;
        public WeaponClearanceProfile Profile;
        public readonly List<Object> Made = new();
        PlayableGraph _graph;
        AnimationClipPlayable _clipPlayable;
        AnimationClip _graphClip;
        AnimationPlayableOutput _output;

        /// <summary>Feet at y = 0 in the idle pose.</summary>
        public float FloorY;

        public void Dispose()
        {
            if (_graph.IsValid()) _graph.Destroy();
            Handler?.Dispose();
            foreach (var o in Made) if (o != null) Object.DestroyImmediate(o);
            Made.Clear();
        }

        /// <summary>Poses the whole body from <paramref name="clip"/> at <paramref name="time"/> s.</summary>
        public void SampleBase(AnimationClip clip, float time)
        {
            if (!_graph.IsValid())
            {
                _graph = PlayableGraph.Create("CarryPose");
                _output = AnimationPlayableOutput.Create(_graph, "pose", Animator);
            }
            if (_graphClip != clip)
            {
                // One graph for the rig's life: a second graph on the same Animator
                // never takes over the pose in edit mode, so swap the clip instead.
                if (_clipPlayable.IsValid()) _clipPlayable.Destroy();
                _clipPlayable = AnimationClipPlayable.Create(_graph, clip);
                _output.SetSourcePlayable(_clipPlayable);
                _graphClip = clip;
            }
            _clipPlayable.SetTime(time);
            _graph.Evaluate(0f);
            var hips = Animator.GetBoneTransform(HumanBodyBones.Hips);
            _hipsPosition = hips.position;
            _hipsRotation = hips.rotation;
        }

        Vector3 _hipsPosition;
        Quaternion _hipsRotation;

        /// <summary>
        /// Applies <paramref name="pose"/>, keeping the hips where the last
        /// <see cref="SampleBase"/> put them. A HumanPoseHandler round trip on this rig
        /// lifts the whole body about 0.6 m (the model sits under an offset child), and
        /// only the arm's pose relative to the body matters here.
        /// </summary>
        public void Apply(ref HumanPose pose)
        {
            Handler.SetHumanPose(ref pose);
            var hips = Animator.GetBoneTransform(HumanBodyBones.Hips);
            hips.SetPositionAndRotation(_hipsPosition, _hipsRotation);
        }
    }

    /// <summary>Builds the rig for <paramref name="heldPrefab"/>. Instantiates into
    /// <paramref name="scene"/> (the open scene when not given); the caller disposes it.
    /// Null (with an error) if a part is missing.</summary>
    public static Rig CreateRig(string heldPrefab, string clearanceProfile,
                                UnityEngine.SceneManagement.Scene scene = default)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChibiPrefab);
        var heldAsset = AssetDatabase.LoadAssetAtPath<GameObject>(heldPrefab);
        if (prefab == null || heldAsset == null)
        {
            Debug.LogError($"Carry pose: missing {(prefab == null ? ChibiPrefab : heldPrefab)}.");
            return null;
        }

        var rig = new Rig();
        rig.Root = scene.IsValid()
            ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene)
            : (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        rig.Root.hideFlags = HideFlags.DontSave;
        rig.Made.Add(rig.Root);
        rig.Root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        rig.Animator = rig.Root.GetComponentInChildren<Animator>();
        foreach (var t in rig.Root.GetComponentsInChildren<Transform>(true))
            if (t.name is "Skateboard" or "BladeTrail" or "DashArc" or "Rope" or "WeaponVisual")
                t.gameObject.SetActive(false);

        // The grab point as Setup Weapon Grip solves it (the prefab may lag the scene).
        var hand = rig.Animator.GetBoneTransform(HumanBodyBones.RightHand);
        rig.Socket = hand.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "WeaponSocket");
        if (rig.Socket == null)
        {
            rig.Socket = new GameObject("WeaponSocket").transform;
            rig.Socket.SetParent(hand, false);
        }
        var gripProfile = HandGripSetup.LoadOrCreateProfile();
        if (HandGripSetup.TrySolve(rig.Animator, true, gripProfile, out GripFrame frame, out _))
            rig.Socket.SetLocalPositionAndRotation(frame.Position, frame.Rotation);
        var grip = HandGripSetup.Attach(rig.Animator, true, rig.Socket, gripProfile);
        var held = ((GameObject)PrefabUtility.InstantiatePrefab(heldAsset, rig.Socket)).GetComponent<HeldItem>();
        grip.Hold(held);
        rig.BladeBase = held.BladeBase;
        rig.BladeTip = held.BladeTip;

        rig.Handler = new HumanPoseHandler(rig.Animator.avatar, rig.Animator.transform);
        var names = HumanTrait.MuscleName;
        rig.ArmIndices = ArmMuscles.Select(m => Array.IndexOf(names, m)).ToArray();
        rig.Profile = string.IsNullOrEmpty(clearanceProfile)
            ? null : AssetDatabase.LoadAssetAtPath<WeaponClearanceProfile>(clearanceProfile);

        rig.SampleBase(LoadClip(IdleFile), 0f);
        rig.FloorY = LowestFoot(rig.Animator);
        return rig;
    }

    /// <summary>The first real clip in a model file (or the clip asset itself).</summary>
    public static AnimationClip LoadClip(string path)
        => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

    static float LowestFoot(Animator a)
    {
        float y = float.PositiveInfinity;
        foreach (var b in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
                                  HumanBodyBones.LeftToes, HumanBodyBones.RightToes })
        {
            var t = a.GetBoneTransform(b);
            if (t != null) y = Mathf.Min(y, t.position.y);
        }
        // Ankle and toe joints sit a few cm above the sole.
        return float.IsPositiveInfinity(y) ? 0f : y - 0.06f;
    }

    // ---------------------------------------------------------------- solve

    /// <summary>
    /// Finds the arm muscles for <paramref name="spec"/> over the spec's base clip and, if
    /// <paramref name="write"/>, writes the clip. Returns null if the rig can't be built.
    /// </summary>
    public static AnimationClip Solve(Spec spec, bool write, out Result result)
    {
        result = default;
        // A preview scene, so solving never touches (or dirties) the open scene.
        var scene = EditorSceneManager.NewPreviewScene();
        HumanPose pose;
        try
        {
            using var rig = CreateRig(spec.HeldPrefab, spec.ClearanceProfile, scene);
            if (rig == null) return null;
            var baseClip = LoadClip(spec.BaseClip);
            if (baseClip == null)
            {
                Debug.LogError($"Carry pose: no clip in {spec.BaseClip}.");
                return null;
            }
            rig.SampleBase(baseClip, spec.BaseTime);
            pose = new HumanPose();
            rig.Handler.GetHumanPose(ref pose);
            result = Search(rig, spec, pose);
            for (int i = 0; i < rig.ArmIndices.Length; i++) pose.muscles[rig.ArmIndices[i]] = result.Muscles[i];
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
        Debug.Log($"Carry pose {Path.GetFileNameWithoutExtension(spec.ClipPath)}: {result}");
        return write ? WriteClip(spec.ClipPath, pose) : null;
    }

    static Result Search(Rig rig, Spec spec, HumanPose pose)
    {
        int n = rig.ArmIndices.Length;
        var fromClip = new float[n];
        for (int i = 0; i < n; i++) fromClip[i] = pose.muscles[rig.ArmIndices[i]];

        // Two starts (the base clip's own arm, and the avatar's neutral arm), each solved
        // twice: first for the geometry alone, then with the clearance and floor terms.
        // Starting with the penalties on traps the search behind a volume the blade has
        // to sweep through on its way from the clip's pose to the carry.
        float[] best = null;
        float bestCost = float.PositiveInfinity;
        foreach (var start in new[] { fromClip, new float[n] })
        {
            var m = (float[])start.Clone();
            Descend(rig, spec, ref pose, m, penalties: false);
            float c = Descend(rig, spec, ref pose, m, penalties: true);
            if (c < bestCost) { bestCost = c; best = m; }
        }
        Evaluate(rig, spec, ref pose, best, true, out var result);
        result.Muscles = (float[])best.Clone();
        return result;
    }

    /// <summary>Coordinate descent with a shrinking step: try ±step on each muscle, keep
    /// any improvement, halve the step when a whole sweep finds none.</summary>
    static float Descend(Rig rig, Spec spec, ref HumanPose pose, float[] m, bool penalties)
    {
        float best = Evaluate(rig, spec, ref pose, m, penalties, out _);
        for (float step = 0.25f; step > 0.001f;)
        {
            bool improved = false;
            for (int i = 0; i < m.Length; i++)
            {
                for (int s = 0; s < 2; s++)
                {
                    float old = m[i];
                    m[i] = Mathf.Clamp(old + (s == 0 ? step : -step), -1f, 1f);
                    float c = Evaluate(rig, spec, ref pose, m, penalties, out _);
                    if (c < best - 1e-5f) { best = c; improved = true; }
                    else m[i] = old;
                }
            }
            if (!improved) step *= 0.5f;
        }
        return best;
    }

    /// <summary>Applies the arm muscles and scores the pose. Lower is better.</summary>
    static float Evaluate(Rig rig, Spec spec, ref HumanPose pose, float[] arm, bool penalties, out Result r)
    {
        for (int i = 0; i < arm.Length; i++) pose.muscles[rig.ArmIndices[i]] = arm[i];
        rig.Apply(ref pose);
        r = Measure(rig);

        var root = rig.Root.transform;
        Vector3 bladeWant = root.TransformDirection(spec.Blade.normalized);
        Vector3 edgeWant = Vector3.ProjectOnPlane(root.TransformDirection(spec.Edge), bladeWant).normalized;
        Vector3 fistWant = root.TransformPoint(spec.Fist);

        Vector3 blade = (rig.BladeTip.position - rig.BladeBase.position).normalized;
        Vector3 edge = rig.BladeBase.forward;
        r.BladeError = Vector3.Angle(blade, bladeWant);
        r.EdgeError = Vector3.Angle(Vector3.ProjectOnPlane(edge, blade), edgeWant);
        r.FistError = Vector3.Distance(rig.BladeBase.position, fistWant);
        r.Fist = root.InverseTransformPoint(rig.BladeBase.position);
        r.Blade = root.InverseTransformDirection(blade);

        float cost = r.BladeError + 0.35f * r.EdgeError + 100f * r.FistError;
        // Soft wrist limit: past 0.6 of its range a wrist bend reads as broken.
        for (int i = 7; i < arm.Length; i++)
            cost += 40f * Mathf.Max(0f, Mathf.Abs(arm[i]) - 0.6f);
        if (penalties)
        {
            float floor = spec.FloorMargin - r.TipHeight;
            if (floor > 0f) cost += 1000f * floor;
            float gap = r.WorstGap - spec.ExtraClearance;
            if (gap < 0f) cost += 1000f * -gap;
        }
        r.Cost = cost;
        return cost;
    }

    /// <summary>Clearance and floor numbers for the rig's current pose.</summary>
    public static Result Measure(Rig rig)
    {
        var r = new Result { WorstGap = float.PositiveInfinity, WorstVolume = "none" };
        Vector3 a = rig.BladeBase.position, b = rig.BladeTip.position;
        if (rig.Profile != null)
        {
            foreach (var v in rig.Profile.Volumes)
            {
                var bone = rig.Animator.GetBoneTransform(v.bone);
                if (bone == null) continue;
                Vector3 c = bone.TransformPoint(v.centre);
                float gap = DistanceToSegment(c, a, b) - (v.radius + rig.Profile.BladeRadius + rig.Profile.Margin);
                if (gap < r.WorstGap) { r.WorstGap = gap; r.WorstVolume = v.bone.ToString(); }
            }
        }
        float width = rig.Profile != null ? rig.Profile.BladeRadius : 0.05f;
        r.TipHeight = Mathf.Min(a.y, b.y) - width - rig.FloorY;
        return r;
    }

    static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
        return Vector3.Distance(p, a + ab * t);
    }

    // ----------------------------------------------------------------- clip

    /// <summary>
    /// Writes <paramref name="pose"/> as a looping two-key Humanoid clip at
    /// <paramref name="path"/>, in place if it exists (its GUID, and so every reference
    /// to it, is kept). Curves that already hold the same values are left alone.
    /// </summary>
    static AnimationClip WriteClip(string path, HumanPose pose)
    {
        ChibiSandboxSetup.EnsureFolderPublic(Path.GetDirectoryName(path)!.Replace('\\', '/'));
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        bool created = clip == null;
        if (created) clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(path) };

        const float end = 1f / 30f;
        void Curve(string property, float value)
        {
            var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), property);
            var curve = new AnimationCurve(new Keyframe(0f, value), new Keyframe(end, value));
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        var names = HumanTrait.MuscleName;
        for (int i = 0; i < names.Length; i++) Curve(MuscleProperty(names[i]), Round(pose.muscles[i]));
        Curve("RootT.x", Round(pose.bodyPosition.x));
        Curve("RootT.y", Round(pose.bodyPosition.y));
        Curve("RootT.z", Round(pose.bodyPosition.z));
        Curve("RootQ.x", Round(pose.bodyRotation.x));
        Curve("RootQ.y", Round(pose.bodyRotation.y));
        Curve("RootQ.z", Round(pose.bodyRotation.z));
        Curve("RootQ.w", Round(pose.bodyRotation.w));

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        settings.keepOriginalOrientation = true;
        settings.keepOriginalPositionY = true;
        settings.keepOriginalPositionXZ = true;
        settings.loopBlendOrientation = true;
        settings.loopBlendPositionY = true;
        settings.loopBlendPositionXZ = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        if (created) AssetDatabase.CreateAsset(clip, path);
        else EditorUtility.SetDirty(clip);
        return clip;
    }

    /// <summary>Five decimals: enough for a pose, and keeps re-runs byte-identical.</summary>
    static float Round(float v) => Mathf.Round(v * 100000f) / 100000f;

    /// <summary>
    /// The clip property for a <see cref="HumanTrait.MuscleName"/> entry. Finger muscles
    /// are named differently in clips: "Right Index 1 Stretched" is
    /// "RightHand.Index.1 Stretched", "Right Thumb Spread" is "RightHand.Thumb.Spread".
    /// </summary>
    public static string MuscleProperty(string muscle)
    {
        foreach (var side in new[] { "Left", "Right" })
            foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
            {
                string prefix = side + " " + finger + " ";
                if (muscle.StartsWith(prefix)) return $"{side}Hand.{finger}.{muscle.Substring(prefix.Length)}";
            }
        return muscle;
    }

    /// <summary>The nine arm muscles stored in <paramref name="clip"/>, in search order.</summary>
    public static float[] ArmMusclesOf(AnimationClip clip)
    {
        var values = new float[ArmMuscles.Length];
        for (int i = 0; i < ArmMuscles.Length; i++)
        {
            var curve = AnimationUtility.GetEditorCurve(clip,
                EditorCurveBinding.FloatCurve("", typeof(Animator), MuscleProperty(ArmMuscles[i])));
            values[i] = curve != null ? curve.Evaluate(0f) : 0f;
        }
        return values;
    }

    // ------------------------------------------------------------ previews

    /// <summary>One cell of a contact sheet: a base clip at a time, with a carry arm laid
    /// over it or not.</summary>
    public struct Cell
    {
        public string Label;
        public AnimationClip Base;
        public float Time;
        /// <summary>The nine arm muscles to lay over the base pose; null shows the clip's own arm.</summary>
        public float[] Arm;
    }

    /// <summary>
    /// Renders <paramref name="cells"/> in columns, one row per view: the game camera
    /// (behind and above, as SandboxOrbitCamera frames him), his right side, and his
    /// front-right three-quarter. Each cell's arm muscles, if any, are laid over its base
    /// pose. Writes a PNG and returns a clearance report, one line per cell.
    /// </summary>
    public static string RenderSheet(Spec spec, IList<Cell> cells, string png, int cellSize = 360)
    {
        var report = new StringBuilder();
        using var rig = CreateRig(spec.HeldPrefab, spec.ClearanceProfile);
        if (rig == null) return "no rig";

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        rig.Made.Add(floor);
        floor.transform.position = new Vector3(0f, rig.FloorY, 0f);
        floor.transform.localScale = Vector3.one * 0.6f;
        var floorMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.78f, 0.82f, 0.76f) };
        rig.Made.Add(floorMat);
        floor.GetComponent<Renderer>().sharedMaterial = floorMat;
        var lineMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.5f, 0.55f, 0.5f) };
        rig.Made.Add(lineMat);
        for (int g = -3; g <= 3; g++)
        {
            rig.Made.Add(Line(new Vector3(g * 0.5f, rig.FloorY + 0.002f, 0f), new Vector3(0.01f, 0.001f, 3f), lineMat));
            rig.Made.Add(Line(new Vector3(0f, rig.FloorY + 0.002f, g * 0.5f), new Vector3(3f, 0.001f, 0.01f), lineMat));
        }

        var lightGo = new GameObject("CarryPreviewLight") { hideFlags = HideFlags.DontSave };
        rig.Made.Add(lightGo);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var camGo = new GameObject("CarryPreviewCam") { hideFlags = HideFlags.DontSave };
        rig.Made.Add(camGo);
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.65f, 0.78f);
        var rt = new RenderTexture(cellSize, cellSize, 24);
        rig.Made.Add(rt);
        cam.targetTexture = rt;

        // The game camera: SandboxOrbitCamera's default framing, 4 m out at a 16° pitch,
        // aimed 1.35 m up, from behind and a little to his right.
        Vector3 target = new(0f, 1.35f + rig.FloorY, 0f);
        Vector3 gameDir = Quaternion.Euler(16f, -12f, 0f) * Vector3.back;
        var views = new[]
        {
            (pos: target + gameDir * 4f, look: target, fov: 50f),
            (pos: new Vector3(3.6f, 0.9f + rig.FloorY, -0.2f), look: new Vector3(0f, 0.65f + rig.FloorY, -0.2f), fov: 38f),
            (pos: new Vector3(2.2f, 1.6f + rig.FloorY, 3.0f), look: new Vector3(0f, 0.7f + rig.FloorY, 0f), fov: 38f),
        };

        var sheet = new Texture2D(cellSize * cells.Count, cellSize * views.Length, TextureFormat.RGB24, false);
        rig.Made.Add(sheet);
        // Skinned meshes don't re-skin between renders in edit mode, so each cell bakes
        // them into stand-in meshes that are drawn instead.
        var skins = rig.Root.GetComponentsInChildren<SkinnedMeshRenderer>(false);
        var baked = new (SkinnedMeshRenderer skin, Transform t, Mesh mesh)[skins.Length];
        for (int i = 0; i < skins.Length; i++)
        {
            var go = new GameObject("Baked_" + skins[i].name) { hideFlags = HideFlags.DontSave };
            rig.Made.Add(go);
            var mesh = new Mesh();
            rig.Made.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = skins[i].sharedMaterials;
            baked[i] = (skins[i], go.transform, mesh);
            skins[i].enabled = false;
        }

        cam.Render();   // the first render after the camera is made comes out blank
        var pose = new HumanPose();
        for (int c = 0; c < cells.Count; c++)
        {
            var cell = cells[c];
            rig.SampleBase(cell.Base, cell.Time);
            if (cell.Arm != null)
            {
                rig.Handler.GetHumanPose(ref pose);
                for (int i = 0; i < rig.ArmIndices.Length; i++) pose.muscles[rig.ArmIndices[i]] = cell.Arm[i];
                rig.Apply(ref pose);
            }
            foreach (var (skin, t, mesh) in baked)
            {
                skin.BakeMesh(mesh, true);
                t.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
            }
            var m = Measure(rig);
            report.AppendLine($"{cell.Label}: worst gap {m.WorstGap * 100f:F1} cm ({m.WorstVolume}), " +
                              $"lowest blade point {m.TipHeight * 100f:F1} cm");
            for (int v = 0; v < views.Length; v++)
            {
                cam.fieldOfView = views[v].fov;
                cam.transform.position = views[v].pos;
                cam.transform.LookAt(views[v].look);
                cam.Render();
                RenderTexture.active = rt;
                sheet.ReadPixels(new Rect(0, 0, cellSize, cellSize), c * cellSize, (views.Length - 1 - v) * cellSize);
                RenderTexture.active = null;
            }
        }
        sheet.Apply();
        cam.targetTexture = null;
        Directory.CreateDirectory(Path.GetDirectoryName(png)!);
        File.WriteAllBytes(png, sheet.EncodeToPNG());
        return report.ToString();
    }

    static GameObject Line(Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.hideFlags = HideFlags.DontSave;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.position = pos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }
}
