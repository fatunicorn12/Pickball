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
    /// Sunshine-style movement on the Training stage's flat floor, ramps and wall: walk, run,
    /// sprint, skid, single / double / triple jump, dive, belly slide, dash, crouch, wall contact
    /// and slopes. Records; never fails on feel.
    /// </summary>
    [Category("Playtest")]
    [Explicit("Playtest harness: run on purpose, never in the normal suite.")]
    public class MovementScenario
    {
        // A clear lane on the stage floor (floor spans x -28..28, z -28..16).
        static readonly Vector3 Lane = new(6f, 0.05f, -26f);

        [UnityTest, Timeout(900000)]
        public IEnumerator Movement() => UniTask.ToCoroutine(Run);

        static async UniTask Run()
        {
            using var s = await PlaytestSession.Begin("Movement", PlaytestScenes.TrainingStage);
            s.GrantAllMovementAbilities();
            var kb = s.In;
            Vector3 north = Vector3.forward;

            // ---- walk, run, sprint, stop, skid ----------------------------------------
            await s.Teleport(Lane, Lane + north * 10f);
            s.Log("walk (stick 0.45 on the pad)");
            int mark = s.Rec.Samples.Count;
            kb.SetLeftStick(new Vector2(0f, 0.45f));
            await s.Seconds(1.5f);
            s.Note($"walk 1.5 s: {s.Describe(mark)}");
            await s.Shot("walk");

            s.Log("run (stick 1.0)");
            mark = s.Rec.Samples.Count;
            kb.SetLeftStick(new Vector2(0f, 1f));
            await s.Seconds(1.5f);
            s.Note($"run 1.5 s: {s.Describe(mark)}");

            s.Log("sprint (hold Shift)");
            mark = s.Rec.Samples.Count;
            kb.Hold(Key.LeftShift);
            await s.Seconds(3f);
            s.Note($"sprint 3 s: {s.Describe(mark)}");
            await s.Shot("sprint");

            s.Log("release stick at sprint speed (run-stop)");
            mark = s.Rec.Samples.Count;
            kb.SetLeftStick(Vector2.zero);
            kb.Release(Key.LeftShift);
            await s.Seconds(0.2f);
            await s.Shot("run-stop");
            await s.Seconds(0.8f);
            s.Note($"stop from sprint: {s.Describe(mark)}");

            await s.Teleport(Lane, Lane + north * 10f);
            s.Log("sprint then reverse (skid turn)");
            kb.SetLeftStick(new Vector2(0f, 1f));
            kb.Hold(Key.LeftShift);
            await s.Seconds(2.5f);
            mark = s.Rec.Samples.Count;
            kb.SetLeftStick(new Vector2(0f, -1f));
            await s.Seconds(0.8f);
            await s.Shot("skid");
            s.Note($"reverse at sprint: {s.Describe(mark)}");
            kb.SetLeftStick(Vector2.zero);
            kb.Release(Key.LeftShift);
            await s.Seconds(0.5f);

            // ---- single jump: tap vs hold ---------------------------------------------
            await s.Teleport(Lane, Lane + north * 10f);
            s.Log("tap jump (Space 0.05 s)");
            mark = s.Rec.Samples.Count;
            await s.Tap(Key.Space, 0.05f);
            await s.UntilGrounded(3f);
            await s.Seconds(0.3f);
            s.Note($"tap jump: {s.Describe(mark)}");

            s.Log("held jump (Space 0.6 s)");
            mark = s.Rec.Samples.Count;
            kb.Hold(Key.Space);
            await s.Seconds(0.25f);
            await s.Shot("jump-apex");
            await s.Seconds(0.35f);
            kb.Release(Key.Space);
            await s.UntilGrounded(3f);
            await s.Seconds(0.3f);
            s.Note($"held jump: {s.Describe(mark)}");

            // ---- double jump (feather) ------------------------------------------------
            s.Log("double jump");
            mark = s.Rec.Samples.Count;
            await s.Tap(Key.Space, 0.3f);
            await s.Seconds(0.25f);
            await s.Tap(Key.Space, 0.3f);
            await s.Seconds(0.3f);
            await s.Shot("double-jump");
            await s.UntilGrounded(4f);
            await s.Seconds(0.3f);
            s.Note($"double jump: {s.Describe(mark)}");

            // ---- triple jump: three jumps landing running -----------------------------
            await s.Teleport(Lane, Lane + north * 10f);
            s.Log("triple jump chain at run speed");
            kb.SetLeftStick(new Vector2(0f, 1f));
            kb.Hold(Key.LeftShift);
            await s.Seconds(1.8f);
            mark = s.Rec.Samples.Count;
            for (int i = 0; i < 3; i++)
            {
                await s.Tap(Key.Space, 0.25f);
                await s.UntilAirborne(0.5f);
                if (i == 2) { await s.Seconds(0.3f); await s.Shot("triple-jump"); }
                await s.UntilGrounded(3f);
            }
            s.Note($"triple chain (3 jumps on landing): {s.Describe(mark)}; jump kinds seen: " +
                   string.Join(",", Distinct(s, mark, x => x.jump)));
            kb.SetLeftStick(Vector2.zero);
            kb.Release(Key.LeftShift);
            await s.Seconds(0.5f);

            // ---- dive and belly slide -------------------------------------------------
            await s.Teleport(Lane, Lane + north * 10f);
            s.Log("sprint, jump, dive (Ctrl in the air), belly slide");
            kb.SetLeftStick(new Vector2(0f, 1f));
            kb.Hold(Key.LeftShift);
            await s.Seconds(2f);
            mark = s.Rec.Samples.Count;
            await s.Tap(Key.Space, 0.12f);
            await s.Seconds(0.15f);
            await s.Tap(Key.LeftCtrl, 0.1f);
            await s.Seconds(0.25f);
            await s.Shot("dive");
            await s.UntilGrounded(3f);
            kb.SetLeftStick(Vector2.zero);
            kb.Release(Key.LeftShift);
            await s.Seconds(0.3f);
            await s.Shot("belly-slide");
            await s.Seconds(1.5f);
            s.Note($"dive + slide: {s.Describe(mark)}");

            // ---- dash -----------------------------------------------------------------
            await s.Teleport(Lane, Lane + north * 10f);
            s.Log("ground dash (E), then air dash");
            kb.SetLeftStick(new Vector2(0f, 1f));
            await s.Seconds(0.8f);
            mark = s.Rec.Samples.Count;
            await s.Tap(Key.E, 0.1f);
            await s.Seconds(0.15f);
            await s.Shot("dash");
            await s.Seconds(0.7f);
            s.Note($"ground dash: {s.Describe(mark)}");
            mark = s.Rec.Samples.Count;
            await s.Tap(Key.Space, 0.2f);
            await s.Seconds(0.25f);
            await s.Tap(Key.E, 0.1f);
            await s.Seconds(0.6f);
            s.Note($"air dash: {s.Describe(mark)}");
            await s.UntilGrounded(3f);
            kb.SetLeftStick(Vector2.zero);
            await s.Seconds(0.5f);

            // ---- crouch ---------------------------------------------------------------
            s.Log("crouch and crouch-walk");
            mark = s.Rec.Samples.Count;
            kb.Hold(Key.LeftCtrl);
            await s.Seconds(0.6f);
            kb.SetLeftStick(new Vector2(0f, 1f));
            await s.Seconds(1f);
            await s.Shot("crouch-walk");
            kb.SetLeftStick(Vector2.zero);
            kb.Release(Key.LeftCtrl);
            await s.Seconds(0.5f);
            s.Note($"crouch: {s.Describe(mark)}");

            // ---- wall contact: the wall-kick corridor (CorridorWallWest x -27.5..-26.5, CorridorWallEast
            // x -24.5..-23.5, both z 5..14, 10 m tall). Start between them, sprint at the east wall.
            var wall = GameObject.Find("CorridorWallEast");
            if (wall == null) s.CannotDrive("CorridorWallEast not found on the Training stage: no wall contact test");
            else
            {
                Vector3 wallFace = new(wall.transform.position.x - 0.5f, 0.05f, wall.transform.position.z);
                await s.Teleport(wallFace + Vector3.left * 1.3f, wallFace);
                if (Physics.Raycast(s.A.Player.transform.position + Vector3.up, Vector3.right, out var ahead, 20f))
                    s.Note($"first collider ahead of the wall run: {ahead.collider.name} at {ahead.distance:0.0} m");
                s.Log("sprint into the wall");
                mark = s.Rec.Samples.Count;
                kb.SetLeftStick(new Vector2(0f, 1f));
                kb.Hold(Key.LeftShift);
                await s.Seconds(2.2f);
                await s.Shot("wall-contact");
                s.Note($"sprint into wall (face x {wallFace.x:0.0}): {s.Describe(mark)}; ended at x {s.A.Player.transform.position.x:0.00}");
                s.Log("jump into the wall");
                mark = s.Rec.Samples.Count;
                await s.Tap(Key.Space, 0.4f);
                await s.Seconds(0.5f);
                await s.Shot("wall-jump");
                await s.UntilGrounded(3f);
                s.Note($"jump against wall: {s.Describe(mark)} (no wall-kick state exists in MoveKind; the stage only labels the wall)");
                kb.SetLeftStick(Vector2.zero);
                kb.Release(Key.LeftShift);
                await s.Seconds(0.5f);
            }

            // ---- slopes (ramps climb toward +z; bases at y = 0) -----------------------
            foreach (var (name, x) in new[] { ("Ramp_15deg", -26f), ("Ramp_30deg", -22f), ("Ramp_45deg", -18f), ("Ramp_60deg", -14f) })
            {
                var ramp = GameObject.Find(name);
                if (ramp == null) { s.CannotDrive($"{name} not found on the Training stage"); continue; }
                float baseZ = ramp.transform.position.z;
                await s.Teleport(new Vector3(x, 0.05f, baseZ - 3f), new Vector3(x, 0f, baseZ + 3f));
                s.Log($"walk up {name}");
                mark = s.Rec.Samples.Count;
                kb.SetLeftStick(new Vector2(0f, 1f));
                await s.Seconds(4f);
                if (name == "Ramp_30deg") await s.Shot("slope-30");
                kb.SetLeftStick(Vector2.zero);
                s.Note($"{name}: {s.Describe(mark)}");
                await s.Seconds(0.3f);
            }

            s.Complete();
        }

        static System.Collections.Generic.IEnumerable<string> Distinct(PlaytestSession s, int from, System.Func<Sample, string> pick)
        {
            var seen = new System.Collections.Generic.List<string>();
            for (int i = from; i < s.Rec.Samples.Count; i++)
            {
                string v = pick(s.Rec.Samples[i]);
                if (v != null && !seen.Contains(v)) seen.Add(v);
            }
            return seen;
        }
    }
}
