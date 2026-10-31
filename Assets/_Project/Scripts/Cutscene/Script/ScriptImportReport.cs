using System.Collections.Generic;

namespace Pickleball.Cutscene.Script
{
    /// <summary>
    /// What a re-import changed: ids that are new, ids that went away (and their audio with them),
    /// and how many assigned clips were carried over. The importer logs it so a renumbered script
    /// can't silently orphan voice lines.
    /// </summary>
    public sealed class ScriptImportReport
    {
        /// <summary>Creates a report.</summary>
        public ScriptImportReport(IReadOnlyList<string> added, IReadOnlyList<string> removed, int audioKept)
        {
            Added = added;
            Removed = removed;
            AudioKept = audioKept;
        }

        /// <summary>Ids in the new script that the asset did not have.</summary>
        public IReadOnlyList<string> Added { get; }

        /// <summary>Ids the asset had that the new script does not.</summary>
        public IReadOnlyList<string> Removed { get; }

        /// <summary>Lines whose AudioClip was carried over by id.</summary>
        public int AudioKept { get; }
    }
}
