using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Tests.PlayMode.Harness;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Pickleball.Tests.PlayMode.Scenarios
{
    /// <summary>
    /// Harness self-check: loads each scene, lists what the harness found, pushes real input
    /// through the virtual devices and takes one screenshot. Run it first when a scenario
    /// misbehaves, to separate "the harness broke" from "the game did something".
    /// </summary>
    [Category("Playtest")]
    [Explicit("Playtest harness: run on purpose, never in the normal suite.")]
    public class ProbeScenario
    {
        [UnityTest, Timeout(600000)]
        public IEnumerator Probe_ChibiSandbox() => UniTask.ToCoroutine(() => Run("Probe_ChibiSandbox", PlaytestScenes.ChibiSandbox));

        [UnityTest, Timeout(600000)]
        public IEnumerator Probe_TrainingStage() => UniTask.ToCoroutine(() => Run("Probe_TrainingStage", PlaytestScenes.TrainingStage));

        static async UniTask Run(string name, string scene)
        {
            using var s = await PlaytestSession.Begin(name, scene);
            var a = s.A;
            s.Note($"player at {a.Player.transform.position} yaw {a.Player.transform.eulerAngles.y:0}, camera at {a.Camera.transform.position}");
            s.Note("found: " + string.Join(", ", new[]
            {
                a.Motor != null ? "Motor" : "-Motor", a.Melee != null ? "Melee" : "-Melee",
                a.LockOn != null ? "LockOn" : "-LockOn", a.Abilities != null ? "Abilities" : "-Abilities",
                a.SwordAbilities != null ? "SwordAbilities" : "-SwordAbilities", a.Interactor != null ? "Interactor" : "-Interactor",
                a.Director != null ? "Director" : "-Director", a.Boss != null ? "Boss" : "-Boss",
                a.PlayerHealth != null ? "PlayerHealth" : "-PlayerHealth", a.Mana != null ? "Mana" : "-Mana",
                a.Caster != null ? "Caster" : "-Caster", a.CameraRig != null ? "CameraRig" : "-CameraRig",
                a.Training != null ? "Training" : "-Training",
            }));
            foreach (var t in a.Talkables) s.Note($"talkable {t.name} ({t.DisplayName}) at {t.transform.position}");
            if (a.Boss != null) s.Note($"boss {a.Boss.name} at {a.Boss.transform.position} hp {a.Boss.Health.Current}");
            foreach (var h in Object.FindObjectsByType<Health>(FindObjectsSortMode.None).Take(20))
                s.Note($"health {h.name} at {h.transform.position} hp {h.Current}/{h.Max}");
            if (a.Abilities != null)
                s.Note("abilities: " + string.Join(",", System.Enum.GetValues(typeof(AbilityKind)).Cast<AbilityKind>().Where(k => a.Abilities.Has(k))));

            // Geometry the movement and training scenarios aim at (training stage names).
            if (a.Training != null)
                foreach (var key in new[] { "Ramp_15deg", "Ramp_30deg", "Ramp_45deg", "Ramp_60deg", "Wall_6m", "Step_0.15m",
                                            "Step_0.60m", "Gap_3m", "Gap_5m", "Ladder_2.0m", "Plateau", "Tower", "Pyramid_1",
                                            "PlayerSpawn", "Dummy_Floor", "Pedestal_DOUBLE JUMP", "Pedestal_DASH", "Floor" })
                {
                    var go = GameObject.Find(key);
                    if (go == null) { s.Note($"geometry {key}: not found"); continue; }
                    var col = go.GetComponentInChildren<Collider>();
                    s.Note($"geometry {key}: pos {go.transform.position}" + (col != null ? $" bounds centre {col.bounds.center} size {col.bounds.size}" : ""));
                }

            if (a.Training != null)
                foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
                    if (col.name.Contains("Wall") || col.name.Contains("Corridor") || col.name.Contains("Tunnel"))
                        s.Note($"collider {col.name}: centre {col.bounds.center} size {col.bounds.size}");

            await s.Shot("start");

            // Real input: W for a second, then Space.
            Vector3 p0 = a.Player.transform.position;
            s.In.Hold(Key.W);
            await s.Seconds(1.0f);
            s.In.Release(Key.W);
            Vector3 p1 = a.Player.transform.position;
            s.Note($"after W for 1 s: moved {(p1 - p0).magnitude:0.00} m (keyboard {(s.In.Keyboard.wKey.isPressed ? "still down" : "released")})");
            if ((p1 - p0).magnitude < 0.5f) s.CannotDrive("keyboard W did not move the player in the probe");

            await s.Tap(Key.Space, 0.1f);
            await s.Seconds(0.5f);
            await s.Shot("after-jump");
            s.Complete();
        }
    }
}
