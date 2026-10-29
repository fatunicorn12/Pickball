using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// The in-memory <see cref="IChoiceStore"/>: the choice log and the facts
    /// table, with no Unity runtime dependency beyond the contract's value
    /// types, so every rule is testable in EditMode. Saving lives in
    /// <see cref="ChoiceStoreSave"/>; <see cref="ChoiceStoreHost"/> owns one of
    /// these in a scene.
    ///
    /// <b>Time.</b> Each <see cref="ChoiceRecord.Time"/> comes from the injected
    /// clock (seconds of play time). The host passes a <see cref="PlayTimeClock"/>
    /// that carries play time across sessions; without one the store uses a
    /// stopwatch started when it was built.
    ///
    /// <b>Bad input is ignored, never thrown.</b> A write (<see cref="RecordChoice"/>,
    /// <see cref="SetFact"/>, <see cref="ClearFact"/>) with a null or empty key
    /// or id changes nothing, raises nothing and is reported to the injected
    /// <c>warning</c> sink (the host routes it to <c>Debug.LogWarning</c>; with no
    /// sink it is silent). A read with a null or empty key or id just finds
    /// nothing. Dialogue content is authored data, so a typo must cost a missing
    /// fact, not a broken conversation.
    ///
    /// <b>Events fire after the state is final.</b> Each operation finishes
    /// changing the store before it raises anything, so a listener always reads
    /// the new state. Listeners are called one at a time: one that throws is
    /// reported to the injected <c>listenerError</c> sink and the others still
    /// run. With no sink, the exception is rethrown once every listener of that
    /// operation has run, so it is never swallowed and never leaves the store
    /// half updated.
    ///
    /// <b>The log is append-only</b> through <see cref="IChoiceStore"/>; only
    /// <see cref="ResetAll"/> (new game) and <see cref="Restore"/> (loading a
    /// save) replace it. <see cref="Choices"/> and <see cref="Facts"/> are
    /// read-only live views, not copies.
    /// </summary>
    public sealed class ChoiceStore : IChoiceStore
    {
        readonly List<ChoiceRecord> _log = new();
        readonly Dictionary<string, FactValue> _facts = new(StringComparer.Ordinal);
        readonly ReadOnlyCollection<ChoiceRecord> _logView;
        readonly ReadOnlyDictionary<string, FactValue> _factsView;
        readonly Func<double> _clock;
        readonly Action<string> _warning;
        readonly Action<Exception> _listenerError;

        // Listener exceptions caught during the current operation when there is
        // no listenerError sink; rethrown when the operation ends.
        List<Exception> _caught;

        /// <param name="clock">Seconds of play time stamped on each pick. Null:
        /// a stopwatch started now.</param>
        /// <param name="warning">Told about rejected writes and skipped save
        /// entries. Null: ignored silently.</param>
        /// <param name="listenerError">Told about each listener that throws.
        /// Null: the exception is rethrown after every listener has run.</param>
        public ChoiceStore(Func<double> clock = null, Action<string> warning = null, Action<Exception> listenerError = null)
        {
            _clock = clock ?? StopwatchClock();
            _warning = warning;
            _listenerError = listenerError;
            _logView = _log.AsReadOnly();
            _factsView = new ReadOnlyDictionary<string, FactValue>(_facts);
        }

        /// <inheritdoc/>
        public IReadOnlyList<ChoiceRecord> Choices => _logView;

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, FactValue> Facts => _factsView;

        /// <summary>The clock's current reading: what the next pick would be stamped with.</summary>
        public double Now => _clock();

        /// <inheritdoc/>
        public event Action<ChoiceRecord> ChoiceRecorded;

        /// <inheritdoc/>
        public event Action<FactChange> FactChanged;

        /// <inheritdoc/>
        public event Action Reset;

        /// <inheritdoc/>
        /// <remarks>Any null or empty id: ignored with a warning, no event.</remarks>
        public void RecordChoice(string dialogueId, string nodeId, string choiceId)
        {
            if (string.IsNullOrEmpty(dialogueId) || string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(choiceId))
            {
                Warn($"RecordChoice ignored: dialogueId '{dialogueId}', nodeId '{nodeId}' and choiceId '{choiceId}' must all be non-empty.");
                return;
            }

            var record = new ChoiceRecord(dialogueId, nodeId, choiceId, _clock());
            _log.Add(record);
            Raise(ChoiceRecorded, record);
            ThrowCaught();
        }

        /// <inheritdoc/>
        /// <remarks>An empty <paramref name="choiceId"/> also means "anything at
        /// that node" (no pick can have an empty id). A null or empty dialogue or
        /// node id is never chosen.</remarks>
        public bool HasChosen(string dialogueId, string nodeId, string choiceId = null)
        {
            if (string.IsNullOrEmpty(dialogueId) || string.IsNullOrEmpty(nodeId)) return false;
            string choice = string.IsNullOrEmpty(choiceId) ? null : choiceId;
            for (int i = _log.Count - 1; i >= 0; i--)
                if (_log[i].Matches(dialogueId, nodeId, choice)) return true;
            return false;
        }

        /// <inheritdoc/>
        public string LastChoice(string dialogueId, string nodeId)
        {
            if (string.IsNullOrEmpty(dialogueId) || string.IsNullOrEmpty(nodeId)) return null;
            for (int i = _log.Count - 1; i >= 0; i--)
                if (_log[i].Matches(dialogueId, nodeId, null)) return _log[i].ChoiceId;
            return null;
        }

        /// <inheritdoc/>
        /// <remarks>A null or empty key is never set: false, quietly.</remarks>
        public bool TryGetFact(string key, out FactValue value)
        {
            if (!string.IsNullOrEmpty(key)) return _facts.TryGetValue(key, out value);
            value = default;
            return false;
        }

        /// <inheritdoc/>
        /// <remarks>"Changes" uses <see cref="FactValue"/> equality: same type and
        /// payload. Storing int 1 over bool true is a change. A null or empty key
        /// is ignored with a warning.</remarks>
        public void SetFact(string key, FactValue value)
        {
            if (string.IsNullOrEmpty(key))
            {
                Warn($"SetFact ignored: empty key (value {value}).");
                return;
            }

            bool existed = _facts.TryGetValue(key, out FactValue previous);
            if (existed && previous.Equals(value)) return;

            _facts[key] = value;
            Raise(FactChanged, new FactChange(key, existed, previous, true, value));
            ThrowCaught();
        }

        /// <inheritdoc/>
        /// <remarks>A null or empty key is ignored with a warning and returns false.</remarks>
        public bool ClearFact(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                Warn("ClearFact ignored: empty key.");
                return false;
            }

            if (!_facts.TryGetValue(key, out FactValue previous)) return false;

            _facts.Remove(key);
            Raise(FactChanged, new FactChange(key, true, previous, false, default));
            ThrowCaught();
            return true;
        }

        /// <inheritdoc/>
        /// <remarks>The log and every fact are gone before the first event, so
        /// listeners already see the empty store. The per-fact events come in
        /// ordinal key order; <see cref="Reset"/> is raised even when the store
        /// was already empty.</remarks>
        public void ResetAll()
        {
            List<KeyValuePair<string, FactValue>> cleared = SortedFacts();
            _log.Clear();
            _facts.Clear();

            for (int i = 0; i < cleared.Count; i++)
                Raise(FactChanged, new FactChange(cleared[i].Key, true, cleared[i].Value, false, default));
            Raise(Reset);
            ThrowCaught();
        }

        /// <summary>
        /// Replaces the whole state with a loaded one: the save system's entry
        /// point. Raises <see cref="Reset"/> once and no
        /// <see cref="FactChanged"/>, since a load is not a run of individual
        /// changes and listeners re-read everything on Reset anyway.
        ///
        /// Records with an empty id and facts with an empty key are skipped with
        /// a warning; for a repeated key the last value wins. Null sequences
        /// count as empty. Records keep their saved order and times.
        /// </summary>
        public void Restore(IEnumerable<ChoiceRecord> choices, IEnumerable<KeyValuePair<string, FactValue>> facts)
        {
            _log.Clear();
            _facts.Clear();
            int skipped = 0;

            if (choices != null)
                foreach (ChoiceRecord record in choices)
                {
                    if (record.DialogueId.Length == 0 || record.NodeId.Length == 0 || record.ChoiceId.Length == 0)
                    {
                        skipped++;
                        continue;
                    }
                    _log.Add(record);
                }

            if (facts != null)
                foreach (KeyValuePair<string, FactValue> fact in facts)
                {
                    if (string.IsNullOrEmpty(fact.Key))
                    {
                        skipped++;
                        continue;
                    }
                    _facts[fact.Key] = fact.Value;
                }

            if (skipped > 0) Warn($"Restore skipped {skipped} entr{(skipped == 1 ? "y" : "ies")} with an empty id or key.");
            Raise(Reset);
            ThrowCaught();
        }

        /// <summary>The facts as a new list sorted by key (ordinal), for stable
        /// event order and save files. Allocates; not for per-frame use.</summary>
        public List<KeyValuePair<string, FactValue>> SortedFacts()
        {
            var list = new List<KeyValuePair<string, FactValue>>(_facts);
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return list;
        }

        void Warn(string message) => _warning?.Invoke("ChoiceStore: " + message);

        // One listener at a time, so a throwing one can't stop the rest. The
        // invocation list allocates; these events fire a few times per talk.
        void Raise<T>(Action<T> handlers, T arg)
        {
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try { ((Action<T>)handler)(arg); }
                catch (Exception e) { Caught(e); }
            }
        }

        void Raise(Action handlers)
        {
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try { ((Action)handler)(); }
                catch (Exception e) { Caught(e); }
            }
        }

        void Caught(Exception e)
        {
            if (_listenerError != null) _listenerError(e);
            else (_caught ??= new List<Exception>()).Add(e);
        }

        void ThrowCaught()
        {
            if (_caught == null) return;
            List<Exception> caught = _caught;
            _caught = null;
            if (caught.Count == 1) ExceptionDispatchInfo.Capture(caught[0]).Throw();
            throw new AggregateException("Several ChoiceStore listeners threw.", caught);
        }

        static Func<double> StopwatchClock()
        {
            Stopwatch watch = Stopwatch.StartNew();
            return () => watch.Elapsed.TotalSeconds;
        }
    }
}
