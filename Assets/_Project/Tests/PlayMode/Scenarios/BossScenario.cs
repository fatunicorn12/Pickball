using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Pickleball.Tests.PlayMode.Harness;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Pickleball.Tests.PlayMode.Scenarios
{
    /// <summary>
    /// The first 60 seconds of the Newton fight in the sandbox. The fight starts when the scene
    /// does (Newton attacks from Start), so there is no trigger to walk through. A simple bot
    /// locks on, closes to sword range, swings, and jumps now and then; it does not read telegraphs,
    /// so a death here is a data point about an average-skill attempt, not a verdict.
    /// </summary>
    [Category("Playtest")]
    [Explicit("Playtest harness: run on purpose, never in the normal suite.")]
    public class BossScenario
    {
        [UnityTest, Timeout(1200000)]
        public IEnumerator NewtonFirstMinute() => UniTask.ToCoroutine(() => Run("NewtonBoss", aggressive: true));

        /// <summary>Same minute, but the player keeps ~9 m back, never swings and jumps on a timer:
        /// a look at Newton's attack patterns and telegraphs with the player alive to see them.</summary>
        [UnityTest, Timeout(1200000)]
        public IEnumerator NewtonFirstMinute_Observer() => UniTask.ToCoroutine(() => Run("NewtonBoss_Observer", aggressive: false));

        static async UniTask Run(string name, bool aggressive)
        {
            using var s = await PlaytestSession.Begin(name, PlaytestScenes.ChibiSandbox);
            var a = s.A;
            if (a.Boss == null) { s.CannotDrive("no NewtonBoss in the scene"); s.Complete(); return; }
            var boss = a.Boss.transform;
            s.Note("No set-up: player starts as the scene ships (no abilities granted, base moveset). " +
                   (aggressive ? "Bot: lock on, close to 3 m, swing every 0.35 s, jump every 3 s." : "Bot: lock on, stay ~9 m back, never swing, jump every 3 s."));
            await s.Shot("t00-start");

            s.Log("Tab: lock on");
            await s.Tap(Key.Tab, 0.1f);
            await s.Seconds(0.5f);
            s.Note($"lock-on after Tab: {(a.LockOn != null && a.LockOn.IsLocked ? a.LockOn.Target.name : "NOT LOCKED")}");

            bool dead = false;
            if (a.PlayerHealth != null) a.PlayerHealth.Died += _ => dead = true;
            float deadAt = -1f;
            int frames = Mathf.RoundToInt(60f / PlaytestSession.Step);
            int nextShot = 5;
            float nextSwing = 0f, nextJump = 2f, nextLock = 8f;
            float hpPrev = a.PlayerHealth != null ? a.PlayerHealth.Current : 0f;
            bool shotHurt = false;
            int startFrame = s.Rec.Samples.Count;

            for (int f = 0; f < frames; f++)
            {
                float t = s.Rec.Now;
                if (dead)
                {
                    if (deadAt < 0f) { deadAt = t; s.Log("player died"); await s.Shot("death"); s.CannotDrive("player died at " + t.ToString("0.0") + " s; bot stopped acting"); }
                    s.In.SetLeftStick(Vector2.zero);
                    if (t - deadAt > 4f) break;
                    await UniTask.Yield();
                    continue;
                }

                // Close to ~3 m of Newton, then hold ground.
                float dist = s.DistanceTo(boss.position);
                float hold = aggressive ? 3.2f : 9f;
                Vector3 toBoss = boss.position - a.Player.transform.position;
                s.In.SetLeftStick(dist > hold + 0.5f ? s.StickToward(toBoss)
                                  : (!aggressive && dist < hold - 1.5f ? s.StickToward(-toBoss) : Vector2.zero));
                if (dist > 12f) s.In.Hold(Key.LeftShift); else s.In.Release(Key.LeftShift);

                if (aggressive && t >= nextSwing && dist < 4.5f)
                {
                    s.In.SetMouseButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left, true);
                    nextSwing = t + 0.35f;
                    await UniTask.Yield();
                    s.In.SetMouseButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left, false);
                }
                if (t >= nextJump) { nextJump = t + 3f; s.In.Hold(Key.Space); await UniTask.DelayFrame(6); s.In.Release(Key.Space); }
                if (t >= nextLock && a.LockOn != null && !a.LockOn.IsLocked) { nextLock = t + 8f; await s.Tap(Key.Tab, 0.08f); }

                if (t >= nextShot) { await s.Shot($"t{nextShot:00}"); nextShot += aggressive ? (nextShot < 30 ? 10 : 15) : 4; }
                if (a.PlayerHealth != null)
                {
                    if (!shotHurt && a.PlayerHealth.Current < hpPrev - 1f) { shotHurt = true; await s.Shot("first-hurt"); }
                    hpPrev = a.PlayerHealth.Current;
                }
                await UniTask.Yield();
            }
            s.In.ReleaseAll();
            if (!dead) await s.Shot("t60-end");
            s.Note(dead ? $"RESULT: player died at {deadAt:0.0} s" : "RESULT: player survived 60 s");
            s.Note($"boss end hp {a.Boss.Health.Current:0}/{a.Boss.Health.Max:0}, phase {a.Boss.Phase}");
            s.Complete();
        }
    }
}
