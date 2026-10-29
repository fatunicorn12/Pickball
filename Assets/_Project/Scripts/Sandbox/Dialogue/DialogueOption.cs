using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// One answer the player can give at a <see cref="DialogueNodeKind.Choice"/>
    /// node: its words, where the talk goes after it, when it is offered, and
    /// what picking it changes.
    ///
    /// Picking it records <c>(conversation id, node id, option id)</c> in the
    /// <see cref="IChoiceStore"/> and then applies <see cref="Effects"/> in
    /// order. All references are stable string ids, never list positions, so a
    /// recorded pick still means the same thing after the asset is edited:
    /// <b>never rename an id that has shipped.</b>
    /// </summary>
    [Serializable]
    public sealed class DialogueOption
    {
        [Tooltip("Stable id, unique within this choice node. It is what the choice log " +
                 "records, so never rename it once it has shipped.")]
        [SerializeField] private string id = "";

        [Tooltip("What the player answers, in the option list. One short line: about 40 " +
                 "characters fits comfortably at 1080p.")]
        [SerializeField] private string text = "";

        [Tooltip("Id of the node the talk goes to after this option is picked. Empty ends " +
                 "the conversation.")]
        [SerializeField] private string next = "";

        [Tooltip("Every condition must hold for the option to be picked. Empty: always " +
                 "available.")]
        [SerializeField] private FactCondition[] conditions = Array.Empty<FactCondition>();

        [Tooltip("While a condition doesn't hold: Hide leaves the option out of the list; " +
                 "Disable shows it greyed out, and the cursor skips it.")]
        [SerializeField] private UnmetOptionMode whenUnmet = UnmetOptionMode.Hide;

        [Tooltip("Applied in order when the option is picked, after the pick is recorded. " +
                 "Ignored when no choice store is wired.")]
        [SerializeField] private FactEffect[] effects = Array.Empty<FactEffect>();

        /// <summary>Stable id within its node. Never null.</summary>
        public string Id => id ?? string.Empty;

        /// <summary>The answer shown in the list. Never null.</summary>
        public string Text => text ?? string.Empty;

        /// <summary>Node after this option; empty ends the talk. Never null.</summary>
        public string Next => next ?? string.Empty;

        /// <summary>All must hold for the option to be picked. Never null.</summary>
        public IReadOnlyList<FactCondition> Conditions => conditions ?? Array.Empty<FactCondition>();

        /// <summary>Whether the option is hidden or greyed out while unavailable.</summary>
        public UnmetOptionMode WhenUnmet => whenUnmet;

        /// <summary>Applied in order on pick. Never null.</summary>
        public IReadOnlyList<FactEffect> Effects => effects ?? Array.Empty<FactEffect>();

        /// <summary>True when the option has no conditions, so it is always offered.</summary>
        public bool IsUnconditional => conditions == null || conditions.Length == 0;

        /// <summary>Whether every condition holds against <paramref name="store"/>
        /// (null reads as an empty store) inside conversation <paramref name="dialogueId"/>.</summary>
        public bool IsAvailable(IChoiceStore store, string dialogueId)
            => FactCondition.AllMet(conditions, store, dialogueId);

        /// <summary>Builds an option in code: tests and the setup menus that seed
        /// content. The arrays are copied.</summary>
        public static DialogueOption Create(string id, string text, string next,
                                            FactCondition[] conditions = null,
                                            UnmetOptionMode whenUnmet = UnmetOptionMode.Hide,
                                            FactEffect[] effects = null)
            => new()
            {
                id = id ?? string.Empty,
                text = text ?? string.Empty,
                next = next ?? string.Empty,
                conditions = conditions != null ? (FactCondition[])conditions.Clone() : Array.Empty<FactCondition>(),
                whenUnmet = whenUnmet,
                effects = effects != null ? (FactEffect[])effects.Clone() : Array.Empty<FactEffect>(),
            };
    }
}
