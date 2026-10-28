namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// How a <see cref="SpellDefinition"/> reaches its target once fire is
    /// pressed while aiming. <see cref="SpellCaster"/> switches on it; each value
    /// has its own gameplay component (and prefab) so a new kind of spell is a
    /// new delivery, not a new branch inside an existing one.
    ///
    /// Stored as an int in every spell asset: append only, never reorder.
    /// </summary>
    public enum SpellDelivery
    {
        /// <summary>A projectile from the hand to the crosshair (<see cref="MagicOrb"/>):
        /// the Arcane Orb and the Fireball.</summary>
        Orb = 0,

        /// <summary>A telegraphed strike called down onto the crosshair point or
        /// the target under it (<see cref="LightningStrike"/>): the Thunderbolt.</summary>
        Strike = 1,

        /// <summary>An area placed on the ground at the crosshair point
        /// (<see cref="FreezeZone"/>): the Freeze Zone.</summary>
        Zone = 2,

        /// <summary>Held fire, standing still, for a fixed time; restores the
        /// caster's own health (<see cref="SpellChannel"/>): Heal. Needs no
        /// crosshair.</summary>
        Channel = 3,
    }
}
