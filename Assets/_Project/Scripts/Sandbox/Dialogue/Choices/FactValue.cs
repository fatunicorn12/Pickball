using System;
using System.Globalization;
using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>Which field of a <see cref="FactValue"/> holds the value.</summary>
    public enum FactType
    {
        Bool = 0,
        Int = 1,
        String = 2,
    }

    /// <summary>
    /// One value in the facts table: a bool, an int or a string.
    ///
    /// A small tagged struct rather than <c>object</c>, so it serializes inline
    /// on dialogue assets (choice effects and conditions) and in the save file,
    /// and compares without boxing. <c>default</c> is <c>false</c>.
    ///
    /// Reads convert between types so content can't trip over them: an int
    /// reads as a bool by being non-zero, a string as an int by parsing it.
    /// </summary>
    [Serializable]
    public struct FactValue : IEquatable<FactValue>
    {
        [SerializeField] private FactType type;
        [SerializeField] private bool boolValue;
        [SerializeField] private int intValue;
        [SerializeField] private string stringValue;

        public static FactValue Of(bool value) => new() { type = FactType.Bool, boolValue = value };
        public static FactValue Of(int value) => new() { type = FactType.Int, intValue = value };
        public static FactValue Of(string value) => new() { type = FactType.String, stringValue = value ?? string.Empty };

        /// <summary>The default value of <paramref name="type"/>: false, 0 or "".
        /// What a fact that was never set reads as when compared.</summary>
        public static FactValue DefaultOf(FactType type) => type switch
        {
            FactType.Int => Of(0),
            FactType.String => Of(string.Empty),
            _ => Of(false),
        };

        public FactType Type => type;

        public bool AsBool => type switch
        {
            FactType.Int => intValue != 0,
            FactType.String => !string.IsNullOrEmpty(stringValue),
            _ => boolValue,
        };

        public int AsInt => type switch
        {
            FactType.Bool => boolValue ? 1 : 0,
            FactType.String => int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0,
            _ => intValue,
        };

        /// <summary>Never null.</summary>
        public string AsString => type switch
        {
            FactType.Bool => boolValue ? "true" : "false",
            FactType.Int => intValue.ToString(CultureInfo.InvariantCulture),
            _ => stringValue ?? string.Empty,
        };

        /// <summary>Same type and same payload. A bool true and an int 1 are not equal.</summary>
        public bool Equals(FactValue other) => type == other.type && type switch
        {
            FactType.Int => intValue == other.intValue,
            FactType.String => string.Equals(AsString, other.AsString, StringComparison.Ordinal),
            _ => boolValue == other.boolValue,
        };

        public override bool Equals(object obj) => obj is FactValue other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(type, type switch
        {
            FactType.Int => intValue,
            FactType.String => AsString.GetHashCode(),
            _ => boolValue ? 1 : 0,
        });

        public static bool operator ==(FactValue a, FactValue b) => a.Equals(b);
        public static bool operator !=(FactValue a, FactValue b) => !a.Equals(b);

        public override string ToString() => $"{type}:{AsString}";
    }
}
