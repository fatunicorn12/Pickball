using System;
using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// One entry in the choice log: which option the player picked, where.
    ///
    /// Everything is a stable string id, never a list index, so a record made
    /// today still means the same thing after the dialogue asset is edited.
    /// Serializable so the save file can hold the log as written.
    /// </summary>
    [Serializable]
    public struct ChoiceRecord
    {
        [SerializeField] private string dialogueId;
        [SerializeField] private string nodeId;
        [SerializeField] private string choiceId;
        [SerializeField] private double time;

        public ChoiceRecord(string dialogueId, string nodeId, string choiceId, double time)
        {
            this.dialogueId = dialogueId ?? string.Empty;
            this.nodeId = nodeId ?? string.Empty;
            this.choiceId = choiceId ?? string.Empty;
            this.time = time;
        }

        /// <summary>The conversation's stable id (DialogueDefinition's id).</summary>
        public string DialogueId => dialogueId ?? string.Empty;

        /// <summary>The choice node's stable id within that conversation.</summary>
        public string NodeId => nodeId ?? string.Empty;

        /// <summary>The picked option's stable id within that node.</summary>
        public string ChoiceId => choiceId ?? string.Empty;

        /// <summary>When it was picked, in seconds on the store's clock (play
        /// time, not wall time), so the log orders and compares across sessions.</summary>
        public double Time => time;

        public bool Matches(string dialogue, string node, string choice)
            => string.Equals(DialogueId, dialogue, StringComparison.Ordinal)
               && string.Equals(NodeId, node, StringComparison.Ordinal)
               && (choice == null || string.Equals(ChoiceId, choice, StringComparison.Ordinal));

        public override string ToString() => $"{DialogueId}/{NodeId}/{ChoiceId} @{Time:0.##}s";
    }
}
