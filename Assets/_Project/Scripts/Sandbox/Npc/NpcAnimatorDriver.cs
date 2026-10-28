using Pickleball.Sandbox.Dialogue;
using UnityEngine;

namespace Pickleball.Sandbox.Npc
{
    /// <summary>
    /// Presents a talking NPC: feeds its Animator from the conversation and
    /// death events, and turns it to face whoever it is talking to.
    ///
    /// Presentation only (CodingStandards §1). It listens to the
    /// <see cref="Talkable"/> on the same object (never to the dialogue
    /// director) and to its <see cref="Health"/>, and changes nothing but the
    /// Animator, this transform's yaw and, once dead, the model's height
    /// (the <see cref="NpcDeathLift"/> that keeps the body on the ground).
    ///
    /// After a conversation the NPC keeps facing the player while they stay
    /// within the config's restReturnDistance, and turns back to the facing it
    /// was placed with once they walk off.
    ///
    /// <b>Conversations run with <c>Time.timeScale</c> at 0.</b> On
    /// ConversationStarted the Animator is switched to
    /// <see cref="AnimatorUpdateMode.UnscaledTime"/> so it keeps gesturing, and
    /// its previous mode is restored when the conversation ends (or this
    /// component is disabled). The turn toward the partner uses unscaled time
    /// for the same reason.
    ///
    /// Line by line: LineStarted reads the line's <see cref="DialogueEmote"/>
    /// and plays that gesture; LineRevealed settles back to the listening idle
    /// (after a minimum gesture time, so a skipped line still gestures).
    /// Death plays once and holds; nothing leaves it.
    ///
    /// The rules live in <see cref="NpcTalkTracker"/>, <see cref="NpcFacing"/>
    /// and <see cref="IdleVariantPicker"/>; this component only wires them to
    /// Unity. Parameter and state names are the contract in
    /// <see cref="NpcAnimatorParameters"/>.
    ///
    /// Hot-reload safe: events are subscribed in OnEnable and released in
    /// OnDisable (which also restores the update mode and drops the death
    /// lift), runtime helpers are rebuilt lazily, and OnEnable re-syncs a
    /// conversation that is under way, or one that ended while disabled;
    /// a death is re-synced on the next Tick.
    ///
    /// Time is only read in Update and passed to <see cref="Tick"/>; the
    /// event handlers use the clock Tick was last given.
    /// </summary>
    [DisallowMultipleComponent]
    public class NpcAnimatorDriver : MonoBehaviour
    {
        [Tooltip("The NPC model's Animator, built with the controller from its Rebuild … " +
                 "Animator menu. Empty: the first Animator in the children.")]
        [SerializeField] private Animator animator;

        [Tooltip("Whose conversations to act out. Empty: the Talkable on this object.")]
        [SerializeField] private Talkable talkable;

        [Tooltip("Whose death to act out. Empty: the Health on this object, if any.")]
        [SerializeField] private Health health;

        [Tooltip("Turning, gesture and idle tunables. Empty: built-in defaults.")]
        [SerializeField] private NpcAnimationConfig config;

        NpcTalkTracker _tracker;
        IdleVariantPicker _picker;
        NpcAnimationConfig _defaultConfig;

        bool _updateModeOverridden;
        AnimatorUpdateMode _restUpdateMode;

        bool _hasRestYaw;
        float _restYaw;
        // Who the NPC keeps watching after a conversation, until they walk off.
        Transform _watching;

        // The clocks Tick was last given, so the event handlers never read
        // Time directly and tests that inject time stay in control of them.
        float _unscaledNow;

        // How far the model is currently raised by the death lift, m. Undone in
        // OnDisable, so a re-enable or script reload never stacks it twice.
        float _appliedDeathLift;

        RuntimeAnimatorController _scannedController;
        bool _hasParameters;
        bool _hasIdleVariantParameter;
        int[] _idleStateHashes;
        int _idleVariantCount = -1;
        int _idleState = -1;
        int _idleLoop = -1;

        /// <summary>Whether the NPC is acting out a conversation.</summary>
        public bool IsTalking => Tracker.Talking;

        /// <summary>Whether the NPC is playing its death. Permanent.</summary>
        public bool IsDead => Tracker.Dead;

        /// <summary>The emote of the line most recently started.</summary>
        public DialogueEmote CurrentEmote => Tracker.Emote;

        /// <summary>Whether a line's gesture plays at <paramref name="unscaledNow"/> (seconds of unscaled time).</summary>
        public bool IsSpeakingAt(float unscaledNow) => Tracker.IsSpeaking(unscaledNow);

        /// <summary>
        /// Normalised time into the Death state, as of the last Tick: 0 until
        /// the cross-fade into it has finished, then 0–1 over the clip, and
        /// past 1 while its last frame holds. What <see cref="NpcDeathLift"/>
        /// and <see cref="NpcRotor"/> time the fall by.
        /// </summary>
        public float DeathProgress { get; private set; }

        NpcTalkTracker Tracker => _tracker ??= new NpcTalkTracker();

        NpcAnimationConfig Config
        {
            get
            {
                if (config != null) return config;
                if (_defaultConfig == null)
                {
                    _defaultConfig = ScriptableObject.CreateInstance<NpcAnimationConfig>();
                    _defaultConfig.hideFlags = HideFlags.HideAndDontSave;
                }
                return _defaultConfig;
            }
        }

        void OnEnable()
        {
            if (talkable == null) talkable = GetComponent<Talkable>();
            if (health == null) health = GetComponent<Health>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            if (!_hasRestYaw)
            {
                _restYaw = transform.eulerAngles.y;
                _hasRestYaw = true;
            }

            if (talkable != null)
            {
                talkable.ConversationStarted += OnConversationStarted;
                talkable.LineStarted += OnLineStarted;
                talkable.LineRevealed += OnLineRevealed;
                talkable.ConversationEnded += OnConversationEnded;
            }
            if (health != null) health.Died += OnDied;

            // Rescan the controller lazily: a reload may have changed it.
            _scannedController = null;
            _idleVariantCount = -1;
            _idleState = -1;

            // A script reload mid-conversation re-runs OnEnable with fresh
            // state; pick the conversation back up rather than idling through it.
            // And the other way round: re-enabled after a conversation ended
            // while disabled (its Ended never reached us), stop acting it out.
            // (Death is re-synced in Tick, after every Health has run Awake.)
            if (talkable != null && talkable.IsInConversation) OnConversationStarted(talkable);
            else Tracker.EndConversation();
        }

        void OnDisable()
        {
            if (talkable != null)
            {
                talkable.ConversationStarted -= OnConversationStarted;
                talkable.LineStarted -= OnLineStarted;
                talkable.LineRevealed -= OnLineRevealed;
                talkable.ConversationEnded -= OnConversationEnded;
            }
            if (health != null) health.Died -= OnDied;
            RestoreUpdateMode();
            ApplyDeathLift(0f);
        }

        void OnDestroy()
        {
            if (_defaultConfig == null) return;
            if (Application.isPlaying) Destroy(_defaultConfig);
            else DestroyImmediate(_defaultConfig);
        }

        // The only place this component reads the clocks. Scaled delta time is
        // used only after a conversation (watching the player walk off, turning
        // back to rest), when time runs again; everything that runs while the
        // game is paused uses the unscaled pair.
        void Update() => Tick(Time.unscaledTime, Time.unscaledDeltaTime, Time.deltaTime);

        /// <summary>
        /// One frame: re-sync death, push the Animator parameters, advance the
        /// idle variants, lift a dead body onto the ground and turn. Public so
        /// EditMode tests can drive time explicitly (RuntimeConventions: inject
        /// time); the event handlers use the clock given here last.
        /// </summary>
        /// <param name="unscaledNow">Unscaled time, s: gesture timing.</param>
        /// <param name="unscaledDt">Unscaled frame time, s: turning while talking (time is paused).</param>
        /// <param name="scaledDt">Game frame time, s: watching and turning back after a conversation.</param>
        public void Tick(float unscaledNow, float unscaledDt, float scaledDt)
        {
            _unscaledNow = unscaledNow;
            var cfg = Config;
            var tracker = Tracker;
            tracker.MinGestureSeconds = cfg.MinGestureSeconds;

            // Catches a death missed while disabled or across a script reload.
            if (!tracker.Dead && health != null && health.IsDead) tracker.Die();

            if (HasParameters())
            {
                animator.SetBool(NpcAnimatorParameters.TalkingHash, tracker.Talking);
                animator.SetBool(NpcAnimatorParameters.SpeakingHash, tracker.IsSpeaking(unscaledNow));
                animator.SetInteger(NpcAnimatorParameters.EmoteHash, (int)tracker.Emote);
                animator.SetBool(NpcAnimatorParameters.DeadHash, tracker.Dead);
                UpdateIdleVariant(cfg);
                if (tracker.Dead) UpdateDeathLift(cfg);
            }

            UpdateFacing(cfg, unscaledDt, scaledDt);
        }

        // ---------------------------------------------------------------- events

        void OnConversationStarted(Talkable source)
        {
            Tracker.BeginConversation();
            if (source != null && source.Partner != null) _watching = source.Partner;
            UseUnscaledTime();
        }

        void OnLineStarted(Talkable source, int _)
        {
            Tracker.MinGestureSeconds = Config.MinGestureSeconds;
            // CurrentLine is set before LineStarted fires, and is the default
            // line (emote Talk) for an index the definition does not have.
            var emote = source != null ? source.CurrentLine.Emote : DialogueEmote.Talk;
            Tracker.BeginLine(emote, _unscaledNow);
            UseUnscaledTime();
        }

        void OnLineRevealed(Talkable _, int __) => Tracker.RevealLine();

        // Completed and Aborted end the same way: this NPC has no goodbye
        // gesture, so there is nothing to skip on an abort. The controller
        // cross-fades straight from whatever was playing to idle (or Death
        // wins, if the abort was a kill).
        // After the end, the NPC keeps watching the partner (_watching, set at
        // the start) until they walk off; see UpdateFacing.
        void OnConversationEnded(Talkable _)
        {
            Tracker.EndConversation();
            RestoreUpdateMode();
        }

        void OnDied(Health _) => Tracker.Die();

        // ----------------------------------------------------------- update mode

        void UseUnscaledTime()
        {
            if (animator == null || _updateModeOverridden) return;
            _restUpdateMode = animator.updateMode;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            _updateModeOverridden = true;
        }

        void RestoreUpdateMode()
        {
            if (!_updateModeOverridden) return;
            _updateModeOverridden = false;
            if (animator != null) animator.updateMode = _restUpdateMode;
        }

        // -------------------------------------------------------------- animator

        /// <summary>Whether the Animator has this driver's parameters. Rescanned
        /// only when the controller changes, because Animator.parameters allocates.</summary>
        bool HasParameters()
        {
            if (animator == null || !animator.isActiveAndEnabled || !animator.isInitialized) return false;
            var controller = animator.runtimeAnimatorController;
            if (controller == null) return false;
            if (controller == _scannedController) return _hasParameters;

            _scannedController = controller;
            _idleVariantCount = -1;
            int found = 0;
            _hasIdleVariantParameter = false;
            foreach (var p in animator.parameters)
            {
                int h = p.nameHash;
                if (h == NpcAnimatorParameters.TalkingHash || h == NpcAnimatorParameters.SpeakingHash ||
                    h == NpcAnimatorParameters.EmoteHash || h == NpcAnimatorParameters.DeadHash)
                    found++;
                else if (h == NpcAnimatorParameters.IdleVariantHash)
                    _hasIdleVariantParameter = true;
            }
            _hasParameters = found == 4;
            if (!_hasParameters)
                Debug.LogWarning($"{name}: controller '{controller.name}' lacks the NPC parameters " +
                                 "(Talking, Speaking, Emote, Dead). Rebuild it with its setup menu.", this);
            return _hasParameters;
        }

        /// <summary>
        /// Picks the next idle variant at the start of every idle loop. The
        /// controller's idle-to-idle transitions fire at the loop's exit time
        /// when IdleVariant names a different variant.
        /// </summary>
        void UpdateIdleVariant(NpcAnimationConfig cfg)
        {
            if (!_hasIdleVariantParameter || Tracker.Talking || Tracker.Dead)
            {
                _idleState = -1;
                return;
            }
            if (_idleVariantCount < 0) CountIdleVariants();
            if (_idleVariantCount <= 1 || animator.IsInTransition(0)) return;

            var info = animator.GetCurrentAnimatorStateInfo(0);
            int variant = -1;
            for (int i = 0; i < _idleVariantCount; i++)
                if (_idleStateHashes[i] == info.fullPathHash) { variant = i; break; }
            if (variant < 0)
            {
                _idleState = -1;
                return;
            }

            int loop = Mathf.FloorToInt(info.normalizedTime);
            if (variant == _idleState && loop == _idleLoop) return;
            _idleState = variant;
            _idleLoop = loop;

            _picker ??= new IdleVariantPicker(new System.Random(GetInstanceID()));
            animator.SetInteger(NpcAnimatorParameters.IdleVariantHash,
                                _picker.Next(variant, _idleVariantCount, cfg.MainIdleChance));
        }

        void CountIdleVariants()
        {
            _idleStateHashes ??= new int[NpcAnimatorParameters.MaxIdleVariants];
            int count = 0;
            for (int i = 0; i < NpcAnimatorParameters.MaxIdleVariants; i++)
            {
                int hash = NpcAnimatorParameters.FullPathHash(NpcAnimatorParameters.IdleStateName(i));
                if (!animator.HasState(0, hash)) break;
                _idleStateHashes[count++] = hash;
            }
            _idleVariantCount = count;
        }

        // ----------------------------------------------------------------- death

        /// <summary>Eases the model up over the death clip (NpcDeathLift), so
        /// the body ends on the ground rather than sunk into it.</summary>
        void UpdateDeathLift(NpcAnimationConfig cfg)
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            float t = info.fullPathHash == DeathStateHash ? info.normalizedTime : 0f;
            DeathProgress = Mathf.Max(DeathProgress, t);
            ApplyDeathLift(NpcDeathLift.At(DeathProgress, cfg.DeathGroundLift, cfg.DeathLiftStart, cfg.DeathLiftEnd));
        }

        static readonly int DeathStateHash = NpcAnimatorParameters.FullPathHash(NpcAnimatorParameters.DeathState);

        void ApplyDeathLift(float lift)
        {
            if (animator == null || Mathf.Approximately(lift, _appliedDeathLift)) return;
            animator.transform.localPosition += Vector3.up * (lift - _appliedDeathLift);
            _appliedDeathLift = lift;
        }

        // ---------------------------------------------------------------- facing

        /// <summary>
        /// While talking: face the partner, on unscaled time (the game is
        /// paused). Afterwards: keep watching them while they stay within
        /// restReturnDistance, then turn back to the placed facing. Dead NPCs
        /// don't turn.
        /// </summary>
        void UpdateFacing(NpcAnimationConfig cfg, float unscaledDt, float scaledDt)
        {
            if (Tracker.Dead) return;

            float target, rate, dt;
            if (Tracker.Talking)
            {
                var partner = talkable != null ? talkable.Partner : null;
                if (partner == null || !NpcFacing.TryYawTowards(transform.position, partner.position, out target))
                    return;
                rate = cfg.TurnRate;
                dt = unscaledDt;
            }
            else if (_watching != null && WithinWatchRange(cfg))
            {
                if (!NpcFacing.TryYawTowards(transform.position, _watching.position, out target)) return;
                rate = cfg.TurnRate;
                dt = scaledDt;
            }
            else
            {
                _watching = null;
                if (!cfg.ReturnToRestFacing || !_hasRestYaw) return;
                target = _restYaw;
                rate = cfg.RestTurnRate;
                dt = scaledDt;
            }

            float yaw = transform.eulerAngles.y;
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, target)) < 0.01f) return;
            transform.rotation = Quaternion.Euler(0f, NpcFacing.StepYaw(yaw, target, rate, cfg.MaxTurnSpeed, dt), 0f);
        }

        bool WithinWatchRange(NpcAnimationConfig cfg)
        {
            Vector3 d = _watching.position - transform.position;
            d.y = 0f;
            return d.sqrMagnitude <= cfg.RestReturnDistance * cfg.RestReturnDistance;
        }
    }
}
