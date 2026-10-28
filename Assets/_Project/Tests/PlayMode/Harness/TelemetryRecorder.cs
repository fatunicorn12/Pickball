using System;
using System.Collections.Generic;
using System.Diagnostics;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Combat;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Pickleball.Tests.PlayMode.Harness
{
    /// <summary>One frame of what the player, the systems around them and the camera were doing.</summary>
    [Serializable]
    public class Sample
    {
        public int frame;
        /// <summary>Game time since the scenario began (s).</summary>
        public float t;
        /// <summary>Scaled frame delta (s): fixed by Time.captureDeltaTime in scenarios.</summary>
        public float dt;
        /// <summary>Wall-clock time since the previous sample (ms): the real frame cost.</summary>
        public float wallMs;
        /// <summary>Frame cost (ms) before the pacing sleep: update, physics and scripts, not the sleep.</summary>
        public float workMs;
        /// <summary>True for the first second after a teleport: camera and air metrics skip it.</summary>
        public bool settling;
        public float px, py, pz, vx, vy, vz;
        /// <summary>Planar speed (m/s).</summary>
        public float speed;
        public bool grounded;
        public string move, jump;
        public float timeInMove;
        public bool attacking, hitstop;
        public int step = -1;
        public string stepName, role, track;
        public float swing;
        public string lockTarget;
        public string dialogue;
        public int line = -1;
        public float bossHp = -1, bossMax, bossPhase;
        public float hp = -1, hpMax;
        public int mana = -1, manaMax;
        public float cx, cy, cz, crx, cry, crz;
        /// <summary>Camera to player chest (m).</summary>
        public float camDist;
        /// <summary>Where the player's chest is on screen (viewport 0..1). A steady value while
        /// moving is a tight camera; drift is lag.</summary>
        public float vpx, vpy;
        /// <summary>Camera speed (m/s).</summary>
        public float camSpeed;
    }

    /// <summary>A discrete thing that happened: a state change, swing, hit or a scenario marker.</summary>
    [Serializable]
    public class EventRecord
    {
        public int frame;
        public float t;
        public string kind;
        public string detail;
    }

    /// <summary>An error or exception the game logged.</summary>
    [Serializable]
    public class LogRecord
    {
        public float t;
        public string type;
        public string message;
        public string stack;
    }

    /// <summary>
    /// Samples the actors every frame after everything else has run (execution order is the
    /// last), and hooks their events for the discrete record. Does not change what it watches.
    /// Allocates freely: it is test tooling, not gameplay.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public sealed class TelemetryRecorder : MonoBehaviour
    {
        public readonly List<Sample> Samples = new(4096);
        public readonly List<EventRecord> Events = new(256);
        public readonly List<LogRecord> Logs = new();

        Actors _a;
        float _start;
        int _startFrame;
        long _lastTicks, _paceTicks;
        int _lastTeleportFrame = -9999;
        Vector3 _lastCam;
        bool _hasLastCam;
        bool _recording;

        /// <summary>Count of logged exceptions (hard failure).</summary>
        public int ExceptionCount { get; private set; }

        /// <summary>Count of logged errors and asserts (recorded, not fatal).</summary>
        public int ErrorCount { get; private set; }

        /// <summary>Scenario clock (s): frames since Begin times the fixed step, so it keeps running while a conversation holds Time.timeScale at 0.</summary>
        public float Now => (Time.frameCount - _startFrame) * PlaytestSession.Step;

        /// <summary>Begins sampling and hooks the actors' events.</summary>
        public void Begin(Actors actors)
        {
            _a = actors;
            _start = Time.time;
            _startFrame = Time.frameCount;
            _lastTicks = _paceTicks = Stopwatch.GetTimestamp();
            _recording = true;
            Application.logMessageReceived += OnLog;
            Hook();
        }

        /// <summary>Stops sampling and unhooks everything.</summary>
        public void End()
        {
            _recording = false;
            Application.logMessageReceived -= OnLog;
        }

        void OnDestroy() => Application.logMessageReceived -= OnLog;

        /// <summary>Adds a scenario-authored event (a step started, a screenshot taken).</summary>
        public void Mark(string kind, string detail = "")
        {
            if (kind == "teleport") _lastTeleportFrame = Time.frameCount;
            Events.Add(new EventRecord { frame = Time.frameCount - _startFrame, t = Now, kind = kind, detail = detail });
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (!_recording || type == LogType.Log || type == LogType.Warning) return;
            if (type == LogType.Exception) ExceptionCount++; else ErrorCount++;
            Logs.Add(new LogRecord
            {
                t = Now,
                type = type.ToString(),
                message = condition,
                stack = stackTrace != null && stackTrace.Length > 600 ? stackTrace.Substring(0, 600) : stackTrace,
            });
        }

        void Hook()
        {
            if (_a.Motor != null) _a.Motor.MoveChanged += (from, to) => Mark("move", $"{from} -> {to} ({_a.Motor.CurrentJump})");
            if (_a.Melee != null)
            {
                _a.Melee.SwingStarted += s => Mark("swing",
                    $"{(s.Step != null ? s.Step.DisplayName : "?")} #{s.Index} {s.Track} {s.Role}{(s.IsLast ? " last" : "")}");
                _a.Melee.ChainEnded += s => Mark("chainEnd", $"after {(s.Step != null ? s.Step.DisplayName : "?")} #{s.Index}");
                _a.Melee.HitLanded += r => Mark("hit",
                    $"{(r.Collider != null ? r.Collider.name : "?")} dmg {r.Damage:0.0}{(r.FromBurst ? " burst" : "")}");
            }
            if (_a.PlayerHealth != null)
            {
                _a.PlayerHealth.Hurt += (amt, at) => Mark("playerHurt", $"{amt:0.0} hp now {_a.PlayerHealth.Current:0.0}");
                _a.PlayerHealth.Died += _ => Mark("playerDied");
            }
            if (_a.Boss != null && _a.Boss.Health != null)
            {
                _a.Boss.Health.Hurt += (amt, at) => Mark("bossHurt", $"{amt:0.0} hp now {_a.Boss.Health.Current:0.0}");
                _a.Boss.Health.Died += _ => Mark("bossDied");
            }
            if (_a.Director != null)
            {
                _a.Director.ConversationStarted += t => Mark("talkStart", t != null ? t.DisplayName : "?");
                _a.Director.LineStarted += (t, i) => Mark("talkLine", $"{i}");
                _a.Director.LineRevealed += (t, i) => Mark("talkLineRevealed", $"{i}");
                _a.Director.ConversationEnded += t => Mark("talkEnd", t != null ? t.LastEndReason.ToString() : "?");
            }
        }

        /// <summary>
        /// Holds each frame to one fixed step of real time. Game time is already fixed by
        /// <c>Time.captureDeltaTime</c>, but several systems (dialogue typing, camera recovery) run on
        /// <c>unscaledDeltaTime</c>, which is real time: without this the frames fly by at 1 ms and
        /// those systems look 15x too slow.
        /// </summary>
        void Pace()
        {
            double target = PlaytestSession.Step * 1000.0;
            double elapsed = (Stopwatch.GetTimestamp() - _paceTicks) * 1000.0 / Stopwatch.Frequency;
            if (elapsed < target - 1.5) System.Threading.Thread.Sleep((int)(target - elapsed - 1.5));
            while ((Stopwatch.GetTimestamp() - _paceTicks) * 1000.0 / Stopwatch.Frequency < target) { }
            _paceTicks = Stopwatch.GetTimestamp();
        }

        void LateUpdate()
        {
            if (!_recording || _a == null || _a.Player == null) return;

            long now = Stopwatch.GetTimestamp();
            float wallMs = (now - _lastTicks) * 1000f / Stopwatch.Frequency;
            _lastTicks = now;

            var s = new Sample { frame = Time.frameCount - _startFrame, t = Now, dt = Time.deltaTime, wallMs = wallMs };
            Vector3 p = _a.Player.transform.position;
            s.px = p.x; s.py = p.y; s.pz = p.z;

            if (_a.Motor != null)
            {
                Vector3 v = _a.Motor.Velocity;
                s.vx = v.x; s.vy = v.y; s.vz = v.z;
                s.speed = new Vector2(v.x, v.z).magnitude;
                s.grounded = _a.Motor.IsGrounded;
                s.move = _a.Motor.Current.ToString();
                s.jump = _a.Motor.CurrentJump.ToString();
                s.timeInMove = _a.Motor.TimeInMove;
            }
            if (_a.Melee != null)
            {
                s.attacking = _a.Melee.IsAttacking;
                s.hitstop = _a.Melee.IsInHitstop;
                s.swing = _a.Melee.SwingProgress;
                var cur = _a.Melee.Current;
                if (cur.IsValid)
                {
                    s.step = cur.Index;
                    s.stepName = cur.Step.DisplayName;
                    s.role = cur.Role.ToString();
                    s.track = cur.Track.ToString();
                }
            }
            if (_a.LockOn != null && _a.LockOn.Target != null) s.lockTarget = _a.LockOn.Target.name;
            if (_a.Director != null)
            {
                s.dialogue = _a.Director.State.ToString();
                s.line = _a.Director.LineIndex;
            }
            if (_a.Boss != null && _a.Boss.Health != null)
            {
                s.bossHp = _a.Boss.Health.Current;
                s.bossMax = _a.Boss.Health.Max;
                s.bossPhase = _a.Boss.Phase;
            }
            if (_a.PlayerHealth != null) { s.hp = _a.PlayerHealth.Current; s.hpMax = _a.PlayerHealth.Max; }
            if (_a.Mana != null) { s.mana = _a.Mana.Current; s.manaMax = _a.Mana.Max; }

            if (_a.Camera != null)
            {
                Transform c = _a.Camera.transform;
                s.cx = c.position.x; s.cy = c.position.y; s.cz = c.position.z;
                Vector3 e = c.eulerAngles;
                s.crx = e.x; s.cry = e.y; s.crz = e.z;
                Vector3 chest = p + Vector3.up * 1.0f;
                s.camDist = Vector3.Distance(c.position, chest);
                Vector3 vp = _a.Camera.WorldToViewportPoint(chest);
                s.vpx = vp.x; s.vpy = vp.y;
                if (_hasLastCam && Time.deltaTime > 0f) s.camSpeed = (c.position - _lastCam).magnitude / Time.deltaTime;
                _lastCam = c.position;
                _hasLastCam = true;
            }
            s.settling = Time.frameCount - _lastTeleportFrame < 70;
            long afterWork = Stopwatch.GetTimestamp();
            s.workMs = (afterWork - _paceTicks) * 1000f / Stopwatch.Frequency;
            Samples.Add(s);
            Pace();
        }
    }
}
