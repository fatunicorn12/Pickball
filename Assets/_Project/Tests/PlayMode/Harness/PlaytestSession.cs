using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Pickleball.Tests.PlayMode.Harness
{
    /// <summary>The scene paths scenarios load. Read from the project; never edited.</summary>
    public static class PlaytestScenes
    {
        public const string ChibiSandbox = "Assets/_Project/Scenes/ChibiSandbox.unity";
        public const string TrainingStage = "Assets/_Project/Scenes/TrainingStage.unity";
    }

    /// <summary>Serialized form of one scenario run (telemetry.json).</summary>
    [Serializable]
    public class ScenarioReport
    {
        public string scenario, runId, scene, startedAt;
        public bool exceptionFree;
        public float gameSeconds;
        public List<string> cannotDrive = new();
        public List<string> notes = new();
        public List<string> screenshots = new();
        public List<Metric> metrics = new();
        public List<AirEpisode> airEpisodes = new();
        public List<EventRecord> events = new();
        public List<LogRecord> errors = new();
        public List<Sample> samples = new();
    }

    /// <summary>
    /// One scenario's world: the loaded scene, virtual devices, the telemetry recorder, and the
    /// helpers to wait, steer and photograph. Dispose removes the devices, restores time and
    /// writes the outputs, whether the scenario finished or threw.
    ///
    /// Frames are paced by <c>Time.captureDeltaTime</c>, so a scenario at "2 seconds" is
    /// 120 frames of 1/60 s regardless of how fast the machine renders them.
    /// </summary>
    public sealed class PlaytestSession : IDisposable
    {
        /// <summary>The fixed step every frame advances game time by (s).</summary>
        public const float Step = 1f / 60f;

        readonly List<string> _cannot = new();
        readonly List<string> _notes = new();
        readonly List<string> _shots = new();
        readonly string _dir;
        readonly string _scenePath;
        readonly DateTime _started = DateTime.Now;
        bool _written;
        bool _disposed;
        int _shotIndex;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode? _restoreInputBehaviour;
        InputSettings.BackgroundBehavior _restoreBackground;
#endif

        public string Name { get; }
        public Actors A { get; private set; }
        public VirtualInput In { get; private set; }
        public TelemetryRecorder Rec { get; private set; }

        PlaytestSession(string name, string scenePath)
        {
            Name = name;
            _scenePath = scenePath;
            _dir = PlaytestOutput.ScenarioDir(name);
        }

        /// <summary>Loads <paramref name="scenePath"/>, finds the actors and starts recording.</summary>
        public static async UniTask<PlaytestSession> Begin(string name, string scenePath)
        {
            var s = new PlaytestSession(name, scenePath);
            Application.runInBackground = true;
            Time.captureDeltaTime = Step;
            LogAssert.ignoreFailingMessages = true;   // errors are recorded; exceptions fail in Complete()

#if UNITY_EDITOR
            // By path, without touching the build list (Editor-only API).
            var op = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                scenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            var op = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
#endif
            while (!op.isDone) await UniTask.Yield();
            await UniTask.DelayFrame(5);

            s.RelaxInputFocusRules();
            s.In = new VirtualInput();
            await s.EnsureInputReachesGame();

            s.A = Actors.Find();
            Assert.IsNotNull(s.A.Player, $"[{name}] harness: no ChibiLocomotion in {scenePath}");
            Assert.IsNotNull(s.A.Camera, $"[{name}] harness: no main camera in {scenePath}");

            var go = new GameObject("PlaytestRecorder");
            s.Rec = go.AddComponent<TelemetryRecorder>();
            s.Rec.Begin(s.A);
            await s.Frames(30);   // settle: the player lands, the camera catches up
            return s;
        }

        // ------------------------------------------------------------------ input

        /// <summary>
        /// Confirms a queued key reaches the Input System in the current editor state. In an
        /// unfocused or headless editor, keyboard and mouse state is discarded unless the Input
        /// System is told every device goes to the Game view; that setting is switched on in
        /// memory for the run (never saved) and restored on Dispose.
        /// </summary>
        async UniTask EnsureInputReachesGame()
        {
            bool ok = await ProbeKey();
            _notes.Add(ok
                ? "input self-check: virtual keyboard events reach the Input System."
                : "input self-check FAILED: virtual keyboard not seen by the Input System " +
                  $"(focused={Application.isFocused}, enabled={In.Keyboard.enabled}, " +
                  $"updateMode={InputSystem.settings.updateMode}). Keyboard-driven scenarios will not move the player.");
        }

        /// <summary>
        /// An unfocused or headless editor drops keyboard and mouse state unless the Input
        /// System is told every device goes to the Game view and none stops in the background.
        /// Both are switched in memory before the devices are created (the flag is read when a
        /// device is added), and restored on Dispose. Never saved.
        /// </summary>
        void RelaxInputFocusRules()
        {
#if UNITY_EDITOR
            var settings = InputSystem.settings;
            _restoreInputBehaviour = settings.editorInputBehaviorInPlayMode;
            _restoreBackground = settings.backgroundBehavior;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#endif
        }

        async UniTask<bool> ProbeKey()
        {
            In.Hold(Key.F12);
            await UniTask.DelayFrame(2);
            bool seen = In.Keyboard.f12Key.isPressed;
            In.Release(Key.F12);
            await UniTask.DelayFrame(2);
            return seen;
        }

        // ----------------------------------------------------------------- timing

        /// <summary>Waits <paramref name="n"/> rendered frames.</summary>
        public UniTask Frames(int n) => UniTask.DelayFrame(Math.Max(1, n));

        /// <summary>Waits <paramref name="seconds"/> of game time (fixed-step frames).</summary>
        public UniTask Seconds(float seconds) => Frames(Mathf.RoundToInt(seconds / Step));

        // ------------------------------------------------------------- reporting

        /// <summary>Records that something the scenario wanted to do was not possible.</summary>
        public void CannotDrive(string what)
        {
            _cannot.Add(what);
            Rec?.Mark("cannotDrive", what);
            Debug.Log($"[Playtest:{Name}] COULD NOT DRIVE: {what}");
        }

        /// <summary>Adds a free-text note to the summary.</summary>
        public void Note(string note)
        {
            _notes.Add(note);
            Debug.Log($"[Playtest:{Name}] {note}");
        }

        /// <summary>Marks a scenario step in the event record and the log.</summary>
        public void Log(string label)
        {
            Rec?.Mark("step", label);
            Debug.Log($"[Playtest:{Name}] t={Rec?.Now:0.00} {label}");
        }

        // ------------------------------------------------------------ screenshots

        /// <summary>
        /// Renders the main camera (and the overlay UI, rendered through that camera for the
        /// shot) to a 1280x720 PNG. Uses a RenderTexture, not ScreenCapture, so it works with
        /// no Game view.
        /// </summary>
        public async UniTask<string> Shot(string label, int width = 1280, int height = 720)
        {
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
            var cam = A.Camera != null ? A.Camera : Camera.main;
            if (cam == null) { CannotDrive($"screenshot '{label}': no camera"); return null; }

            var canvases = new List<(Canvas c, RenderMode mode, Camera cam, float plane)>();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                canvases.Add((c, c.renderMode, c.worldCamera, c.planeDistance));
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = cam.nearClipPlane + 0.3f;
            }

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                cam.targetTexture = rt;
                Canvas.ForceUpdateCanvases();
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply(false);
                string file = $"{++_shotIndex:00}-{Sanitize(label)}.png";
                File.WriteAllBytes(Path.Combine(_dir, file), tex.EncodeToPNG());
                _shots.Add(file);
                Rec?.Mark("screenshot", file);
                return file;
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                foreach (var (c, mode, wc, plane) in canvases)
                {
                    if (c == null) continue;
                    c.renderMode = mode;
                    c.worldCamera = wc;
                    c.planeDistance = plane;
                }
                if (tex != null) Object.Destroy(tex);
                rt.Release();
                Object.Destroy(rt);
            }
        }

        static string Sanitize(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s) sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
            return sb.ToString();
        }

        // --------------------------------------------------------------- steering

        /// <summary>The left-stick value that walks toward a world direction, given the camera.</summary>
        public Vector2 StickToward(Vector3 worldDir)
        {
            Transform c = A.Camera.transform;
            Vector3 f = Vector3.ProjectOnPlane(c.forward, Vector3.up).normalized;
            Vector3 r = Vector3.ProjectOnPlane(c.right, Vector3.up).normalized;
            worldDir = Vector3.ProjectOnPlane(worldDir, Vector3.up);
            if (worldDir.sqrMagnitude < 1e-6f) return Vector2.zero;
            worldDir.Normalize();
            return new Vector2(Vector3.Dot(worldDir, r), Vector3.Dot(worldDir, f));
        }

        /// <summary>Planar distance from the player to a world point.</summary>
        public float DistanceTo(Vector3 p)
        {
            Vector3 d = p - A.Player.transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        /// <summary>
        /// Steers the left stick at <paramref name="target"/> every frame until within
        /// <paramref name="stopDistance"/> or the timeout. Returns whether it arrived.
        /// The stick is released on return.
        /// </summary>
        public async UniTask<bool> WalkTo(Func<Vector3> target, float stopDistance, float timeoutSeconds, bool sprint = false)
        {
            if (sprint) In.Hold(Key.LeftShift);
            int frames = Mathf.RoundToInt(timeoutSeconds / Step);
            bool arrived = false;
            for (int i = 0; i < frames; i++)
            {
                Vector3 t = target();
                if (DistanceTo(t) <= stopDistance) { arrived = true; break; }
                In.SetLeftStick(StickToward(t - A.Player.transform.position));
                await UniTask.Yield();
            }
            In.SetLeftStick(Vector2.zero);
            if (sprint) In.Release(Key.LeftShift);
            return arrived;
        }

        /// <summary>Holds a key for <paramref name="seconds"/> then releases it.</summary>
        public async UniTask Tap(Key key, float seconds = 0.08f)
        {
            In.Hold(key);
            await Seconds(seconds);
            In.Release(key);
        }

        /// <summary>Holds a pad button for <paramref name="seconds"/> then releases it.</summary>
        public async UniTask Tap(GamepadButton button, float seconds = 0.08f)
        {
            In.SetPadButton(button, true);
            await Seconds(seconds);
            In.SetPadButton(button, false);
        }

        /// <summary>Clicks a mouse button for <paramref name="seconds"/>.</summary>
        public async UniTask Click(MouseButton button, float seconds = 0.06f)
        {
            In.SetMouseButton(button, true);
            await Seconds(seconds);
            In.SetMouseButton(button, false);
        }

        // ------------------------------------------------------------------ finish

        /// <summary>Writes the outputs and fails the test if the game threw an exception.</summary>
        public void Complete()
        {
            Write();
            Assert.AreEqual(0, Rec != null ? Rec.ExceptionCount : 0,
                $"[{Name}] the game logged exceptions during the scenario; see {_dir}");
        }

        void Write()
        {
            if (_written || Rec == null) return;
            _written = true;
            Rec.End();

            var report = new ScenarioReport
            {
                scenario = Name, runId = PlaytestOutput.RunId, scene = _scenePath,
                startedAt = _started.ToString("s"),
                exceptionFree = Rec.ExceptionCount == 0,
                gameSeconds = Rec.Samples.Count > 0 ? Rec.Samples[Rec.Samples.Count - 1].t : 0f,
                cannotDrive = _cannot, notes = _notes, screenshots = _shots,
                metrics = Metrics.Compute(Rec.Samples, Rec.Events),
                airEpisodes = Metrics.AirEpisodes(Rec.Samples),
                events = Rec.Events, errors = Rec.Logs, samples = Rec.Samples,
            };
            File.WriteAllText(Path.Combine(_dir, "telemetry.json"), JsonUtility.ToJson(report));
            File.WriteAllText(Path.Combine(_dir, "summary.md"), BuildSummary(report));
            Debug.Log($"[Playtest:{Name}] wrote {_dir}");
        }

        string BuildSummary(ScenarioReport r)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {r.scenario}");
            sb.AppendLine();
            sb.AppendLine($"- run `{r.runId}`, scene `{r.scene}`, {r.gameSeconds:0.0} game seconds, started {r.startedAt}");
            sb.AppendLine($"- exceptions: {Rec.ExceptionCount}, logged errors: {Rec.ErrorCount}");
            sb.AppendLine();
            sb.AppendLine("## Could NOT drive");
            if (r.cannotDrive.Count == 0) sb.AppendLine("- (nothing recorded)");
            foreach (var c in r.cannotDrive) sb.AppendLine($"- {c}");
            sb.AppendLine();
            sb.AppendLine("## Notes");
            if (r.notes.Count == 0) sb.AppendLine("- (none)");
            foreach (var n in r.notes) sb.AppendLine($"- {n}");
            sb.AppendLine();
            sb.AppendLine("## Metrics");
            foreach (var m in r.metrics) sb.AppendLine($"- **{m.name.Trim()}**: {m.value}");
            sb.AppendLine();
            sb.AppendLine("## Screenshots");
            foreach (var s in r.screenshots) sb.AppendLine($"- {s}");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            int shown = 0;
            foreach (var e in r.events)
            {
                if (e.kind == "screenshot" || e.kind == "talkLineRevealed") continue;
                if (shown++ >= 120) { sb.AppendLine("- ... (truncated; see telemetry.json)"); break; }
                sb.AppendLine($"- {e.t:0.00}s [{e.kind}] {e.detail}");
            }
            if (r.errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Logged errors and exceptions");
                foreach (var l in r.errors.GetRange(0, Math.Min(25, r.errors.Count)))
                    sb.AppendLine($"- {l.t:0.00}s {l.type}: {l.message}");
            }
            return sb.ToString();
        }

        /// <summary>Removes devices, restores time and settings, writes outputs if not yet written.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Write(); }
            catch (Exception e) { Debug.LogWarning($"[Playtest:{Name}] writing outputs failed: {e.Message}"); }
            if (Rec != null) Object.Destroy(Rec.gameObject);
            In?.Dispose();
#if UNITY_EDITOR
            if (_restoreInputBehaviour.HasValue)
            {
                InputSystem.settings.editorInputBehaviorInPlayMode = _restoreInputBehaviour.Value;
                InputSystem.settings.backgroundBehavior = _restoreBackground;
            }
#endif
            Time.captureDeltaTime = 0f;
            Time.timeScale = 1f;
            LogAssert.ignoreFailingMessages = false;
        }
    }
}
