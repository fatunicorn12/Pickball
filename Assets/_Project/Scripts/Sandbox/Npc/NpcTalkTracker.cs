using Pickleball.Sandbox.Dialogue;

namespace Pickleball.Sandbox.Npc
{
    /// <summary>
    /// What an NPC's body should be doing in a conversation, worked out from
    /// the <see cref="Talkable"/> and <see cref="Health"/> events alone: idle,
    /// listening between lines, gesturing a line with a given emote, or dead.
    ///
    /// A plain class so the rules can be tested without a scene or an
    /// Animator. <see cref="NpcAnimatorDriver"/> feeds it events and copies its
    /// answers into Animator parameters every frame.
    ///
    /// The one timing rule: a gesture is held for at least
    /// <see cref="MinGestureSeconds"/> after its line starts, even if the
    /// player skips the typing in the same frame. Without it a skipped line
    /// would start and stop the gesture within one frame and the Animator
    /// would never see it.
    /// </summary>
    public sealed class NpcTalkTracker
    {
        bool _lineTyping;
        float _gestureUntil = float.NegativeInfinity;

        /// <summary>Seconds a line's gesture plays at minimum (unscaled time).</summary>
        public float MinGestureSeconds { get; set; }

        /// <summary>True from the conversation's start until its end.</summary>
        public bool Talking { get; private set; }

        /// <summary>True once the NPC has died. Never cleared: the NPC stays dead.</summary>
        public bool Dead { get; private set; }

        /// <summary>The emote of the most recent line.</summary>
        public DialogueEmote Emote { get; private set; }

        /// <param name="minGestureSeconds">See <see cref="MinGestureSeconds"/>.</param>
        public NpcTalkTracker(float minGestureSeconds = 0f) => MinGestureSeconds = minGestureSeconds;

        /// <summary>
        /// Whether a line's gesture should be playing at <paramref name="now"/>
        /// (unscaled seconds): while the line types out, and for at least
        /// <see cref="MinGestureSeconds"/> after it started. Never while dead.
        /// </summary>
        public bool IsSpeaking(float now) => Talking && !Dead && (_lineTyping || now < _gestureUntil);

        /// <summary>A conversation began.</summary>
        public void BeginConversation()
        {
            Talking = true;
            _lineTyping = false;
            _gestureUntil = float.NegativeInfinity;
        }

        /// <summary>
        /// A line started typing at <paramref name="now"/> (unscaled seconds).
        /// Implies a conversation, so a line seen without its start (a script
        /// reload mid-conversation) still plays.
        /// </summary>
        public void BeginLine(DialogueEmote emote, float now)
        {
            Talking = true;
            Emote = emote;
            _lineTyping = true;
            _gestureUntil = now + (MinGestureSeconds > 0f ? MinGestureSeconds : 0f);
        }

        /// <summary>The current line is fully shown: settle back toward listening.</summary>
        public void RevealLine() => _lineTyping = false;

        /// <summary>The conversation is over.</summary>
        public void EndConversation()
        {
            Talking = false;
            _lineTyping = false;
            _gestureUntil = float.NegativeInfinity;
        }

        /// <summary>The NPC died. Death outranks every other state, for good.</summary>
        public void Die() => Dead = true;
    }
}
