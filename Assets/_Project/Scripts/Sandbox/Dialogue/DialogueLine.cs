using System;
using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// One box of text in a conversation: what is said and how it is delivered.
    /// A struct so a definition's line array serializes inline and edits as a
    /// plain list in the Inspector.
    /// </summary>
    [Serializable]
    public struct DialogueLine
    {
        [Tooltip("Text shown in the dialogue box. Keep it to about two lines at the default box " +
                 "width; longer text wraps to a third line and starts to crowd the name tab.")]
        [TextArea(2, 4)]
        [SerializeField] private string text;

        [Tooltip("Gesture the speaker plays while this line types out.")]
        [SerializeField] private DialogueEmote emote;

        /// <summary>Creates a line in code: tests and setup menus that seed content.</summary>
        public DialogueLine(string text, DialogueEmote emote = DialogueEmote.Talk)
        {
            this.text = text;
            this.emote = emote;
        }

        /// <summary>The words shown in the box. Never null.</summary>
        public string Text => text ?? string.Empty;

        /// <summary>How the speaker delivers this line.</summary>
        public DialogueEmote Emote => emote;
    }
}
