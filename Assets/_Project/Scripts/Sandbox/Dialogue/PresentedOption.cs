namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// An option as the player sees it right now: the authored
    /// <see cref="DialogueOption"/> and whether its conditions hold. Hidden
    /// options never become one of these; a disabled one does, with
    /// <see cref="IsEnabled"/> false, so the list can show it greyed out.
    /// </summary>
    public readonly struct PresentedOption
    {
        public PresentedOption(DialogueOption option, bool isEnabled)
        {
            Option = option;
            IsEnabled = isEnabled;
        }

        /// <summary>The authored option.</summary>
        public DialogueOption Option { get; }

        /// <summary>False while greyed out: shown, skipped by the cursor, can't be picked.</summary>
        public bool IsEnabled { get; }

        /// <summary>The option's stable id. Never null.</summary>
        public string OptionId => Option != null ? Option.Id : string.Empty;

        /// <summary>The words shown. Never null.</summary>
        public string Text => Option != null ? Option.Text : string.Empty;
    }
}
