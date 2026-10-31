using System.Collections.Generic;

namespace Pickleball.Cutscene.Script
{
    /// <summary>The whole parsed script: its scenes and every voiced line in script order.</summary>
    public sealed class ParsedScript
    {
        /// <summary>Creates a parsed script.</summary>
        public ParsedScript(IReadOnlyList<ParsedScene> scenes, IReadOnlyList<ParsedLine> lines)
        {
            Scenes = scenes;
            Lines = lines;
        }

        /// <summary>Scenes in heading order.</summary>
        public IReadOnlyList<ParsedScene> Scenes { get; }

        /// <summary>Lines in script order across all scenes.</summary>
        public IReadOnlyList<ParsedLine> Lines { get; }

        /// <summary>The line with this id, or null.</summary>
        public ParsedLine Find(string id)
        {
            for (int i = 0; i < Lines.Count; i++)
                if (Lines[i].Id == id) return Lines[i];
            return null;
        }
    }
}
