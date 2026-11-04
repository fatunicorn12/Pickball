using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Pickleball.Sandbox.Movement;
using Pickleball.Sandbox.Movement.Presentation;
using Pickleball.Tests.PlayMode.Harness;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Pickleball.Tests.PlayMode.Scenarios
{
    /// <summary>
    /// The side flip's sparks through real skids: straight away from the camera, across it (a side
    /// view), and with Jump pressed mid-window. Notes how long the window lasted, whether sparks
    /// kept being added after it closed, and how the spot compares with each foot as the skid began.
    /// Shoots every frame of each skid. Records; it does not judge the look.
    /// </summary>
    [Category("Playtest")]
    [Explicit("Playtest harness: run on purpose, never in the normal suite.")]
    public class SideFlipSparksScenario
    {
        const float Step = 1f / 60f;

        [UnityTest, Timeout(900000)]
        public IEnumerator Skids() => UniTask.ToCoroutine(Run);

        static async UniTask Run()
        {
            using var s = await PlaytestSession.Begin("SideFlipSparks", PlaytestScenes.TrainingStage);
            var sparks = s.A.Player.GetComponentInChildren<SideFlipSparks>(true);
            if (sparks == null) { s.CannotDrive("no SideFlipSparks on the player"); s.Complete(); return; }

            await Skid(s, sparks, "away", new Vector3(6f, 0.05f, -26f), Vector2.up, jumpAtFrame: -1);
            await Skid(s, sparks, "across", new Vector3(-12f, 0.05f, -24f), Vector2.right, jumpAtFrame: -1);
            await Skid(s, sparks, "flip", new Vector3(-12f, 0.05f, -24f), Vector2.right, jumpAtFrame: 4);
            s.Complete();
        }

        static async UniTask Skid(PlaytestSession s, SideFlipSparks sparks, string label, Vector3 start,
                                  Vector2 stick, int jumpAtFrame)
        {
            var motor = s.A.Player.GetComponent<MovementMotor>();
            var animator = s.A.Player.GetComponentInChildren<Animator>();
            var system = sparks.GetComponent<ParticleSystem>();
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightFoot);

            await s.Teleport(start, start + Vector3.forward * 10f);
            s.In.SetLeftStick(stick);
            s.In.Hold(Key.LeftShift);
            await s.Seconds(2f);

            int opened = -1, closed = -1, frame = 0;
            Vector3 travel = default, leftAtOpen = default, rightAtOpen = default, rootAtOpen = default;
            MoveKind leftFor = MoveKind.Ground;
            void Watch(MoveKind from, MoveKind to)
            {
                if (to == MoveKind.SkidTurn && opened < 0)
                {
                    opened = frame;
                    travel = motor.TravelDirection;
                    leftAtOpen = left.position;
                    rightAtOpen = right.position;
                    rootAtOpen = motor.transform.position;
                }
                else if (from == MoveKind.SkidTurn && closed < 0)
                {
                    closed = frame;
                    leftFor = to;
                }
            }
            motor.MoveChanged += Watch;

            s.Log($"{label}: reverse the stick");
            s.In.SetLeftStick(-stick);
            int peak = 0, atClose = 0, addedAfterClose = 0, previous = 0;
            Vector3 spot = default;
            for (frame = 0; frame < 48; frame++)
            {
                if (frame == jumpAtFrame) s.In.Hold(Key.Space);
                if (frame == jumpAtFrame + 3) s.In.Release(Key.Space);
                await s.Shot($"{label}-{frame:00}");

                int count = system.particleCount;
                peak = Mathf.Max(peak, count);
                if (opened >= 0 && closed < 0) spot = sparks.transform.position;
                if (closed >= 0)
                {
                    if (frame == closed) atClose = count;
                    else if (count > previous) addedAfterClose += count - previous;
                }
                previous = count;
            }
            motor.MoveChanged -= Watch;
            s.In.SetLeftStick(Vector2.zero);
            s.In.Release(Key.LeftShift);
            await s.Seconds(1.5f);

            if (opened < 0) { s.CannotDrive($"{label}: no skid happened"); return; }
            float l = Vector3.Dot(leftAtOpen - rootAtOpen, travel), r = Vector3.Dot(rightAtOpen - rootAtOpen, travel);
            s.Note($"{label}: window {closed - opened} frames ({(closed - opened) * Step:0.000} s), left into {leftFor}, " +
                   $"jump kind {motor.CurrentJump}; particles peak {peak}, at close {atClose}, added after close {addedAfterClose}");
            s.Note($"{label}: along travel from his root at open: left foot {l:0.00} m, right foot {r:0.00} m " +
                   $"-> further is {(l > r ? "left" : "right")}; spot {Vector3.Dot(spot - rootAtOpen, travel):0.00} m along travel, " +
                   $"{spot.y - rootAtOpen.y:0.00} m above his root, " +
                   $"{Vector3.Distance(Flat(spot), Flat(l > r ? leftAtOpen : rightAtOpen)):0.00} m from that foot");
        }

        /// <summary>The promise the sparks make: while any are on screen, Jump gives a side flip.
        /// Presses Jump at a spread of moments after reversing and notes what was on screen
        /// against what came out.</summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator SparksMeanSideFlip() => UniTask.ToCoroutine(RunPromise);

        static async UniTask RunPromise()
        {
            using var s = await PlaytestSession.Begin("SideFlipSparksPromise", PlaytestScenes.TrainingStage);
            var sparks = s.A.Player.GetComponentInChildren<SideFlipSparks>(true);
            if (sparks == null) { s.CannotDrive("no SideFlipSparks on the player"); s.Complete(); return; }
            var motor = s.A.Motor;
            var system = sparks.GetComponent<ParticleSystem>();
            var downRight = new Vector2(1f, -1f).normalized;
            int broken = 0;

            foreach (var (label, reverse) in new[] { ("down-right", downRight), ("straight back", Vector2.down) })
            foreach (int press in new[] { 2, 6, 10, 13, 16, 20, 26, 34 })
            {
                var start = new Vector3(6f, 0.05f, -26f);
                await s.Teleport(start, start + Vector3.forward * 10f);
                s.In.SetLeftStick(Vector2.up);
                s.In.Hold(Key.LeftShift);
                await s.Seconds(2f);

                s.In.SetLeftStick(reverse);
                int visibleAtPress = 0;
                MoveKind moveAtPress = MoveKind.Ground;
                for (int frame = 0; frame <= press; frame++)
                {
                    await s.Frames(1);
                    visibleAtPress = system.particleCount;
                    moveAtPress = motor.Current;
                }
                s.In.Hold(Key.Space);
                await s.Frames(3);
                MoveKind after = motor.Current;
                JumpKind kind = motor.CurrentJump;
                s.In.Release(Key.Space);
                s.In.SetLeftStick(Vector2.zero);
                s.In.Release(Key.LeftShift);
                await s.Seconds(2.2f);

                bool flip = after == MoveKind.Jump && kind == JumpKind.SideFlip;
                bool promiseBroken = visibleAtPress > 0 && !flip;
                if (promiseBroken) broken++;
                s.Note($"{label}, jump at frame {press}: {moveAtPress}, {visibleAtPress} sparks visible -> {after}/{kind}: " +
                       (promiseBroken ? "BROKEN PROMISE" : "ok"));
            }
            s.Complete();
            // A rule, not a matter of feel, so unlike the rest of the harness this one fails.
            Assert.That(broken, Is.EqualTo(0), "sparks were on screen when Jump would not give a side flip");
        }

        static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
    }
}
