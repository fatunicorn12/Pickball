using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Pickleball.Sandbox.Enemies;
using Pickleball.Tests.PlayMode.Harness;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Pickleball.Tests.PlayMode.Scenarios
{
    /// <summary>
    /// The basic enemy in the real game: placed enemies stay put until the player is in
    /// range, run in, strike for 10 every 0.5 s, then get cut down by the real sword through
    /// the real input system. Needs Tools ▸ Pickleball ▸ Enemies ▸ Build Enemy Trial Scene.
    /// The notes in summary.md carry the measured numbers.
    /// </summary>
    [Category("Playtest")]
    [Explicit("Playtest harness: run on purpose, never in the normal suite.")]
    public class EnemyScenario
    {
        const string TrialScene = "Assets/_Project/Scenes/Trials/EnemyTrial.unity";

        [UnityTest, Timeout(600000)]
        public IEnumerator Enemy() => UniTask.ToCoroutine(Run);

        static async UniTask Run()
        {
            using var s = await PlaytestSession.Begin("Enemy", TrialScene);
            var a = s.A;

            var near = Find("AlienEnemy_1");
            var far = Find("AlienEnemy_2");
            var farther = Find("AlienEnemy_3");
            if (near == null || far == null || farther == null || a.PlayerHealth == null)
            {
                s.CannotDrive("the trial scene lacks its enemies or the player's Health");
                s.Complete();
                return;
            }

            s.Note($"spawned: HP {near.Health.Current}/{near.Health.Max}, damage {near.Config.AttackDamage}, " +
                   $"interval {near.Config.AttackInterval} s, player HP {a.PlayerHealth.Current}/{a.PlayerHealth.Max}, " +
                   $"player hit grace {a.PlayerHealth.HitGraceSeconds} s");
            s.Note($"placed at: near {near.transform.position}, far {far.transform.position}, " +
                   $"farther {farther.transform.position}");

            // 1. Range: the near one comes, the far ones do not.
            Vector3 nearStart = near.transform.position, farStart = far.transform.position;
            float chaseFrom = Time.time;
            await s.Seconds(0.6f);
            await s.Shot("near-enemy-chasing");
            s.Note($"after 0.6 s: near={near.State}, far={far.State}, farther={farther.State}, " +
                   $"far moved {Vector3.Distance(farStart, far.transform.position):0.00} m");

            // 2. Contact: time the run, then measure strikes against an idle player.
            float give = Time.time + 10f;
            while (near.State != EnemyState.Attack && Time.time < give) await s.Frames(1);
            float flat = Flat(near.transform.position, a.Player.transform.position);
            s.Note($"reached the player in {Time.time - chaseFrom:0.00} s, covering " +
                   $"{Flat(nearStart, near.transform.position):0.00} m, stopping {flat:0.00} m away; state={near.State}");

            var hurts = new List<float>();
            float lost = 0f;
            void OnPlayerHurt(float amount, Vector3 _) { hurts.Add(Time.time); lost += amount; }
            int strikes = 0;
            void OnStruck() => strikes++;
            a.PlayerHealth.Hurt += OnPlayerHurt;
            near.Struck += OnStruck;
            float before = a.PlayerHealth.Current;
            await s.Seconds(1.6f);
            await s.Shot("near-enemy-striking");
            await s.Seconds(1.4f);
            near.Struck -= OnStruck;
            s.Note($"3 s of contact: {strikes} strikes, {hurts.Count} landed, {lost:0} damage, " +
                   $"player HP {before:0} to {a.PlayerHealth.Current:0}, gaps {Gaps(hurts)}");
            a.PlayerHealth.Hurt -= OnPlayerHurt;

            // 3. Run off: it follows, then gives up past the lose range.
            a.PlayerHealth.Heal(a.PlayerHealth.Max);
            await s.Teleport(near.transform.position + new Vector3(-9f, 0.05f, -9f), near.transform.position);
            await s.Seconds(0.3f);
            s.Note($"player 12.7 m away: near={near.State}");
            await s.Teleport(new Vector3(-26f, 0.05f, 14f), near.transform.position);
            await s.Seconds(0.3f);
            s.Note($"player {Vector3.Distance(near.transform.position, a.Player.transform.position):0.0} m away: " +
                   $"near={near.State} (gives up beyond {near.Config.LoseRange} m)");

            // 4. Walk into the far one's range: it wakes.
            a.PlayerHealth.Heal(a.PlayerHealth.Max);
            await s.Teleport(far.transform.position + new Vector3(-8f, 0.05f, 8f), far.transform.position);
            await s.Seconds(1.2f);
            await s.Shot("far-enemy-woken");
            s.Note($"beside the far one: far={far.State}, moved {Vector3.Distance(farStart, far.transform.position):0.00} m");

            // 5. Kill the near one with the real sword, keeping the player topped up.
            await s.Teleport(near.transform.position + new Vector3(-1.2f, 0.05f, -1.4f), near.transform.position);
            float fightFrom = Time.time;
            int swings = 0, breakOuts = 0;
            bool defeated = false;
            void OnBreak() => breakOuts++;
            void OnDefeat() => defeated = true;
            near.BrokeOut += OnBreak;
            near.Defeated += OnDefeat;
            float shotAt = 0f;
            give = Time.time + 60f;
            while (!defeated && Time.time < give)
            {
                a.PlayerHealth.Heal(a.PlayerHealth.Max);
                if (Flat(near.transform.position, a.Player.transform.position) > 2f)
                    await s.Teleport(near.transform.position + new Vector3(-1.2f, 0.05f, -1.4f), near.transform.position);
                await s.Click(MouseButton.Left, 0.05f);
                swings++;
                await s.Seconds(0.25f);
                if (shotAt == 0f && near.Health.Current < near.Health.Max * 0.8f)
                {
                    shotAt = Time.time;
                    await s.Shot("sword-on-enemy");
                }
            }
            s.Note($"sword: {swings} clicks over {Time.time - fightFrom:0.0} s, HP left {near.Health.Current:0}, " +
                   $"break-outs {breakOuts}, defeated={defeated}, state={near.State}");

            await s.Seconds(0.4f);
            await s.Shot("enemy-toppling");
            await s.Seconds(near != null ? near.Config.CorpseSeconds + 0.5f : 1f);
            s.Note($"after the corpse time, the body is gone: {near == null}");
            await s.Shot("enemy-gone");

            near = null;
            s.Complete();
        }

        static Enemy Find(string name)
        {
            var go = GameObject.Find(name);
            return go != null ? go.GetComponent<Enemy>() : null;
        }

        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        static string Gaps(List<float> times)
        {
            var gaps = new List<string>();
            for (int i = 1; i < times.Count; i++) gaps.Add((times[i] - times[i - 1]).ToString("0.000"));
            return "[" + string.Join(", ", gaps) + "]";
        }
    }
}
