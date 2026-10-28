using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Pickleball.Tests.PlayMode.Harness;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Pickleball.Tests.PlayMode.Scenarios
{
    /// <summary>
    /// Kingdom-Hearts-style sword on the Training stage's dummies: the base chain at different
    /// cadences, the finisher once abilities are granted, an air combo, and lock-on switching
    /// between the three dummies. The mouse swings; the right stick flicks lock-on targets.
    /// </summary>
    [Category("Playtest")]
    [Explicit("Playtest harness: run on purpose, never in the normal suite.")]
    public class SwordScenario
    {
        [UnityTest, Timeout(900000)]
        public IEnumerator Sword() => UniTask.ToCoroutine(Run);

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
            using var s = await PlaytestSession.Begin("Sword", PlaytestScenes.TrainingStage);
            if (s.A.Melee == null) { s.CannotDrive("no MeleeCombat on the player"); s.Complete(); return; }

            // Stand 1.8 m short of the floor dummy, facing it.
            var dummy = GameObject.Find("Dummy_Floor");
            Vector3 target = dummy != null ? dummy.transform.position : new Vector3(9.5f, 0f, 10f);
            Vector3 stand = target + new Vector3(-1.2f, 0.05f, -1.4f);
            if (dummy == null) s.CannotDrive("Dummy_Floor not found; used its expected position");
            if (!s.A.CameraRig.CursorCaptured) s.CannotDrive("the orbit camera has not captured the cursor: mouse swings would be dropped");

            // ---- base moveset ---------------------------------------------------------
            await s.Teleport(stand, target);
            int hitsBefore = CountHits(s);
            s.Log("single click, then wait out the chain");
            await Click(s, 1, 0.1f);
            await s.Seconds(1.2f);
            await s.Shot("single-swing");

            s.Log("3-click chain at 0.30 s");
            await Click(s, 3, 0.30f);
            await s.Shot("chain-3");
            await s.Seconds(1.2f);

            s.Log("slow clicks (0.9 s apart): does the chain break?");
            await Click(s, 3, 0.9f);
            await s.Seconds(1.0f);

            s.Log("mash (10 clicks at 0.12 s)");
            await Click(s, 10, 0.12f);
            await s.Seconds(1.2f);
            await s.Shot("mash");
            s.Note($"base moveset hits on the dummy: {CountHits(s) - hitsBefore}");

            // ---- finisher with every ability -----------------------------------------
            s.GrantAllSwordAbilities();
            await s.Teleport(stand, target);
            s.Log("8-click chain at 0.25 s with all abilities (finisher expected at the end)");
            await Click(s, 8, 0.25f);
            await s.Shot("finisher");
            await s.Seconds(1.5f);
            int finishers = 0;
            foreach (var e in s.Rec.Events) if (e.kind == "swing" && e.detail.Contains("Finisher")) finishers++;
            s.Note($"swings tagged Finisher in the whole run: {finishers}");

            // ---- air combo ------------------------------------------------------------
            await s.Teleport(stand, target);
            s.Log("jump, then 4 clicks in the air");
            await s.Tap(Key.Space, 0.25f);
            await s.Seconds(0.15f);
            await Click(s, 4, 0.2f);
            await s.Shot("air-combo");
            await s.UntilGrounded(3f);
            await s.Seconds(1.0f);

            // ---- lock-on switching ----------------------------------------------------
            if (s.A.LockOn == null) s.CannotDrive("no LockOnTargeting on the player");
            else
            {
                // Clear of the pedestal row at z = 4; all three dummies are ahead, spread left to right.
                await s.Teleport(new Vector3(1.5f, 0.05f, 0.8f), new Vector3(1.5f, 0f, 10f));
                s.Log("Tab: lock on");
                await s.Tap(Key.Tab, 0.1f);
                await s.Seconds(0.6f);
                s.Note($"after Tab: locked on {(s.A.LockOn.Target != null ? s.A.LockOn.Target.name : "(nothing)")}");
                await s.Shot("lock-1");
                for (int i = 0; i < 4; i++)
                {
                    float dir = i % 2 == 0 ? 1f : -1f;
                    s.Log($"right-stick flick {(dir > 0 ? "right" : "left")}");
                    s.In.SetRightStick(new Vector2(dir, 0f));
                    await s.Seconds(0.1f);
                    s.In.SetRightStick(Vector2.zero);
                    await s.Seconds(0.7f);
                    s.Note($"flick {(dir > 0 ? "right" : "left")} -> {(s.A.LockOn.Target != null ? s.A.LockOn.Target.name : "(nothing)")}");
                    await s.Shot($"lock-flick-{i + 1}");
                }
                s.Log("Tab: release");
                await s.Tap(Key.Tab, 0.1f);
                await s.Seconds(0.5f);
                s.Note($"after second Tab: {(s.A.LockOn.IsLocked ? "still locked on " + s.A.LockOn.Target.name : "released")}");
            }

            s.Complete();
        }

        static int CountHits(PlaytestSession s)
        {
            int n = 0;
            foreach (var e in s.Rec.Events) if (e.kind == "hit") n++;
            return n;
        }
    }
}
