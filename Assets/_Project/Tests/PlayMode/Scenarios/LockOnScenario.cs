using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Tests.PlayMode.Harness;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Pickleball.Tests.PlayMode.Scenarios
{
    /// <summary>
    /// The lock-on rework's proof, on the Training stage with targets spawned at runtime (nothing
    /// is saved). Three parts, one per reported problem:
    ///  1. The camera under a floating target: walk, sprint and dash straight under it, circle it
    ///     and stand beneath it, logging the yaw rate per frame.
    ///  2. The aim point against the collider the sword can hit: scenery the sword can't hit, a
    ///     body hovering on its root, a moving root and a juggled dummy.
    ///  3. Whether a press locks: on screen, just off the edge, behind the camera, and behind
    ///     walls, with the hint compared against the press each time.
    ///
    /// Unlike most scenarios this one fails on its numbers, because they are the acceptance test.
    /// It reads only API that also existed before the rework (the cap is looked up by reflection),
    /// so the same file can be run against the old code for a before-and-after. Writes
    /// lockon-frames.csv and lockon-table.md beside the telemetry.
    /// </summary>
    [Category("Playtest")]
    [Explicit("Playtest harness: run on purpose, never in the normal suite.")]
    public class LockOnScenario
    {
        // A clear lane on the stage floor (floor spans x -28..28, z -28..16; see MovementScenario).
        static readonly Vector3 Under = new(6f, 0.05f, -14f);

        /// <summary>The cap to judge by when the camera has none (the old code).</summary>
        const float FallbackCap = 240f;

        [UnityTest, Timeout(900000)]
        public IEnumerator LockOn() => UniTask.ToCoroutine(Run);

        // ------------------------------------------------------------------ records

        sealed class Trace
        {
            public string Name;
            public int Frames, OffScreenFrames, LockedFrames, Reversals;
            public float MaxRate, MaxWindow, TotalYaw, MinViewY = 9f, MaxViewY = -9f, MaxPitchRate;
            public bool EverLost;
            public float LastSign;
            public readonly Queue<float> Window = new();
        }

        readonly struct CamPose
        {
            public readonly float Yaw, Pitch;
            public CamPose(Transform t) { Yaw = t.eulerAngles.y; Pitch = t.eulerAngles.x; }
        }

        static async UniTask Run()
        {
            using var s = await PlaytestSession.Begin("LockOn", PlaytestScenes.TrainingStage);
            var failures = new List<string>();
            var csv = new StringBuilder("pass,frame,t,yawDeg,pitchDeg,yawRateDegPerS,px,pz,flatToTarget,viewX,viewY,viewZ,locked\n");
            var table = new StringBuilder();
            string dir = PlaytestOutput.ScenarioDir("LockOn");

            if (s.A.LockOn == null || s.A.CameraRig == null)
            {
                s.CannotDrive("no LockOnTargeting or SandboxOrbitCamera in the scene");
                s.Complete();
                return;
            }

            s.GrantAllMovementAbilities();
            float cap = ReadCap(s.A.CameraRig);
            s.Note($"yaw cap judged by: {cap:0} deg/s; camera aspect {s.A.Camera.aspect:0.00}, vertical FOV {s.A.Camera.fieldOfView:0}");

            await PartOneYaw(s, cap, failures, csv, table);
            await PartTwoAim(s, failures, table);
            await PartThreeInView(s, failures, table);

            File.WriteAllText(Path.Combine(dir, "lockon-frames.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(dir, "lockon-table.md"), table.ToString());
            s.Note("table:\n" + table);
            foreach (string f in failures) s.Note("FAILED: " + f);

            s.Complete();
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        static float ReadCap(SandboxOrbitCamera rig)
        {
            var field = typeof(SandboxOrbitCamera).GetField("lockMaxYawRate", BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null ? (float)field.GetValue(rig) : FallbackCap;
        }

        // ------------------------------------------------------------------ part 1

        static async UniTask PartOneYaw(PlaytestSession s, float cap, List<string> failures, StringBuilder csv, StringBuilder table)
        {
            table.AppendLine("## Yaw under a floating target");
            table.AppendLine();
            table.AppendLine("| pass | body height | max yaw rate deg/s | max yaw in 0.1 s | reversals | total yaw | frames off screen | view y range | lock held |");
            table.AppendLine("|---|---|---|---|---|---|---|---|---|");

            foreach (float height in new[] { 6f, 9f })
            {
                var floater = MakeFloater("LockOnTest_Floater", new Vector3(Under.x, 0f, Under.z), height);
                Physics.SyncTransforms();
                Vector3 north = Vector3.forward;
                string h = $"{height:0} m";

                // Straight through, dead centre: the flat vector shrinks through exactly zero.
                await Pass(s, floater, $"walk-exact-{h}", new Vector3(Under.x, 0.05f, -24f), north, 0.75f, 6f, cap, failures, csv, table, height);
                await Pass(s, floater, $"sprint-offset-0.4m-{h}", new Vector3(Under.x + 0.4f, 0.05f, -26f), north, 1f, 4.5f, cap, failures, csv, table, height, sprint: true);
                await Pass(s, floater, $"sprint-exact-{h}", new Vector3(Under.x, 0.05f, -26f), north, 1f, 4.5f, cap, failures, csv, table, height, sprint: true);
                await Pass(s, floater, $"dash-through-{h}", new Vector3(Under.x, 0.05f, -22f), north, 1f, 3.5f, cap, failures, csv, table, height, dashAtDistance: 4f);
                await Circle(s, floater, $"circle-2m-{h}", 2f, cap, failures, csv, table, height);
                await Circle(s, floater, $"circle-0.8m-{h}", 0.8f, cap, failures, csv, table, height);
                await ZigZag(s, floater, $"zigzag-under-{h}", cap, failures, csv, table, height);
                await Stand(s, floater, $"stand-under-{h}", cap, failures, csv, table, height);

                Object.Destroy(floater.gameObject);
                await s.Frames(2);
            }
        }

        /// <summary>Taps the toggle and waits for the update it lands in. <paramref name="expected"/>
        /// is only for the log: what the caller is trying to lock, if anything.</summary>
        static async UniTask Press(PlaytestSession s, Health expected)
        {
            await s.Tap(Key.Tab, 0.1f);
            await s.Frames(2);
            var now = s.A.LockOn.Target;
            s.Log($"Tab -> {(now != null ? now.name : "(nothing)")}" + (expected != null ? $" (wanted {expected.name})" : ""));
        }

        static async UniTask EnsureReleased(PlaytestSession s)
        {
            if (s.A.LockOn.IsLocked) await Press(s, null);
            s.In.SetLeftStick(Vector2.zero);
            s.In.Release(Key.LeftShift);
            await s.Seconds(0.2f);
        }

        /// <summary>Locks from far back down the lane, looking up first if the body is above the
        /// frame, as a player would, then teleports (a test shortcut, logged) to the pass's start
        /// with the lock held. A body overhead can't be locked from close, and shouldn't be.</summary>
        static async UniTask<bool> LockFromLane(PlaytestSession s, Health target, Vector3 from, string name, List<string> failures)
        {
            Vector3 far = new(Under.x, 0.05f, -27f);
            await s.Teleport(far, Under);
            Vector3 body = BodyCollider(target).bounds.center;
            for (int i = 0; i < 90 && s.A.Camera.WorldToViewportPoint(body).y > 0.8f; i++)
            {
                s.In.SetRightStick(new Vector2(0f, 1f));
                await UniTask.Yield();
            }
            s.In.SetRightStick(Vector2.zero);
            await s.Frames(3);
            await Press(s, target);
            bool locked = s.A.LockOn.IsLocked && s.A.LockOn.Target == target;
            if (!locked)
            {
                failures.Add($"{name}: Tab did not lock the floater from {far}");
                return false;
            }
            s.Note($"{name}: locked from {far}; aim {s.A.LockOn.AimPoint}");
            await s.Teleport(from, Under);
            await s.Seconds(0.8f);   // let the swing onto the target finish before judging the pass
            if (s.A.LockOn.Target != target) { failures.Add($"{name}: the lock dropped on the way to the start"); return false; }
            return true;
        }

        /// <summary>Back and forth across the point under the target: the flat vector crosses
        /// zero again and again, which is where the old camera kept spinning.</summary>
        static async UniTask ZigZag(PlaytestSession s, Health target, string name, float cap, List<string> failures,
                                    StringBuilder csv, StringBuilder table, float height)
        {
            s.Log(name);
            Vector3 centre = new(Under.x + 0.3f, 0.05f, Under.z);
            if (!await LockFromLane(s, target, centre - Vector3.forward, name, failures)) { await EnsureReleased(s); return; }

            var trace = new Trace { Name = name };
            var pose = new CamPose(s.A.Camera.transform);
            float sign = 1f;
            int frames = Mathf.RoundToInt(5f / PlaytestSession.Step);
            for (int i = 0; i < frames; i++)
            {
                float along = s.A.Player.transform.position.z - centre.z;
                if (along > 1.2f) sign = -1f;
                else if (along < -1.2f) sign = 1f;
                float across = centre.x - s.A.Player.transform.position.x;
                s.In.SetLeftStick(s.StickToward(new Vector3(across, 0f, sign)));
                await UniTask.Yield();
                pose = Sample(s, trace, target, pose, csv, i);
            }
            s.In.SetLeftStick(Vector2.zero);
            Judge(trace, cap, failures, table, height);
            await EnsureReleased(s);
        }

        static async UniTask Pass(PlaytestSession s, Health target, string name, Vector3 from, Vector3 direction, float stick,
                                  float seconds, float cap, List<string> failures, StringBuilder csv, StringBuilder table,
                                  float height, bool sprint = false, float dashAtDistance = -1f)
        {
            s.Log(name);
            if (!await LockFromLane(s, target, from, name, failures)) { await EnsureReleased(s); return; }

            if (sprint) s.In.Hold(Key.LeftShift);
            var trace = new Trace { Name = name };
            var pose = new CamPose(s.A.Camera.transform);
            bool dashed = dashAtDistance < 0f;
            int frames = Mathf.RoundToInt(seconds / PlaytestSession.Step);
            for (int i = 0; i < frames; i++)
            {
                s.In.SetLeftStick(s.StickToward(direction) * stick);
                if (!dashed && s.DistanceTo(target.transform.position) <= dashAtDistance)
                {
                    dashed = true;
                    s.Log($"{name}: dash at {s.DistanceTo(target.transform.position):0.0} m");
                    await s.Tap(Key.E, 0.05f);
                }
                await UniTask.Yield();
                pose = Sample(s, trace, target, pose, csv, i);
                if (i == frames / 2) await s.Shot(name + "-mid");
            }
            s.In.SetLeftStick(Vector2.zero);
            if (sprint) s.In.Release(Key.LeftShift);
            Judge(trace, cap, failures, table, height);
            await EnsureReleased(s);
        }

        static async UniTask Circle(PlaytestSession s, Health target, string name, float radius, float cap,
                                    List<string> failures, StringBuilder csv, StringBuilder table, float height)
        {
            s.Log(name);
            Vector3 centre = new(Under.x, 0.05f, Under.z);
            if (!await LockFromLane(s, target, centre - Vector3.forward * radius, name, failures)) { await EnsureReleased(s); return; }

            var trace = new Trace { Name = name };
            var pose = new CamPose(s.A.Camera.transform);
            int frames = Mathf.RoundToInt(5f / PlaytestSession.Step);
            for (int i = 0; i < frames; i++)
            {
                Vector3 from = s.A.Player.transform.position - centre;
                from.y = 0f;
                Vector3 tangent = Vector3.Cross(Vector3.up, from.normalized);
                // Tangent plus a correction back onto the circle.
                Vector3 dir = tangent + from.normalized * ((radius - from.magnitude) * 2f);
                s.In.SetLeftStick(s.StickToward(dir));
                await UniTask.Yield();
                pose = Sample(s, trace, target, pose, csv, i);
                if (i == frames / 2) await s.Shot(name + "-mid");
            }
            s.In.SetLeftStick(Vector2.zero);
            Judge(trace, cap, failures, table, height);
            await EnsureReleased(s);
        }

        static async UniTask Stand(PlaytestSession s, Health target, string name, float cap, List<string> failures,
                                   StringBuilder csv, StringBuilder table, float height)
        {
            s.Log(name);
            Vector3 from = new(Under.x, 0.05f, Under.z - 8f);
            if (!await LockFromLane(s, target, from, name, failures)) { await EnsureReleased(s); return; }

            await s.WalkTo(() => target.transform.position, 0.25f, 4f);
            var trace = new Trace { Name = name };
            var pose = new CamPose(s.A.Camera.transform);
            int frames = Mathf.RoundToInt(3f / PlaytestSession.Step);
            for (int i = 0; i < frames; i++)
            {
                await UniTask.Yield();
                pose = Sample(s, trace, target, pose, csv, i);
            }
            await s.Shot(name);
            Judge(trace, cap, failures, table, height);

            // Release from under it, tilted as far as the camera ever gets: the worst single
            // frame of the hand-back. The tilt may fade, but nothing may jump.
            await Press(s, null);
            float lastYaw = s.A.Camera.transform.eulerAngles.y, lastPitch = s.A.Camera.transform.eulerAngles.x;
            float worstYaw = 0f, worstPitch = 0f;
            for (int i = 0; i < 90; i++)
            {
                await UniTask.Yield();
                var e = s.A.Camera.transform.eulerAngles;
                worstYaw = Mathf.Max(worstYaw, Mathf.Abs(Mathf.DeltaAngle(lastYaw, e.y)));
                worstPitch = Mathf.Max(worstPitch, Mathf.Abs(Mathf.DeltaAngle(lastPitch, e.x)));
                lastYaw = e.y;
                lastPitch = e.x;
            }
            s.Note($"{name}: release: worst single frame yaw {worstYaw:0.00} deg, pitch {worstPitch:0.00} deg");
            if (worstYaw > cap * PlaytestSession.Step || worstPitch > 4f)
                failures.Add($"{name}: the release jumped, {worstYaw:0.00} deg yaw and {worstPitch:0.00} deg pitch in one frame");
            await EnsureReleased(s);
        }

        static CamPose Sample(PlaytestSession s, Trace t, Health target, CamPose last, StringBuilder csv, int frame)
        {
            var cam = s.A.Camera;
            var pose = new CamPose(cam.transform);
            float delta = Mathf.DeltaAngle(last.Yaw, pose.Yaw);
            float rate = Mathf.Abs(delta) / PlaytestSession.Step;
            float pitchRate = Mathf.Abs(Mathf.DeltaAngle(last.Pitch, pose.Pitch)) / PlaytestSession.Step;

            t.Frames++;
            t.MaxRate = Mathf.Max(t.MaxRate, rate);
            t.MaxPitchRate = Mathf.Max(t.MaxPitchRate, pitchRate);
            t.TotalYaw += Mathf.Abs(delta);
            if (Mathf.Abs(delta) > 0.05f)
            {
                float sign = Mathf.Sign(delta);
                if (t.LastSign != 0f && sign != t.LastSign) t.Reversals++;
                t.LastSign = sign;
            }
            t.Window.Enqueue(delta);
            while (t.Window.Count > 6) t.Window.Dequeue();
            float sum = 0f;
            foreach (float d in t.Window) sum += d;
            t.MaxWindow = Mathf.Max(t.MaxWindow, Mathf.Abs(sum));

            bool locked = s.A.LockOn.IsLocked;
            if (locked) t.LockedFrames++; else t.EverLost = true;
            Vector3 view = locked ? cam.WorldToViewportPoint(s.A.LockOn.AimPoint) : new Vector3(-1f, -1f, -1f);
            if (locked)
            {
                bool on = view.z > 0f && view.x >= 0f && view.x <= 1f && view.y >= 0f && view.y <= 1f;
                if (!on) t.OffScreenFrames++;
                t.MinViewY = Mathf.Min(t.MinViewY, view.y);
                t.MaxViewY = Mathf.Max(t.MaxViewY, view.y);
            }

            Vector3 p = s.A.Player.transform.position;
            float flat = s.DistanceTo(target.transform.position);
            csv.AppendLine(FormattableString.Invariant(
                $"{t.Name},{frame},{s.Rec.Now:0.000},{pose.Yaw:0.00},{pose.Pitch:0.00},{rate:0.0},{p.x:0.00},{p.z:0.00},{flat:0.00},{view.x:0.000},{view.y:0.000},{view.z:0.00},{(locked ? 1 : 0)}"));
            return pose;
        }

        static void Judge(Trace t, float cap, List<string> failures, StringBuilder table, float height)
        {
            string line = $"| {t.Name} | {height:0} m | {t.MaxRate:0} | {t.MaxWindow:0} | {t.Reversals} | {t.TotalYaw:0} | " +
                          $"{t.OffScreenFrames}/{t.Frames} | {t.MinViewY:0.00}..{t.MaxViewY:0.00} | {(t.EverLost ? "LOST" : "held")} |";
            table.AppendLine(line);
            Debug.Log("[Playtest:LockOn] " + line);

            // 2% of slack for the frame's float rounding, not for the design.
            if (t.MaxRate > cap * 1.02f) failures.Add($"{t.Name}: yaw rate {t.MaxRate:0} deg/s is over the {cap:0} cap");
            // A flip: more than the cap could cover in 0.1 s, which also catches a burst one frame hides.
            if (t.MaxWindow > cap * 0.1f * 1.05f) failures.Add($"{t.Name}: {t.MaxWindow:0} deg of yaw in 0.1 s");
            // Straight past it the turn is one clean sweep. Going round or to and fro the player reverses,
            // and the camera may sway with him, as long as it isn't following him round the clock.
            bool wandering = t.Name.StartsWith("circle-0.8") || t.Name.StartsWith("zigzag");
            if (!wandering && t.Reversals > 2) failures.Add($"{t.Name}: yaw changed direction {t.Reversals} times");
            if (wandering && t.TotalYaw > 600f) failures.Add($"{t.Name}: {t.TotalYaw:0} deg of yaw in all: it is chasing him");
        }

        // ------------------------------------------------------------------ part 2

        static async UniTask PartTwoAim(PlaytestSession s, List<string> failures, StringBuilder table)
        {
            table.AppendLine();
            table.AppendLine("## Aim point against the hittable collider");
            table.AppendLine();
            table.AppendLine("| case | frames | frames outside | worst miss, m |");
            table.AppendLine("|---|---|---|---|");

            Vector3 from = new(Under.x, 0.05f, Under.z - 9f);

            // Scenery the sword can't hit: a banner with a renderer and no collider, and not
            // marked LockOnIgnore, well above the body.
            var banner = MakeFloater("LockOnTest_Banner", new Vector3(Under.x, 0f, Under.z), 3f);
            var cloth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cloth.name = "Banner";
            Object.Destroy(cloth.GetComponent<Collider>());
            cloth.transform.SetParent(banner.transform, false);
            cloth.transform.localPosition = new Vector3(0f, 8f, 0f);
            cloth.transform.localScale = new Vector3(5f, 5f, 0.2f);
            await s.Frames(2);
            await AimCase(s, banner, BodyCollider(banner), from, "unhittable scenery", 1f, _ => { }, failures, table);
            Object.Destroy(banner.gameObject);
            await s.Frames(2);

            // A body hovering on a planted root, as Hypatia flies hers.
            var hover = MakeFloater("LockOnTest_Hover", new Vector3(Under.x, 0f, Under.z), 3f);
            Transform body = hover.transform.GetChild(0);
            Vector3 rest = body.localPosition;
            await AimCase(s, hover, BodyCollider(hover), from, "body hovering on its root", 4f,
                          t => body.localPosition = rest + Vector3.up * (Mathf.Sin(t * 2f) * 2f), failures, table);
            Object.Destroy(hover.gameObject);
            await s.Frames(2);

            // A root that walks.
            var walker = MakeFloater("LockOnTest_Walker", new Vector3(Under.x, 0f, Under.z), 1.5f);
            Vector3 home = walker.transform.position;
            await AimCase(s, walker, BodyCollider(walker), from, "moving root", 4f,
                          t => walker.transform.position = home + Vector3.right * (Mathf.Sin(t * 1.5f) * 4f), failures, table);
            Object.Destroy(walker.gameObject);
            await s.Frames(2);

            // The training dummy, launched: Health.BodyOffset and the collider ride together.
            var dummyGo = GameObject.Find("Dummy_Floor");
            if (dummyGo == null) { s.CannotDrive("Dummy_Floor not found"); return; }
            var dummy = dummyGo.GetComponent<Health>();
            Vector3 dummyAt = dummyGo.transform.position;
            float launchAt = -1f, peak = 0f;
            await AimCase(s, dummy, dummyGo.GetComponent<CapsuleCollider>(), dummyAt + Vector3.back * 6f + Vector3.up * 0.05f,
                          "juggled dummy (BodyOffset)", 3f, t =>
                          {
                              peak = Mathf.Max(peak, dummy.BodyOffset.y);
                              if (launchAt >= 0f) return;
                              launchAt = t;
                              dummy.TakeHit(1f, dummy.transform.position, Vector3.zero, true);
                          }, failures, table);
            s.Note($"dummy launched: highest BodyOffset.y {peak:0.00} m");
            if (peak < 0.5f) s.CannotDrive("the launch did not lift the dummy, so the juggled case proved nothing");
        }

        static async UniTask AimCase(PlaytestSession s, Health target, Collider hittable, Vector3 from, string name, float seconds,
                                     Action<float> drive, List<string> failures, StringBuilder table)
        {
            s.Log("aim: " + name);
            await s.Teleport(from, target.transform.position);
            await Press(s, target);
            if (s.A.LockOn.Target != target)
            {
                failures.Add($"aim {name}: Tab did not lock it");
                await EnsureReleased(s);
                return;
            }

            int frames = Mathf.RoundToInt(seconds / PlaytestSession.Step), outside = 0;
            float worst = 0f, t = 0f;
            for (int i = 0; i < frames; i++)
            {
                drive(t);
                t += PlaytestSession.Step;
                await UniTask.Yield();
                if (s.A.LockOn.Target != target) continue;
                Physics.SyncTransforms();
                Bounds b = hittable.bounds;
                b.Expand(0.04f);
                float miss = Mathf.Sqrt(b.SqrDistance(s.A.LockOn.AimPoint));
                if (miss > 0f) outside++;
                worst = Mathf.Max(worst, miss);
            }
            string line = $"| {name} | {frames} | {outside} | {worst:0.00} |";
            table.AppendLine(line);
            Debug.Log("[Playtest:LockOn] " + line);
            await s.Shot("aim-" + name.Replace(' ', '-'));
            if (outside > 0) failures.Add($"aim {name}: outside the hittable bounds on {outside}/{frames} frames, by up to {worst:0.00} m");
            await EnsureReleased(s);
        }

        // ------------------------------------------------------------------ part 3

        static async UniTask PartThreeInView(PlaytestSession s, List<string> failures, StringBuilder table)
        {
            table.AppendLine();
            table.AppendLine("## Does a press lock?");
            table.AppendLine();
            table.AppendLine("| case | hint before | press locked | wanted |");
            table.AppendLine("|---|---|---|---|");

            Vector3 at = new(Under.x, 0.05f, Under.z - 8f);
            Vector3 ahead = at + Vector3.forward * 10f;
            var post = MakeFloater("LockOnTest_Post", new Vector3(ahead.x, 0f, ahead.z), 1f);
            Transform root = post.transform;

            // In front, in plain view: the control every other case is measured against.
            await s.Teleport(at, ahead);
            await PressCase(s, post, "in plain view", true, failures, table);

            // Turned round: it is behind the camera. Zelda's fallback then recentres.
            await s.Teleport(at, at - Vector3.forward * 10f);
            float offYaw = await SwingCameraOff(s);
            await PressCase(s, post, "behind the camera", false, failures, table);
            await s.Seconds(0.6f);
            float backYaw = Mathf.Abs(Mathf.DeltaAngle(s.A.Camera.transform.eulerAngles.y, s.A.Player.transform.eulerAngles.y));
            s.Note($"recentre with nothing to lock: camera was {offYaw:0} deg off the player's facing, then {backYaw:0} deg");
            if (backYaw > 10f) failures.Add($"press with nothing in view: camera still {backYaw:0} deg off the player's back");

            // Just past the screen's edge, and just inside it. Placed through the camera so the
            // aim lands where wanted whatever the aspect is. The body sits at 1 m + its half-height.
            await s.Teleport(at, ahead);
            await PlaceAtViewport(s, post, 1.01f, 0.5f, 9f);
            await PressCase(s, post, "1% past the right edge", false, failures, table);
            await PlaceAtViewport(s, post, 0.9f, 0.5f, 9f);
            await PressCase(s, post, "10% inside the right edge", true, failures, table);

            // Behind walls. Back to straight ahead first.
            root.position = new Vector3(ahead.x, 0f, ahead.z);
            Physics.SyncTransforms();
            await s.Teleport(at, ahead);

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "LockOnTest_Wall";
            wall.transform.position = new Vector3(at.x, 3f, at.z + 5f);
            wall.transform.localScale = new Vector3(8f, 6f, 0.4f);
            Physics.SyncTransforms();
            await s.Frames(3);
            await PressCase(s, post, "behind a wall, blocking player and camera", false, failures, table);

            // A wall the camera can't see past but the player's chest can: the old check cast
            // from the chest. Off to one side, on the camera's line and not the chest's.
            Vector3 aside = at + new Vector3(-3f, 0f, 8f);
            root.position = new Vector3(aside.x, 0f, aside.z);
            Physics.SyncTransforms();
            await s.Frames(2);
            Vector3 aim = BodyCollider(post).bounds.center;
            Vector3 cam = s.A.Camera.transform.position;
            Vector3 chest = s.A.Player.transform.position + Vector3.up * 1.2f;
            float wallZ = at.z + 1.2f;
            Vector3 onCamLine = Vector3.Lerp(cam, aim, Mathf.InverseLerp(cam.z, aim.z, wallZ));
            Vector3 onChestLine = Vector3.Lerp(chest, aim, Mathf.InverseLerp(chest.z, aim.z, wallZ));
            // From just past the chest's line out beyond the camera's.
            float inner = onChestLine.x - 0.15f, outer = onCamLine.x - 1.5f;
            wall.transform.position = new Vector3((inner + outer) * 0.5f, 3f, wallZ);
            wall.transform.localScale = new Vector3(Mathf.Abs(inner - outer), 6f, 0.2f);
            Physics.SyncTransforms();
            await s.Frames(3);
            bool camBlocked = Physics.Linecast(cam, aim, out var camHit) && camHit.collider.gameObject == wall;
            bool chestClear = !(Physics.Linecast(chest, aim, out var chestHit) && chestHit.collider.gameObject == wall);
            s.Note($"side wall: camera line blocked={camBlocked}, chest line clear={chestClear}, " +
                   $"camera x at wall {onCamLine.x:0.00}, chest x at wall {onChestLine.x:0.00}");
            if (!camBlocked || !chestClear) s.CannotDrive("the side wall did not separate the camera's line from the chest's");
            await PressCase(s, post, "behind a wall only the camera's line crosses", false, failures, table);

            // Same CamPose with the wall gone: it must lock, or the case above proved nothing.
            Object.Destroy(wall);
            await s.Frames(2);
            Physics.SyncTransforms();
            await s.Frames(3);
            await PressCase(s, post, "same place, wall removed", true, failures, table);

            // A held lock with a wall dropped in: lets go after the grace, and hands back cleanly.
            await s.Teleport(at, ahead);
            root.position = new Vector3(ahead.x, 0f, ahead.z);
            Physics.SyncTransforms();
            await s.Frames(2);
            await Press(s, post);
            await s.Seconds(0.8f);
            var held = GameObject.CreatePrimitive(PrimitiveType.Cube);
            held.name = "LockOnTest_HeldWall";
            held.transform.position = new Vector3(at.x, 3f, at.z + 5f);
            held.transform.localScale = new Vector3(8f, 6f, 0.4f);
            Physics.SyncTransforms();
            float start = s.Rec.Now, lastYaw = s.A.Camera.transform.eulerAngles.y, worstStep = 0f;
            bool releasedInTime = false;
            float lastPitch = s.A.Camera.transform.eulerAngles.x, worstPitch = 0f;
            for (int i = 0; i < 300; i++)
            {
                await UniTask.Yield();
                var c = s.A.Camera.transform.eulerAngles;
                worstStep = Mathf.Max(worstStep, Mathf.Abs(Mathf.DeltaAngle(lastYaw, c.y)));
                worstPitch = Mathf.Max(worstPitch, Mathf.Abs(Mathf.DeltaAngle(lastPitch, c.x)));
                lastYaw = c.y;
                lastPitch = c.x;
                if (!s.A.LockOn.IsLocked && !releasedInTime)
                {
                    releasedInTime = true;
                    s.Note($"held lock behind a new wall let go after {s.Rec.Now - start:0.00} s");
                }
            }
            s.Note($"held lock release: worst per-frame step yaw {worstStep:0.00} deg, pitch {worstPitch:0.00} deg");
            if (!releasedInTime) failures.Add("a held lock behind a wall never let go");
            Object.Destroy(held);
            Object.Destroy(post.gameObject);
            await s.Frames(2);
        }

        static async UniTask<float> SwingCameraOff(PlaytestSession s)
        {
            s.In.SetRightStick(new Vector2(1f, 0f));
            await s.Seconds(0.3f);
            s.In.SetRightStick(Vector2.zero);
            await s.Frames(3);
            return Mathf.Abs(Mathf.DeltaAngle(s.A.Camera.transform.eulerAngles.y, s.A.Player.transform.eulerAngles.y));
        }

        /// <summary>Puts the post's body so its aim point projects at (<paramref name="vx"/>,
        /// <paramref name="vy"/>), <paramref name="depth"/> metres in front of the camera.</summary>
        static async UniTask PlaceAtViewport(PlaytestSession s, Health post, float vx, float vy, float depth)
        {
            var cam = s.A.Camera;
            Vector3 wanted = cam.ViewportToWorldPoint(new Vector3(vx, vy, depth));
            Vector3 offset = s.A.LockOn.IsLocked ? Vector3.zero : BodyCollider(post).transform.position - post.transform.position;
            post.transform.position = wanted - offset;
            Physics.SyncTransforms();
            await s.Frames(3);
        }

        static async UniTask PressCase(PlaytestSession s, Health target, string name, bool wanted, List<string> failures, StringBuilder table)
        {
            s.Log("press: " + name);
            string hint = ReticleMode(s.A.LockOn);
            bool hinted = hint == "Hint";
            await s.Shot("press-" + name.Replace(' ', '-').Replace(',', '_'));
            await Press(s, null);
            bool locked = s.A.LockOn.IsLocked;
            string line = $"| {name} | {(hinted ? "yes" : "no")} | {(locked ? "yes" : "no")} | {(wanted ? "lock" : "no lock")} |";
            table.AppendLine(line);
            Debug.Log("[Playtest:LockOn] " + line);
            if (locked != wanted) failures.Add($"press {name}: locked={locked}, wanted {wanted}");
            if (hinted != locked) failures.Add($"press {name}: hint said {hinted} but the press locked={locked}");
            await EnsureReleased(s);
            await s.Frames(4);
        }

        /// <summary>What the reticle was last told to show, from its private state (read-only).</summary>
        static string ReticleMode(LockOnTargeting lockOn)
        {
            var reticle = typeof(LockOnTargeting).GetField("_reticle", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(lockOn);
            if (reticle == null) return "None";
            return typeof(LockOnReticle).GetField("_mode", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(reticle)?.ToString();
        }

        // ------------------------------------------------------------------ targets

        /// <summary>A Health planted on the floor whose capsule body (the only collider, 2 m tall,
        /// 1 m wide) is a child floating <paramref name="height"/> above it, as Hypatia's is.</summary>
        static Health MakeFloater(string name, Vector3 root, float height)
        {
            var go = new GameObject(name);
            go.transform.position = root;
            var health = go.AddComponent<Health>();
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = Vector3.up * height;
            return health;
        }

        static Collider BodyCollider(Health h) => h.transform.GetChild(0).GetComponent<Collider>();
    }
}
