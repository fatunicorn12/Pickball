using System.Collections;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Dialogue;
using Pickleball.Tests.PlayMode.Harness;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Pickleball.Tests.PlayMode.Scenarios
{
    /// <summary>
    /// The real targets and the real neighbours of the lock-on camera, in the real scenes.
    ///  - <see cref="Targets"/>: what the sword-hittable measure resolves to for every Health in
    ///    the shipped scenes (Newton, Hypatia, the dummies, Birdy) and the alien prefab, and whether a
    ///    lock's aim stays inside the collider while the fight runs.
    ///  - <see cref="TalkWhileLocked"/>: lock Birdy, talk, and check the dialogue camera's
    ///    hand-back frame by frame.
    /// </summary>
    [Category("Playtest")]
    [Explicit("Playtest harness: run on purpose, never in the normal suite.")]
    public class LockOnAuditScenario
    {
        const string HypatiaScene = "Assets/_Project/Scenes/HypatiaFight.unity";
        const string AlienPrefab = "Assets/_Project/Prefabs/Enemies/AlienEnemy.prefab";

        [UnityTest, Timeout(900000)]
        public IEnumerator Targets() => UniTask.ToCoroutine(RunTargets);

        [UnityTest, Timeout(900000)]
        public IEnumerator TalkWhileLocked() => UniTask.ToCoroutine(RunTalk);

        // ------------------------------------------------------------------ targets

        static async UniTask RunTargets()
        {
            var failures = new List<string>();
            await AuditScene("LockOnAudit-Training", PlaytestScenes.TrainingStage, true, failures);
            await AuditScene("LockOnAudit-Sandbox", PlaytestScenes.ChibiSandbox, false, failures);
            await AuditScene("LockOnAudit-Hypatia", HypatiaScene, false, failures);
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        static async UniTask AuditScene(string name, string scene, bool spawnAlien, List<string> failures)
        {
            using var s = await PlaytestSession.Begin(name, scene);
            if (spawnAlien) await SpawnAlien(s);

            int mask = s.A.Melee != null ? s.A.Melee.HittableLayers.value : ~0;
            var colliders = new List<Collider>();
            var found = Object.FindObjectsByType<Health>(FindObjectsSortMode.None);
            foreach (var h in found)
            {
                if (s.A.PlayerHealth == h) continue;
                string line = Describe(h, mask, colliders);
                s.Note(line);
                if (!LockOnAim.TryMeasureHittable(h, mask, LockOnAim.DefaultChestBias, colliders, out var body))
                    failures.Add($"{scene}: {h.name} has no collider the sword can hit");
                else if (!body.Bounds.Contains(body.AimPoint))
                    failures.Add($"{scene}: {h.name}'s aim point is outside its body");
            }

            // Then hold a lock on the nearest real enemy while the scene runs, boss brains included.
            // In the Training stage that is the alien, which runs at the player: a moving root.
            var alien = spawnAlien ? Object.FindFirstObjectByType<Pickleball.Sandbox.Enemies.Enemy>() : null;
            Health enemy = alien != null ? alien.Health : Nearest(s, found);
            if (enemy == null) s.CannotDrive("no enemy to hold a lock on");
            else await HoldOn(s, enemy, mask, failures);
            s.Complete();
        }

        static async UniTask SpawnAlien(PlaytestSession s)
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(AlienPrefab);
            if (prefab == null) { s.CannotDrive("no alien prefab at " + AlienPrefab); return; }
            Object.Instantiate(prefab, new Vector3(0f, 0.05f, -20f), Quaternion.identity);
            s.Note("SET-UP: spawned the alien prefab, which no shipped scene carries");
            await s.Frames(3);
#else
            await UniTask.Yield();
#endif
        }

        static string Describe(Health h, int mask, List<Collider> scratch)
        {
            var sb = new StringBuilder($"{h.name}: ");
            foreach (var c in h.GetComponentsInChildren<Collider>(true))
            {
                bool hit = LockOnAim.IsHittable(c, h, mask);
                sb.Append(hit ? "[hit] " : "[no] ").Append(c.GetType().Name).Append(" on ").Append(c.name);
                if (c.isTrigger) sb.Append(" (trigger)");
                sb.Append("; ");
            }
            if (LockOnAim.TryMeasureHittable(h, mask, LockOnAim.DefaultChestBias, scratch, out var body))
                sb.Append($"aim {body.AimPoint - h.transform.position} from root, radius {body.Radius:0.00}, half height {body.HalfHeight:0.00}");
            else sb.Append("no hittable collider");
            LockOnTargeting.MeasureAim(h, out Vector3 offset, out _, out _);
            return sb.ToString() + $" | MeasureAim offset {offset}";
        }

        static Health Nearest(PlaytestSession s, Health[] found)
        {
            Health best = null;
            float bestDistance = float.MaxValue;
            foreach (var h in found)
            {
                if (h == s.A.PlayerHealth || !h.GrantsXp) continue;
                float d = s.DistanceTo(h.transform.position);
                if (d < bestDistance) { bestDistance = d; best = h; }
            }
            return best;
        }

        static async UniTask HoldOn(PlaytestSession s, Health enemy, int mask, List<string> failures)
        {
            // Stand off on the line to it, so it is in view, then press as a player would.
            Vector3 at = enemy.transform.position;
            Vector3 away = s.A.Player.transform.position - at;
            away.y = 0f;
            away = away.sqrMagnitude > 1f ? away.normalized : Vector3.back;
            await s.Teleport(at + away * 8f + Vector3.up * 0.5f, at);
            await s.Tap(Key.Tab, 0.1f);
            await s.Frames(2);
            if (s.A.LockOn == null || s.A.LockOn.Target != enemy)
            {
                s.CannotDrive($"Tab did not lock {enemy.name} from 8 m (locked: {(s.A.LockOn != null && s.A.LockOn.Target != null ? s.A.LockOn.Target.name : "nothing")})");
                return;
            }

            var colliders = new List<Collider>();
            int frames = Mathf.RoundToInt(8f / PlaytestSession.Step), outside = 0, held = 0;
            float worst = 0f, lastYaw = s.A.Camera.transform.eulerAngles.y, maxRate = 0f, top = float.MinValue, bottom = float.MaxValue;
            for (int i = 0; i < frames; i++)
            {
                await UniTask.Yield();
                float yaw = s.A.Camera.transform.eulerAngles.y;
                maxRate = Mathf.Max(maxRate, Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw)) / PlaytestSession.Step);
                lastYaw = yaw;
                if (s.A.LockOn.Target != enemy) continue;
                held++;
                Vector3 aim = s.A.LockOn.AimPoint;
                top = Mathf.Max(top, aim.y);
                bottom = Mathf.Min(bottom, aim.y);
                if (!LockOnAim.TryMeasureHittable(enemy, mask, LockOnAim.DefaultChestBias, colliders, out var body)) continue;
                Bounds b = body.Bounds;
                b.Expand(0.02f);
                float miss = Mathf.Sqrt(b.SqrDistance(aim));
                if (miss > 0f) outside++;
                worst = Mathf.Max(worst, miss);
            }
            s.Note($"held {enemy.name}: locked {held}/{frames} frames, aim outside its body on {outside}, worst {worst:0.00} m, " +
                   $"aim height {bottom:0.00}..{top:0.00}, max yaw rate {maxRate:0} deg/s");
            await s.Shot("held-" + enemy.name);
            if (outside > 0) failures.Add($"{enemy.name}: the aim left its body on {outside} frames");

            // Then walk in as close as it lets him, and under it if it floats.
            if (s.A.LockOn.Target == enemy)
            {
                lastYaw = s.A.Camera.transform.eulerAngles.y;
                maxRate = 0f;
                Vector3 towards = enemy.transform.position - s.A.Player.transform.position;
                towards.y = 0f;
                int off = 0, walked = Mathf.RoundToInt(3f / PlaytestSession.Step);
                for (int i = 0; i < walked && s.A.LockOn.Target == enemy; i++)
                {
                    s.In.SetLeftStick(s.StickToward(towards));
                    await UniTask.Yield();
                    float yaw = s.A.Camera.transform.eulerAngles.y;
                    maxRate = Mathf.Max(maxRate, Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw)) / PlaytestSession.Step);
                    lastYaw = yaw;
                    if (s.A.LockOn.Target == enemy && !LockOnView.InsideViewport(s.A.Camera.WorldToViewportPoint(s.A.LockOn.AimPoint), 0f)) off++;
                }
                s.In.SetLeftStick(Vector2.zero);
                await s.Shot("close-" + enemy.name);
                s.Note($"walked in on {enemy.name}: {s.DistanceTo(enemy.transform.position):0.0} m away at the end, max yaw rate {maxRate:0} deg/s, " +
                       $"aim off screen on {off}/{walked} frames, still locked {s.A.LockOn.Target == enemy}");
            }
            if (s.A.LockOn.IsLocked) { await s.Tap(Key.Tab, 0.1f); await s.Frames(2); }
        }

        // --------------------------------------------------------------------- talk

        static async UniTask RunTalk()
        {
            using var s = await PlaytestSession.Begin("LockOnTalk", PlaytestScenes.ChibiSandbox);
            var a = s.A;
            if (a.Director == null || a.LockOn == null) { s.CannotDrive("no DialogueDirector or LockOnTargeting"); s.Complete(); return; }
            Talkable birdy = null;
            foreach (var t in a.Talkables) if (t.name.Contains("Birdy")) birdy = t;
            if (birdy == null) { s.CannotDrive("no Birdy in the scene"); s.Complete(); return; }

            Vector3 b = birdy.transform.position;
            Vector3 dir = a.Player.transform.position - b;
            dir.y = 0f;
            dir.Normalize();
            await s.Teleport(b + dir * 2.5f + Vector3.up * 0.3f, b);
            await s.Seconds(0.8f);

            s.Log("Tab: lock Birdy");
            await s.Tap(Key.Tab, 0.1f);
            await s.Seconds(1f);
            string lockedOn = a.LockOn.Target != null ? a.LockOn.Target.name : "(nothing)";
            s.Note($"locked on: {lockedOn}; prompt target: {(a.Interactor != null && a.Interactor.CurrentTarget != null ? a.Interactor.CurrentTarget.name : "none")}");
            var failures = new List<string>();
            if (a.LockOn.Target == null) failures.Add("Tab did not lock Birdy from talking range");

            Transform cam = a.Camera.transform;
            Pose before = new(cam.position, cam.rotation);
            s.Log("E: talk");
            await s.Tap(Key.E, 0.1f);
            int guard = 0;
            while (a.Director.State == DialogueDirector.Phase.Idle && guard++ < 120) await UniTask.Yield();
            if (a.Director.State == DialogueDirector.Phase.Idle)
            {
                s.CannotDrive("pressing E did not start a conversation");
                s.Complete();
                Assert.IsEmpty(failures, string.Join("\n", failures));
                return;
            }
            s.Note($"talking: lock released = {!a.LockOn.IsLocked}");
            if (a.LockOn.IsLocked) failures.Add("the conversation did not release the lock");
            await s.Seconds(0.6f);
            await s.Shot("talking");

            float gaveUpAt = s.Rec.Now + 90f;
            while (a.Director.State != DialogueDirector.Phase.Idle && s.Rec.Now < gaveUpAt)
            {
                if (a.Director.IsChoiceReady)
                {
                    s.Log("SET-UP: picks the first option through ConfirmChoice");
                    a.Director.ConfirmChoice(0);
                    await s.Seconds(0.2f);
                }
                else if (a.Director.State == DialogueDirector.Phase.Talking && a.Director.IsLineRevealed)
                {
                    await s.Tap(Key.E, 0.08f);
                    await s.Seconds(0.2f);
                }
                else await s.Seconds(0.1f);
            }
            if (a.Director.State != DialogueDirector.Phase.Idle)
            {
                s.CannotDrive("the conversation did not end within 90 s");
                failures.Add("the conversation did not end, so the hand-back was not measured");
            }

            // Frame by frame from the end of the talk: where the camera lands, and how hard.
            Pose first = new(cam.position, cam.rotation);
            Pose last = first;
            float worstAngle = 0f, worstMove = 0f;
            for (int i = 0; i < 90; i++)
            {
                await UniTask.Yield();
                worstAngle = Mathf.Max(worstAngle, Quaternion.Angle(last.rotation, cam.rotation));
                worstMove = Mathf.Max(worstMove, Vector3.Distance(last.position, cam.position));
                last = new Pose(cam.position, cam.rotation);
            }
            s.Note($"talk over: restored pose vs the pose before the talk: {Vector3.Distance(first.position, before.position):0.000} m, " +
                   $"{Quaternion.Angle(first.rotation, before.rotation):0.00} deg; then over 1.5 s the worst single frame is " +
                   $"{worstMove:0.000} m, {worstAngle:0.00} deg");
            await s.Shot("after");
            if (Quaternion.Angle(first.rotation, before.rotation) > 1f)
                failures.Add($"the talk did not restore the pose: {Quaternion.Angle(first.rotation, before.rotation):0.00} deg off");
            if (worstAngle > 5f) failures.Add($"the camera jumped {worstAngle:0.00} deg in one frame after the talk");
            s.Complete();
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
    }
}
