namespace Pickleball.Sandbox.Inventory
{
    /// <summary>What <see cref="InventoryModel.Add"/> did with an item.</summary>
    public enum InventoryAddResult
    {
        /// <summary>The item took the next free slot on its tab.</summary>
        Added,

        /// <summary>Already owned (same asset, or another asset with the same id). Nothing changed.</summary>
        AlreadyOwned,

        /// <summary>Its tab has no free slot. Nothing changed; a pickup should stay in the world.</summary>
        Full,

        /// <summary>Null item. Nothing changed.</summary>
        Invalid,
    }
}
