using System;
using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// Owns a conversation from the talk press to the moment play resumes.
    /// Breath of the Wild's rule: while someone is talking, the world stops.
    ///
    /// <b>Start, in this order.</b>
    /// <list type="number">
    /// <item><see cref="TryStart"/> claims <see cref="InputBlock.All"/> on the
    /// player's <see cref="PlayerInputGate"/> and raises
    /// <see cref="ConversationStarting"/>; the camera starts to push in.</item>
    /// <item><b>Settling</b> for <see cref="DialogueConfig.SettleSeconds"/>: the
    /// world keeps running with the player's input blocked, so he slows to idle
    /// instead of freezing mid-stride, and (<see cref="DialogueConfig.TurnToFaceNpc"/>)
    /// turns to face the NPC through <see cref="TalkFacing"/>.</item>
    /// <item><c>Time.timeScale</c> goes to 0, the previous value kept. If time
    /// is already stopped by someone else by then, the talk is aborted rather
    /// than recording 0 as the value to restore.</item>
    /// <item>The Talkable is told and <see cref="ConversationStarted"/> fires;
    /// the box fades in. <b>Opening</b> lasts
    /// <see cref="DialogueConfig.TextStartDelaySeconds"/> and ignores presses.
    /// It runs longer while the NPC is still turning toward the player and is
    /// more than <see cref="DialogueConfig.WaitForNpcFacingDegrees"/> off (at
    /// most <see cref="DialogueConfig.WaitForNpcFacingSeconds"/> more), so
    /// nobody starts talking with their back half turned.</item>
    /// </list>
    /// <b>Talking.</b> A <see cref="DialogueRunner"/> types each line in unscaled
    /// time. Interact (E / RB, from <see cref="InteractInput"/>) shows the whole
    /// line, then moves on.
    ///
    /// <b>End, in this order.</b>
    /// <list type="number">
    /// <item><see cref="ConversationEnding"/>: the box hides. <b>Closing</b> lasts
    /// <see cref="DialogueConfig.CloseSeconds"/> while the camera blends back,
    /// time still stopped and input still blocked.</item>
    /// <item>The Talkable is told (<see cref="ConversationEndReason.Completed"/>).</item>
    /// <item>The previous time scale is restored.</item>
    /// <item><see cref="ConversationEnded"/>: the camera puts its pose back and
    /// returns to the orbit camera (on the first frame time actually moves).</item>
    /// <item><b>Releasing</b>: the input claim is dropped one frame later, then
    /// <see cref="DialogueConfig.RestartCooldownSeconds"/> of quiet must pass
    /// before a new talk can start. It is a debounce: every Interact press inside
    /// it starts it again, so neither the press that closed the box nor a player
    /// mashing E to skip text can reopen the conversation.</item>
    /// </list>
    ///
    /// <b>Exception safety.</b> The end path never stops half way. Lifecycle
    /// events are invoked one subscriber at a time, and a subscriber that throws
    /// is logged and skipped; restoring time, leaving the conversation and
    /// releasing input sit in <c>finally</c> blocks besides. A broken listener
    /// can cost its own clean-up, never a frozen game.
    ///
    /// <b>The press that starts a talk is not an advance.</b> The interactor and
    /// this director both hear the same press. <see cref="TryStart"/> drops any
    /// press latched so far and ignores the start frame, and Settling and
    /// Opening ignore presses.
    ///
    /// <b>Aborts.</b> Every frame the director checks its Talkable: destroyed,
    /// inactive, disabled, out of the conversation or dead (a Health on its
    /// root) ends the talk with <see cref="ConversationEndReason.Aborted"/>. So
    /// does disabling the director, which a script reload in Play mode does
    /// first: <c>OnDisable</c> restores time and releases input at once, and the
    /// camera and the views undo their own state from the events or their own
    /// <c>OnDisable</c>.
    ///
    /// The only writer of <c>Time.timeScale</c> in the project. It will not
    /// start a talk while time is already stopped.
    ///
    /// <see cref="Tick"/> is public so EditMode tests can drive the whole
    /// lifecycle with explicit time, like <c>SpacetimeView.Tick</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public class DialogueDirector : MonoBehaviour
    {
        /// <summary>Where the director is in a conversation.</summary>
        public enum Phase
        {
            /// <summary>No conversation. Play runs normally (maybe in the restart cooldown).</summary>
            Idle,
            /// <summary>Started; input blocked, the world still running while the player settles.</summary>
            Settling,
            /// <summary>Time stopped; the box and camera are coming in. Presses are ignored.</summary>
            Opening,
            /// <summary>Lines are typing or waiting for a press.</summary>
            Talking,
            /// <summary>The last line was dismissed; the box and camera are going out.</summary>
            Closing,
            /// <summary>Time has resumed; input is released on the next frame.</summary>
            Releasing,
        }

        [Header("Wiring")]
        [Tooltip("Timing, pause and camera tunables. Set by Setup Dialogue.")]
        [SerializeField] private DialogueConfig config;
        [Tooltip("The player's input gate. Everything is blocked for the whole conversation. " +
                 "Empty: nothing is blocked (time still stops).")]
        [SerializeField] private PlayerInputGate inputGate;
        [Tooltip("The player's Interact button (E / RB); a press advances the text. Empty: " +
                 "nothing can advance, so a conversation never ends by itself.")]
        [SerializeField] private InteractInput interactInput;

        // Longest unscaled step one frame may take. A hitch (a load, the Editor
        // regaining focus) would otherwise jump a fade or a blend to its end.
        const float MaxStep = 0.1f;

        DialogueRunner _runner;
        TimeScalePause _pause;
        InteractInput _listening;
        bool _advancePressed;
        int _startFrame = -1;
        float _phaseTime;
        float _clock;
        float _cooldownUntil;
        Health _currentHealth;
        TalkFacing _facing;
        float _npcYawLastTick;

        /// <summary>Current phase.</summary>
        public Phase State { get; private set; } = Phase.Idle;

        /// <summary>True from <see cref="TryStart"/> until input is released.</summary>
        public bool IsBusy => State != Phase.Idle;

        /// <summary>True after a conversation, until a new one may start.</summary>
        public bool IsCoolingDown => State == Phase.Idle && _clock < _cooldownUntil;

        /// <summary>Whether <see cref="TryStart"/> would accept a new conversation now:
        /// idle, past the restart cooldown, and configured.</summary>
        public bool CanStartConversation => State == Phase.Idle && _clock >= _cooldownUntil && config != null;

        /// <summary>Who is talking, or null when idle.</summary>
        public Talkable Current { get; private set; }

        /// <summary>Who they are talking to (the player's root), or null when idle.</summary>
        public Transform Partner { get; private set; }

        /// <summary>Index of the line showing, or -1.</summary>
        public int LineIndex => Runner.LineIndex;

        /// <summary>Full text of the line showing (rich-text tags included).</summary>
        public string CurrentText => Runner.CurrentText;

        /// <summary>Characters of the current line showing, tags excluded.</summary>
        public int VisibleCharacters => Runner.VisibleCharacters;

        /// <summary>True while the current line is fully shown and waits for a press.</summary>
        public bool IsLineRevealed => Runner.State == DialogueRunner.Phase.Revealed;

        /// <summary>The talk press was accepted. Input is blocked; time still runs
        /// (the settle). The camera starts to push in.</summary>
        public event Action<Talkable> ConversationStarting;

        /// <summary>Time is stopped and the Talkable has been told. The box fades in.</summary>
        public event Action<Talkable> ConversationStarted;

        /// <summary>Line <c>index</c> began typing. <see cref="CurrentText"/> holds it.</summary>
        public event Action<Talkable, int> LineStarted;

        /// <summary>The count of characters showing changed (for the typewriter view).</summary>
        public event Action<int> CharactersRevealed;

        /// <summary>Line <c>index</c> is fully shown (typed out or skipped).</summary>
        public event Action<Talkable, int> LineRevealed;

        /// <summary>The conversation is closing, normally or by an abort. The box
        /// hides; time is still stopped.</summary>
        public event Action<Talkable> ConversationEnding;

        /// <summary>The conversation is over and time has been restored. Input is
        /// released a frame later. The Talkable may be null if it was destroyed.</summary>
        public event Action<Talkable> ConversationEnded;

        // Built on first use, so they survive a script reload.
        DialogueRunner Runner
        {
            get
            {
                if (_runner != null) return _runner;
                _runner = new DialogueRunner(config != null ? config.Typewriter : default);
                _runner.LineStarted += OnLineStarted;
                _runner.CharactersRevealed += OnCharactersRevealed;
                _runner.LineRevealed += OnLineRevealed;
                _runner.Finished += BeginClosing;
                return _runner;
            }
        }

        TimeScalePause Pause => _pause ??= new TimeScalePause();

        void OnEnable()
        {
            _listening = interactInput;
            if (_listening != null) _listening.Pressed += OnInteractPressed;
        }

        void OnDisable()
        {
            try
            {
                Abort();
            }
            finally
            {
                if (_listening != null) _listening.Pressed -= OnInteractPressed;
                _listening = null;
                _advancePressed = false;
            }
        }

        void OnInteractPressed() => _advancePressed = true;

        void Update()
        {
            bool pressed = _advancePressed && Time.frameCount != _startFrame;
            _advancePressed = false;
            Tick(Mathf.Min(Time.unscaledDeltaTime, MaxStep), pressed);
        }

        /// <summary>
        /// Starts a conversation between <paramref name="talkable"/> and
        /// <paramref name="partner"/> (the player's root). Returns false, and
        /// changes nothing, when one is already running, the restart cooldown is
        /// active, there is no config, time is already stopped, or the Talkable
        /// cannot talk.
        /// </summary>
        public bool TryStart(Talkable talkable, Transform partner)
        {
            if (!CanStartConversation || talkable == null || !talkable.CanTalk) return false;
            if (!(Time.timeScale > 0f)) return false;

            Current = talkable;
            Partner = partner;
            _currentHealth = talkable.TryGetComponent(out Health health) ? health : null;
            State = Phase.Settling;
            _phaseTime = 0f;
            _startFrame = Time.frameCount;
            _advancePressed = false;

            if (inputGate != null) inputGate.Claim(this, InputBlock.All);
            if (config.TurnToFaceNpc) (_facing ??= new TalkFacing()).Begin(partner, talkable.FocusPosition);
            Raise(ConversationStarting, talkable);
            if (State == Phase.Settling && config.SettleSeconds <= 0f) EnterPause();
            return true;
        }

        /// <summary>
        /// One frame of the conversation: <paramref name="unscaledDeltaTime"/>
        /// seconds pass, and <paramref name="advancePressed"/> says whether the
        /// talk button went down this frame. Update calls it with unscaled time;
        /// tests call it directly.
        /// </summary>
        public void Tick(float unscaledDeltaTime, bool advancePressed)
        {
            float dt = unscaledDeltaTime > 0f ? unscaledDeltaTime : 0f;
            _clock += dt;

            // Debounce: a press while cooling down restarts the cooldown, so only
            // a press after a quiet spell can open a new talk.
            if (advancePressed && IsCoolingDown && config != null)
                _cooldownUntil = _clock + config.RestartCooldownSeconds;

            if (State is (Phase.Settling or Phase.Opening or Phase.Talking) && !CurrentStillTalking())
            {
                End(ConversationEndReason.Aborted, releaseNow: false);
                return;
            }

            switch (State)
            {
                case Phase.Settling:
                    // Unscaled, so a test can drive it; equal to scaled time here,
                    // because nothing else changes the time scale.
                    _phaseTime += dt;
                    if (_phaseTime >= config.SettleSeconds) EnterPause();
                    return;

                case Phase.Opening:
                    _phaseTime += dt;
                    bool npcTurning = NpcStillTurningAway();
                    if (_phaseTime < config.TextStartDelaySeconds) return;
                    if (npcTurning && _phaseTime < config.TextStartDelaySeconds + config.WaitForNpcFacingSeconds)
                        return;
                    State = Phase.Talking;
                    Runner.Timing = config.Typewriter;
                    if (!Runner.Begin(Current.Dialogue)) BeginClosing();
                    return;

                case Phase.Talking:
                    // Re-read every frame so typing speed can be tuned live.
                    Runner.Timing = config.Typewriter;
                    Runner.Tick(dt);
                    if (advancePressed && State == Phase.Talking) Runner.Advance();
                    return;

                case Phase.Closing:
                    _phaseTime += dt;
                    if (_phaseTime >= config.CloseSeconds) End(ConversationEndReason.Completed, releaseNow: false);
                    return;

                case Phase.Releasing:
                    ReleaseInput();
                    return;
            }
        }

        /// <summary>Ends any conversation at once, as an abort: skips the close,
        /// restores time and releases input in this call. Safe to call when idle.</summary>
        public void Abort() => End(ConversationEndReason.Aborted, releaseNow: true);

        // ------------------------------------------------------------ internals

        bool CurrentStillTalking()
        {
            if (Current == null || !Current.isActiveAndEnabled) return false;
            // Told only when the pause begins, so Settling cannot check this yet.
            if (State != Phase.Settling && !Current.IsInConversation) return false;
            return _currentHealth == null || !_currentHealth.IsDead;
        }

        /// <summary>True while the NPC is still turning (its yaw moved since the last
        /// tick) and is more than <see cref="DialogueConfig.WaitForNpcFacingDegrees"/>
        /// off facing the player. An NPC that doesn't turn never holds the text up.</summary>
        bool NpcStillTurningAway()
        {
            if (Current == null || Partner == null) return false;
            Transform npc = Current.transform;
            float yaw = npc.eulerAngles.y;
            bool turning = Mathf.Abs(Mathf.DeltaAngle(_npcYawLastTick, yaw)) > 0.01f;
            _npcYawLastTick = yaw;
            if (!turning) return false;
            Vector3 toPartner = Partner.position - npc.position;
            Vector3 forward = npc.forward;
            toPartner.y = 0f;
            forward.y = 0f;
            if (toPartner.sqrMagnitude < 1e-4f || forward.sqrMagnitude < 1e-6f) return false;
            return Vector3.Angle(forward, toPartner) > config.WaitForNpcFacingDegrees;
        }

        void EnterPause()
        {
            // The motor stops with time: the turn is over, hand facing back.
            _facing?.End();
            // Someone else stopped time during the settle (a second director, a
            // menu): their 0 must never become ours to restore.
            if (!Pause.Hold(config.PauseAudio))
            {
                End(ConversationEndReason.Aborted, releaseNow: false);
                return;
            }
            State = Phase.Opening;
            _phaseTime = 0f;
            _npcYawLastTick = Current != null ? Current.transform.eulerAngles.y : 0f;
            // The Talkable marks itself in conversation before it raises its
            // event, so a throwing NPC listener cannot leave it half started.
            try { Current.NotifyConversationStarted(Partner); }
            catch (Exception e) { Debug.LogException(e, this); }
            Raise(ConversationStarted, Current);
        }

        void BeginClosing()
        {
            if (State is not (Phase.Settling or Phase.Opening or Phase.Talking)) return;
            State = Phase.Closing;
            _phaseTime = 0f;
            Raise(ConversationEnding, Current);
        }

        /// <summary>
        /// The end sequence: box out (if not already), Talkable told, time
        /// restored, <see cref="ConversationEnded"/>. Input is released now
        /// (<paramref name="releaseNow"/>, for OnDisable) or on the next tick.
        /// Each step sits in a finally of the step before, so none is skipped.
        /// </summary>
        void End(ConversationEndReason reason, bool releaseNow)
        {
            if (State == Phase.Idle) return;
            try
            {
                if (State == Phase.Releasing) return;
                var talkable = Current;
                try
                {
                    if (State != Phase.Closing)
                    {
                        State = Phase.Closing;
                        Raise(ConversationEnding, talkable);
                    }
                    Runner.Stop();
                    if (talkable != null)
                    {
                        try { talkable.NotifyConversationEnded(reason); }
                        catch (Exception e) { Debug.LogException(e, this); }
                    }
                }
                finally
                {
                    _facing?.End();
                    Pause.Release();
                    State = Phase.Releasing;
                    Current = null;
                    Partner = null;
                    _currentHealth = null;
                }
                Raise(ConversationEnded, talkable);
            }
            finally
            {
                if (releaseNow) ReleaseInput();
            }
        }

        void ReleaseInput()
        {
            if (inputGate != null) inputGate.Release(this);
            State = Phase.Idle;
            _cooldownUntil = _clock + (config != null ? config.RestartCooldownSeconds : 0f);
        }

        /// <summary>Invokes a lifecycle event one subscriber at a time, logging and
        /// skipping any that throws, so one broken listener cannot stop the
        /// others or the conversation. Lifecycle events only: the invocation list
        /// allocates, which is fine a few times per conversation.</summary>
        void Raise(Action<Talkable> handlers, Talkable talkable)
        {
            if (handlers == null) return;
            foreach (var handler in handlers.GetInvocationList())
            {
                try { ((Action<Talkable>)handler)(talkable); }
                catch (Exception e) { Debug.LogException(e, this); }
            }
        }

        void OnLineStarted(int index)
        {
            if (Current != null) Current.NotifyLineStarted(index);
            LineStarted?.Invoke(Current, index);
        }

        void OnCharactersRevealed(int count) => CharactersRevealed?.Invoke(count);

        void OnLineRevealed(int index)
        {
            if (Current != null) Current.NotifyLineRevealed(index);
            LineRevealed?.Invoke(Current, index);
        }
    }
}
