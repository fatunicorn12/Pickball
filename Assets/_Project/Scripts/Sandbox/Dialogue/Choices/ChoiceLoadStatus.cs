namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>How <see cref="ChoiceStoreSave.Load"/> went. In every case the
    /// store holds a consistent state afterwards: the file's, or empty.</summary>
    public enum ChoiceLoadStatus
    {
        /// <summary>The file was read at the current version.</summary>
        Loaded = 0,

        /// <summary>The file was an older version and was migrated in memory.
        /// The next save writes it back at the current version.</summary>
        Migrated = 1,

        /// <summary>No save file: a new game. The store is empty.</summary>
        Missing = 2,

        /// <summary>The file was unreadable as a save. It was renamed to
        /// <c>&lt;name&gt;.corrupt-&lt;timestamp&gt;.json</c> beside it and the
        /// store is empty, so the next save starts fresh without losing it.</summary>
        Corrupt = 3,

        /// <summary>The file was written by a newer build. It is left untouched
        /// and the store is empty; the caller must not save over it.</summary>
        TooNew = 4,

        /// <summary>The file could not be read or quarantined (an I/O or
        /// permission error, or a path inside Assets/). It is left as is and
        /// the store is empty; the caller must not save over it.</summary>
        Failed = 5,
    }
}
