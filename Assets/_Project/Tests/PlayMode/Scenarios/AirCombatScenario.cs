using System.Collections;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Combat;
using Pickleball.Tests.PlayMode.Harness;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Pickleball.Tests.PlayMode.Scenarios
{
    /// <summary>
    /// The Kingdom-Hearts-style air game on a Training stage dummy: an air swing homing across a
    /// gap and stopping short of the target, a launcher followed by an air chain, the air finisher
    /// throwing the target clear, and a long combo making it break out. Records what happened
    /// (distances, heights, reactions); it does not judge feel.
    /// </summary>
    [Category("Playtest")]
    [Explicit("Playtest harness: run on purpose, never in the normal suite.")]
    public class AirCombatScenario
    {
        [UnityTest, Timeout(900000)]
        public IEnumerator AirCombat() => UniTask.ToCoroutine(Run);

        static async UniTask Click(PlaytestSession s, int count, float interval)
        {
            for (int i = 0; i < count; i++)
            {
                await s.Click(MouseButton.Left, 0.05f);
                await s.Seconds(Mathf.Max(0f, interval - 0.05f));
            }
        }

        static float Planar(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        static async UniTask Run()
        {
            using var s = await PlaytestSession.Begin("AirCombat", PlaytestScenes.TrainingStage);
            if (s.A.Melee == null) { s.CannotDrive("no MeleeCombat on the player"); s.Complete(); return; }
            var root = GameObject.Find("Dummy_Floor");
            if (root == null) { s.CannotDrive("Dummy_Floor not found"); s.Complete(); return; }
            var dummy = root.GetComponent<TrainingDummy>();
            var body = root.GetComponent<CapsuleCollider>();
            Vector3 target = root.transform.position;
            // Straight back along the lane: a diagonal approach clips PlatformRight's corner
            // at jump height and stands him on it, which reads as a mid-air landing.
            Vector3 away = Vector3.back;

            // Test the code's defaults, not whatever the imported asset baked: a changed
            // default with no change of layout doesn't reimport what carries the component,
            // so in a worktree that has already imported it the old values stay.
            var homingField = typeof(MeleeCombat).GetField("airHoming", BindingFlags.Instance | BindingFlags.NonPublic);
            var loaded = (AirHomingSettings)homingField.GetValue(s.A.Melee);
            s.Note($"airHoming as loaded: stopGap {loaded.StopGap:0.00}, dropTolerance {loaded.DropTolerance:0.00}; " +
                   $"code default: stopGap {AirHomingSettings.Default.StopGap:0.00}, dropTolerance {AirHomingSettings.Default.DropTolerance:0.00}");
            homingField.SetValue(s.A.Melee, AirHomingSettings.Default);

            int hits = 0;
            float lastHitHeight = 0f;
            s.A.Melee.HitLanded += _ =>
            {
                hits++;
                lastHitHeight = dummy.Juggle.Height;
                s.Rec.Mark("airhit", $"dummy height {dummy.Juggle.Height:0.00} airHits {dummy.Juggle.AirHits} " +
                                     $"thrown {dummy.Juggle.IsKnockedAway} player y {s.A.Player.transform.position.y:0.00}");
            };
            int breakOuts = 0;
            dummy.BrokeOut += () =>
            {
                breakOuts++;
                s.Rec.Mark("breakout", $"at revenge limit, armor {dummy.Revenge.ArmorLeft:0.0} s");
            };

            // ---- homing across a gap, no lock ------------------------------------------
            await s.Teleport(target + away * 3.5f + Vector3.up * 0.05f, target);
            float before = Planar(s.A.Player.transform.position, target);
            int from = s.Rec.Samples.Count;
            s.Log("no lock: jump 3.5 m out, one air swing");
            await s.Tap(Key.Space, 0.25f);
            await s.Seconds(0.15f);
            await Click(s, 1, 0.05f);
            await s.Seconds(0.45f);
            float after = Planar(s.A.Player.transform.position, target);
            float speedAtEnd = new Vector3(s.A.Motor.Velocity.x, 0f, s.A.Motor.Velocity.z).magnitude;
            s.Note($"homing, no lock: planar distance {before:0.00} -> {after:0.00} m, planar speed after {speedAtEnd:0.00} m/s, " +
                   $"hits so far {hits}. {s.Describe(from)}");
            await s.Shot("homing-no-lock");
            await s.UntilGrounded(3f);
            await s.Seconds(1f);

            // ---- how far one air swing connects with a grounded target -------------------
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var tip = (Transform)typeof(MeleeCombat).GetField("bladeTip", flags).GetValue(s.A.Melee);
            var hilt = (Transform)typeof(MeleeCombat).GetField("bladeBase", flags).GetValue(s.A.Melee);
            bool live = false;
            s.A.Melee.BladeLiveChanged += on => live = on;

            async UniTask Sweep(string label, float distance, float jumpHold, float beforeSwing)
            {
                await s.Teleport(target + away * distance + Vector3.up * 0.05f, target);
                int hitsBefore = hits;
                await s.Tap(Key.Space, jumpHold);
                await s.Seconds(beforeSwing);
                await s.Click(MouseButton.Left, 0.05f);
                float low = float.MaxValue, high = float.MinValue, feetLow = float.MaxValue;
                for (int f = 0; f < 40; f++)
                {
                    await UniTask.Yield();
                    if (!live) continue;
                    float feet = s.A.Player.transform.position.y;
                    feetLow = Mathf.Min(feetLow, feet);
                    low = Mathf.Min(low, Mathf.Min(tip.position.y, hilt.position.y) - feet);
                    high = Mathf.Max(high, Mathf.Max(tip.position.y, hilt.position.y) - feet);
                }
                s.Note($"{label}: from {distance:0.0} m, {hits - hitsBefore} hit(s), ended " +
                       $"{Planar(s.A.Player.transform.position, target):0.00} m away; while the blade was live feet " +
                       $"bottomed at y {feetLow:0.00} and the blade ran {low:0.00} to {high:0.00} m above the feet");
                await s.UntilGrounded(3f);
                await s.Seconds(1f);
            }

            foreach (float distance in new[] { 1.5f, 2.5f, 3f }) await Sweep("connect sweep", distance, 0.25f, 0.15f);

            // The same swing with homing off, as it was before air homing existed.
            var homing = typeof(MeleeCombat).GetField("airHoming", flags);
            object tuned = homing.GetValue(s.A.Melee);
            homing.SetValue(s.A.Melee, default(AirHomingSettings));
            await Sweep("homing off, near the apex", 1.3f, 0.25f, 0.15f);
            await Sweep("homing off, just after take-off", 1.3f, 0.05f, 0f);
            homing.SetValue(s.A.Melee, tuned);

            // ---- homing on a lock ------------------------------------------------------
            if (s.A.LockOn == null) s.CannotDrive("no LockOnTargeting on the player");
            else
            {
                await s.Teleport(target + away * 4.5f + Vector3.up * 0.05f, target);
                await s.Tap(Key.Tab, 0.1f);
                await s.Seconds(0.4f);
                var locked = s.A.LockOn.Target;
                s.Note($"locked on {(locked != null ? locked.name : "(nothing)")}");
                if (locked != null)
                {
                    Vector3 lockedAt = locked.transform.position;
                    before = Planar(s.A.Player.transform.position, lockedAt);
                    s.Log("locked: jump and one air swing");
                    await s.Tap(Key.Space, 0.25f);
                    await s.Seconds(0.15f);
                    await Click(s, 1, 0.05f);
                    await s.Seconds(0.45f);
                    s.Note($"homing, locked: planar distance {before:0.00} -> {Planar(s.A.Player.transform.position, lockedAt):0.00} m, " +
                           $"player y {s.A.Player.transform.position.y:0.00}, hits so far {hits}");
                    await s.UntilGrounded(3f);
                    await s.Tap(Key.Tab, 0.1f);
                    await s.Seconds(1f);
                }
            }

            // ---- out of range ----------------------------------------------------------
            await s.Teleport(target + away * 9f + Vector3.up * 0.05f, target);
            before = Planar(s.A.Player.transform.position, target);
            s.Log("9 m out: an air swing should not home");
            await s.Tap(Key.Space, 0.25f);
            await s.Seconds(0.15f);
            await Click(s, 1, 0.05f);
            await s.Seconds(0.45f);
            s.Note($"out of range: planar distance {before:0.00} -> {Planar(s.A.Player.transform.position, target):0.00} m");
            await s.UntilGrounded(3f);
            await s.Seconds(1f);

            // ---- launcher into an air chain, finisher throws ------------------------------
            s.GrantAllSwordAbilities();
            await s.Teleport(target + away * 1.6f + Vector3.up * 0.05f, target);
            hits = 0;
            float peak = 0f;
            float thrownPeak = 0f;
            float colliderPeak = 0f;
            bool wasThrown = false;
            bool recovered = false;
            s.Log("ground chain to the launcher");
            for (int i = 0; i < 7 && !dummy.Juggle.IsAirborne; i++) await Click(s, 1, 0.3f);
            s.Note($"launcher: dummy airborne {dummy.Juggle.IsAirborne}, height {dummy.Juggle.Height:0.00}, after {hits} hits");

            s.Log("jump and mash the air chain");
            await s.Tap(Key.Space, 0.12f);
            int groundHits = hits;
            for (int i = 0; i < 14; i++)
            {
                await s.Click(MouseButton.Left, 0.05f);
                for (int f = 0; f < 12; f++)
                {
                    await UniTask.Yield();
                    peak = Mathf.Max(peak, dummy.Juggle.Height);
                    if (body != null) colliderPeak = Mathf.Max(colliderPeak, body.bounds.center.y - target.y);
                    if (dummy.Juggle.IsKnockedAway)
                    {
                        wasThrown = true;
                        thrownPeak = Mathf.Max(thrownPeak, Planar(root.GetComponent<Health>().BodyOffset + target, target));
                    }
                }
                if (i == 3) await s.Shot("air-chain");
            }
            s.Note($"air chain: {hits - groundHits} hits, dummy height peak {peak:0.00} m, collider centre peak {colliderPeak:0.00} m, " +
                   $"last hit landed with the dummy {lastHitHeight:0.00} m up");
            s.Note($"air finisher: thrown {wasThrown}, thrown {thrownPeak:0.00} m across");
            for (int f = 0; f < 240 && !recovered; f++)
            {
                await UniTask.Yield();
                recovered = wasThrown && dummy.Juggle.CanAct;
            }
            s.Note($"after the throw: can act again {recovered}, armored {dummy.Revenge.IsArmored}");
            await s.Shot("after-throw");
            await s.UntilGrounded(3f);
            await s.Seconds(3f);
            s.Note($"after 3 s: dummy body offset {root.GetComponent<Health>().BodyOffset}");

            // ---- a long combo breaks out -----------------------------------------------
            await s.Teleport(target + away * 1.6f + Vector3.up * 0.05f, target);
            breakOuts = 0;
            hits = 0;
            s.Log("mash the ground chain until the dummy breaks out");
            float tally = 0f;
            for (int i = 0; i < 80 && breakOuts == 0; i++)
            {
                tally = Mathf.Max(tally, dummy.Revenge.Value);
                await Click(s, 1, 0.3f);
            }
            s.Note($"break-out: {breakOuts} after {hits} hits, tally peaked at {tally:0} damage, armored {dummy.Revenge.IsArmored}");
            await s.Shot("break-out");
            int airHitsThen = dummy.Juggle.AirHits;
            float stunThen = dummy.Juggle.HitstunLeft;
            await Click(s, 3, 0.3f);
            s.Note($"while armored: stun {stunThen:0.00} -> {dummy.Juggle.HitstunLeft:0.00}, airborne {dummy.Juggle.IsAirborne}, air hits {airHitsThen} -> {dummy.Juggle.AirHits}");

            s.Complete();
        }
    }
}
