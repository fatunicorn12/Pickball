using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>What a <see cref="FactCondition"/> tests.</summary>
    public enum FactConditionKind
    {
        /// <summary>The fact equals Value. A fact never set reads as Value's type default (false, 0, "").</summary>
        FactEquals = 0,
        FactNotEquals = 1,
        /// <summary>The fact, read as an int, is at least Value's int.</summary>
        FactAtLeast = 2,
        /// <summary>The fact, read as an int, is at most Value's int.</summary>
        FactAtMost = 3,
        FactExists = 4,
        FactMissing = 5,
        /// <summary>The player picked ChoiceId at DialogueId/NodeId (or anything there, if ChoiceId is empty).</summary>
        Chose = 6,
        NotChose = 7,
    }

    /// <summary>
    /// One test against the <see cref="IChoiceStore"/>, authored on dialogue
    /// assets: shows, hides or greys out an option, or picks a branch.
    ///
    /// The fact kinds use <see cref="Key"/> and <see cref="Value"/>; the choice
    /// kinds use <see cref="DialogueId"/>, <see cref="NodeId"/> and
    /// <see cref="ChoiceId"/>. A null store behaves as an empty one, so content
    /// still runs in a scene with no store wired.
    /// </summary>
    [Serializable]
    public struct FactCondition
    {
        [SerializeField] private FactConditionKind kind;

        [Tooltip("Fact key, for the Fact* kinds.")]
        [SerializeField] private string key;

        [Tooltip("Compared against the fact, for FactEquals/NotEquals/AtLeast/AtMost.")]
        [SerializeField] private FactValue value;

        [Tooltip("For Chose/NotChose: the conversation's id. Empty means the conversation this condition sits in.")]
        [SerializeField] private string dialogueId;

        [Tooltip("For Chose/NotChose: the choice node's id.")]
        [SerializeField] private string nodeId;

        [Tooltip("For Chose/NotChose: the option's id. Empty means any option at that node.")]
        [SerializeField] private string choiceId;

        public FactConditionKind Kind => kind;
        public string Key => key ?? string.Empty;
        public FactValue Value => value;
        public string DialogueId => dialogueId ?? string.Empty;
        public string NodeId => nodeId ?? string.Empty;
        public string ChoiceId => choiceId ?? string.Empty;

        public static FactCondition Fact(FactConditionKind kind, string key, FactValue value = default)
            => new() { kind = kind, key = key, value = value };

        public static FactCondition Choice(bool chose, string dialogueId, string nodeId, string choiceId = "")
            => new()
            {
                kind = chose ? FactConditionKind.Chose : FactConditionKind.NotChose,
                dialogueId = dialogueId, nodeId = nodeId, choiceId = choiceId,
            };

        /// <param name="currentDialogueId">Used when <see cref="DialogueId"/> is empty.</param>
        public bool Evaluate(IChoiceStore store, string currentDialogueId = null)
        {
            switch (kind)
            {
                case FactConditionKind.FactExists:
                    return store != null && store.TryGetFact(Key, out _);
                case FactConditionKind.FactMissing:
                    return store == null || !store.TryGetFact(Key, out _);
                case FactConditionKind.Chose:
                case FactConditionKind.NotChose:
                {
                    string dialogue = string.IsNullOrEmpty(dialogueId) ? currentDialogueId ?? string.Empty : dialogueId;
                    string choice = string.IsNullOrEmpty(choiceId) ? null : choiceId;
                    bool chose = store != null && store.HasChosen(dialogue, NodeId, choice);
                    return kind == FactConditionKind.Chose ? chose : !chose;
                }
            }

            FactValue fact = store != null && store.TryGetFact(Key, out FactValue v) ? v : FactValue.DefaultOf(value.Type);
            return kind switch
            {
                FactConditionKind.FactEquals => Same(fact, value),
                FactConditionKind.FactNotEquals => !Same(fact, value),
                FactConditionKind.FactAtLeast => fact.AsInt >= value.AsInt,
                FactConditionKind.FactAtMost => fact.AsInt <= value.AsInt,
                _ => false,
            };
        }

        /// <summary>True when every condition holds; an empty or null list holds.</summary>
        public static bool AllMet(IReadOnlyList<FactCondition> conditions, IChoiceStore store, string currentDialogueId = null)
        {
            if (conditions == null) return true;
            for (int i = 0; i < conditions.Count; i++)
                if (!conditions[i].Evaluate(store, currentDialogueId)) return false;
            return true;
        }

        // Compared in the authored value's type, so a condition "met_birdy == true"
        // holds whether the fact was stored as a bool or as 1.
        static bool Same(FactValue fact, FactValue authored) => authored.Type switch
        {
            FactType.Int => fact.AsInt == authored.AsInt,
            FactType.String => string.Equals(fact.AsString, authored.AsString, StringComparison.Ordinal),
            _ => fact.AsBool == authored.AsBool,
        };
    }
}
