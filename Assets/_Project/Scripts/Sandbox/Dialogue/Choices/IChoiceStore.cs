using System;
using System.Collections.Generic;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// A fact changed. <see cref="Existed"/>/<see cref="Previous"/> say what it
    /// was, <see cref="Exists"/>/<see cref="Current"/> what it is now; a cleared
    /// fact has <see cref="Exists"/> false.
    /// </summary>
    public readonly struct FactChange
    {
        public FactChange(string key, bool existed, FactValue previous, bool exists, FactValue current)
        {
            Key = key;
            Existed = existed;
            Previous = previous;
            Exists = exists;
            Current = current;
        }

        public string Key { get; }
        public bool Existed { get; }
        public FactValue Previous { get; }
        public bool Exists { get; }
        public FactValue Current { get; }
    }

    /// <summary>
    /// Everything the player has chosen in conversations, and the facts those
    /// choices set: the one place quests, reputation or later branches read
    /// from, so none of them needs to know about dialogue.
    ///
    /// Two parts:
    ///  - an append-only <see cref="Choices"/> log of every pick;
    ///  - a key/value <see cref="Facts"/> table that choice effects write
    ///    (<see cref="FactEffect"/>) and choice conditions read
    ///    (<see cref="FactCondition"/>).
    ///
    /// Plain C#, no MonoBehaviour, so it is testable in EditMode. Saving and
    /// loading live beside the store, not in it.
    /// </summary>
    public interface IChoiceStore
    {
        /// <summary>Every pick in the order made. Never null.</summary>
        IReadOnlyList<ChoiceRecord> Choices { get; }

        /// <summary>Every fact currently set. Never null.</summary>
        IReadOnlyDictionary<string, FactValue> Facts { get; }

        /// <summary>Appends a pick to the log and raises <see cref="ChoiceRecorded"/>.</summary>
        void RecordChoice(string dialogueId, string nodeId, string choiceId);

        /// <summary>Whether the player ever picked <paramref name="choiceId"/> at
        /// that node. A null <paramref name="choiceId"/> asks whether they picked
        /// anything there.</summary>
        bool HasChosen(string dialogueId, string nodeId, string choiceId = null);

        /// <summary>The most recent pick at that node, or null if none.</summary>
        string LastChoice(string dialogueId, string nodeId);

        bool TryGetFact(string key, out FactValue value);

        /// <summary>Sets a fact. Raises <see cref="FactChanged"/> only when the
        /// stored value actually changes.</summary>
        void SetFact(string key, FactValue value);

        /// <summary>Removes a fact. Returns whether it existed; raises
        /// <see cref="FactChanged"/> if it did.</summary>
        bool ClearFact(string key);

        /// <summary>New game: empties the log and the facts. Raises
        /// <see cref="FactChanged"/> for each fact cleared, then <see cref="Reset"/>.</summary>
        void ResetAll();

        event Action<ChoiceRecord> ChoiceRecorded;
        event Action<FactChange> FactChanged;

        /// <summary>After <see cref="ResetAll"/> or after a save is loaded over
        /// the current state: anything caching store data should re-read it.</summary>
        event Action Reset;
    }

    /// <summary>Typed reads with a fallback for facts that were never set.</summary>
    public static class ChoiceStoreExtensions
    {
        public static bool GetBool(this IChoiceStore store, string key, bool fallback = false)
            => store != null && store.TryGetFact(key, out FactValue v) ? v.AsBool : fallback;

        public static int GetInt(this IChoiceStore store, string key, int fallback = 0)
            => store != null && store.TryGetFact(key, out FactValue v) ? v.AsInt : fallback;

        public static string GetString(this IChoiceStore store, string key, string fallback = "")
            => store != null && store.TryGetFact(key, out FactValue v) ? v.AsString : fallback;
    }
}
