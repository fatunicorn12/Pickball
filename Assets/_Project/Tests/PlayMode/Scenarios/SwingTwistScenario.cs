using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Combat;
using Pickleball.Sandbox.Weapon;
using Pickleball.Tests.PlayMode.Harness;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Pickleball.Tests.PlayMode.Scenarios
{
    /// <summary>
    /// The swing-3 contortion experiment. Standing still on open floor, it chains ground
    /// swings 1-2-3 under four conditions:
    ///  - <c>SwingTwist.strength</c> at 1, then 0, then 0.5;
    ///  - strength 1 again, with Step_Slash3's lunge set to 0.
    ///
    /// Every frame, after every LateUpdate, it records:
    ///  - SwingTwist and WeaponClearance <c>LastCorrection</c>;
    ///  - the waist: the shoulder line's yaw against the hip line's, and the neck's forward and
    ///    sideways bend in the pelvis frame;
    ///  - the right hand's distance to the head ("swatting flies" puts the hand at the face);
    ///  - planar foot slide while a foot is down, and root travel.
    ///
    /// It photographs swing 3 from the gameplay camera at n ≈ 0.5, and from the front-right,
    /// with the blade trail hidden, at n ≈ 0.35, 0.5 and 0.65. At strength 1 it also
    /// photographs swings 1 and 2 at n ≈ 0.5.
    ///
    /// Strength and lunge are set through reflection on the live objects and restored in a
    /// finally block. Nothing is marked dirty or saved.
    ///
    /// Outputs, next to the usual telemetry, in the scenario folder:
    ///  - <c>swing3-frames.csv</c>, one row per frame;
    ///  - <c>swing3-table.md</c>, the per-swing summary.
    /// </summary>
    [Category("Playtest")]
    [Explicit("Playtest harness: run on purpose, never in the normal suite.")]
    public class SwingTwistScenario
    {
        [UnityTest, Timeout(900000)]
        public IEnumerator Swing3Contortion() => UniTask.ToCoroutine(Run);

        const string Scenario = "Swing3Contortion";

        // Open floor 9 m short of the dummies, facing them: nothing to hit, so no hitstop
        // changes the timing between conditions.
        static readonly Vector3 Stand = new(1.5f, 0.05f, 0.8f);
        static readonly Vector3 Facing = new(1.5f, 0f, 10f);

        const string Slash3Path = "Assets/_Project/ScriptableObjects/Combat/Steps/Step_Slash3.asset";

        /// <summary>Above this, a foot counts as lifted (stepping), not sliding (m).</summary>
        const float LiftThreshold = 0.02f;

        const int Bins = 20;

        readonly struct Condition
        {
            public readonly string Label;
            public readonly float Strength;
            public readonly bool NoLunge;
            public Condition(string label, float strength, bool noLunge)
            {
                Label = label; Strength = strength; NoLunge = noLunge;
            }
        }

        sealed class Row
        {
            public string cond;
            public int frame, step;
            public float n, twistCorr, clearance, torsoYaw, hipYaw, twist, fwdBend, sideBend,
                         handHead, lSlide, rSlide, lLift, rLift, rootMove;
            public bool attacking;
        }

        /// <summary>The bones the measurements read, and the measurements.</summary>
        sealed class Rig
        {
            public Animator Anim;
            public Transform Hips, Spine, Neck, Head, LUpperArm, RUpperArm, LUpperLeg, RUpperLeg,
                             LFoot, RFoot, RHand;

            public static Rig Find(GameObject player)
            {
                var anim = player.GetComponentInChildren<Animator>();
                if (anim == null || !anim.isHuman) return null;
                Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
                return new Rig
                {
                    Anim = anim,
                    Hips = B(HumanBodyBones.Hips), Spine = B(HumanBodyBones.Spine),
                    Neck = B(HumanBodyBones.Neck), Head = B(HumanBodyBones.Head),
                    LUpperArm = B(HumanBodyBones.LeftUpperArm), RUpperArm = B(HumanBodyBones.RightUpperArm),
                    LUpperLeg = B(HumanBodyBones.LeftUpperLeg), RUpperLeg = B(HumanBodyBones.RightUpperLeg),
                    LFoot = B(HumanBodyBones.LeftFoot), RFoot = B(HumanBodyBones.RightFoot),
                    RHand = B(HumanBodyBones.RightHand),
                };
            }

            /// <summary>Yaw of a world direction in the Animator's frame, measured from its +X (°).</summary>
            float Yaw(Vector3 v)
            {
                Vector3 l = Anim.transform.InverseTransformDirection(v);
                l.y = 0f;
                return Vector3.SignedAngle(Vector3.right, l, Vector3.up);
            }

            public void Measure(Row r)
            {
                r.torsoYaw = Yaw(RUpperArm.position - LUpperArm.position);
                r.hipYaw = Yaw(RUpperLeg.position - LUpperLeg.position);
                r.twist = Mathf.DeltaAngle(r.hipYaw, r.torsoYaw);

                Vector3 mid = (LUpperLeg.position + RUpperLeg.position) * 0.5f;
                Vector3 right = (RUpperLeg.position - LUpperLeg.position).normalized;
                Vector3 up = (Spine.position - mid).normalized;
                Vector3 fwd = Vector3.Cross(right, up).normalized;
                right = Vector3.Cross(up, fwd);
                Vector3 t = (Neck.position - Spine.position).normalized;
                r.fwdBend = Mathf.Asin(Mathf.Clamp(Vector3.Dot(t, fwd), -1f, 1f)) * Mathf.Rad2Deg;
                r.sideBend = Mathf.Asin(Mathf.Clamp(Vector3.Dot(t, right), -1f, 1f)) * Mathf.Rad2Deg;
                r.handHead = Vector3.Distance(RHand.position, Head.position);
            }
        }

        static async UniTask Click(PlaytestSession s, int count, float interval)
        {
            for (int i = 0; i < count; i++)
            {
                await s.Click(MouseButton.Left, 0.05f);
                await s.Seconds(Mathf.Max(0f, interval - 0.05f));
            }
        }

        static async UniTask Run()
        {
            using var s = await PlaytestSession.Begin(Scenario, PlaytestScenes.TrainingStage);
            if (s.A.Melee == null) { s.CannotDrive("no MeleeCombat on the player"); s.Complete(); return; }

            var player = s.A.Player.gameObject;
            var twist = player.GetComponent<SwingTwist>();
            var clearance = player.GetComponentInChildren<WeaponClearance>(true);
            var rig = Rig.Find(player);
            if (twist == null) { s.CannotDrive("no SwingTwist on the player"); s.Complete(); return; }
            if (rig == null) { s.CannotDrive("no humanoid Animator on the player"); s.Complete(); return; }
            if (clearance == null) s.CannotDrive("no WeaponClearance on the player: clearance column is 0");

            const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
            var strengthField = typeof(SwingTwist).GetField("strength", Private);
            var lungeField = typeof(ComboStepDefinition).GetField("lungeSpeed", Private);
            ComboStepDefinition slash3 = null;
#if UNITY_EDITOR
            slash3 = UnityEditor.AssetDatabase.LoadAssetAtPath<ComboStepDefinition>(Slash3Path);
#endif
            if (strengthField == null) { s.CannotDrive("SwingTwist.strength not found"); s.Complete(); return; }
            if (slash3 == null || lungeField == null) s.CannotDrive("Step_Slash3 lunge cannot be zeroed: lunge-0 condition skipped");

            float strengthWas = (float)strengthField.GetValue(twist);
            float lungeWas = slash3 != null && lungeField != null ? (float)lungeField.GetValue(slash3) : 0f;
            s.Note($"SET-UP: SwingTwist.strength was {strengthWas:0.##}; Step_Slash3 lungeSpeed was {lungeWas:0.##} m/s. " +
                   "Both are changed in memory through reflection for this run and restored at the end.");

            var conditions = new List<Condition>
            {
                new("strength-1", 1f, false),
                new("strength-0", 0f, false),
                new("strength-0.5", 0.5f, false),
            };
            if (slash3 != null && lungeField != null) conditions.Add(new("strength-1-lunge0", 1f, true));

            var rows = new List<Row>(4096);
            string dir = PlaytestOutput.ScenarioDir(Scenario);
            bool alive = true, sampling = false;
            string current = "";
            Vector3 lastL = default, lastR = default, lastRoot = default;
            float idleLY = 0f, idleRY = 0f;
            bool primed = false;

            // Samples after every LateUpdate (SwingTwist -100, WeaponClearance -50, cloth,
            // trail), so the numbers are the pose that renders.
            async UniTaskVoid Sampler()
            {
                while (alive)
                {
                    await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
                    if (!alive || !sampling) { primed = false; continue; }
                    Vector3 l = rig.LFoot.position, r = rig.RFoot.position, root = player.transform.position;
                    if (!primed)
                    {
                        lastL = l; lastR = r; lastRoot = root;
                        primed = true;
                    }
                    var cur = s.A.Melee.Current;
                    var row = new Row
                    {
                        cond = current,
                        frame = Time.frameCount,
                        step = cur.IsValid ? cur.Index : -1,
                        n = s.A.Melee.SwingProgress,
                        attacking = s.A.Melee.IsAttacking,
                        twistCorr = twist.LastCorrection,
                        clearance = clearance != null ? clearance.LastCorrection : 0f,
                        lLift = l.y - root.y - idleLY,
                        rLift = r.y - root.y - idleRY,
                        rootMove = Planar(root - lastRoot),
                    };
                    // A foot that moves while it is down is sliding; one that is lifted is stepping.
                    // A planted foot should not move in the world at all, so its world travel is
                    // the slide (the root's lunge is reported separately).
                    row.lSlide = row.lLift < LiftThreshold ? Planar(l - lastL) : 0f;
                    row.rSlide = row.rLift < LiftThreshold ? Planar(r - lastR) : 0f;
                    rig.Measure(row);
                    rows.Add(row);
                    lastL = l; lastR = r; lastRoot = root;
                }
            }
            Sampler().Forget();

            try
            {
                foreach (var c in conditions)
                {
                    strengthField.SetValue(twist, c.Strength);
                    if (slash3 != null && lungeField != null) lungeField.SetValue(slash3, c.NoLunge ? 0f : lungeWas);

                    await s.Teleport(Stand, Facing);
                    await s.Seconds(0.5f);
                    // Idle foot heights above the root, so lift is measured from the stance.
                    await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
                    Vector3 rootNow = player.transform.position;
                    idleLY = rig.LFoot.position.y - rootNow.y;
                    idleRY = rig.RFoot.position.y - rootNow.y;

                    current = c.Label;
                    sampling = true;
                    s.Log($"{c.Label}: idle 10 frames, then 3 clicks 0.30 s apart");
                    await s.Frames(10);

                    var clicks = Click(s, 3, 0.30f);
                    // (swing index, n to wait for, also take the gameplay-camera shot). Swing 3 gets a
                    // three-frame filmstrip from the side; the gameplay shot is at n ≈ 0.5.
                    var wanted = new List<(int step, float n, bool main)>
                    {
                        (2, 0.35f, false), (2, 0.5f, true), (2, 0.65f, false),
                    };
                    if (c.Label == "strength-1") { wanted.Insert(0, (1, 0.5f, true)); wanted.Insert(0, (0, 0.5f, true)); }
                    for (int f = 0; f < 240 && wanted.Count > 0; f++)
                    {
                        await UniTask.Yield();
                        var cur = s.A.Melee.Current;
                        if (!cur.IsValid) continue;
                        var next = wanted[0];
                        if (cur.Index > next.step)
                        {
                            s.CannotDrive($"{c.Label}: chained past swing {next.step + 1} before n {next.n:0.00}");
                            wanted.RemoveAt(0);
                            continue;
                        }
                        if (cur.Index != next.step || s.A.Melee.SwingProgress < next.n) continue;
                        wanted.RemoveAt(0);
                        string tag = $"{c.Label}-swing{cur.Index + 1}-n{s.A.Melee.SwingProgress:0.00}";
                        if (next.main) await s.Shot(tag);
                        else await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
                        SideShot(s, rig, dir, tag + "-side");
                    }
                    await clicks;
                    foreach (var w in wanted)
                        s.CannotDrive($"{c.Label}: never photographed swing {w.step + 1} at n >= {w.n:0.00}");

                    // Let the chain finish and the Attack layer fade out.
                    for (int f = 0; f < 240 && s.A.Melee.IsAttacking; f++) await UniTask.Yield();
                    await s.Seconds(0.6f);
                    sampling = false;
                    await s.Frames(2);
                }
            }
            finally
            {
                alive = false;
                strengthField.SetValue(twist, strengthWas);
                if (slash3 != null && lungeField != null) lungeField.SetValue(slash3, lungeWas);
                s.Note($"RESTORED: SwingTwist.strength {strengthWas:0.##}, Step_Slash3 lungeSpeed {lungeWas:0.##}.");
            }

            WriteFrames(rows, Path.Combine(dir, "swing3-frames.csv"));
            string table = Summarise(rows, conditions);
            File.WriteAllText(Path.Combine(dir, "swing3-table.md"), table);
            foreach (string line in table.Split('\n'))
                if (line.StartsWith("|")) s.Note(line.TrimEnd());
            s.Complete();
        }

        static float Planar(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        /// <summary>Renders a temporary camera at the player's front-right to a PNG, with the blade
        /// trail hidden so it does not cover the body. Runtime-only objects, destroyed or
        /// re-enabled straight after.</summary>
        static void SideShot(PlaytestSession s, Rig rig, string dir, string label)
        {
            var go = new GameObject("Swing3SideCamera");
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prevActive = RenderTexture.active;
            Texture2D tex = null;
            var hidden = new List<Renderer>();
            foreach (var trail in Object.FindObjectsByType<BladeTrail>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                foreach (var r in trail.GetComponentsInChildren<Renderer>())
                    if (r.enabled) { r.enabled = false; hidden.Add(r); }
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.fieldOfView = 35f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 200f;
                if (s.A.Camera != null)
                {
                    cam.clearFlags = s.A.Camera.clearFlags;
                    cam.backgroundColor = s.A.Camera.backgroundColor;
                    cam.cullingMask = s.A.Camera.cullingMask;
                }
                Transform p = s.A.Player.transform;
                Vector3 chest = rig.Hips.position + Vector3.up * 0.3f;
                cam.transform.position = chest + p.right * 2.2f + p.forward * 1.8f + Vector3.up * 0.4f;
                cam.transform.LookAt(chest);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(1280, 720, TextureFormat.RGB24, false, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply(false);
                string file = $"side-{Sanitize(label)}.png";
                File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());
                s.Rec.Mark("screenshot", file);
                cam.targetTexture = null;
            }
            catch (Exception e)
            {
                s.CannotDrive($"side shot '{label}' failed: {e.Message}");
            }
            finally
            {
                foreach (var r in hidden) if (r != null) r.enabled = true;
                RenderTexture.active = prevActive;
                if (tex != null) Object.Destroy(tex);
                rt.Release();
                Object.Destroy(rt);
                Object.Destroy(go);
            }
        }

        static string Sanitize(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s) sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.' ? ch : '_');
            return sb.ToString();
        }

        static void WriteFrames(List<Row> rows, string path)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("cond,frame,step,n,attacking,twistCorr,clearance,torsoYaw,hipYaw,twist,fwdBend,sideBend,handHead,lSlide,rSlide,lLift,rLift,rootMove");
            foreach (var r in rows)
                sb.AppendLine(string.Join(",", r.cond, r.frame.ToString(ci), r.step.ToString(ci),
                    r.n.ToString("0.000", ci), r.attacking ? "1" : "0",
                    r.twistCorr.ToString("0.0", ci), r.clearance.ToString("0.0", ci),
                    r.torsoYaw.ToString("0.0", ci), r.hipYaw.ToString("0.0", ci), r.twist.ToString("0.0", ci),
                    r.fwdBend.ToString("0.0", ci), r.sideBend.ToString("0.0", ci), r.handHead.ToString("0.000", ci),
                    r.lSlide.ToString("0.0000", ci), r.rSlide.ToString("0.0000", ci),
                    r.lLift.ToString("0.000", ci), r.rLift.ToString("0.000", ci), r.rootMove.ToString("0.0000", ci)));
            File.WriteAllText(path, sb.ToString());
        }

        sealed class Stat
        {
            public int frames, stepping, wrenched;
            public float maxCorr, maxClear, maxTwist, maxTwistFromIdle, maxSide, maxFwd, minHandHead = float.MaxValue,
                         slide, maxSlideFrame, root;
            public readonly Vector2[] yawBins = new Vector2[Bins];
        }

        /// <summary>Per condition, per swing: the peaks and totals, plus how far the chest's facing
        /// strays from the strength-1 run at the same point of the swing (mean |Δ yaw| over 20 bins of n).</summary>
        static string Summarise(List<Row> rows, List<Condition> conditions)
        {
            var stats = new Dictionary<(string, int), Stat>();
            var idleTwist = new Dictionary<string, float>();
            foreach (var r in rows)
                if (!r.attacking && !idleTwist.ContainsKey(r.cond)) idleTwist[r.cond] = r.twist;

            foreach (var r in rows)
            {
                if (!r.attacking || r.step < 0 || r.step > 2) continue;
                var key = (r.cond, r.step);
                if (!stats.TryGetValue(key, out var st)) stats[key] = st = new Stat();
                st.frames++;
                if (r.lLift > 0.01f || r.rLift > 0.01f) st.stepping++;
                if (Mathf.Abs(r.twist) > 100f && Mathf.Abs(r.sideBend) > 30f) st.wrenched++;
                float idle = idleTwist.TryGetValue(r.cond, out var it) ? it : 0f;
                st.maxCorr = Mathf.Max(st.maxCorr, r.twistCorr);
                st.maxClear = Mathf.Max(st.maxClear, r.clearance);
                st.maxTwist = Mathf.Max(st.maxTwist, Mathf.Abs(r.twist));
                st.maxTwistFromIdle = Mathf.Max(st.maxTwistFromIdle, Mathf.Abs(Mathf.DeltaAngle(idle, r.twist)));
                st.maxSide = Mathf.Max(st.maxSide, Mathf.Abs(r.sideBend));
                st.maxFwd = Mathf.Max(st.maxFwd, Mathf.Abs(r.fwdBend));
                st.minHandHead = Mathf.Min(st.minHandHead, r.handHead);
                float slide = r.lSlide + r.rSlide;
                st.slide += slide;
                st.maxSlideFrame = Mathf.Max(st.maxSlideFrame, Mathf.Max(r.lSlide, r.rSlide));
                st.root += r.rootMove;
                int bin = Mathf.Clamp((int)(r.n * Bins), 0, Bins - 1);
                float a = r.torsoYaw * Mathf.Deg2Rad;
                st.yawBins[bin] += new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }

            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("# Swing 3 contortion: standing 1-2-3 chain");
            sb.AppendLine();
            sb.AppendLine("Peaks over each swing (attacking frames only; swings 1 and 2 are cut short by the chain). " +
                          "Twist = shoulder-line yaw minus hip-line yaw; Δidle = largest change from the idle stance's " +
                          "twist. Bends are the neck-over-spine lean in the pelvis frame. Wrenched = frames with " +
                          "|twist| > 100° AND |side bend| > 30° at once. Hand-head is the right hand's closest approach " +
                          "to the head bone. Foot travel = world travel of a foot while it is within 2 cm of its stance " +
                          "height (both feet, summed): mostly the start and end of re-plant steps. Stepping = frames with " +
                          "a foot lifted > 1 cm. Chest Δ = mean |chest yaw − strength-1's| at the same n.");
            sb.AppendLine();
            sb.AppendLine("| Condition | Swing | SwingTwist max ° | Twist max ° (Δidle) | Side bend ° | Fwd bend ° | Wrenched frames | Hand-head min m | Clearance max ° | Foot travel down cm (max/frame) | Stepping frames | Root travel cm | Chest Δ vs s=1 ° |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var c in conditions)
                for (int step = 0; step < 3; step++)
                {
                    if (!stats.TryGetValue((c.Label, step), out var st)) { sb.AppendLine($"| {c.Label} | {step + 1} | (not reached) |||||||||||"); continue; }
                    string chest = "–";
                    if (c.Label != "strength-1" && stats.TryGetValue(("strength-1", step), out var baseSt))
                    {
                        float sum = 0f; int n = 0;
                        for (int b = 0; b < Bins; b++)
                        {
                            if (st.yawBins[b].sqrMagnitude < 1e-6f || baseSt.yawBins[b].sqrMagnitude < 1e-6f) continue;
                            float y1 = Mathf.Atan2(st.yawBins[b].y, st.yawBins[b].x) * Mathf.Rad2Deg;
                            float y0 = Mathf.Atan2(baseSt.yawBins[b].y, baseSt.yawBins[b].x) * Mathf.Rad2Deg;
                            sum += Mathf.Abs(Mathf.DeltaAngle(y0, y1));
                            n++;
                        }
                        if (n > 0) chest = (sum / n).ToString("0", ci);
                    }
                    sb.AppendLine(string.Format(ci,
                        "| {0} | {1} | {2:0} | {3:0} ({4:0}) | {5:0} | {6:0} | {7} / {8} | {9:0.00} | {10:0} | {11:0.0} ({12:0.0}) | {13} | {14:0.0} | {15} |",
                        c.Label, step + 1, st.maxCorr, st.maxTwist, st.maxTwistFromIdle, st.maxSide, st.maxFwd,
                        st.wrenched, st.frames, st.minHandHead, st.maxClear, st.slide * 100f, st.maxSlideFrame * 100f,
                        st.stepping, st.root * 100f, chest));
                }
            return sb.ToString();
        }
    }
}
