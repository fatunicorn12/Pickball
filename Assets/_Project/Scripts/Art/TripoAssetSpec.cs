using System;
using UnityEngine;

namespace Pickleball.Art
{
    /// <summary>
    /// One row of the <see cref="TripoAssetCatalog"/>: everything the import needs
    /// to turn one Tripo export into a game-ready prefab, and everything the
    /// validation tests check it against.
    ///
    /// The paths are derived, never stored, so a row cannot point at a folder the
    /// import would not have written. Rename the asset by changing
    /// <see cref="AssetName"/> and re-running the import; the old folder is left
    /// for you to delete.
    /// </summary>
    [Serializable]
    public sealed class TripoAssetSpec
    {
        [Header("Identity")]
        [Tooltip("The asset-plan id, e.g. NF-03. The import looks for the download under " +
                 "<extracted>/<id>/, and the ledger and docs use the same id.")]
        [SerializeField] private string id = "";

        [Tooltip("PascalCase name used for the folder, FBX, material and prefab " +
                 "(Art/Tripo/<Category>/<Name>/<Name>.fbx, Prefabs/Tripo/<Name>.prefab).")]
        [SerializeField] private string assetName = "";

        [Tooltip("What the asset is for. Also its folder under Art/Tripo.")]
        [SerializeField] private TripoCategory category = TripoCategory.Props;

        [Tooltip("None for static meshes; Humanoid for rigged characters, which then take any " +
                 "humanoid clip in the project.")]
        [SerializeField] private TripoRig rig = TripoRig.None;

        [Tooltip("Which download is imported (the extracted folder's name, e.g. NF-03_Apple_v2). " +
                 "Written by the import's staging step; informational.")]
        [SerializeField] private string source = "";

        [Header("Size and pivot")]
        [Tooltip("Which measurement Target Size refers to: Height, the larger of width and " +
                 "depth, or the largest extent (a roundish object's diameter).")]
        [SerializeField] private TripoSizeAxis sizeAxis = TripoSizeAxis.Height;

        [Tooltip("Metres, at prefab scale 1. The import sets the model's scale factor so the " +
                 "measured size equals this. Code that rescales the object (the well core, the " +
                 "orb) gets a unit size here.")]
        [Min(0.01f)] [SerializeField] private float targetSize = 1f;

        [Tooltip("Fraction of Target Size the validation test accepts either side " +
                 "(0.05 = ±5 %). The import fits exactly; this only absorbs rounding.")]
        [Range(0.001f, 0.5f)] [SerializeField] private float sizeTolerance = 0.05f;

        [Tooltip("Where the prefab's origin sits: bottom-centre on the ground for props, " +
                 "bounds centre for things code moves.")]
        [SerializeField] private TripoPivot pivot = TripoPivot.BottomCentre;

        [Tooltip("Degrees. Turns the model inside the prefab before the pivot is fitted, to " +
                 "fix an export that faces the wrong way (props face +Z, projectiles fly +Z).")]
        [SerializeField] private Vector3 modelRotation = Vector3.zero;

        [Tooltip("On: Unity generates Mesh LODs at import (Unity 6.2+), and each renderer draws a " +
                 "coarser level as the object shrinks on screen. For meshes spawned in numbers " +
                 "(apples, volley orbs). Costs some index memory; no extra asset.")]
        [SerializeField] private bool generateMeshLods;

        [Header("Budget (mobile)")]
        [Tooltip("Triangles the asset plan targets. The import warns above it.")]
        [Min(1)] [SerializeField] private int triangleBudget = 3000;

        [Tooltip("Hard cap in triangles. The import reports an error and the validation test " +
                 "fails above it; regenerate with a lower polycount rather than raise it.")]
        [Min(1)] [SerializeField] private int triangleCap = 5000;

        [Header("Textures and material")]
        [Tooltip("Pixels. Import cap for the base colour (and normal) map: 1024 for props, 512 " +
                 "for small props and projectiles, 2048 for characters. 0 = geometry-only " +
                 "export with no texture.")]
        [Min(0)] [SerializeField] private int textureMaxSize = 1024;

        [Tooltip("Baked into the base map at import, 0-1: 0 greys it out, 1 keeps the painted " +
                 "colours. Lower it to pull an off-colour paint job (beige 'marble') toward neutral.")]
        [Range(0f, 1f)] [SerializeField] private float textureSaturation = 1f;

        [Tooltip("Baked into the base map at import, as a brightness multiplier after the " +
                 "saturation change. 1 = unchanged; ~1.25 lifts a desaturated beige to off-white.")]
        [Range(0.5f, 2f)] [SerializeField] private float textureGain = 1f;

        [Tooltip("Vertex-colour multiplier at the mesh's lowest point, baked at import. Leave " +
                 "both gradient ends white for none. Any other value switches the material to " +
                 "URP Particles/Lit, which multiplies vertex colour in (URP Lit ignores it).")]
        [SerializeField] private Color gradientBottom = Color.white;

        [Tooltip("Vertex-colour multiplier at the mesh's highest point (see Gradient Bottom).")]
        [SerializeField] private Color gradientTop = Color.white;

        [Tooltip("Tint multiplied over the base map. White keeps the painted colours; for a " +
                 "geometry-only asset this is its whole colour.")]
        [SerializeField] private Color baseColor = Color.white;

        [Tooltip("URP Lit smoothness, 0-1. ~0.15 is cloth and wood, ~0.5 a polished apple, " +
                 "~0.85 glass or obsidian. Higher shows a sharper highlight.")]
        [Range(0f, 1f)] [SerializeField] private float smoothness = 0.2f;

        [Tooltip("URP Lit metallic, 0-1. Leave 0 for painted textures: one material covers " +
                 "every part, so a metallic value darkens the wood along with the brass.")]
        [Range(0f, 1f)] [SerializeField] private float metallic;

        [Tooltip("HDR emission colour. Black = no emission. With a base map the map is also the " +
                 "emission map, so painted detail glows rather than a flat colour.")]
        [ColorUsage(false, true)] [SerializeField] private Color emissionColor = Color.black;

        [Header("Prefab")]
        [Tooltip("Box for solid props the player can bump into; None for projectiles and " +
                 "effect meshes, whose code does its own hit tests.")]
        [SerializeField] private TripoColliderShape colliderShape = TripoColliderShape.None;

        [Tooltip("On for solid props. Off for effect cores and glowing projectiles, where a " +
                 "shadow reads as a solid object and costs a shadow pass per instance.")]
        [SerializeField] private bool castShadows = true;

        /// <summary>Creates an empty row; Unity's serializer needs this.</summary>
        public TripoAssetSpec() { }

        /// <summary>Creates a fully specified row. Used by the editor seed for a batch.</summary>
        public TripoAssetSpec(string id, string assetName, TripoCategory category,
                              TripoSizeAxis sizeAxis, float targetSize, TripoPivot pivot,
                              int triangleBudget, int triangleCap, int textureMaxSize,
                              TripoColliderShape colliderShape, bool castShadows,
                              float smoothness, float metallic = 0f,
                              Color? baseColor = null, Color? emissionColor = null,
                              Vector3? modelRotation = null, float sizeTolerance = 0.05f,
                              float textureSaturation = 1f, float textureGain = 1f,
                              TripoRig rig = TripoRig.None, bool generateMeshLods = false,
                              Color? gradientBottom = null, Color? gradientTop = null)
        {
            this.rig = rig;
            this.generateMeshLods = generateMeshLods;
            this.gradientBottom = gradientBottom ?? Color.white;
            this.gradientTop = gradientTop ?? Color.white;
            this.textureSaturation = textureSaturation;
            this.textureGain = textureGain;
            this.id = id;
            this.assetName = assetName;
            this.category = category;
            this.sizeAxis = sizeAxis;
            this.targetSize = targetSize;
            this.pivot = pivot;
            this.triangleBudget = triangleBudget;
            this.triangleCap = triangleCap;
            this.textureMaxSize = textureMaxSize;
            this.colliderShape = colliderShape;
            this.castShadows = castShadows;
            this.smoothness = smoothness;
            this.metallic = metallic;
            this.baseColor = baseColor ?? Color.white;
            this.emissionColor = emissionColor ?? Color.black;
            this.modelRotation = modelRotation ?? Vector3.zero;
            this.sizeTolerance = sizeTolerance;
        }

        /// <summary>Asset-plan id (e.g. NF-03); also the download folder name.</summary>
        public string Id => id;
        /// <summary>PascalCase name of the folder, model, material and prefab.</summary>
        public string AssetName => assetName;
        /// <summary>What the asset is for; also its folder.</summary>
        public TripoCategory Category => category;
        /// <summary>How the skeleton is imported.</summary>
        public TripoRig Rig => rig;
        /// <summary>The extracted download last staged into the project.</summary>
        public string Source => source;
        /// <summary>Which measurement <see cref="TargetSize"/> refers to.</summary>
        public TripoSizeAxis SizeAxis => sizeAxis;
        /// <summary>Metres at prefab scale 1.</summary>
        public float TargetSize => targetSize;
        /// <summary>Accepted fractional deviation from <see cref="TargetSize"/>.</summary>
        public float SizeTolerance => sizeTolerance;
        /// <summary>Where the prefab origin sits.</summary>
        public TripoPivot Pivot => pivot;
        /// <summary>Euler degrees applied to the model inside the prefab.</summary>
        public Vector3 ModelRotation => modelRotation;
        /// <summary>Planned triangle count.</summary>
        public int TriangleBudget => triangleBudget;
        /// <summary>Triangle count that fails validation.</summary>
        public int TriangleCap => triangleCap;
        /// <summary>Texture import cap in pixels; 0 = no texture.</summary>
        public int TextureMaxSize => textureMaxSize;
        /// <summary>Whether this asset ships with a base colour map.</summary>
        public bool HasTexture => textureMaxSize > 0;
        /// <summary>Whether Unity generates Mesh LODs at import.</summary>
        public bool GenerateMeshLods => generateMeshLods;
        /// <summary>Bottom-to-top vertex-colour gradient baked at import.</summary>
        public TripoVertexGradient VertexGradient => new(gradientBottom, gradientTop);
        /// <summary>Colour correction baked into the base map at import.</summary>
        public TripoTextureGrade TextureGrade => new(textureSaturation, textureGain);
        /// <summary>Tint over the base map.</summary>
        public Color BaseColor => baseColor;
        /// <summary>URP Lit smoothness.</summary>
        public float Smoothness => smoothness;
        /// <summary>URP Lit metallic.</summary>
        public float Metallic => metallic;
        /// <summary>HDR emission; black means none.</summary>
        public Color EmissionColor => emissionColor;
        /// <summary>Whether the material emits.</summary>
        public bool Emits => emissionColor.maxColorComponent > 0f;
        /// <summary>Collider placed on the prefab root.</summary>
        public TripoColliderShape ColliderShape => colliderShape;
        /// <summary>Whether the prefab's renderers cast shadows.</summary>
        public bool CastShadows => castShadows;

        /// <summary>Folder holding the model, textures and material.</summary>
        public string Folder => $"{TripoAssetCatalog.ArtRoot}/{category}/{assetName}";
        /// <summary>The imported model.</summary>
        public string ModelPath => $"{Folder}/{assetName}.fbx";
        /// <summary>File name stem of the base colour map (extension follows the download).</summary>
        public string BaseColorStem => $"{assetName}_BaseColor";
        /// <summary>File name stem of the optional normal map.</summary>
        public string NormalStem => $"{assetName}_Normal";
        /// <summary>The asset's one URP Lit material.</summary>
        public string MaterialPath => $"{Folder}/{assetName}.mat";
        /// <summary>The game-ready prefab.</summary>
        public string PrefabPath => $"{TripoAssetCatalog.PrefabRoot}/{assetName}.prefab";

        /// <summary>Records which extracted download was staged. Editor import only.</summary>
        public void SetSource(string value) => source = value ?? "";

        /// <inheritdoc/>
        public override string ToString() => $"{id} {assetName}";
    }
}
