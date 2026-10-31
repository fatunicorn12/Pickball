using UnityEngine;

namespace Pickleball.Cutscene.Actors
{
    /// <summary>
    /// Changes a character's face by swapping its base-colour texture for a painted variant: the
    /// eyes (MC's closed for the scene-1 wake-up, Birdy's dropped lids for the scene-6 cheer slip)
    /// and, where painted, the mouth (closed, half, open; set by <see cref="MouthSwap"/> from the
    /// voice). Neither rig has blendshapes, eye, jaw or beak bones, and the face is painted into
    /// the texture atlas, so a texture swap is the only change that doesn't need a new rig.
    /// </summary>
    /// <remarks>
    /// <para><b>One writer for the face.</b> Eyes and mouth live in the same atlas, so each painted
    /// combination is its own full texture (a grid: eye variant × mouth shape) and this component
    /// is the only thing that writes the base map. A second component writing the mouth would
    /// overwrite the eyes. Mouth shape 0 is the model's own (closed) mouth, so the eye-only
    /// variants are row 0 of the grid.</para>
    /// <para>The swap goes through a <see cref="MaterialPropertyBlock"/> on each renderer, never
    /// a material instance, so the shared material and every other user of it are untouched.
    /// The block is read back first and only the texture slot is overwritten, so other
    /// property-block users (the player's hurt flash) keep their values. Going back to the
    /// original writes the original texture into the block.</para>
    /// <para>Timeline animates <see cref="variant"/> (a float, rounded: 0 = original eyes, 1..n =
    /// the eye variants in order), or calls <see cref="Show(int)"/>. The mouth is set with
    /// <see cref="SetMouth"/>. A swap is applied only when the eye or mouth changes, so it costs
    /// nothing per frame. ExecuteAlways so a timeline scrub in the Editor shows the eyes too.</para>
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

        [Tooltip("Eye variant names, in the same order as the textures (\"Closed\", \"Drop\").")]
        [SerializeField] private string[] variantNames = new string[0];

        [Tooltip("Painted copies of the base map, one per eye variant (with the model's own mouth).")]
        [SerializeField] private Texture[] variantTextures = new Texture[0];

        [Tooltip("Mouth shapes painted for this model, counting the model's own closed mouth: " +
                 "1 = no painted mouths (the mouth flap nods the head instead), 3 = closed, half, open.")]
        [Min(1)]
        [SerializeField] private int mouthShapes = 1;

        [Tooltip("The face grid, row-major by eye variant (0 = original eyes, then the variants) " +
                 "and mouth shape (0 = closed). Entries for mouth 0 may be empty: those come from " +
                 "the original texture or the eye variants above.")]
        [SerializeField] private Texture[] faceTextures = new Texture[0];

        [Header("Playback (Timeline may animate this)")]
        [Tooltip("Which eyes show: 0 = the original, 1..n = the variants in order. A float so " +
                 "an animation curve can key it; it is rounded.")]
        [SerializeField] private float variant;

        private MaterialPropertyBlock _block;
        private int _propertyId;
        private int _applied = -1;
        private int _mouth;
        private int _appliedMouth = -1;

        /// <summary>Number of eye variants (not counting the original).</summary>
        public int VariantCount => variantTextures != null ? variantTextures.Length : 0;

        /// <summary>Mouth shapes, counting the closed one (1 = no painted mouths).</summary>
        public int MouthShapes => mouthShapes;

        /// <summary>True when the model has painted mouth shapes to swap.</summary>
        public bool HasMouths => mouthShapes > 1 && faceTextures != null &&
                                 faceTextures.Length >= (VariantCount + 1) * mouthShapes;

        /// <summary>The eye variant on screen: 0 = original, 1..n = variants.</summary>
        public int Shown => _applied < 0 ? 0 : _applied;

        /// <summary>The mouth shape on screen (0 = closed).</summary>
        public int MouthShown => _appliedMouth < 0 ? 0 : _appliedMouth;

        /// <summary>The renderers this swaps on.</summary>
        public Renderer[] Renderers => renderers;

        /// <summary>Name of an eye variant (1-based), or null.</summary>
        public string VariantName(int index)
        {
            int i = index - 1;
            return variantNames != null && i >= 0 && i < variantNames.Length ? variantNames[i] : null;
        }

        /// <summary>Texture of an eye variant (1-based), or null.</summary>
        public Texture VariantTexture(int index)
        {
            int i = index - 1;
            return variantTextures != null && i >= 0 && i < variantTextures.Length ? variantTextures[i] : null;
        }

        /// <summary>
        /// The painted texture for an eye variant and mouth shape, or null where the grid relies
        /// on the original or an eye-only variant (mouth 0).
        /// </summary>
        public Texture FaceTexture(int eye, int mouth)
        {
            int i = eye * mouthShapes + mouth;
            return faceTextures != null && mouth > 0 && i >= 0 && i < faceTextures.Length ? faceTextures[i] : null;
        }

        /// <summary>Setup-menu entry point for eye variants only (no painted mouths).</summary>
        public void Configure(Renderer[] targets, string property, string[] names, Texture[] textures)
        {
            Configure(targets, property, names, textures, 1, null);
        }

        /// <summary>Setup-menu entry point: eye variants plus the eye × mouth grid.</summary>
        public void Configure(Renderer[] targets, string property, string[] names, Texture[] textures,
            int mouths, Texture[] faceGrid)
        {
            renderers = targets ?? new Renderer[0];
            textureProperty = string.IsNullOrEmpty(property) ? "_BaseMap" : property;
            variantNames = names ?? new string[0];
            variantTextures = textures ?? new Texture[0];
            mouthShapes = Mathf.Max(1, mouths);
            faceTextures = faceGrid ?? new Texture[0];
            variant = 0f;
            _applied = -1;
            _appliedMouth = -1;
            _mouth = 0;
        }

        /// <summary>Show an eye variant: 0 = original, 1..n = variants. Out of range shows the original.</summary>
        public void Show(int index)
        {
            variant = index;
            Apply();
        }

        /// <summary>Show an eye variant by name (case-insensitive); unknown names show the original.</summary>
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

        /// <summary>
        /// Show a mouth shape (0 closed, 1 half, 2 open). Ignored (stays closed) on a model with no
        /// painted mouths. Applies only on change.
        /// </summary>
        public void SetMouth(int shape)
        {
            int m = HasMouths ? Mathf.Clamp(shape, 0, mouthShapes - 1) : 0;
            if (m == _mouth && _appliedMouth == m) return;
            _mouth = m;
            Apply();
        }

        private void OnEnable()
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            _propertyId = Shader.PropertyToID(string.IsNullOrEmpty(textureProperty) ? "_BaseMap" : textureProperty);
            _applied = -1;
            _appliedMouth = -1;
            if (Mathf.RoundToInt(variant) != 0 || _mouth != 0) Apply();
        }

        private void OnDisable()
        {
            // Put the original back so a disabled swap never leaves closed eyes or an open mouth.
            variant = 0f;
            _mouth = 0;
            if (_applied > 0 || _appliedMouth > 0) Apply();
            _applied = -1;
            _appliedMouth = -1;
        }

        private void LateUpdate()
        {
            int eye = Mathf.RoundToInt(variant);
            bool pending = _applied < 0 && (eye != 0 || _mouth != 0);
            if (eye != Shown || _mouth != MouthShown || pending) Apply();
        }

        private void Apply()
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            int eye = Mathf.RoundToInt(variant);
            if (eye < 0 || eye > VariantCount) eye = 0;
            int mouth = HasMouths ? Mathf.Clamp(_mouth, 0, mouthShapes - 1) : 0;
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;
                Texture tex = FaceTexture(eye, mouth);
                if (tex == null) tex = eye > 0 ? variantTextures[eye - 1] : Original(r);
                if (tex == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetTexture(_propertyId, tex);
                r.SetPropertyBlock(_block);
            }
            _applied = eye;
            _appliedMouth = mouth;
        }

        private Texture Original(Renderer r)
        {
            Material m = r.sharedMaterial;
            return m != null && m.HasProperty(_propertyId) ? m.GetTexture(_propertyId) : null;
        }
    }
}
