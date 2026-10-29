using System;
using System.Collections.Generic;
using Pickleball.Sandbox.Dialogue;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// A small in-memory <see cref="IChoiceStore"/> for the dialogue tests: no
    /// disk, no clock, nothing shared, so a test can never touch the real save.
    /// Counts every write, so tests can pin "recorded once" and "nothing
    /// recorded".
    /// </summary>
    public sealed class FakeChoiceStore : IChoiceStore
    {
        readonly List<ChoiceRecord> _choices = new();
        readonly Dictionary<string, FactValue> _facts = new(StringComparer.Ordinal);

        /// <summary>Calls to <see cref="SetFact"/>, <see cref="ClearFact"/> (that removed something).</summary>
        public int FactWrites { get; private set; }

        public IReadOnlyList<ChoiceRecord> Choices => _choices;
        public IReadOnlyDictionary<string, FactValue> Facts => _facts;

        public event Action<ChoiceRecord> ChoiceRecorded;
        public event Action<FactChange> FactChanged;
        public event Action Reset;

        public void RecordChoice(string dialogueId, string nodeId, string choiceId)
        {
            var record = new ChoiceRecord(dialogueId, nodeId, choiceId, _choices.Count);
            _choices.Add(record);
            ChoiceRecorded?.Invoke(record);
        }

        public bool HasChosen(string dialogueId, string nodeId, string choiceId = null)
        {
            foreach (var record in _choices)
                if (record.Matches(dialogueId, nodeId, choiceId)) return true;
            return false;
        }

        public string LastChoice(string dialogueId, string nodeId)
        {
            for (int i = _choices.Count - 1; i >= 0; i--)
                if (_choices[i].Matches(dialogueId, nodeId, null)) return _choices[i].ChoiceId;
            return null;
        }

        public bool TryGetFact(string key, out FactValue value) => _facts.TryGetValue(key ?? string.Empty, out value);

        public void SetFact(string key, FactValue value)
        {
            FactWrites++;
            bool existed = _facts.TryGetValue(key, out var previous);
            if (existed && previous == value) return;
            _facts[key] = value;
            FactChanged?.Invoke(new FactChange(key, existed, previous, true, value));
        }

        public bool ClearFact(string key)
        {
            if (!_facts.TryGetValue(key, out var previous)) return false;
            FactWrites++;
            _facts.Remove(key);
            FactChanged?.Invoke(new FactChange(key, true, previous, false, default));
            return true;
        }

        public void ResetAll()
        {
            _choices.Clear();
            _facts.Clear();
            Reset?.Invoke();
        }
    }
}
