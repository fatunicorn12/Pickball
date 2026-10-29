namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>What <see cref="ChoiceStoreSave.Load"/> found and did, so the
    /// caller decides what to log and whether saving is still safe. The save
    /// code itself never logs.</summary>
    public readonly struct ChoiceLoadResult
    {
        public ChoiceLoadResult(ChoiceLoadStatus status, double playTime = 0, int fileVersion = 0,
            string message = null, string quarantinedPath = null)
        {
            Status = status;
            PlayTime = playTime;
            FileVersion = fileVersion;
            Message = message ?? string.Empty;
            QuarantinedPath = quarantinedPath;
        }

        public ChoiceLoadStatus Status { get; }

        /// <summary>Play time banked in the file, in seconds; 0 unless loaded or migrated.</summary>
        public double PlayTime { get; }

        /// <summary>The version the file declared (0 if missing or unparsable).</summary>
        public int FileVersion { get; }

        /// <summary>A human-readable explanation for the log; empty when there is nothing to say.</summary>
        public string Message { get; }

        /// <summary>Where a <see cref="ChoiceLoadStatus.Corrupt"/> file was moved; null otherwise.</summary>
        public string QuarantinedPath { get; }

        /// <summary>False for <see cref="ChoiceLoadStatus.TooNew"/> and
        /// <see cref="ChoiceLoadStatus.Failed"/>: the file on disk is not one this
        /// build understands or could move aside, so writing would destroy it.</summary>
        public bool SafeToSave => Status != ChoiceLoadStatus.TooNew && Status != ChoiceLoadStatus.Failed;

        public override string ToString() => string.IsNullOrEmpty(Message) ? Status.ToString() : $"{Status}: {Message}";
    }
}
