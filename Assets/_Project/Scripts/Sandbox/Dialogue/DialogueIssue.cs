namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// One problem <see cref="DialogueValidator"/> found in a
    /// <see cref="DialogueDefinition"/>: how bad, which node (empty for the
    /// conversation as a whole) and what to do about it.
    /// </summary>
    public readonly struct DialogueIssue
    {
        public DialogueIssue(DialogueIssueSeverity severity, string nodeId, string message)
        {
            Severity = severity;
            NodeId = nodeId ?? string.Empty;
            Message = message ?? string.Empty;
        }

        /// <summary>Error or warning.</summary>
        public DialogueIssueSeverity Severity { get; }

        /// <summary>The node it concerns; empty for the whole conversation.</summary>
        public string NodeId { get; }

        /// <summary>What is wrong, in words a writer can act on.</summary>
        public string Message { get; }

        /// <summary>True for <see cref="DialogueIssueSeverity.Error"/>.</summary>
        public bool IsError => Severity == DialogueIssueSeverity.Error;

        public override string ToString()
            => string.IsNullOrEmpty(NodeId) ? $"{Severity}: {Message}" : $"{Severity} [{NodeId}]: {Message}";
    }
}
