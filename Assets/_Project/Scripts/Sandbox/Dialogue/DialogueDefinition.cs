using System;
using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// A complete conversation: the speaker's name and the lines in order.
    ///
    /// Content lives on an asset rather than on the NPC so writers can edit it
    /// without touching scenes, and so a setup menu can seed it once and then
    /// leave hand edits alone.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Dialogue/Dialogue Definition", fileName = "Dialogue")]
    public class DialogueDefinition : ScriptableObject
    {
        [Tooltip("Name shown on the dialogue box's name tab. Empty falls back to the NPC's " +
                 "GameObject name.")]
        [SerializeField] private string speakerName = "";

        [Tooltip("Lines in the order they are shown. E advances to the next one; the " +
                 "conversation ends after the last.")]
        [SerializeField] private DialogueLine[] lines = Array.Empty<DialogueLine>();

        /// <summary>Name for the dialogue box's name tab; may be empty.</summary>
        public string SpeakerName => speakerName;

        /// <summary>Number of lines. A definition with none cannot be talked to.</summary>
        public int LineCount => lines != null ? lines.Length : 0;

        /// <summary>The line at <paramref name="index"/> (0-based, below <see cref="LineCount"/>).</summary>
        public DialogueLine GetLine(int index) => lines[index];
    }
}
