using UnityEngine;

namespace Pickleball.Sandbox.Cloth
{
    /// <summary>
    /// Tunables for <see cref="SpringCloth"/> (runtime feel) and for the editor
    /// generator that builds the skirt chains (Tools ▸ Pickleball ▸ Setup Cloth).
    ///
    /// Every length is a FRACTION OF CHARACTER HEIGHT, so one asset works at any
    /// import scale — Newton is imported at 2.2 and the chibi at 1.7, and both
    /// skirts were measured against their own height.
    ///
    /// Runtime values can be dialled live in Play mode. Generation values only
    /// take effect when the setup menu is re-run.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Spring Cloth Config", fileName = "SpringClothConfig")]
    public class SpringClothConfig : ScriptableObject
    {
        [Header("Runtime — motion")]
        [Tooltip("Acceleration applied to every free joint (m/s²). Lower than real gravity reads floatier.")]
        public Vector3 gravity = new(0f, -6f, 0f);

        [Tooltip("Pull back toward the animated pose per 60 Hz frame, at the waist end of a chain.")]
        [Range(0f, 1f)] public float stiffnessRoot = 0.22f;

        [Tooltip("Pull back toward the animated pose per 60 Hz frame, at the hem.")]
        [Range(0f, 1f)] public float stiffnessTip = 0.06f;

        [Tooltip("Velocity lost per 60 Hz frame at the waist end. Higher = settles faster, less swing.")]
        [Range(0f, 1f)] public float dampingRoot = 0.25f;

        [Tooltip("Velocity lost per 60 Hz frame at the hem.")]
        [Range(0f, 1f)] public float dampingTip = 0.12f;

        [Tooltip("How much of the character's own travel the cloth lags behind. " +
                 "1 = full world-space inertia (big trailing coat when running), 0 = moves rigidly with the body.")]
        [Range(0f, 1f)] public float inertia = 0.55f;

        [Tooltip("Hard limit on how far a segment may swing away from its animated direction (degrees).")]
        [Range(0f, 180f)] public float maxAngle = 60f;

        [Header("Runtime — collision")]
        [Tooltip("Radius of each cloth joint, as a fraction of character height.")]
        [Range(0f, 0.1f)] public float particleRadius = 0.015f;

        [Tooltip("Multiplier on the leg capsule radii measured at setup. >1 keeps the hem further off the legs.")]
        [Range(0.5f, 2f)] public float legRadiusScale = 1.1f;

        [Header("Runtime — housekeeping")]
        [Tooltip("A single-frame jump larger than this (fraction of height) is treated as a teleport and the cloth snaps to pose.")]
        public float teleportDistance = 1.5f;

        [Tooltip("Keep simulating while no camera sees the character. Off saves CPU on mobile.")]
        public bool simulateWhenInvisible;

        [Header("Generation (re-run Tools ▸ Pickleball ▸ Setup Cloth to apply)")]
        [Tooltip("Top of the skirt region, above the hips pivot (fraction of height). Vertices above it are never touched.")]
        public float regionTop = 0.05f;

        [Tooltip("Vertices at least this far from every leg bone (fraction of height) are certainly cloth; the flood fill starts from them.")]
        public float seedClearance = 0.09f;

        [Tooltip("The flood fill never enters vertices closer than this to a leg bone — the breeches/trousers/stockings.")]
        public float innerClearance = 0.035f;

        [Tooltip("Largest downward step (fraction of height) the flood fill may take. It grows up and across the skirt from the hem, so this stops it climbing the coat and coming back down the breeches.")]
        public float monotoneTolerance = 0.02f;

        [Tooltip("Weight above which a vertex skinned to the head, hands or forearms is excluded outright.")]
        [Range(0f, 1f)] public float maxExcludedBoneWeight = 0.5f;

        [Tooltip("Chains spaced evenly around the hips. Empty sectors (an open coat front) are dropped.")]
        [Range(3, 16)] public int chainCount = 8;

        [Tooltip("Bones per chain between waist and hem.")]
        [Range(1, 6)] public int segmentsPerChain = 3;

        [Tooltip("A sector needs this many cloth vertices to get a chain.")]
        public int minVerticesPerChain = 3;

        [Tooltip("Fraction of the skirt length (from the top) over which skinning blends from the original bones to the chains.")]
        [Range(0.05f, 1f)] public float blendFalloff = 0.4f;

        [Tooltip("Percentile of leg-vertex distances used as the leg capsule radius.")]
        [Range(0.1f, 1f)] public float legRadiusPercentile = 0.7f;

        [Tooltip("Renderer bounds padding so a swinging hem is not culled (fraction of height).")]
        public float boundsPadding = 0.15f;
    }
}
