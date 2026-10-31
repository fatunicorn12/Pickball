using System.Collections.Generic;

namespace Pickleball.Cutscene.Script
{
    /// <summary>
    /// One <c>## Scene N: Title</c> section: its number, title and the italic stage directions,
    /// kept as notes for the people building the shots (they are never voiced or subtitled).
    /// </summary>
    public sealed class ParsedScene
    {
        /// <summary>Creates a parsed scene.</summary>
        public ParsedScene(int number, string title, IReadOnlyList<string> notes)
        {
            Number = number;
            Title = title;
            Notes = notes;
        }

        /// <summary>Scene number.</summary>
        public int Number { get; }

        /// <summary>Title after the colon.</summary>
        public string Title { get; }

        /// <summary>Stage directions in order, without the asterisks.</summary>
        public IReadOnlyList<string> Notes { get; }
    }
}
