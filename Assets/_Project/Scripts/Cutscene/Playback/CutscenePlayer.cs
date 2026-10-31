using System;
using System.Collections.Generic;
using Pickleball.Sandbox;
using UnityEngine;
using UnityEngine.Playables;

namespace Pickleball.Cutscene.Playback
{
    /// <summary>
    /// Plays a block of timelines back to back on one PlayableDirector and owns everything around
    /// them: player input, the gameplay HUD, the camera, and hold-to-skip.
    /// <list type="bullet">
    /// <item><b>Start:</b> claims <see cref="InputBlock.All"/> on the <see cref="PlayerInputGate"/>,
    /// fades the HUD CanvasGroups out, hides the gameplay world-space indicators
    /// (<see cref="GameplayVisuals"/>), takes the camera (<see cref="CameraHandoff"/>), then plays
    /// the first timeline and raises <see cref="Started"/>.</item>
    /// <item><b>Between timelines:</b> when the director stops at a timeline's end, the next one
    /// starts on the same frame.</item>
    /// <item><b>Finish / skip:</b> the director is evaluated once at the end of the block's last
    /// timeline, so every actor stands on its final mark whether the block ran out or was skipped;
    /// subtitles clear; the gameplay indicators are restored exactly; the camera is handed back
    /// (the orbit camera returns on a frame with dt above zero); the gate is released; the HUD
    /// fades back; <see cref="Finished"/> is raised.</item>
    /// </list>
    /// Never writes Time.timeScale. Runs in Update with unscaled time for the HUD and the skip, and
    /// allocates nothing per frame. Disabling it mid-cutscene (a script reload) restores
    /// everything at once.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CutscenePlayer : MonoBehaviour, ICutscenePlayback
    {
        /// <summary>Longest block the queue holds.</summary>
        public const int MaxBlockLength = 8;

        /// <summary>How far before a timeline's end the settle evaluation samples, in seconds, so the last clips are still active.</summary>
        const double SettleEpsilon = 1e-4;

        [Header("Wiring")]
        [Tooltip("The director the timelines play on. Its bindings cover every timeline's tracks.")]
        [SerializeField] private PlayableDirector director;

        [Tooltip("The relay the subtitle tracks bind to; cleared on skip and at the end.")]
        [SerializeField] private SubtitleRelay subtitles;

        [Tooltip("Skip hold time and HUD fade speed.")]
        [SerializeField] private CutscenePlaybackConfig config;

        [Tooltip("The player's input gate. All input is claimed while a cutscene plays.")]
        [SerializeField] private PlayerInputGate inputGate;

        [Header("Camera")]
        [SerializeField] private CameraHandoff cameraHandoff = new CameraHandoff();

        [Header("HUD")]
        [Tooltip("Gameplay HUD canvases faded out during cutscenes (every screen-space root canvas " +
                 "except CutsceneHUD). Set by the setup menu.")]
        [SerializeField] private CanvasGroup[] hudToHide = new CanvasGroup[0];

        [Header("Gameplay indicators")]
        [Tooltip("World-space gameplay indicators on the Player (heal ring and rune, dash arc, blade " +
                 "trail, tether rope, skateboard, effect lights) hidden for the length of a cutscene " +
                 "and restored exactly on hand-back. Set by the setup menu.")]
        [SerializeField] private GameplayVisuals gameplayVisuals = new GameplayVisuals();

        [Tooltip("Animators restarted from their default state when a cutscene starts (the Player's model): " +
                 "the locomotion controller keeps running under the timeline and had wandered into a run or " +
                 "sword pose by the hand-back. The hand-back scenes ease MC into it. Set by the setup menu.")]
        [SerializeField] private Animator[] restartOnHandBack = new Animator[0];

        [Tooltip("Actor Animators the timeline drives (the Player's model, Birdy's model). While a cutscene " +
                 "plays they apply root motion and their model sits at its rest pose under the actor root: with " +
                 "root motion off, Timeline moved the model itself by the clips' root height (MC rode 0.64 m " +
                 "high in Play mode). Restored exactly at hand-back. Set by the setup menu.")]
        [SerializeField] private Animator[] actorAnimators = new Animator[0];

        [Tooltip("Seconds before the end of a block's last timeline at which the gameplay behaviours " +
                 "(the Player's foot IK) come back on: the length of the hand-back scenes' eased release " +
                 "(OpeningTimelineBuilder.HandBackRelease + HandBackEase), so MC is already in gameplay's " +
                 "grounded pose when the camera hands back.")]
        [Min(0f)]
        [SerializeField] private float behavioursBackBeforeEnd = 1.6f;

        bool _behavioursBack;
        bool[] _rootMotionWas;
        Vector3[] _modelPositionWas;
        Quaternion[] _modelRotationWas;

        PlayableAsset[] _queue;
        int _count;
        int _index;
        string _id;
        bool _playing;
        float _hudTarget = 1f;
        bool _hudSettled = true;
        SkipHold _skip;
        SkipInputReader _skipInput;
        readonly TimelineCompletion _completion = new TimelineCompletion();

        /// <inheritdoc />
        public event Action<string> Started;

        /// <inheritdoc />
        public event Action<string, bool> Finished;

        /// <inheritdoc />
        public bool IsPlaying => _playing;

        /// <inheritdoc />
        public float SkipProgress => _playing && _skip != null ? _skip.Progress : 0f;

        /// <summary>The id of the block playing, or null.</summary>
        public string CurrentId => _playing ? _id : null;

        /// <summary>The camera hand-off, for a camera system that needs to read or extend it.</summary>
        public CameraHandoff CameraHandoff => cameraHandoff;

        /// <summary>The director.</summary>
        public PlayableDirector Director => director;

        /// <summary>The gameplay indicators hidden during cutscenes (edit-mode previews hide them too).</summary>
        public GameplayVisuals GameplayVisuals => gameplayVisuals;

        void OnEnable()
        {
            _queue ??= new PlayableAsset[MaxBlockLength];
            _skip ??= new SkipHold(SkipSeconds);
            _skipInput ??= new SkipInputReader();
            cameraHandoff ??= new CameraHandoff();
            gameplayVisuals ??= new GameplayVisuals();
            if (director != null)
            {
                director.playOnAwake = false;
                director.extrapolationMode = DirectorWrapMode.None;
                director.stopped += OnDirectorStopped;
            }
        }

        void OnDisable()
        {
            if (director != null) director.stopped -= OnDirectorStopped;
            if (!_playing) return;
            _playing = false;
            if (director != null) director.Stop();
            if (subtitles != null) subtitles.Clear();
            gameplayVisuals.Restore();
            HoldAnimators(false);
            cameraHandoff.ForceRestore();
            if (inputGate != null) inputGate.Release(this);
            SetHudAlpha(1f);
        }

        /// <summary>
        /// Plays <paramref name="timelines"/> back to back as the block <paramref name="id"/>.
        /// False (and nothing happens) when a block is already playing or the list is empty.
        /// </summary>
        public bool Play(string id, IReadOnlyList<PlayableAsset> timelines)
        {
            if (_playing || director == null || timelines == null) return false;
            _queue ??= new PlayableAsset[MaxBlockLength];
            _skip ??= new SkipHold(SkipSeconds);
            _skipInput ??= new SkipInputReader();

            _count = 0;
            for (int i = 0; i < timelines.Count && _count < _queue.Length; i++)
                if (timelines[i] != null) _queue[_count++] = timelines[i];
            if (_count == 0) return false;
            if (timelines.Count > _queue.Length)
                Debug.LogWarning($"Cutscene {id}: only the first {_queue.Length} timelines play.", this);

            _id = id;
            _playing = true;
            _index = 0;
            if (inputGate != null) inputGate.Claim(this, InputBlock.All);
            gameplayVisuals ??= new GameplayVisuals();
            gameplayVisuals.Hide();
            _behavioursBack = false;
            HoldAnimators(true);
            // Restart the Player's controller on its idle now: it keeps running under the timeline
            // and is what the final frames ease into (it had wandered into a run or sword pose).
            RestartAnimators();
            cameraHandoff.Take();
            // Both blocks open on a hard cut, so the HUD (and the greybox's debug readout) is gone
            // on the first cutscene frame; it fades back in only at the end, where S2 blends.
            SetHudAlpha(config != null ? config.HudHiddenAlpha : 0f);
            _skip.HoldSeconds = SkipSeconds;
            _skip.Arm(_skipInput.IsHeld());
            if (subtitles != null) subtitles.Clear();
            PlayCurrent();
            Started?.Invoke(id);
            return true;
        }

        /// <summary>Skips to the end of the current block, as a completed hold does.</summary>
        public void Skip()
        {
            if (_playing) Finish(true);
        }

        void Update()
        {
            float unscaled = Time.unscaledDeltaTime;
            cameraHandoff.Tick(Time.deltaTime);
            StepHud(unscaled);
            if (!_playing) return;

            if (_skip.Tick(_skipInput.IsHeld(), unscaled))
            {
                Finish(true);
                return;
            }
            if (!_behavioursBack && _index == _count - 1 && director.playableAsset != null &&
                director.time >= director.playableAsset.duration - behavioursBackBeforeEnd)
            {
                gameplayVisuals.RestoreBehaviours();
                _behavioursBack = true;
            }
            // Finished only when the time reached the end, or the director stopped after we saw it
            // play. A director that isn't advancing (not started yet, app paused or unfocused) is
            // resumed where it was, never skipped (TimelineCompletion).
            bool directorPlaying = director.state == PlayState.Playing;
            double duration = director.playableAsset != null ? director.playableAsset.duration : 0.0;
            bool finished = _completion.Tick(directorPlaying, director.time, duration);
            if (!finished)
            {
                if (_completion.NeedsResume(directorPlaying, false)) ResumeDirector();
                return;
            }
            if (_index + 1 < _count)
            {
                _index++;
                PlayCurrent();
            }
            else Finish(false);
        }

        void PlayCurrent()
        {
            director.extrapolationMode = DirectorWrapMode.None;
            director.playableAsset = _queue[_index];
            director.time = 0;
            _completion.Begin();
            director.Play();
        }

        void OnDirectorStopped(PlayableDirector stopped)
        {
            if (_playing && stopped == director) _completion.OnStopped();
        }

        /// <summary>Gets a director that isn't advancing going again, from where it is.</summary>
        void ResumeDirector()
        {
            if (director.playableGraph.IsValid()) director.Resume();
            else director.Play();
        }

        // Coming back from an app pause or focus loss mid-block: carry on from the same time.
        void OnApplicationPause(bool paused)
        {
            if (!paused) ResumeIfInterrupted();
        }

        void OnApplicationFocus(bool focused)
        {
            if (focused) ResumeIfInterrupted();
        }

        void ResumeIfInterrupted()
        {
            if (!_playing || director == null || director.state == PlayState.Playing) return;
            double duration = director.playableAsset != null ? director.playableAsset.duration : 0.0;
            if (!_completion.IsOver(director.time, duration)) ResumeDirector();
        }

        void Finish(bool skipped)
        {
            SettleOnEnd();
            HoldAnimators(false);
            _playing = false;
            if (subtitles != null) subtitles.Clear();
            gameplayVisuals.Restore();
            cameraHandoff.Release();
            if (inputGate != null) inputGate.Release(this);
            SetHudTarget(1f);
            for (int i = 0; i < _count; i++) _queue[i] = null;
            string id = _id;
            _count = 0;
            Finished?.Invoke(id, skipped);
        }

        /// <summary>
        /// Evaluates the block's last timeline just before its end and stops, so blocking leaves
        /// every actor on its final mark however the block ended.
        /// </summary>
        void SettleOnEnd()
        {
            var last = _queue[_count - 1];
            director.Stop();
            director.playableAsset = last;
            director.time = Math.Max(0.0, last.duration - SettleEpsilon);
            director.Evaluate();
            director.Stop();
        }

        /// <summary>
        /// Puts each hand-back Animator back to its default state at time 0 (the locomotion idle),
        /// evaluated at once, so the controller the hand-back scenes ease into is idling.
        /// </summary>
        void RestartAnimators()
        {
            if (restartOnHandBack == null) return;
            for (int i = 0; i < restartOnHandBack.Length; i++)
            {
                var a = restartOnHandBack[i];
                if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null) continue;
                a.Rebind();
                a.Update(0f);
            }
        }

        /// <summary>
        /// On: records each actor Animator's root-motion flag and model pose, then turns root motion
        /// on and puts the model back on its recorded rest pose. Off: puts both back.
        /// </summary>
        void HoldAnimators(bool on)
        {
            if (actorAnimators == null) return;
            int n = actorAnimators.Length;
            if (on)
            {
                if (_rootMotionWas == null || _rootMotionWas.Length != n)
                {
                    _rootMotionWas = new bool[n];
                    _modelPositionWas = new Vector3[n];
                    _modelRotationWas = new Quaternion[n];
                }
                for (int i = 0; i < n; i++)
                {
                    var a = actorAnimators[i];
                    if (a == null) continue;
                    _rootMotionWas[i] = a.applyRootMotion;
                    _modelPositionWas[i] = a.transform.localPosition;
                    _modelRotationWas[i] = a.transform.localRotation;
                    a.applyRootMotion = true;
                }
                return;
            }
            if (_rootMotionWas == null) return;
            for (int i = 0; i < n && i < _rootMotionWas.Length; i++)
            {
                var a = actorAnimators[i];
                if (a == null) continue;
                a.applyRootMotion = _rootMotionWas[i];
                a.transform.SetLocalPositionAndRotation(_modelPositionWas[i], _modelRotationWas[i]);
            }
            _rootMotionWas = null;
        }

        /// <summary>Sets the actor Animators held during cutscenes (setup menu).</summary>
        public void ConfigureActorAnimators(Animator[] animators) => actorAnimators = animators ?? new Animator[0];

        /// <summary>Sets the hand-back Animators (setup menu).</summary>
        public void ConfigureHandBack(Animator[] animators) => restartOnHandBack = animators ?? new Animator[0];

        float SkipSeconds => config != null ? config.SkipHoldSeconds : CutscenePlaybackConfig.DefaultSkipHoldSeconds;

        void SetHudTarget(float alpha)
        {
            _hudTarget = alpha;
            _hudSettled = false;
            bool visible = alpha > 0.5f;
            for (int i = 0; i < hudToHide.Length; i++)
            {
                var g = hudToHide[i];
                if (g == null) continue;
                g.interactable = visible;
                g.blocksRaycasts = visible;
            }
        }

        void StepHud(float unscaled)
        {
            if (_hudSettled || hudToHide == null) return;
            float rate = config != null ? config.HudFadeRate : CutscenePlaybackConfig.DefaultHudFadeRate;
            bool settled = true;
            for (int i = 0; i < hudToHide.Length; i++)
            {
                var g = hudToHide[i];
                if (g == null) continue;
                g.alpha = HudFade.Step(g.alpha, _hudTarget, rate, unscaled);
                if (g.alpha != _hudTarget) settled = false;
            }
            _hudSettled = settled;
        }

        void SetHudAlpha(float alpha)
        {
            _hudTarget = alpha;
            _hudSettled = true;
            for (int i = 0; i < hudToHide.Length; i++)
            {
                var g = hudToHide[i];
                if (g == null) continue;
                g.alpha = alpha;
                g.interactable = alpha > 0.5f;
                g.blocksRaycasts = alpha > 0.5f;
            }
        }
    }
}
