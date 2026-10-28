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
    /// Smash-training-mode readability pass: load the Training stage, look around with the mouse,
    /// walk, jump, swing at a dummy, aim and fire the spell, and photograph the labelled areas.
    /// The screenshots are the product here; the telemetry is for the readout and the HUD.
    /// </summary>
    [Category("Playtest")]
    [Explicit("Playtest harness: run on purpose, never in the normal suite.")]
    public class TrainingStageScenario
    {
        [UnityTest, Timeout(900000)]
        public IEnumerator TrainingStage() => UniTask.ToCoroutine(Run);

        static async UniTask Run()
        {
            using var s = await PlaytestSession.Begin("TrainingStage", PlaytestScenes.TrainingStage);
            var a = s.A;
            await s.Shot("spawn");

            // Mouse look: left then right, then back.
            s.Log("mouse look");
            for (int i = 0; i < 20; i++) { s.In.MoveMouse(new Vector2(-25f, 0f)); await UniTask.Yield(); }
            await s.Seconds(0.3f);
            await s.Shot("look-left");
            for (int i = 0; i < 40; i++) { s.In.MoveMouse(new Vector2(25f, 0f)); await UniTask.Yield(); }
            await s.Seconds(0.3f);
            await s.Shot("look-right");
            for (int i = 0; i < 20; i++) { s.In.MoveMouse(new Vector2(-25f, 0f)); await UniTask.Yield(); }
            await s.Seconds(0.3f);

            // Basic movement with the keyboard: walk, sprint, jump; the readout should report them.
            s.Log("keyboard: W, sprint, jump");
            s.In.Hold(Key.W);
            await s.Seconds(1.0f);
            s.In.Hold(Key.LeftShift);
            await s.Seconds(1.0f);
            await s.Tap(Key.Space, 0.35f);
            await s.UntilGrounded(3f);
            s.In.Release(Key.LeftShift);
            s.In.Release(Key.W);
            await s.Seconds(0.5f);
            await s.Shot("after-run-jump-readout");

            // Pedestals: step up to the ability pickups.
            await s.Teleport(new Vector3(-4.5f, 0.05f, 0.5f), new Vector3(-4.5f, 0f, 4f));
            await s.Shot("pedestals");

            // Sword at the floor dummy.
            var dummy = GameObject.Find("Dummy_Floor");
            if (dummy == null) s.CannotDrive("Dummy_Floor not found");
            else
            {
                Vector3 d = dummy.transform.position;
                await s.Teleport(d + new Vector3(-1.2f, 0.05f, -1.4f), d);
                s.Log("sword: 3 clicks");
                for (int i = 0; i < 3; i++) { await s.Click(MouseButton.Left, 0.05f); await s.Seconds(0.25f); }
                await s.Shot("sword-on-dummy");
                await s.Seconds(1.0f);
            }

            // Spell: hold right mouse to aim, click left to fire.
            if (a.Caster == null || a.Caster.Spell == null) s.CannotDrive("no SpellCaster/spell assigned on the player in this scene");
            else
            {
                int manaBefore = a.Mana != null ? a.Mana.Current : -1;
                s.Log("spell: hold RMB to aim");
                s.In.SetMouseButton(MouseButton.Right, true);
                await s.Seconds(0.6f);
                s.Note($"aiming: caster.IsAiming={a.Caster.IsAiming}");
                await s.Shot("spell-aim");
                await s.Click(MouseButton.Left, 0.06f);
                await s.Seconds(0.3f);
                await s.Shot("spell-fire");
                s.In.SetMouseButton(MouseButton.Right, false);
                await s.Seconds(1.0f);
                s.Note($"mana {manaBefore} -> {(a.Mana != null ? a.Mana.Current : -1)} after one cast");
            }

            // Readability tour of the labelled areas.
            foreach (var (label, pos, look) in new[]
            {
                ("ramps",   new Vector3(-20f, 0.05f, -14f), new Vector3(-20f, 0f, -3f)),
                ("steps",   new Vector3(-21f, 0.05f, -18f), new Vector3(-21f, 0f, -14f)),
                ("tower",   new Vector3(14f, 0.05f, 4f),    new Vector3(19.5f, 0f, 10.5f)),
                ("gaps",    new Vector3(30f, 0.05f, -26f),  new Vector3(45f, 0f, -26f)),
                ("overview", new Vector3(0f, 0.05f, -20f),  new Vector3(0f, 0f, 10f)),
            })
            {
                await s.Teleport(pos, look);
                await s.Shot("tour-" + label);
            }
            s.Complete();
        }
    }
}
