namespace Pickleball.Sandbox
{
    /// <summary>
    /// Whether the player is holding free aim (the spell's over-the-shoulder
    /// mode). It is how the systems that share aim's buttons step aside without
    /// knowing magic exists: while it is true the camera swings over the
    /// shoulder, lock-on lets go and ignores its toggle (RT fires instead), and
    /// the sword ignores Attack (left click fires instead).
    ///
    /// Implemented by <c>SpellCaster</c>. Readers take it as a serialized
    /// <c>MonoBehaviour</c> cast to this interface, like <c>ICombatMovement</c>,
    /// and treat a missing source as "never aiming".
    /// </summary>
    public interface IAimState
    {
        /// <summary>True every frame aim is held and allowed (not during a dash,
        /// tether or skateboard ride).</summary>
        bool IsAiming { get; }
    }
}
