namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// Everything <see cref="ComboResolver"/> is allowed to know when it picks
    /// the next step. Built fresh by <c>MeleeCombat</c> on each press.
    ///
    /// Kept to plain values on purpose: the resolver is pure logic, tested in
    /// EditMode without a scene, so nothing here may be a live component. New
    /// conditions (target airborne, surrounded, player dashing) add a field here
    /// and a check in <see cref="ComboCondition"/>, and nothing else changes.
    /// </summary>
    public readonly struct ComboContext
    {
        /// <summary>Which chain the press continues: ground or air.</summary>
        public ComboTrack Track { get; }

        /// <summary>Steps already played in this chain. 0 means the press starts
        /// a new chain. For the air track this counts every air hit since the
        /// player last touched the ground, so one jump buys one air combo.</summary>
        public int HitsInChain { get; }

        /// <summary>True while lock-on has a target.</summary>
        public bool HasTarget { get; }

        /// <summary>Distance to the locked target's surface, in metres. Only
        /// meaningful when <see cref="HasTarget"/> is true.</summary>
        public float TargetDistance { get; }

        public ComboContext(ComboTrack track, int hitsInChain,
                            bool hasTarget = false, float targetDistance = 0f)
        {
            Track = track;
            HitsInChain = hitsInChain;
            HasTarget = hasTarget;
            TargetDistance = targetDistance;
        }
    }
}
