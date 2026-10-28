using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pickleball.Tests.PlayMode.Harness
{
    /// <summary>One named result line in the summary.</summary>
    [Serializable]
    public class Metric
    {
        public string name;
        public string value;
    }

    /// <summary>One airborne stretch, from leaving the ground to landing.</summary>
    [Serializable]
    public class AirEpisode
    {
        public float start, airTime, rise, carry;
        public string moves;
    }

    /// <summary>
    /// Derived numbers over a sample list. Pure maths on the recorded data, so it can be
    /// unit-tested without a scene.
    /// </summary>
    public static class Metrics
    {
        /// <summary>Airborne stretches longer than <paramref name="minAir"/> seconds.</summary>
        public static List<AirEpisode> AirEpisodes(IReadOnlyList<Sample> s, float minAir = 0.12f)
        {
            var result = new List<AirEpisode>();
            int i = 0;
            while (i < s.Count)
            {
                if (s[i].grounded) { i++; continue; }
                int start = i;
                float y0 = i > 0 ? s[i - 1].py : s[i].py;
                float x0 = i > 0 ? s[i - 1].px : s[i].px, z0 = i > 0 ? s[i - 1].pz : s[i].pz;
                float apex = y0;
                var moves = new List<string>();
                while (i < s.Count && !s[i].grounded)
                {
                    apex = Math.Max(apex, s[i].py);
                    string m = s[i].move == "Jump" ? "Jump:" + s[i].jump : s[i].move;
                    if (moves.Count == 0 || moves[moves.Count - 1] != m) moves.Add(m);
                    i++;
                }
                int end = Math.Min(i, s.Count - 1);
                float air = s[end].t - s[start].t;
                if (air < minAir || s[start].settling) continue;
                float dx = s[end].px - x0, dz = s[end].pz - z0;
                result.Add(new AirEpisode
                {
                    start = s[start].t, airTime = air, rise = apex - y0,
                    carry = (float)Math.Sqrt(dx * dx + dz * dz), moves = string.Join(">", moves),
                });
            }
            return result;
        }

        /// <summary>Frame-time percentile (ms) of the wall-clock column.</summary>
        public static float Percentile(IEnumerable<float> values, float p)
        {
            var arr = values.OrderBy(v => v).ToArray();
            if (arr.Length == 0) return 0f;
            int idx = Math.Min(arr.Length - 1, (int)Math.Ceiling(p * arr.Length) - 1);
            return arr[Math.Max(0, idx)];
        }

        /// <summary>Seconds from <paramref name="from"/> to the first sample in move state <paramref name="move"/>, or -1.</summary>
        public static float TimeToMove(IReadOnlyList<Sample> s, string move, float from = 0f)
        {
            foreach (var x in s) if (x.t >= from && x.move == move) return x.t - from;
            return -1f;
        }

        /// <summary>All metrics for the standard summary.</summary>
        public static List<Metric> Compute(IReadOnlyList<Sample> s, IReadOnlyList<EventRecord> events)
        {
            var m = new List<Metric>();
            void Add(string n, string v) => m.Add(new Metric { name = n, value = v });
            if (s.Count == 0) { Add("samples", "0"); return m; }

            float duration = s[s.Count - 1].t;
            Add("duration (game s)", duration.ToString("0.00"));
            Add("frames", s.Count.ToString());

            var wall = s.Skip(1).Select(x => x.workMs).ToList();
            if (wall.Count > 0)
            {
                Add("frame cost ms (excl. pacing sleep) avg / p95 / p99 / max",
                    $"{wall.Average():0.0} / {Percentile(wall, 0.95f):0.0} / {Percentile(wall, 0.99f):0.0} / {wall.Max():0.0}");
                Add("hitches (> 50 ms)", wall.Count(w => w > 50f).ToString());
            }

            Add("max planar speed (m/s)", s.Max(x => x.speed).ToString("0.00"));
            var byMove = s.GroupBy(x => x.move).Where(g => g.Key != null).OrderByDescending(g => g.Count());
            Add("time per move / max speed",
                string.Join("; ", byMove.Select(g => $"{g.Key} {g.Count() / 60f:0.0}s {g.Max(x => x.speed):0.0}m/s")));
            var first = new StringBuilder();
            foreach (var g in byMove.OrderBy(g => g.First().t))
                first.Append($"{g.Key}@{g.First().t:0.00}s ");
            Add("first seen", first.ToString().Trim());

            var air = AirEpisodes(s);
            Add("air episodes", air.Count.ToString());
            foreach (var e in air.Take(14))
                Add($"  air @{e.start:0.00}s", $"rise {e.rise:0.00} m, air {e.airTime:0.00} s, carry {e.carry:0.0} m [{e.moves}]");
            if (air.Count > 0)
                Add("jump apex max (m) / air time max (s)", $"{air.Max(a => a.rise):0.00} / {air.Max(a => a.airTime):0.00}");

            var swings = events.Where(e => e.kind == "swing").ToList();
            if (swings.Count > 0)
            {
                Add("swings", swings.Count.ToString());
                var gaps = new List<string>();
                for (int i = 0; i < swings.Count; i++)
                {
                    float gap = i == 0 ? 0f : swings[i].t - swings[i - 1].t;
                    gaps.Add($"{swings[i].t:0.00}s{(i > 0 ? $" (+{gap:0.00})" : "")} {swings[i].detail}");
                }
                Add("swing timeline", string.Join(" | ", gaps.Take(24)));
            }
            int hits = events.Count(e => e.kind == "hit");
            if (hits > 0) Add("hits landed", hits.ToString());

            float lockFirst = -1f;
            var locks = new List<string>();
            string prev = null;
            foreach (var x in s)
            {
                if (x.lockTarget != prev)
                {
                    if (x.lockTarget != null && lockFirst < 0) lockFirst = x.t;
                    locks.Add($"{x.t:0.0}s:{x.lockTarget ?? "(none)"}");
                    prev = x.lockTarget;
                }
            }
            if (locks.Count > 1) Add("lock-on changes", string.Join(" ", locks.Take(20)));

            var dlg = events.Where(e => e.kind.StartsWith("talk")).ToList();
            if (dlg.Count > 0) Add("dialogue events", string.Join(" | ", dlg.Take(30).Select(e => $"{e.t:0.0}s {e.kind} {e.detail}")));
            var phases = new List<string>();
            string pp = null;
            foreach (var x in s)
                if (x.dialogue != pp) { phases.Add($"{x.t:0.0}s:{x.dialogue}"); pp = x.dialogue; }
            if (phases.Count > 1) Add("dialogue phases", string.Join(" ", phases));

            if (s.Any(x => x.bossMax > 0))
            {
                var last = s[s.Count - 1];
                Add("boss hp start -> end", $"{s.First(x => x.bossMax > 0).bossHp:0} -> {last.bossHp:0} / {last.bossMax:0} (phase {last.bossPhase})");
            }
            if (s.Any(x => x.hpMax > 0))
            {
                var hp = s.Where(x => x.hpMax > 0).ToList();
                Add("player hp start -> min -> end", $"{hp[0].hp:0} -> {hp.Min(x => x.hp):0} -> {hp[hp.Count - 1].hp:0} / {hp[0].hpMax:0}");
            }
            if (s.Any(x => x.manaMax > 0))
            {
                var mn = s.Where(x => x.manaMax > 0).ToList();
                Add("mana start -> min -> end", $"{mn[0].mana} -> {mn.Min(x => x.mana)} -> {mn[mn.Count - 1].mana} / {mn[0].manaMax}");
            }

            var cam = s.Where(x => !x.settling && x.camDist > 0).ToList();   // teleports excluded
            if (cam.Count > 0)
            {
                var cd = cam.Select(x => x.camDist).ToList();
                Add("camera distance min / avg / max (m)", $"{cd.Min():0.00} / {cd.Average():0.00} / {cd.Max():0.00}");
                Add("camera max speed (m/s)", cam.Max(x => x.camSpeed).ToString("0.0"));
                var vx = cam.Select(x => x.vpx).ToList();
                var vy = cam.Select(x => x.vpy).ToList();
                Add("player on-screen x range / y range", $"{vx.Min():0.00}..{vx.Max():0.00} / {vy.Min():0.00}..{vy.Max():0.00}");
            }
            return m;
        }
    }
}
