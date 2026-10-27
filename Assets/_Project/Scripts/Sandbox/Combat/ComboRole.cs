namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// Where a step sits in a chain, and so how an ability changes the chain.
    ///
    /// The model is Kingdom Hearts 2's: a chain is a run of ordinary hits capped
    /// by one finisher. Combo Plus lengthens the run, a finisher ability replaces
    /// the cap, and an opener replaces the first hit when its condition holds
    /// (Sliding Dash). See <see cref="ComboResolver"/> for the exact rules.
    /// </summary>
    public enum ComboRole
    {
        /// <summary>An ordinary hit from the moveset's base chain. Not valid on
        /// an ability: abilities change the chain, they are not the chain.</summary>
        Regular,

        /// <summary>Combo Plus / Air Combo Plus: one more hit, appended after the
        /// base hits and before the finisher.</summary>
        Extender,

        /// <summary>Replaces the moveset's default finisher as the chain's last
        /// hit. When several qualify, the highest priority wins.</summary>
        Finisher,

        /// <summary>Replaces the chain's first hit when its condition holds; the
        /// chain then continues from its second hit.</summary>
        Opener,
    }
}
