using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// One step of a branching conversation: a line, a choice or a branch
    /// (<see cref="DialogueNodeKind"/>), found by its stable string <see cref="Id"/>.
    ///
    /// One flat class rather than three, so a definition is a single ordered
    /// list that reads top to bottom in the Inspector and serializes without
    /// <c>[SerializeReference]</c>. Each kind uses only some fields:
    /// <list type="table">
    /// <item><term>Line</term><description><see cref="Text"/>, <see cref="Emote"/>,
    /// <see cref="Next"/>.</description></item>
    /// <item><term>Choice</term><description><see cref="Options"/>; an optional prompt
    /// line in <see cref="Text"/> / <see cref="Emote"/>; <see cref="Next"/> is the
    /// fallback taken when no option can be picked.</description></item>
    /// <item><term>Branch</term><description><see cref="Conditions"/>, then
    /// <see cref="Next"/> if all hold, else <see cref="ElseNext"/>.</description></item>
    /// </list>
    /// An empty next id ends the conversation. <see cref="DialogueValidator"/>
    /// checks the links.
    /// </summary>
    [Serializable]
    public sealed class DialogueNode
    {
        // The first string field is what the Inspector labels list elements with.
        [Tooltip("Stable id, unique in this conversation. Other nodes and the choice log refer " +
                 "to it, so never rename it once it has shipped.")]
        [SerializeField] private string id = "";

        [Tooltip("Line: the speaker says Text. Choice: the player picks one of Options " +
                 "(after Text, if any). Branch: shows nothing, goes to Next if every condition " +
                 "holds, else to Else Next.")]
        [SerializeField] private DialogueNodeKind kind;

        [Tooltip("Line: what is said. Choice: an optional prompt line typed before the options " +
                 "appear; empty shows the options under the line before. Branch: unused. Keep " +
                 "it to about two lines at the default box width.")]
        [TextArea(2, 4)]
        [SerializeField] private string text = "";

        [Tooltip("Gesture the speaker plays while Text types out (Line, and a Choice's prompt).")]
        [SerializeField] private DialogueEmote emote;

        [Tooltip("Line: the node after this one. Branch: where to go when every condition " +
                 "holds. Choice: the fallback when no option can be picked (all hidden or " +
                 "disabled). Empty ends the conversation.")]
        [SerializeField] private string next = "";

        [Tooltip("Choice only: the answers offered, top to bottom.")]
        [SerializeField] private DialogueOption[] options = Array.Empty<DialogueOption>();

        [Tooltip("Branch only: all must hold to go to Next; otherwise Else Next.")]
        [SerializeField] private FactCondition[] conditions = Array.Empty<FactCondition>();

        [Tooltip("Branch only: where to go when a condition doesn't hold. Empty ends the " +
                 "conversation.")]
        [SerializeField] private string elseNext = "";

        /// <summary>Stable id. Never null.</summary>
        public string Id => id ?? string.Empty;

        /// <summary>What the node does.</summary>
        public DialogueNodeKind Kind => kind;

        /// <summary>The line, or a choice's prompt; may be empty. Never null.</summary>
        public string Text => text ?? string.Empty;

        /// <summary>Gesture for <see cref="Text"/>.</summary>
        public DialogueEmote Emote => emote;

        /// <summary>The line or prompt as a <see cref="DialogueLine"/>.</summary>
        public DialogueLine Line => new(Text, emote);

        /// <summary>Line: next node. Branch: when met. Choice: fallback. Empty ends. Never null.</summary>
        public string Next => next ?? string.Empty;

        /// <summary>Branch only: where to go when a condition fails. Never null.</summary>
        public string ElseNext => elseNext ?? string.Empty;

        /// <summary>Choice only: the options in order. Never null.</summary>
        public IReadOnlyList<DialogueOption> Options => options ?? Array.Empty<DialogueOption>();

        /// <summary>Branch only: the conditions. Never null.</summary>
        public IReadOnlyList<FactCondition> Conditions => conditions ?? Array.Empty<FactCondition>();

        /// <summary>True for a Line, and for a Choice with a prompt: the node types
        /// a line into the box.</summary>
        public bool ShowsLine => kind == DialogueNodeKind.Line
                                 || (kind == DialogueNodeKind.Choice && !string.IsNullOrEmpty(text));

        /// <summary>A line node, in code (tests, seeding setup menus).</summary>
        public static DialogueNode CreateLine(string id, string text, DialogueEmote emote = DialogueEmote.Talk,
                                              string next = "")
            => new() { id = id ?? string.Empty, kind = DialogueNodeKind.Line, text = text ?? string.Empty,
                       emote = emote, next = next ?? string.Empty };

        /// <summary>A choice node, in code. <paramref name="prompt"/> may be empty;
        /// <paramref name="fallbackNext"/> is taken when no option can be picked.
        /// The option array is copied.</summary>
        public static DialogueNode CreateChoice(string id, string prompt, DialogueEmote emote, string fallbackNext,
                                                params DialogueOption[] options)
            => new()
            {
                id = id ?? string.Empty, kind = DialogueNodeKind.Choice, text = prompt ?? string.Empty,
                emote = emote, next = fallbackNext ?? string.Empty,
                options = options != null ? (DialogueOption[])options.Clone() : Array.Empty<DialogueOption>(),
            };

        /// <summary>A branch node, in code. The condition array is copied.</summary>
        public static DialogueNode CreateBranch(string id, FactCondition[] conditions, string next, string elseNext)
            => new()
            {
                id = id ?? string.Empty, kind = DialogueNodeKind.Branch,
                conditions = conditions != null ? (FactCondition[])conditions.Clone() : Array.Empty<FactCondition>(),
                next = next ?? string.Empty, elseNext = elseNext ?? string.Empty,
            };
    }
}
