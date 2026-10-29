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
using Pickleball.Sandbox.Combat.Presentation;
using Pickleball.Sandbox.Weapon;
using Pickleball.Tests.PlayMode.Harness;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Pickleball.Tests.PlayMode.Scenarios
{
    /// <summary>
    /// The swing-3 contortion experiment, before and after the full-body fix. On open
    /// floor it chains ground swings, each run with Step_Slash3's Full Body When Standing
    /// flag set either way:
    ///  - <b>masked-standing</b>, flag off: 1-2-3 standing still. The old behaviour.
    ///  - <b>fullbody-standing</b>, flag on: 1-2-3 standing still. Swing 3 should play
    ///    full-body.
    ///  - <b>fullbody-walking</b>, flag on: 1-2-3 while walking. Swing 3 should stay masked.
    ///  - <b>masked-move-mid3</b> and <b>fullbody-move-mid3</b>: standing, then walking
    ///    from swing 3 at n ≈ 0.3. With the flag on, the legs should hand back to
    ///    locomotion. The masked run is the baseline for the feet starting to walk.
    ///  - <b>masked-chain4</b> and <b>fullbody-chain4</b>: 1-2-3-4 standing, to look for
    ///    pops in and out of swing 3.
    ///
    /// Every frame, after every LateUpdate, it records:
    ///  - SwingTwist and WeaponClearance <c>LastCorrection</c>, and the full-body layer
    ///    weight;
    ///  - the waist: the shoulder line's yaw against the hip line's, and the neck's
    ///    forward and sideways bend in the pelvis frame;
    ///  - foot travel while a foot is down, steps, and root travel;
    ///  - per-frame jumps of the chest, hips and feet. A pop is a jump far above its
    ///    neighbours.
    ///
    /// It photographs swing 3 from the gameplay camera at n ≈ 0.5, and from the
    /// front-right, with the blade trail hidden, at n ≈ 0.35, 0.5 and 0.65.
    ///
    /// The flag is set by reflection on the live asset and restored in a finally block.
    /// Nothing is marked dirty or saved.
    ///
    /// Outputs, next to the usual telemetry, in the scenario folder:
    ///  - <c>swing3-frames.csv</c>, one row per frame;
    ///  - <c>swing3-table.md</c>, the per-swing summary and the pop check.
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

        /// <summary>Stick deflection for "walking".</summary>
        const float WalkStick = 0.5f;

        enum Walk { None, Throughout, FromSwing3 }

        readonly struct Condition
        {
            public readonly string Label;
            public readonly bool FullBody;
            public readonly Walk Walk;
            public readonly int Clicks;
            public Condition(string label, bool fullBody, Walk walk, int clicks)
            {
                Label = label; FullBody = fullBody; Walk = walk; Clicks = clicks;
            }
        }

        sealed class Row
        {
            public string cond;
            public int frame, step;
            public float n, bodyW, twistCorr, clearance, torsoYaw, hipYaw, twist, fwdBend, sideBend,
                         lSlide, rSlide, lLift, rLift, rootMove, chestStep, hipStep, footJump, speed;
            public bool attacking;
        }

        /// <summary>The bones the measurements read, and the measurements.</summary>
        sealed class Rig
        {
            public Animator Anim;
            public Transform Hips, Spine, Neck, Chest, LUpperArm, RUpperArm, LUpperLeg, RUpperLeg, LFoot, RFoot;

            public static Rig Find(GameObject player)
            {
                var anim = player.GetComponentInChildren<Animator>();
                if (anim == null || !anim.isHuman) return null;
                Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
                return new Rig
                {
                    Anim = anim,
                    Hips = B(HumanBodyBones.Hips), Spine = B(HumanBodyBones.Spine),
                    Neck = B(HumanBodyBones.Neck),
                    Chest = B(HumanBodyBones.UpperChest) != null ? B(HumanBodyBones.UpperChest) : B(HumanBodyBones.Chest),
                    LUpperArm = B(HumanBodyBones.LeftUpperArm), RUpperArm = B(HumanBodyBones.RightUpperArm),
                    LUpperLeg = B(HumanBodyBones.LeftUpperLeg), RUpperLeg = B(HumanBodyBones.RightUpperLeg),
                    LFoot = B(HumanBodyBones.LeftFoot), RFoot = B(HumanBodyBones.RightFoot),
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
            var driver = player.GetComponent<SwordAnimatorDriver>();
            var clearance = player.GetComponentInChildren<WeaponClearance>(true);
            var rig = Rig.Find(player);
            if (twist == null) { s.CannotDrive("no SwingTwist on the player"); s.Complete(); return; }
            if (driver == null) { s.CannotDrive("no SwordAnimatorDriver on the player"); s.Complete(); return; }
            if (rig == null) { s.CannotDrive("no humanoid Animator on the player"); s.Complete(); return; }
            if (clearance == null) s.CannotDrive("no WeaponClearance on the player: clearance column is 0");
            if (rig.Anim.GetLayerIndex("AttackBody") < 0)
                s.CannotDrive("the controller has no AttackBody layer: run Tools ▸ Pickleball ▸ Setup Full-Body Swings");

            const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
            var flagField = typeof(ComboStepDefinition).GetField("fullBodyWhenStanding", Private);
            ComboStepDefinition slash3 = null;
#if UNITY_EDITOR
            slash3 = UnityEditor.AssetDatabase.LoadAssetAtPath<ComboStepDefinition>(Slash3Path);
#endif
            if (slash3 == null || flagField == null) { s.CannotDrive("Step_Slash3 or its full-body flag not found"); s.Complete(); return; }
            bool flagWas = (bool)flagField.GetValue(slash3);
            s.Note($"SET-UP: Step_Slash3.fullBodyWhenStanding was {flagWas}; set per condition in memory and restored at the end.");

            var conditions = new List<Condition>
            {
                new("masked-standing", false, Walk.None, 3),
                new("fullbody-standing", true, Walk.None, 3),
                new("fullbody-walking", true, Walk.Throughout, 3),
                new("masked-move-mid3", false, Walk.FromSwing3, 3),
                new("fullbody-move-mid3", true, Walk.FromSwing3, 3),
                new("masked-chain4", false, Walk.None, 4),
                new("fullbody-chain4", true, Walk.None, 4),
            };

            var rows = new List<Row>(8192);
            string dir = PlaytestOutput.ScenarioDir(Scenario);
            bool alive = true, sampling = false, primed = false;
            string current = "";
            Vector3 lastL = default, lastR = default, lastRoot = default;
            Quaternion lastChest = default, lastHips = default;
            float idleLY = 0f, idleRY = 0f;

            // Samples after every LateUpdate (SwingTwist -100, WeaponClearance -50, cloth,
            // trail), so the numbers are the pose that renders.
            async UniTaskVoid Sampler()
            {
                while (alive)
                {
                    await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
                    if (!alive || !sampling) { primed = false; continue; }
                    Vector3 l = rig.LFoot.position, r = rig.RFoot.position, root = player.transform.position;
                    Quaternion chest = rig.Chest.rotation, hips = rig.Hips.rotation;
                    if (!primed)
                    {
                        lastL = l; lastR = r; lastRoot = root; lastChest = chest; lastHips = hips;
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
                        bodyW = driver.FullBodyWeight,
                        twistCorr = twist.LastCorrection,
                        clearance = clearance != null ? clearance.LastCorrection : 0f,
                        lLift = l.y - root.y - idleLY,
                        rLift = r.y - root.y - idleRY,
                        rootMove = Planar(root - lastRoot),
                        // Body-relative jumps: the root's own travel is not a pop.
                        chestStep = Quaternion.Angle(lastChest, chest),
                        hipStep = Quaternion.Angle(lastHips, hips),
                        footJump = Mathf.Max(((l - root) - (lastL - lastRoot)).magnitude,
                                             ((r - root) - (lastR - lastRoot)).magnitude),
                        speed = s.A.Motor != null ? s.A.Motor.PlanarSpeed : 0f,
                    };
                    // A foot that moves while it is down is sliding; one that is lifted is
                    // stepping. A planted foot should not move in the world at all.
                    row.lSlide = row.lLift < LiftThreshold ? Planar(l - lastL) : 0f;
                    row.rSlide = row.rLift < LiftThreshold ? Planar(r - lastR) : 0f;
                    rig.Measure(row);
                    rows.Add(row);
                    lastL = l; lastR = r; lastRoot = root; lastChest = chest; lastHips = hips;
                }
            }
            Sampler().Forget();

            try
            {
                foreach (var c in conditions)
                {
                    flagField.SetValue(slash3, c.FullBody);

                    await s.Teleport(Stand, Facing);
                    await s.Seconds(0.5f);
                    // Idle foot heights above the root, so lift is measured from the stance.
                    await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
                    Vector3 rootNow = player.transform.position;
                    idleLY = rig.LFoot.position.y - rootNow.y;
                    idleRY = rig.RFoot.position.y - rootNow.y;

                    current = c.Label;
                    sampling = true;
                    s.Log($"{c.Label}: {c.Clicks} clicks 0.30 s apart, walk {c.Walk}");
                    Vector2 stick = WalkStick * s.StickToward(Facing - Stand);
                    if (c.Walk == Walk.Throughout)
                    {
                        s.In.SetLeftStick(stick);
                        await s.Seconds(0.4f);
                    }
                    else await s.Frames(10);

                    var clicks = Click(s, c.Clicks, 0.30f);
                    // (swing index, n to wait for, also take the gameplay-camera shot).
                    bool shots = c.Clicks == 3;
                    var wanted = new List<(int step, float n, bool main)>();
                    if (shots) wanted.AddRange(new[] { (2, 0.35f, false), (2, 0.5f, true), (2, 0.65f, false) });
                    bool walking = c.Walk == Walk.Throughout;
                    for (int f = 0; f < 300; f++)
                    {
                        await UniTask.Yield();
                        var cur = s.A.Melee.Current;
                        if (c.Walk == Walk.FromSwing3 && !walking && cur.IsValid && cur.Index == 2
                            && s.A.Melee.SwingProgress >= 0.3f)
                        {
                            s.In.SetLeftStick(stick);
                            walking = true;
                            s.Log($"{c.Label}: started walking at swing 3 n {s.A.Melee.SwingProgress:0.00}");
                        }
                        if (wanted.Count == 0)
                        {
                            if (!s.A.Melee.IsAttacking && f > 30) break;
                            continue;
                        }
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

                    // Let the chain finish and both swing layers fade out.
                    for (int f = 0; f < 240 && s.A.Melee.IsAttacking; f++) await UniTask.Yield();
                    await s.Seconds(0.6f);
                    s.In.SetLeftStick(Vector2.zero);
                    await s.Seconds(0.3f);
                    sampling = false;
                    await s.Frames(2);
                }
            }
            finally
            {
                alive = false;
                s.In.SetLeftStick(Vector2.zero);
                flagField.SetValue(slash3, flagWas);
                s.Note($"RESTORED: Step_Slash3.fullBodyWhenStanding {flagWas}.");
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
            sb.AppendLine("cond,frame,step,n,attacking,bodyW,speed,twistCorr,clearance,torsoYaw,hipYaw,twist,fwdBend,sideBend,lSlide,rSlide,lLift,rLift,rootMove,chestStep,hipStep,footJump");
            foreach (var r in rows)
                sb.AppendLine(string.Join(",", r.cond, r.frame.ToString(ci), r.step.ToString(ci),
                    r.n.ToString("0.000", ci), r.attacking ? "1" : "0", r.bodyW.ToString("0.000", ci),
                    r.speed.ToString("0.00", ci),
                    r.twistCorr.ToString("0.0", ci), r.clearance.ToString("0.0", ci),
                    r.torsoYaw.ToString("0.0", ci), r.hipYaw.ToString("0.0", ci), r.twist.ToString("0.0", ci),
                    r.fwdBend.ToString("0.0", ci), r.sideBend.ToString("0.0", ci),
                    r.lSlide.ToString("0.0000", ci), r.rSlide.ToString("0.0000", ci),
                    r.lLift.ToString("0.000", ci), r.rLift.ToString("0.000", ci), r.rootMove.ToString("0.0000", ci),
                    r.chestStep.ToString("0.0", ci), r.hipStep.ToString("0.0", ci), r.footJump.ToString("0.0000", ci)));
            File.WriteAllText(path, sb.ToString());
        }

        sealed class Stat
        {
            public int frames, stepping, wrenched;
            public float maxCorr, maxTwist, maxTwistFromIdle, maxSide, maxFwd, slide, root, maxBody;
        }

        sealed class Pops
        {
            public float chest, hips, foot;
            public string chestAt = "", hipsAt = "", footAt = "";
        }

        /// <summary>Per condition, per swing: peaks and totals. Then, per condition over the
        /// whole run (swings and fade-out), the largest per-frame jump of chest, hips and feet.</summary>
        static string Summarise(List<Row> rows, List<Condition> conditions)
        {
            var stats = new Dictionary<(string, int), Stat>();
            var pops = new Dictionary<string, Pops>();
            var idleTwist = new Dictionary<string, float>();
            foreach (var r in rows)
                if (!r.attacking && !idleTwist.ContainsKey(r.cond)) idleTwist[r.cond] = r.twist;

            foreach (var r in rows)
            {
                if (!pops.TryGetValue(r.cond, out var p)) pops[r.cond] = p = new Pops();
                string at = r.attacking ? $"swing {r.step + 1} n {r.n:0.00}" : "between swings";
                if (r.chestStep > p.chest) { p.chest = r.chestStep; p.chestAt = at; }
                if (r.hipStep > p.hips) { p.hips = r.hipStep; p.hipsAt = at; }
                if (r.footJump > p.foot) { p.foot = r.footJump; p.footAt = at; }

                if (!r.attacking || r.step < 0) continue;
                var key = (r.cond, r.step);
                if (!stats.TryGetValue(key, out var st)) stats[key] = st = new Stat();
                st.frames++;
                if (r.lLift > 0.01f || r.rLift > 0.01f) st.stepping++;
                if (Mathf.Abs(r.twist) > 100f && Mathf.Abs(r.sideBend) > 30f) st.wrenched++;
                float idle = idleTwist.TryGetValue(r.cond, out var it) ? it : 0f;
                st.maxCorr = Mathf.Max(st.maxCorr, r.twistCorr);
                st.maxTwist = Mathf.Max(st.maxTwist, Mathf.Abs(r.twist));
                st.maxTwistFromIdle = Mathf.Max(st.maxTwistFromIdle, Mathf.Abs(Mathf.DeltaAngle(idle, r.twist)));
                st.maxSide = Mathf.Max(st.maxSide, Mathf.Abs(r.sideBend));
                st.maxFwd = Mathf.Max(st.maxFwd, Mathf.Abs(r.fwdBend));
                st.slide += r.lSlide + r.rSlide;
                st.root += r.rootMove;
                st.maxBody = Mathf.Max(st.maxBody, r.bodyW);
            }

            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("# Swing 3: masked vs full body when standing");
            sb.AppendLine();
            sb.AppendLine("Per swing, attacking frames only (swings before the last are cut short by the chain). " +
                          "Body = peak weight of the full-body AttackBody layer. Twist = shoulder-line yaw minus " +
                          "hip-line yaw; Δidle = largest change from the idle stance's twist. Bends are the " +
                          "neck-over-spine lean in the pelvis frame. Wrenched = frames with |twist| > 100° AND " +
                          "|side bend| > 30°. Foot travel down = world travel of a foot within 2 cm of its stance " +
                          "height (both feet). Stepping = frames with a foot lifted > 1 cm.");
            sb.AppendLine();
            sb.AppendLine("| Condition | Swing | Body max | SwingTwist max ° | Twist max ° (Δidle) | Side bend ° | Fwd bend ° | Wrenched frames | Foot travel down cm | Stepping frames | Root travel cm |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var c in conditions)
                for (int step = 0; step < c.Clicks; step++)
                {
                    if (!stats.TryGetValue((c.Label, step), out var st)) { sb.AppendLine($"| {c.Label} | {step + 1} | (not reached) ||||||||"); continue; }
                    sb.AppendLine(string.Format(ci,
                        "| {0} | {1} | {2:0.00} | {3:0} | {4:0} ({5:0}) | {6:0} | {7:0} | {8} / {9} | {10:0.0} | {11} | {12:0.0} |",
                        c.Label, step + 1, st.maxBody, st.maxCorr, st.maxTwist, st.maxTwistFromIdle, st.maxSide,
                        st.maxFwd, st.wrenched, st.frames, st.slide * 100f, st.stepping, st.root * 100f));
                }

            sb.AppendLine();
            sb.AppendLine("Pop check: the largest single-frame jump over the whole run, swings and fade-out " +
                          "included. Chest and hips are in degrees per frame; feet are relative to the root, in " +
                          "cm per frame, so the lunge itself is not counted.");
            sb.AppendLine();
            sb.AppendLine("| Condition | Chest °/frame (where) | Hips °/frame (where) | Foot cm/frame (where) |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var c in conditions)
                if (pops.TryGetValue(c.Label, out var p))
                    sb.AppendLine(string.Format(ci, "| {0} | {1:0.0} ({2}) | {3:0.0} ({4}) | {5:0.0} ({6}) |",
                        c.Label, p.chest, p.chestAt, p.hips, p.hipsAt, p.foot * 100f, p.footAt));
            return sb.ToString();
        }
    }
}
