namespace Pickleball.Sandbox
{
    /// <summary>
    /// What an attack asks its target to do, as Kingdom Hearts II's attack data
    /// does. The attack says what kind of blow it is; the receiver decides what
    /// that means for its body (a boss ignores most of it, a dummy obeys).
    /// </summary>
    public enum HitReaction
    {
        /// <summary>A small hit: it stuns, and in the air it holds the target up by
        /// its juggle rule. The default, so older data keeps its behaviour.</summary>
        Flinch = 0,

        /// <summary>Pops a grounded target into the air so it can be juggled.</summary>
        Launch = 1,

        /// <summary>Ends a juggle: throws the target away on an arc, and it needs a
        /// moment on the ground to recover. An air finisher's blow.</summary>
        KnockAway = 2,

        /// <summary>A big stagger on the spot: a longer stun, with no lift. A slam's blow.</summary>
        Heavy = 3,
    }
}
