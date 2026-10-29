using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>What a <see cref="FactEffect"/> does to its fact.</summary>
    public enum FactEffectKind
    {
        /// <summary>Store Value as the fact.</summary>
        Set = 0,
        /// <summary>Add Value's int to the fact read as an int (a fact never set counts as 0).</summary>
        Add = 1,
        /// <summary>Remove the fact.</summary>
        Clear = 2,
    }

    /// <summary>
    /// One write to the facts table, authored on a dialogue option: picking the
    /// option applies its effects. A null store ignores it, so content still
    /// runs in a scene with no store wired.
    /// </summary>
    [Serializable]
    public struct FactEffect
    {
        [SerializeField] private FactEffectKind kind;

        [Tooltip("Fact key to write.")]
        [SerializeField] private string key;

        [Tooltip("For Set: the value stored. For Add: the amount added (read as an int).")]
        [SerializeField] private FactValue value;

        public FactEffectKind Kind => kind;
        public string Key => key ?? string.Empty;
        public FactValue Value => value;

        public static FactEffect Set(string key, FactValue value) => new() { kind = FactEffectKind.Set, key = key, value = value };
        public static FactEffect Add(string key, int amount) => new() { kind = FactEffectKind.Add, key = key, value = FactValue.Of(amount) };
        public static FactEffect Clear(string key) => new() { kind = FactEffectKind.Clear, key = key };

        public void Apply(IChoiceStore store)
        {
            if (store == null || string.IsNullOrEmpty(key)) return;
            switch (kind)
            {
                case FactEffectKind.Set:
                    store.SetFact(key, value);
                    break;
                case FactEffectKind.Add:
                    store.SetFact(key, FactValue.Of(store.GetInt(key) + value.AsInt));
                    break;
                case FactEffectKind.Clear:
                    store.ClearFact(key);
                    break;
            }
        }

        /// <summary>Applies each effect in order.</summary>
        public static void ApplyAll(IReadOnlyList<FactEffect> effects, IChoiceStore store)
        {
            if (effects == null) return;
            for (int i = 0; i < effects.Count; i++) effects[i].Apply(store);
        }
    }
}
