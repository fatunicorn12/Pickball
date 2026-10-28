namespace Pickleball.Art
{
    /// <summary>
    /// What a Tripo asset is for. The name is also its folder under
    /// <see cref="TripoAssetCatalog.ArtRoot"/>, so renaming a value moves nothing
    /// on disk: add a new value instead and re-run the import.
    /// </summary>
    public enum TripoCategory
    {
        /// <summary>Solid set dressing that stands on the ground and gets a collider.</summary>
        Props,

        /// <summary>Things code throws around (apples, orbs). Centred, no collider.</summary>
        Projectiles,

        /// <summary>Visible parts of a boss attack (the well core, the beam prism).</summary>
        BossAttacks,

        /// <summary>Meshes for the player's spells.</summary>
        Spells,

        /// <summary>Rigged characters (imported by their own setup menus; listed here for budgets).</summary>
        Characters,

        /// <summary>Enemies.</summary>
        Enemies,

        /// <summary>Pickups and drops.</summary>
        Items,
    }
}
