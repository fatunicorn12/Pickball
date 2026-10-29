using System;
using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// Marks a character the player can talk to, and is the one place the rest
    /// of the game hears about that character's conversations.
    ///
    /// The dialogue director drives a conversation and reports each step
    /// through the <c>Notify…</c> methods. Presentation on the NPC (its animator
    /// driver, a head-look, audio) subscribes to the events here and never to
    /// the director, so an NPC prefab works the same in any scene that has a
    /// director, and the director never needs to know what the NPC looks like.
    ///
    /// <b>Order of a conversation</b> (fixed; subscribers may rely on it):
    /// <list type="number">
    /// <item>The player's gameplay input is blocked and a short settle runs in
    /// normal time, so the player slows to idle rather than freezing mid-stride.
    /// The camera starts to push in.</item>
    /// <item><c>Time.timeScale</c> goes to 0, then <see cref="ConversationStarted"/>.</item>
    /// <item>Per line: <see cref="CurrentLineIndex"/>, <see cref="CurrentLine"/>
    /// and <see cref="CurrentNodeId"/> are set, <see cref="LineStarted"/> fires,
    /// the text types out, and <see cref="LineRevealed"/> fires when it is fully
    /// shown.</item>
    /// <item>Per choice (branching conversations): once the line before has
    /// typed out, <see cref="IsAwaitingChoice"/> is set and
    /// <see cref="ChoicesShown"/> fires; when the player picks,
    /// <see cref="ChoiceMade"/> fires (the pick is already recorded) and the
    /// talk moves on. A conversation that ends while options are up records
    /// nothing.</item>
    /// <item>After the last line the box hides and the camera blends back, time
    /// still stopped.</item>
    /// <item><see cref="LastEndReason"/> is set, then <see cref="ConversationEnded"/>
    /// fires, still before time resumes.</item>
    /// <item>Time resumes; input is released a frame later.</item>
    /// </list>
    ///
    /// The conversation runs while <c>Time.timeScale</c> is 0. Subscribers that
    /// animate during it must use unscaled time.
    ///
    /// <b>Aborts.</b> A conversation can end early: this component disabled or
    /// destroyed, the character killed, the director disabled (a script reload in
    /// Play mode does that). <see cref="ConversationEnded"/> still fires, with
    /// <see cref="LastEndReason"/> = <see cref="ConversationEndReason.Aborted"/>.
    /// A subscriber that changed something for the conversation must also undo
    /// it in its own <c>OnDisable</c>, because a reload can tear it down before
    /// any event arrives.
    ///
    /// A <see cref="Pickleball.Sandbox.Health"/> that should stop the character
    /// talking when it dies must sit on the same GameObject as this component,
    /// the NPC's root.
    /// </summary>
    [DisallowMultipleComponent]
    public class Talkable : MonoBehaviour
    {
        [Tooltip("What this character says. With no definition, or one with no lines, the " +
                 "character can't be talked to and shows no prompt.")]
        [SerializeField] private DialogueDefinition dialogue;

        [Tooltip("Head-height point the dialogue camera frames and the talk prompt floats " +
                 "above. Usually the Head bone. Empty uses this transform raised by Fallback " +
                 "Focus Height.")]
        [SerializeField] private Transform focusPoint;

        [Tooltip("Metres above the root used as the focus when Focus Point is empty.")]
        [Min(0f)]
        [SerializeField] private float fallbackFocusHeight = 1.2f;

        [Tooltip("Runtime set the player's interactor scans for nearby talkers. Filled by " +
                 "DialogueSetup.MakeTalkable. Empty: the character can still be talked to " +
                 "while the player is locked on to it, but never by walking up to it.")]
        [SerializeField] private TalkableSet registry;

        Health _health;

        /// <summary>The runtime set this character joins while enabled; may be null.</summary>
        public TalkableSet Registry => registry;

        /// <summary>The conversation this character holds; may be null.</summary>
        public DialogueDefinition Dialogue => dialogue;

        /// <summary>Name for the dialogue box: the definition's speaker name, else the GameObject's.</summary>
        public string DisplayName
            => dialogue != null && !string.IsNullOrEmpty(dialogue.SpeakerName) ? dialogue.SpeakerName : name;

        /// <summary>World point the camera frames and the prompt floats above.</summary>
        public Vector3 FocusPosition
            => focusPoint != null ? focusPoint.position : transform.position + Vector3.up * fallbackFocusHeight;

        /// <summary>True from <see cref="ConversationStarted"/> until <see cref="ConversationEnded"/>.</summary>
        public bool IsInConversation { get; private set; }

        /// <summary>Who this character is talking to (the player's root), or null outside a conversation.</summary>
        public Transform Partner { get; private set; }

        /// <summary>Index of the line being shown, or -1 outside a conversation and
        /// before its first line. Set before <see cref="LineStarted"/> fires.</summary>
        public int CurrentLineIndex { get; private set; } = -1;

        /// <summary>The line being shown (text and emote); default outside a
        /// conversation. Set before <see cref="LineStarted"/> fires.</summary>
        public DialogueLine CurrentLine { get; private set; }

        /// <summary>Id of the node being shown (a line, or the choice whose options
        /// are up); empty outside a conversation, and for a line notified without
        /// one. Never null.</summary>
        public string CurrentNodeId { get; private set; } = string.Empty;

        /// <summary>True while options are up and the player hasn't picked yet.</summary>
        public bool IsAwaitingChoice { get; private set; }

        /// <summary>How the last conversation ended. Set before <see cref="ConversationEnded"/> fires.</summary>
        public ConversationEndReason LastEndReason { get; private set; }

        /// <summary>
        /// Whether a conversation may start now. False while disabled, already
        /// talking, without lines, or dead (when a <see cref="Pickleball.Sandbox.Health"/>
        /// sits on the same GameObject).
        /// </summary>
        public bool CanTalk
            => isActiveAndEnabled && !IsInConversation
               && dialogue != null && dialogue.IsPlayable
               && (OwnHealth == null || !OwnHealth.IsDead);

        /// <summary>A conversation began. <see cref="Partner"/> is set. Time is already paused.</summary>
        public event Action<Talkable> ConversationStarted;

        /// <summary>Line <c>index</c> began typing out. <see cref="CurrentLine"/> holds it,
        /// emote included.</summary>
        public event Action<Talkable, int> LineStarted;

        /// <summary>Line <c>index</c> is fully shown and waiting for the player to advance.
        /// Fires whether the text typed out or the player skipped the typewriter.</summary>
        public event Action<Talkable, int> LineRevealed;

        /// <summary>The conversation finished, normally or not (see <see cref="LastEndReason"/>).
        /// Fires before time resumes.</summary>
        public event Action<Talkable> ConversationEnded;

        /// <summary>Options are up at choice node <c>nodeId</c>, under the line just
        /// shown, and the player is deciding. <see cref="IsAwaitingChoice"/> is set.</summary>
        public event Action<Talkable, string> ChoicesShown;

        /// <summary>The player picked option <c>choiceId</c> at choice node
        /// <c>nodeId</c>. It is already recorded and its effects applied.</summary>
        public event Action<Talkable, string, string> ChoiceMade;

        // Fetched lazily rather than in Awake so it survives a hot reload.
        // TryGetComponent, not GetComponent: in the Editor a missing component
        // from GetComponent allocates a "fake null" object on every call.
        Health OwnHealth
        {
            get
            {
                if (_health == null) TryGetComponent(out _health);
                return _health;
            }
        }

        // Joins and leaves the runtime set with the component, so a disabled or
        // destroyed character drops out of the interactor's scan at once, and a
        // script reload (which re-runs OnEnable) re-registers it.
        void OnEnable()
        {
            if (registry != null) registry.Add(this);
        }

        void OnDisable()
        {
            if (registry != null) registry.Remove(this);
            // Disabled or destroyed mid-conversation: end it here, so listeners
            // hear about it even if the director only notices next frame.
            if (IsInConversation) NotifyConversationEnded(ConversationEndReason.Aborted);
        }

        /// <summary>Director only: a conversation with <paramref name="partner"/> has
        /// begun. Ignored while one is already running.</summary>
        public void NotifyConversationStarted(Transform partner)
        {
            if (IsInConversation) return;
            IsInConversation = true;
            Partner = partner;
            CurrentLineIndex = -1;
            CurrentLine = default;
            CurrentNodeId = string.Empty;
            IsAwaitingChoice = false;
            ConversationStarted?.Invoke(this);
        }

        /// <summary>Line <paramref name="index"/> of a linear conversation started
        /// typing: the line is looked up in the definition by index. Kept for
        /// callers that only have the index; the director uses the overload that
        /// passes the line.</summary>
        public void NotifyLineStarted(int index)
        {
            var line = dialogue != null && index >= 0 && index < dialogue.LineCount
                ? dialogue.GetLine(index)
                : default;
            NotifyLineStarted(index, line, string.Empty);
        }

        /// <summary>Director only: <paramref name="line"/> started typing, the
        /// <paramref name="index"/>-th line this conversation has shown, from node
        /// <paramref name="nodeId"/>.</summary>
        public void NotifyLineStarted(int index, DialogueLine line, string nodeId)
        {
            CurrentLineIndex = index;
            CurrentLine = line;
            CurrentNodeId = nodeId ?? string.Empty;
            IsAwaitingChoice = false;
            LineStarted?.Invoke(this, index);
        }

        /// <summary>Director only: options are up at choice node <paramref name="nodeId"/>.</summary>
        public void NotifyChoicesShown(string nodeId)
        {
            CurrentNodeId = nodeId ?? string.Empty;
            IsAwaitingChoice = true;
            ChoicesShown?.Invoke(this, CurrentNodeId);
        }

        /// <summary>Director only: the player picked <paramref name="choiceId"/> at
        /// <paramref name="nodeId"/>.</summary>
        public void NotifyChoiceMade(string nodeId, string choiceId)
        {
            IsAwaitingChoice = false;
            ChoiceMade?.Invoke(this, nodeId ?? string.Empty, choiceId ?? string.Empty);
        }

        /// <summary>Director only: line <paramref name="index"/> is fully shown.</summary>
        public void NotifyLineRevealed(int index) => LineRevealed?.Invoke(this, index);

        /// <summary>Director only (and this component's own OnDisable): the
        /// conversation is over, for <paramref name="reason"/>. Ignored when no
        /// conversation is running, so a second call is harmless.</summary>
        public void NotifyConversationEnded(ConversationEndReason reason = ConversationEndReason.Completed)
        {
            if (!IsInConversation) return;
            IsInConversation = false;
            Partner = null;
            CurrentLineIndex = -1;
            CurrentLine = default;
            CurrentNodeId = string.Empty;
            IsAwaitingChoice = false;
            LastEndReason = reason;
            ConversationEnded?.Invoke(this);
        }
    }
}
