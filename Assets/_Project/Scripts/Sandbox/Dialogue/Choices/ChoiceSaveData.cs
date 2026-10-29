using System;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// The choice save file's shape, as <c>JsonUtility</c> writes it. Arrays
    /// rather than a dictionary because <c>JsonUtility</c> cannot serialize
    /// dictionaries. Public fields because this is a data transfer object, the
    /// file format, and nothing else.
    ///
    /// <code>
    /// {
    ///   "version": 1,
    ///   "playTime": 1234.5,
    ///   "choices": [ { "dialogueId": "birdy_intro", "nodeId": "ask_name", "choiceId": "polite", "time": 61.2 } ],
    ///   "facts":   [ { "key": "met_birdy", "value": { "type": 0, "boolValue": true, "intValue": 0, "stringValue": "" } } ]
    /// }
    /// </code>
    /// <c>type</c> is <see cref="FactType"/> as a number (0 Bool, 1 Int, 2
    /// String). Facts are written in ordinal key order so the file diffs cleanly.
    /// </summary>
    [Serializable]
    public class ChoiceSaveData
    {
        /// <summary>Format version; <see cref="ChoiceStoreSave.CurrentVersion"/> when written.</summary>
        public int version;

        /// <summary>Cumulative play time in seconds when the file was written.</summary>
        public double playTime;

        /// <summary>The choice log, oldest first.</summary>
        public ChoiceRecord[] choices = Array.Empty<ChoiceRecord>();

        /// <summary>The facts table.</summary>
        public FactEntry[] facts = Array.Empty<FactEntry>();

        /// <summary>One fact: its key and value.</summary>
        [Serializable]
        public struct FactEntry
        {
            public string key;
            public FactValue value;

            public FactEntry(string key, FactValue value)
            {
                this.key = key;
                this.value = value;
            }
        }
    }
}
