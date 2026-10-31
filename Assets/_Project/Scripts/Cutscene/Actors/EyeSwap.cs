using UnityEngine;

namespace Pickleball.Cutscene.Actors
{
    /// <summary>
    /// Changes a character's eyes by swapping its base-colour texture for a painted variant
    /// (MC's eyes closed for the scene-1 wake-up, Birdy's dropped lids for the scene-6 cheer
    /// slip). Neither rig has blendshapes or eye bones, and the eyes are painted into the
    /// texture atlas, so a texture swap is the only change that doesn't need a new rig.
    /// </summary>
    /// <remarks>
    /// <para>The swap goes through a <see cref="MaterialPropertyBlock"/> on each renderer, never
    /// a material instance, so the shared material and every other user of it are untouched.
    /// The block is read back first and only the texture slot is overwritten, so other
    /// property-block users (the player's hurt flash) keep their values. Going back to the
    /// original writes the original texture into the block.</para>
    /// <para>Timeline animates <see cref="variant"/> (a float, rounded: 0 = original, 1..n = the
    /// variants in order), or calls <see cref="Show(int)"/>. The swap is applied only when the
    /// rounded value changes, so it costs nothing per frame. ExecuteAlways so a timeline scrub
    /// in the Editor shows the eyes too.</para>
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class EyeSwap : MonoBehaviour
    {
        [Header("Wiring (setup menu)")]
        [Tooltip("Renderers that use the character's base map (every part on Birdy, the one body " +
                 "on MC).")]
        [SerializeField] private Renderer[] renderers = new Renderer[0];

        [Tooltip("Shader property that holds the base-colour texture (_BaseMap on URP Lit).")]
        [SerializeField] private string textureProperty = "_BaseMap";

        [Tooltip("Variant names, in the same order as the textures (\"Closed\", \"Drop\").")]
        [SerializeField] private string[] variantNames = new string[0];

        [Tooltip("Painted copies of the base map, one per variant.")]
        [SerializeField] private Texture[] variantTextures = new Texture[0];

        [Header("Playback (Timeline may animate this)")]
        [Tooltip("Which eyes show: 0 = the original, 1..n = the variants in order. A float so " +
                 "an animation curve can key it; it is rounded.")]
        [SerializeField] private float variant;

        private MaterialPropertyBlock _block;
        private int _propertyId;
        private int _applied = -1;

        /// <summary>Number of variants (not counting the original).</summary>
        public int VariantCount => variantTextures != null ? variantTextures.Length : 0;

        /// <summary>The variant on screen: 0 = original, 1..n = variants.</summary>
        public int Shown => _applied < 0 ? 0 : _applied;

        /// <summary>The renderers this swaps on.</summary>
        public Renderer[] Renderers => renderers;

        /// <summary>Name of a variant (1-based), or null.</summary>
        public string VariantName(int index)
        {
            int i = index - 1;
            return variantNames != null && i >= 0 && i < variantNames.Length ? variantNames[i] : null;
        }

        /// <summary>Texture of a variant (1-based), or null.</summary>
        public Texture VariantTexture(int index)
        {
            int i = index - 1;
            return variantTextures != null && i >= 0 && i < variantTextures.Length ? variantTextures[i] : null;
        }

        /// <summary>Setup-menu entry point.</summary>
        public void Configure(Renderer[] targets, string property, string[] names, Texture[] textures)
        {
            renderers = targets ?? new Renderer[0];
            textureProperty = string.IsNullOrEmpty(property) ? "_BaseMap" : property;
            variantNames = names ?? new string[0];
            variantTextures = textures ?? new Texture[0];
            variant = 0f;
            _applied = -1;
        }

        /// <summary>Show a variant: 0 = original, 1..n = variants. Out of range shows the original.</summary>
        public void Show(int index)
        {
            variant = index;
            Apply();
        }

        /// <summary>Show a variant by name (case-insensitive); unknown names show the original.</summary>
        public void Show(string variantName)
        {
            int found = 0;
            if (variantNames != null && variantName != null)
            {
                for (int i = 0; i < variantNames.Length; i++)
                {
                    if (string.Equals(variantNames[i], variantName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        found = i + 1;
                        break;
                    }
                }
            }
            Show(found);
        }

        private void OnEnable()
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            _propertyId = Shader.PropertyToID(string.IsNullOrEmpty(textureProperty) ? "_BaseMap" : textureProperty);
            _applied = -1;
            if (Mathf.RoundToInt(variant) != 0) Apply();
        }

        private void OnDisable()
        {
            // Put the original back so a disabled swap never leaves closed eyes behind.
            variant = 0f;
            if (_applied > 0) Apply();
            _applied = -1;
        }

        private void LateUpdate()
        {
            if (Mathf.RoundToInt(variant) != Shown || _applied < 0 && Mathf.RoundToInt(variant) != 0) Apply();
        }

        private void Apply()
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            int index = Mathf.RoundToInt(variant);
            if (index < 0 || index > VariantCount) index = 0;
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;
                Texture tex = index > 0 ? variantTextures[index - 1] : Original(r);
                if (tex == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetTexture(_propertyId, tex);
                r.SetPropertyBlock(_block);
            }
            _applied = index;
        }

        private Texture Original(Renderer r)
        {
            Material m = r.sharedMaterial;
            return m != null && m.HasProperty(_propertyId) ? m.GetTexture(_propertyId) : null;
        }
    }
}
