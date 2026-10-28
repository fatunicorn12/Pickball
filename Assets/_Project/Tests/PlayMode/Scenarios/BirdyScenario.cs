using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;
using Pickleball.Tests.PlayMode.Harness;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Pickleball.Tests.PlayMode.Scenarios
{
    /// <summary>
    /// Breath-of-the-Wild-style talk: walk up to Birdy in the sandbox, wait for the prompt, press
    /// Interact, advance every line, and watch the conversation end. Records the camera and the
    /// dialogue phases; screenshots the prompt, the box and the return to play.
    /// </summary>
    [Category("Playtest")]
    [Explicit("Playtest harness: run on purpose, never in the normal suite.")]
    public class BirdyScenario
    {
        [UnityTest, Timeout(900000)]
        public IEnumerator TalkToBirdy() => UniTask.ToCoroutine(Run);

        static async UniTask Run()
        {
            using var s = await PlaytestSession.Begin("TalkToBirdy", PlaytestScenes.ChibiSandbox);
            var a = s.A;
            if (a.Director == null || a.Interactor == null) { s.CannotDrive("no DialogueDirector / PlayerInteractor in the scene"); s.Complete(); return; }
            Talkable birdy = null;
            foreach (var t in a.Talkables) if (t.name.Contains("Birdy")) birdy = t;
            if (birdy == null && a.Talkables.Length > 0) birdy = a.Talkables[0];
            if (birdy == null) { s.CannotDrive("no Talkable in the scene"); s.Complete(); return; }

            s.Log($"walk to {birdy.name} (stick steering, sprint)");
            bool arrived = await s.WalkTo(() => birdy.transform.position, 2.5f, 25f, sprint: true);
            s.Note($"walk-up {(arrived ? "arrived" : "TIMED OUT")}: player at {a.Player.transform.position}, {s.DistanceTo(birdy.transform.position):0.0} m from Birdy");
            if (!arrived)
            {
                s.CannotDrive("straight-line walk to Birdy was blocked (no path-finding in the harness); teleported 2.5 m short instead");
                Vector3 b = birdy.transform.position;
                Vector3 dir = (a.Player.transform.position - b); dir.y = 0f; dir.Normalize();
                await s.Teleport(b + dir * 2.5f + Vector3.up * 0.3f, b);
            }
            await s.Seconds(0.8f);

            // The prompt should offer itself while we stand near.
            int waited = 0;
            while (a.Interactor.CurrentTarget == null && waited++ < 180) await UniTask.Yield();
            s.Note($"prompt target after {waited / 60f:0.0} s: {(a.Interactor.CurrentTarget != null ? a.Interactor.CurrentTarget.name : "NONE")}");
            await s.Shot("prompt");
            if (a.Interactor.CurrentTarget == null)
            {
                s.CannotDrive("no talk prompt appeared (out of range, off-screen, or ineligible); pressing Interact anyway");
            }

            s.Log("press E (Interact)");
            await s.Tap(Key.E, 0.1f);
            int guard = 0;
            while (a.Director.State == DialogueDirector.Phase.Idle && guard++ < 120) await UniTask.Yield();
            s.Note($"conversation started: {a.Director.State} after {guard / 60f:0.0} s");
            if (a.Director.State == DialogueDirector.Phase.Idle) { s.CannotDrive("pressing E did not start a conversation"); s.Complete(); return; }

            int shots = 0;
            int lineSeen = -1;
            guard = 0;
            while (a.Director.State != DialogueDirector.Phase.Idle && guard++ < 60 * 90)
            {
                if (a.Director.State == DialogueDirector.Phase.Talking)
                {
                    if (a.Director.LineIndex != lineSeen)
                    {
                        lineSeen = a.Director.LineIndex;
                        await s.Seconds(0.4f);
                        if (shots < 3) { await s.Shot($"line-{lineSeen}-typing"); shots++; }
                    }
                    if (a.Director.IsLineRevealed)
                    {
                        await s.Seconds(0.5f);   // read it
                        if (shots < 5) { await s.Shot($"line-{lineSeen}-revealed"); shots++; }
                        s.Log($"advance past line {lineSeen}");
                        await s.Tap(Key.E, 0.08f);
                        await s.Seconds(0.3f);
                    }
                    else await s.Seconds(0.25f);
                }
                else await UniTask.Yield();
            }
            s.Note($"conversation ended: director {a.Director.State}, talkable end reason {birdy.LastEndReason}, last line index {lineSeen}");
            if (guard >= 60 * 90) s.CannotDrive("conversation did not end within 90 s");
            await s.Seconds(1.0f);
            await s.Shot("after");
            s.Note($"player free again? movement test: stick forward for 0.5 s");
            Vector3 p0 = a.Player.transform.position;
            s.In.SetLeftStick(new Vector2(0f, 1f));
            await s.Seconds(0.5f);
            s.In.SetLeftStick(Vector2.zero);
            s.Note($"moved {(a.Player.transform.position - p0).magnitude:0.00} m after the talk (input released: {((a.Player.transform.position - p0).magnitude > 0.3f)})");
            s.Complete();
        }
    }
}
