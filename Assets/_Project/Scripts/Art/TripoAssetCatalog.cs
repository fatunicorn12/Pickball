using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Art
{
    /// <summary>
    /// The table that drives the Tripo import (Tools ▸ Pickleball ▸ Tripo ▸ Import
    /// Tripo Assets) and its validation test: one <see cref="TripoAssetSpec"/> per
    /// generated asset, with its size, pivot, budget, texture cap, collider and
    /// material.
    ///
    /// It is data, not code, so a new batch from the generator is new rows, not a
    /// new script. The import seeds it once with batch 1 when it is missing; after
    /// that this asset is the source of truth (the "seeded once" policy in
    /// Docs/Architecture/EditorSetup.md), and hand edits to it survive re-runs.
    ///
    /// Editor-only in practice: nothing at runtime references it, so it never
    /// ships in a build. It lives in the runtime assembly only so the EditMode
    /// tests, which reference Pickleball.Runtime alone, can read it.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Art/Tripo Asset Catalog", fileName = "TripoAssetCatalog")]
    public sealed class TripoAssetCatalog : ScriptableObject
    {
        /// <summary>Where the one catalog lives.</summary>
        public const string AssetPath = ArtRoot + "/TripoAssetCatalog.asset";
        /// <summary>Root of the imported models, textures and materials.</summary>
        public const string ArtRoot = "Assets/_Project/Art/Tripo";
        /// <summary>Folder of the game-ready prefabs.</summary>
        public const string PrefabRoot = "Assets/_Project/Prefabs/Tripo";

        [Tooltip("One row per Tripo asset. Add a row per new download, then run " +
                 "Tools ▸ Pickleball ▸ Tripo ▸ Import Tripo Assets.")]
        [SerializeField] private List<TripoAssetSpec> assets = new();

        /// <summary>Every row, in table order.</summary>
        public IReadOnlyList<TripoAssetSpec> Assets => assets;

        /// <summary>The row with this plan id, or null.</summary>
        public TripoAssetSpec Find(string id)
        {
            foreach (var spec in assets)
                if (spec != null && spec.Id == id) return spec;
            return null;
        }

        /// <summary>
        /// Appends a row unless one with the same id exists, and reports whether it
        /// did. Existing rows are never overwritten, so seeding a batch twice keeps
        /// any hand-tuning. Editor seeding only.
        /// </summary>
        public bool AddIfMissing(TripoAssetSpec spec)
        {
            if (spec == null || Find(spec.Id) != null) return false;
            assets.Add(spec);
            return true;
        }
    }
}
