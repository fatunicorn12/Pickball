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
    /// The conversation runs while <c>Time.timeScale</c> is 0. Subscribers that
    /// animate during it must use unscaled time.
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

        /// <summary>
        /// Whether a conversation may start now. False while disabled, already
        /// talking, without lines, or dead (when a <see cref="Pickleball.Sandbox.Health"/>
        /// sits on the same GameObject).
        /// </summary>
        public bool CanTalk
            => isActiveAndEnabled && !IsInConversation
               && dialogue != null && dialogue.LineCount > 0
               && (OwnHealth == null || !OwnHealth.IsDead);

        /// <summary>A conversation began. <see cref="Partner"/> is set. Time is already paused.</summary>
        public event Action<Talkable> ConversationStarted;

        /// <summary>Line <c>index</c> began typing out. Read its emote from <see cref="Dialogue"/>.</summary>
        public event Action<Talkable, int> LineStarted;

        /// <summary>Line <c>index</c> is fully shown and waiting for the player to advance.</summary>
        public event Action<Talkable, int> LineRevealed;

        /// <summary>The conversation finished. Fires before time resumes.</summary>
        public event Action<Talkable> ConversationEnded;

        // Fetched lazily rather than in Awake so it survives a hot reload.
        Health OwnHealth
        {
            get
            {
                if (_health == null) _health = GetComponent<Health>();
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
        }

        /// <summary>Director only: a conversation with <paramref name="partner"/> has begun.</summary>
        public void NotifyConversationStarted(Transform partner)
        {
            IsInConversation = true;
            Partner = partner;
            ConversationStarted?.Invoke(this);
        }

        /// <summary>Director only: line <paramref name="index"/> started typing.</summary>
        public void NotifyLineStarted(int index) => LineStarted?.Invoke(this, index);

        /// <summary>Director only: line <paramref name="index"/> is fully shown.</summary>
        public void NotifyLineRevealed(int index) => LineRevealed?.Invoke(this, index);

        /// <summary>Director only: the conversation is over.</summary>
        public void NotifyConversationEnded()
        {
            if (!IsInConversation) return;
            IsInConversation = false;
            Partner = null;
            ConversationEnded?.Invoke(this);
        }
    }
}
