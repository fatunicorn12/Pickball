namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// A step as the resolver placed it: the swing, why it is there, and where
    /// it sits in the chain. Presentation reads <see cref="Role"/> and
    /// <see cref="IsLast"/> to make finishers land harder without each step
    /// asset having to declare that it is one.
    /// </summary>
    public readonly struct ResolvedStep
    {
        /// <summary>The swing to play. Null means the chain has nothing more.</summary>
        public ComboStepDefinition Step { get; }

        /// <summary>Regular for a base hit, otherwise the role of the ability (or
        /// default finisher) that put it here.</summary>
        public ComboRole Role { get; }

        /// <summary>Which chain the step was placed in.</summary>
        public ComboTrack Track { get; }

        /// <summary>0-based position in the chain.</summary>
        public int Index { get; }

        /// <summary>True when no further step follows in this chain.</summary>
        public bool IsLast { get; }

        /// <summary>The ability responsible, or null for the moveset's own steps.</summary>
        public SwordAbilityDefinition Source { get; }

        public bool IsValid => Step != null;

        /// <summary>True for the first hit of a chain.</summary>
        public bool StartsChain => Index == 0;

        public ResolvedStep(ComboStepDefinition step, ComboRole role, ComboTrack track, int index,
                            bool isLast, SwordAbilityDefinition source = null)
        {
            Step = step;
            Role = role;
            Track = track;
            Index = index;
            IsLast = isLast;
            Source = source;
        }

        /// <summary>The same placement, re-flagged as the chain's last hit.</summary>
        public ResolvedStep AsLast() => new(Step, Role, Track, Index, true, Source);
    }
}
