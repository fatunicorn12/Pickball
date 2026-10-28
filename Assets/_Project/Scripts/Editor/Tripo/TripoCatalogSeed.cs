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
            foreach (var row in Spells()) yield return row;
            foreach (var row in BossProjectilesAndItems()) yield return row;
        }

        static readonly Color BoltBlue = new(0.62f, 0.85f, 1f);
        static readonly Color IceBlue = new(0.78f, 0.92f, 1f);

        /// <summary>
        /// Batch 2: spell meshes (plan priority 2). Each is authored to the
        /// convention its Magic slot expects (Docs/Systems/Magic.md §4, "Swap in a
        /// model"), so the hookup's modelScale stays near 1. Effect meshes: no
        /// collider, no shadow.
        /// </summary>
        public static IEnumerable<TripoAssetSpec> Spells()
        {
            // Fireball core: ~1 m across, centred, tail toward -Z (the orb spins it about
            // +Z, its travel). The export's flame tongues trail +X, so it is turned 90°
            // about Y. The painted base map doubles as the emission map.
            yield return new TripoAssetSpec("SP-01", "Fireball", TripoCategory.Spells,
                TripoSizeAxis.LargestExtent, 1f, TripoPivot.Centre,
                triangleBudget: 1500, triangleCap: 2000, textureMaxSize: 512,
                TripoColliderShape.None, castShadows: false, smoothness: 0.3f,
                emissionColor: new Color(1.4f, 1.1f, 0.9f), modelRotation: new Vector3(0f, 90f, 0f));

            // Bolt: 1 m tall, pivot at the bottom; the strike stretches it to the 7 m
            // cloud height. Geometry only: a hot blue-white emissive. Generated at 1,000.
            yield return new TripoAssetSpec("SP-02", "LightningBolt", TripoCategory.Spells,
                TripoSizeAxis.Height, 1f, TripoPivot.BottomCentre,
                triangleBudget: 1000, triangleCap: 2000, textureMaxSize: 0,
                TripoColliderShape.None, castShadows: false, smoothness: 0.2f,
                baseColor: BoltBlue, emissionColor: BoltBlue * 3f);

            // Thundercloud: centred on the strike's Cloud transform and scaled by the
            // Thunderbolt's modelScale, which it shares with the bolt's width. The bolt
            // needs ~2.5 to keep its zigzag when stretched to 7 m, so the cloud is
            // authored at 1.6 m to come out 4 m wide (the procedural puffs span ~5 m).
            // Tripo painted a uniform slate: the texture is lifted, then a vertex
            // gradient darkens the underside, so it reads as a storm cloud from below.
            yield return new TripoAssetSpec("SP-03", "Thundercloud", TripoCategory.Spells,
                TripoSizeAxis.LargestHorizontal, 1.6f, TripoPivot.Centre,
                triangleBudget: 2000, triangleCap: 2500, textureMaxSize: 1024,
                TripoColliderShape.None, castShadows: false, smoothness: 0.05f,
                textureSaturation: 0.5f, textureGain: 2f,
                gradientBottom: new Color(0.22f, 0.24f, 0.3f), gradientTop: Color.white);

            // Ice cluster: the Freeze Zone scales its model by the zone radius (3 m), so
            // it is authored for a 1 m radius: 0.5 m tall becomes 1.5 m in play.
            yield return new TripoAssetSpec("SP-04", "IceCrystalCluster", TripoCategory.Spells,
                TripoSizeAxis.Height, 0.5f, TripoPivot.BottomCentre,
                triangleBudget: 2000, triangleCap: 2500, textureMaxSize: 1024,
                TripoColliderShape.None, castShadows: false, smoothness: 0.85f,
                emissionColor: new Color(0.12f, 0.2f, 0.28f));

            // Ice spike: one ring piece, 1 m tall with its pivot at the base (the plan's
            // size); a ring of them is arranged by whoever builds the zone. Geometry only.
            yield return new TripoAssetSpec("SP-05", "IceSpike", TripoCategory.Spells,
                TripoSizeAxis.Height, 1f, TripoPivot.BottomCentre,
                triangleBudget: 800, triangleCap: 2000, textureMaxSize: 0,
                TripoColliderShape.None, castShadows: false, smoothness: 0.85f,
                baseColor: IceBlue, emissionColor: new Color(0.1f, 0.18f, 0.26f));

            // Heal lotus: replaces the heal's rune, which is scaled to the ring's diameter
            // (1.9 m) x modelScale, so it is authored 1 m across, sitting on its base.
            yield return new TripoAssetSpec("SP-06", "HealLotus", TripoCategory.Spells,
                TripoSizeAxis.LargestHorizontal, 1f, TripoPivot.BottomCentre,
                triangleBudget: 2000, triangleCap: 2500, textureMaxSize: 1024,
                TripoColliderShape.None, castShadows: false, smoothness: 0.3f,
                emissionColor: new Color(0.35f, 0.35f, 0.3f));
        }

        /// <summary>Batch 3: boss projectiles, the item and the final boss (plan priorities 3-4).</summary>
        public static IEnumerable<TripoAssetSpec> BossProjectilesAndItems()
        {
            // Pythagorean triangle shot: 0.8 m, flies +Z with its face to the target.
            // Geometry only (marble white). Generated at 2,000 from a concept image after
            // four text attempts failed; the plan's 400 is the budget to come back to.
            yield return new TripoAssetSpec("PJ-01", "TriangleShot", TripoCategory.Projectiles,
                TripoSizeAxis.LargestExtent, 0.8f, TripoPivot.Centre,
                triangleBudget: 400, triangleCap: 2000, textureMaxSize: 0,
                TripoColliderShape.None, castShadows: false, smoothness: 0.5f,
                baseColor: new Color(0.93f, 0.91f, 0.86f), generateMeshLods: true);

            // Pythagoras's Bean: a 0.3 m pickup, centred so a bobbing visual spins it in place.
            yield return new TripoAssetSpec("IT-01", "PythagorasBean", TripoCategory.Items,
                TripoSizeAxis.LargestExtent, 0.3f, TripoPivot.Centre,
                triangleBudget: 500, triangleCap: 2000, textureMaxSize: 512,
                TripoColliderShape.None, castShadows: true, smoothness: 0.6f);

            // The Singularity: abstract final boss, animated in code (no rig). Sized by
            // its whole silhouette, ring and shards included: the black core is ~0.6 of
            // it, so 5 m puts the plan's 3 m core inside. The texture drives a soft
            // emission. 2048 like the characters: a hero asset.
            yield return new TripoAssetSpec("BS-01", "Singularity", TripoCategory.Bosses,
                TripoSizeAxis.LargestExtent, 5f, TripoPivot.Centre,
                triangleBudget: 6000, triangleCap: 10000, textureMaxSize: 2048,
                TripoColliderShape.None, castShadows: true, smoothness: 0.7f,
                emissionColor: new Color(0.35f, 0.35f, 0.35f));

            // Nautilus: the head of Fibonacci's Golden Spiral and an arena piece. The
            // plan gives no size; 1 m across, centred, so code can scale it either way.
            yield return new TripoAssetSpec("PJ-02", "GoldenNautilus", TripoCategory.Projectiles,
                TripoSizeAxis.LargestExtent, 1f, TripoPivot.Centre,
                triangleBudget: 2000, triangleCap: 2500, textureMaxSize: 1024,
                TripoColliderShape.None, castShadows: true, smoothness: 0.45f);
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
            // The v3 export (image-to-3D) is a triangular glass bar with brass end frames
            // whose lens already faces +Z, the way the beam fires.
            yield return new TripoAssetSpec("NF-02", "FluxionPrism", TripoCategory.BossAttacks,
                TripoSizeAxis.LargestExtent, 0.5f, TripoPivot.Centre,
                triangleBudget: 2500, triangleCap: 3000, textureMaxSize: 512,
                TripoColliderShape.None, castShadows: true, smoothness: 0.5f);

            // Apple: the barrage fits it to 0.6 m (FitToDiameter, largest extent), the
            // reward to 0.55 m. 20-30 airborne at once: 800 triangles is a hard cap.
            // Mesh LODs: up to 30 in the air, small on screen.
            yield return new TripoAssetSpec("NF-03", "Apple", TripoCategory.Projectiles,
                TripoSizeAxis.LargestExtent, 0.6f, TripoPivot.Centre,
                triangleBudget: 800, triangleCap: 800, textureMaxSize: 512,
                TripoColliderShape.None, castShadows: true, smoothness: 0.55f,
                generateMeshLods: true);

            // Orb: geometry only, tinted gold with a glow. The volley scales the visual
            // (0.6 m at the reference distance), so unit size. 2,000 triangles: below
            // that Tripo tore the ring (ledger). Up to 32 alive, so Mesh LODs drop the
            // far ones to a few hundred triangles.
            yield return new TripoAssetSpec("NF-04", "InverseSquareOrb", TripoCategory.Projectiles,
                TripoSizeAxis.LargestExtent, 1f, TripoPivot.Centre,
                triangleBudget: 2000, triangleCap: 2000, textureMaxSize: 0,
                TripoColliderShape.None, castShadows: false, smoothness: 0.35f,
                baseColor: OrbGold, emissionColor: OrbGold * 0.6f, generateMeshLods: true);

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
