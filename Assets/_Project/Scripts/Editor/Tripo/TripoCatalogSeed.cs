using System.Collections.Generic;
using Pickleball.Art;
using UnityEngine;

namespace Pickleball.EditorTools.Tripo
{
    /// <summary>
    /// The rows each generator batch adds to the <see cref="TripoAssetCatalog"/>,
    /// written as code so an agent can add a batch without hand-editing asset
    /// YAML. The import appends every row here whose id the catalog lacks and
    /// never touches an existing row, so Inspector edits to the catalog win.
    ///
    /// Numbers come from the asset plan (scratchpad tripo/asset-plan.md, sections
    /// 3.3 to 5) and from how the game sizes each object today; the comment on each
    /// row says which.
    /// </summary>
    public static class TripoCatalogSeed
    {
        /// <summary>The inverse-square orb's gold (NewtonFightSetup.OrbColour).</summary>
        static readonly Color OrbGold = new(1f, 0.85f, 0.35f);

        /// <summary>Every seeded row, in batch order.</summary>
        public static IEnumerable<TripoAssetSpec> All()
        {
            foreach (var row in NewtonFight()) yield return row;
            foreach (var row in Guardians()) yield return row;
        }

        /// <summary>Rigged guardians (plan priority 3). Tripo's Mixamo rig preset,
        /// Blender-preset FBX export; imported as Humanoids.</summary>
        public static IEnumerable<TripoAssetSpec> Guardians()
        {
            // Pythagoras: 2.2 m, towering over the 1.56 m chibi like Newton. 4096 texture
            // imported at 2048 like the chibi; cloth-like smoothness like Birdy (0.15).
            // No collider: an NPC or boss setup adds its own capsule.
            yield return new TripoAssetSpec("GD-01", "Pythagoras", TripoCategory.Characters,
                TripoSizeAxis.Height, 2.2f, TripoPivot.BottomCentre,
                triangleBudget: 10000, triangleCap: 15000, textureMaxSize: 2048,
                TripoColliderShape.None, castShadows: true, smoothness: 0.15f,
                rig: TripoRig.Humanoid);
        }

        /// <summary>Batch 1: the Newton fight (plan priority 1, NF-01 to NF-07).</summary>
        public static IEnumerable<TripoAssetSpec> NewtonFight()
        {
            // Well core: GravityWell scales `core` to coreRadius x 2 x Intensity x pulse
            // (3.6 m at rest), so the mesh is a unit diameter like the sphere it replaces.
            // Effect core: no collider (the well is a field), no shadow.
            yield return new TripoAssetSpec("NF-01", "GravityWellCore", TripoCategory.BossAttacks,
                TripoSizeAxis.LargestExtent, 1f, TripoPivot.Centre,
                triangleBudget: 2000, triangleCap: 2500, textureMaxSize: 1024,
                TripoColliderShape.None, castShadows: false, smoothness: 0.8f);

            // Prism: about 0.5 m long, hovering at Newton's BeamMuzzle. Small on screen,
            // so 512. Boss-mounted: no collider; it is solid, so it casts a shadow.
            // The v2 export is an upright glass pyramid in a brass gear frame; turned
            // 90 degrees about X so its apex points +Z, the way the beam fires.
            yield return new TripoAssetSpec("NF-02", "FluxionPrism", TripoCategory.BossAttacks,
                TripoSizeAxis.LargestExtent, 0.5f, TripoPivot.Centre,
                triangleBudget: 2500, triangleCap: 3000, textureMaxSize: 512,
                TripoColliderShape.None, castShadows: true, smoothness: 0.5f,
                modelRotation: new Vector3(90f, 0f, 0f));

            // Apple: the barrage fits it to 0.6 m (FitToDiameter, largest extent), the
            // reward to 0.55 m. 20-30 airborne at once: 800 triangles is a hard cap.
            yield return new TripoAssetSpec("NF-03", "Apple", TripoCategory.Projectiles,
                TripoSizeAxis.LargestExtent, 0.6f, TripoPivot.Centre,
                triangleBudget: 800, triangleCap: 800, textureMaxSize: 512,
                TripoColliderShape.None, castShadows: true, smoothness: 0.55f);

            // Orb: geometry only, tinted gold with a glow. The volley scales the visual
            // (0.6 m at the reference distance), so unit size. 2,000 triangles: below
            // that Tripo tore the ring (ledger), and it is tiny on screen.
            yield return new TripoAssetSpec("NF-04", "InverseSquareOrb", TripoCategory.Projectiles,
                TripoSizeAxis.LargestExtent, 1f, TripoPivot.Centre,
                triangleBudget: 2000, triangleCap: 2000, textureMaxSize: 0,
                TripoColliderShape.None, castShadows: false, smoothness: 0.35f,
                baseColor: OrbGold, emissionColor: OrbGold * 0.6f);

            // Arena props: plan sizes, bottom-centre on the ground, box colliders.
            yield return new TripoAssetSpec("NF-05", "Telescope", TripoCategory.Props,
                TripoSizeAxis.Height, 1.6f, TripoPivot.BottomCentre,
                triangleBudget: 3000, triangleCap: 5000, textureMaxSize: 1024,
                TripoColliderShape.Box, castShadows: true, smoothness: 0.3f);

            yield return new TripoAssetSpec("NF-06", "AppleCrate", TripoCategory.Props,
                TripoSizeAxis.LargestHorizontal, 0.9f, TripoPivot.BottomCentre,
                triangleBudget: 2500, triangleCap: 5000, textureMaxSize: 1024,
                TripoColliderShape.Box, castShadows: true, smoothness: 0.2f);

            // The export faces -Z: turned to face +Z. Tripo painted it beige twice,
            // so the texture is desaturated and lifted toward the planned white marble.
            yield return new TripoAssetSpec("NF-07", "LeibnizBust", TripoCategory.Props,
                TripoSizeAxis.Height, 1.4f, TripoPivot.BottomCentre,
                triangleBudget: 3000, triangleCap: 5000, textureMaxSize: 1024,
                TripoColliderShape.Box, castShadows: true, smoothness: 0.35f,
                modelRotation: new Vector3(0f, 180f, 0f),
                textureSaturation: 0.15f, textureGain: 1.25f);
        }
    }
}
