using UnityEngine;
using UnityEngine.Rendering;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// The targeting marker, as two camera-facing sprites drawn in code so it
    /// needs no art and no inspector wiring.
    ///
    /// Hint (PRE-LOCK): a thin open ring of four corner arcs over a faint disc,
    /// breathing and drifting gently. It reads as "this one is selectable"
    /// without shouting, because it is on screen most of the fight.
    ///
    /// Locked: a closed ring pierced by three inward triangles (Zelda's
    /// targeting triangles) with small ticks between them, turning slowly. It
    /// clamps in from wide on lock, glides over from wherever the hint (or the
    /// previous target) was, and flashes white as it lands, so the moment of
    /// locking reads even out of the corner of your eye.
    ///
    /// Every change of state is animated: the hint scales up as it appears and
    /// bursts outward as a lock replaces it; a released lock swells and fades
    /// rather than vanishing. Both sprites are white with a soft dark outline,
    /// so the caller's colour tints them while the outline keeps them legible
    /// over a bright sky.
    ///
    /// Callers (<see cref="LockOnTargeting"/>, <see cref="TetherSystem"/>) only
    /// say WHAT to show each frame; the animation runs here in LateUpdate, so a
    /// single <see cref="Hide"/> call still gets its fade-out. It lives at the
    /// scene root so it never inherits the player's rotation.
    /// </summary>
    [DefaultExecutionOrder(1000)]   // after camera rigs, so the billboard faces this frame's camera
    public class LockOnReticle : MonoBehaviour
    {
        // Normalised sprite geometry (canvas spans -1..1). The ring radii are
        // what world sizes are measured against.
        const float LockRingRadius = 0.58f;
        const float HintRingRadius = 0.62f;
        const int LockTextureSize = 512;
        const int HintTextureSize = 256;

        /// <summary>A point moving further than this in one frame is a new
        /// target, not the old one walking, so it glides instead of tracking.</summary>
        const float JumpDistance = 1.5f;

        [Header("Locked")]
        [Tooltip("The ring sits this far out from the target's radius, so it frames the body rather than cutting through it.")]
        [SerializeField] private float ringPadding = 1.15f;
        [Tooltip("Smallest / largest the locked ring may get, as a fraction of screen height. Keeps a distant target readable and a close one from filling the view.")]
        [SerializeField] private Vector2 lockScreenSize = new(0.08f, 0.5f);
        [SerializeField] private float spinDegreesPerSecond = 50f;
        [Tooltip("Seconds for the clamp-in from wide when a lock starts or switches.")]
        [SerializeField] private float lockInSeconds = 0.24f;
        [Tooltip("How wide the ring starts on a fresh lock, as a multiple of its settled size.")]
        [SerializeField] private float lockInStartScale = 2.4f;
        [Tooltip("Start scale when hopping between targets. Smaller than a fresh lock, so switching reads as a hop rather than a re-acquire.")]
        [SerializeField] private float switchStartScale = 1.6f;
        [Tooltip("Extra turn the ring makes while clamping in, unwinding to the steady spin.")]
        [SerializeField] private float lockInSpinDegrees = 100f;
        [SerializeField, Range(0f, 1f)] private float lockFlash = 0.6f;
        [Tooltip("Seconds a released lock takes to swell and fade out.")]
        [SerializeField] private float lockFadeOutSeconds = 0.18f;
        [SerializeField] private float releaseScale = 1.35f;
        [Tooltip("The ring is pulled this many target-radii toward the camera so the body never swallows it.")]
        [SerializeField] private float pullTowardCamera = 1.15f;

        [Header("Hint")]
        [Tooltip("World size of the hint at mid range, before the screen clamp.")]
        [SerializeField] private float hintWorldSize = 0.8f;
        [Tooltip("Smallest / largest the hint may get, as a fraction of screen height.")]
        [SerializeField] private Vector2 hintScreenSize = new(0.06f, 0.11f);
        [SerializeField] private float hintFadeInSeconds = 0.18f;
        [SerializeField] private float hintFadeOutSeconds = 0.14f;
        [Tooltip("Faster fade when a lock replaces the hint, so the two never compete.")]
        [SerializeField] private float hintToLockSeconds = 0.1f;
        [SerializeField, Range(0f, 0.3f)] private float hintPulse = 0.06f;
        [SerializeField] private float hintPulseHz = 1.4f;
        [Tooltip("Bob height as a fraction of the hint's size.")]
        [SerializeField, Range(0f, 0.3f)] private float hintBob = 0.08f;
        [SerializeField] private float hintBobHz = 0.9f;
        [SerializeField] private float hintSpinDegreesPerSecond = 25f;
        [Tooltip("How quickly the hint catches up after hopping to another candidate.")]
        [SerializeField] private float hintGlideSharpness = 14f;

        [Header("Rendering")]
        [Tooltip("Draw over the world (ZTest Always) where the shader allows it, so a lock held behind a pillar stays visible during its grace period.")]
        [SerializeField] private bool drawOverWorld = true;

        // Serialised (hidden) so they survive a script recompile during Play
        // mode, which is what lets the self-heal guard rebuild with the same
        // material the caller originally handed over.
        [SerializeField, HideInInspector] private Material sourceMaterial;
        [SerializeField, HideInInspector] private bool built;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        // Shared by every reticle instance (lock-on and tether): pure white
        // shapes, so one bake serves any colour.
        static Texture2D s_hintTexture, s_lockTexture;
        static Mesh s_quad;

        // Everything Build creates. Deliberately not readonly: a domain reload
        // wipes these, and the guard must be able to rebuild them.
        MeshRenderer _hintRenderer, _lockRenderer;
        Material _hintMaterial, _lockMaterial;
        Texture2D _hintTexture, _lockTexture;
        Mesh _quad;
        MaterialPropertyBlock _block;

        enum Mode { None, Hint, Locked }

        Mode _mode = Mode.None;
        Transform _cam;
        Camera _camComponent;
        Transform _camComponentFor;
        float _radius = 0.8f;
        bool _suspended;

        // Hint animation.
        Vector3 _hintPoint, _hintGlide, _hintShown;
        Color _hintColour = Color.white;
        float _hintFade, _hintSpin;

        // Locked animation.
        Vector3 _lockPoint, _lockFromOffset, _lockShown;
        Color _lockColour = Color.white;
        float _lockFade, _lockAlphaShown, _lockInAlphaFrom;
        float _lockInT = float.MaxValue, _lockInStartScale = 1f, _spin;
        bool _lockInPending;

        // ------------------------------------------------------------ contract

        /// <summary>
        /// Creates the two sprite layers. <paramref name="material"/> is used as
        /// a template if it can carry a texture; otherwise a transparent URP
        /// unlit material is made here, so AddComponent alone is enough.
        /// Safe to call again: it clears what an earlier Build made.
        /// </summary>
        public void Build(Material material)
        {
            sourceMaterial = material;
            built = true;
            DestroyBuilt();

            _block = new MaterialPropertyBlock();
            _quad = SharedQuad();
            _hintTexture = SharedHintTexture();
            _lockTexture = SharedLockTexture();

            Material template = CanCarryTexture(material) ? material : null;
            _hintMaterial = MakeMaterial(template, _hintTexture, "LockOnHint", 0);
            _lockMaterial = MakeMaterial(template, _lockTexture, "LockOnLocked", 1);
            _hintRenderer = MakeLayer("Hint", _hintMaterial);
            _lockRenderer = MakeLayer("Locked", _lockMaterial);

            ResetAnimation();
        }

        /// <summary>The target's radius in metres; the locked ring frames it.</summary>
        public void SetSize(float radius)
        {
            EnsureBuilt();
            _radius = Mathf.Max(0.05f, radius);
        }

        /// <summary>Replays the clamp-in, e.g. on a fresh lock or a target
        /// switch. Takes effect once <see cref="ShowLocked"/> is being called.</summary>
        public void PlayLockIn()
        {
            EnsureBuilt();
            _lockInPending = true;
        }

        /// <summary>Fades out whatever is showing. Call every frame or once;
        /// the fade runs on its own either way.</summary>
        public void Hide()
        {
            EnsureBuilt();
            _mode = Mode.None;
        }

        /// <summary>LOCKED sprite framing <paramref name="aimPoint"/>.</summary>
        public void ShowLocked(Vector3 aimPoint, Transform cam, Color colour)
        {
            EnsureBuilt();
            if (cam == null) { Hide(); return; }
            _cam = cam;

            // Entering the lock always plays the clamp-in, whether or not the
            // caller asked; a big jump while locked is a target switch that
            // skipped PlayLockIn, and gets the same treatment.
            if (_mode != Mode.Locked) _lockInPending = true;
            else if ((aimPoint - _lockPoint).sqrMagnitude > JumpDistance * JumpDistance)
                _lockInPending = true;

            _lockPoint = aimPoint;
            _lockColour = colour;
            _mode = Mode.Locked;
        }

        /// <summary>PRE-LOCK sprite centred on <paramref name="point"/>.</summary>
        public void ShowHint(Vector3 point, Transform cam, Color colour)
        {
            EnsureBuilt();
            if (cam == null) { Hide(); return; }
            _cam = cam;

            if (_mode == Mode.Locked && _lockFade > 0.05f)
            {
                // Released onto a hint: rise out of the old lock rather than
                // popping in somewhere else.
                _hintGlide = _lockShown - point;
            }
            else if (_hintFade > 0.01f)
            {
                if ((point - _hintPoint).sqrMagnitude > JumpDistance * JumpDistance)
                    _hintGlide = (_hintPoint + _hintGlide) - point;   // hop to a new candidate
            }
            else _hintGlide = Vector3.zero;

            _hintPoint = point;
            _hintColour = colour;
            _mode = Mode.Hint;
        }

        /// <summary>Hides at once with no fade, for teardown.</summary>
        public void HideImmediate()
        {
            EnsureBuilt();
            ResetAnimation();
        }

        // ------------------------------------------------------- self-healing

        /// <summary>
        /// Self-heal after hot reload. A script recompile during Play mode keeps
        /// this component but wipes every field Unity cannot serialise (the
        /// renderers, materials, textures, mesh and property block Build made),
        /// which used to throw on the next Show call every frame. If anything
        /// Build created is missing, throw the old children away and Build
        /// again with the material the caller originally passed.
        ///
        /// Skipped while disabled or being destroyed: a caller's OnDisable
        /// (TetherSystem hides on disable) can run during scene unload after
        /// our children are already gone, and rebuilding then would spawn
        /// objects into a closing scene. Every public call is null-safe on its
        /// own, so skipping costs nothing; OnEnable re-arms the guard,
        /// including the OnEnable Unity sends after a hot reload.
        /// </summary>
        void EnsureBuilt()
        {
            if (_suspended) return;
            if (!IsIntact()) Build(sourceMaterial);
        }

        void OnEnable() => _suspended = false;
        void OnDisable() => _suspended = true;

        bool IsIntact() =>
            _block != null && _quad != null
            && _hintTexture != null && _lockTexture != null
            && _hintMaterial != null && _lockMaterial != null
            && _hintRenderer != null && _lockRenderer != null;

        void DestroyBuilt()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                child.SetActive(false);   // Destroy is deferred; do not draw this frame
                Kill(child);
            }
            if (_hintMaterial != null) Kill(_hintMaterial);
            if (_lockMaterial != null) Kill(_lockMaterial);
            _hintRenderer = _lockRenderer = null;
            _hintMaterial = _lockMaterial = null;
        }

        void OnDestroy()
        {
            _suspended = true;
            // Children go with the GameObject; the materials are ours alone.
            // The textures and mesh are shared across instances and stay.
            if (_hintMaterial != null) Kill(_hintMaterial);
            if (_lockMaterial != null) Kill(_lockMaterial);
        }

        static void Kill(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        void ResetAnimation()
        {
            _mode = Mode.None;
            _hintFade = _lockFade = _lockAlphaShown = 0f;
            _hintGlide = Vector3.zero;
            _lockInT = float.MaxValue;
            _lockInPending = false;
            if (_hintRenderer != null) _hintRenderer.enabled = false;
            if (_lockRenderer != null) _lockRenderer.enabled = false;
        }

        // ----------------------------------------------------------- animation

        // Unscaled: hit-stop, slow-mo or a conversation's stopped time should
        // not drag the UI with it, or a fade would freeze half-visible.
        void LateUpdate() => Tick(Time.unscaledDeltaTime);

        /// <summary>One frame of animation, <paramref name="unscaledDeltaTime"/>
        /// seconds. LateUpdate passes unscaled time; public so EditMode tests can
        /// drive the fades with explicit time.</summary>
        public void Tick(float unscaledDeltaTime)
        {
            if (!built) return;
            EnsureBuilt();
            if (_cam == null) { ResetAnimation(); return; }

            float dt = unscaledDeltaTime;
            if (_lockInPending && _mode == Mode.Locked) StartLockIn();
            TickHint(dt);
            TickLocked(dt);
        }

        /// <summary>Starts the clamp-in from wherever the eye already is: the
        /// previous target's ring on a switch, the hint on a fresh lock.</summary>
        void StartLockIn()
        {
            _lockInPending = false;
            bool switching = _lockRenderer.enabled && _lockAlphaShown > 0.05f;

            Vector3 from = _lockPoint;
            if (switching) from = _lockShown;
            else if (_hintFade > 0.05f) from = _hintShown;
            _lockFromOffset = from - _lockPoint;
            if (_lockFromOffset.sqrMagnitude > 400f) _lockFromOffset = Vector3.zero;   // too far to read as a glide

            _lockInStartScale = switching ? switchStartScale : lockInStartScale;
            _lockInAlphaFrom = switching ? _lockAlphaShown : 0f;
            _lockInT = 0f;
            _lockFade = 1f;
        }

        void TickHint(float dt)
        {
            bool wanted = _mode == Mode.Hint;
            float seconds = wanted ? hintFadeInSeconds
                          : _mode == Mode.Locked ? hintToLockSeconds : hintFadeOutSeconds;
            _hintFade = Mathf.MoveTowards(_hintFade, wanted ? 1f : 0f, dt / Mathf.Max(0.01f, seconds));
            if (_hintFade <= 0f)
            {
                _hintRenderer.enabled = false;
                _hintGlide = Vector3.zero;
                return;
            }

            // Glide offsets decay rather than the position being smoothed, so a
            // moving enemy is tracked exactly and only hops are eased.
            _hintGlide *= Mathf.Exp(-hintGlideSharpness * dt);
            _hintSpin = (_hintSpin + hintSpinDegreesPerSecond * dt) % 360f;
            Vector3 centre = _hintPoint + _hintGlide;
            _hintShown = centre;

            float dist = Vector3.Distance(_cam.position, centre);
            if (dist < 0.05f) { _hintRenderer.enabled = false; return; }
            float view = ViewHeightAt(dist);
            float size = Mathf.Clamp(hintWorldSize / view, hintScreenSize.x, hintScreenSize.y) * view;

            float t = Time.unscaledTime;
            float breath = Mathf.Sin(t * Mathf.PI * 2f * hintPulseHz);
            float appear = wanted
                ? Mathf.LerpUnclamped(0.5f, 1f, EaseOutBack(_hintFade, 1.7f))
                // Bursting outward hands over to the lock clamping in; a plain
                // disappearance shrinks away instead.
                : Mathf.Lerp(_mode == Mode.Locked ? 1.5f : 0.7f, 1f, _hintFade);
            float scale = size * appear * (1f + hintPulse * breath);
            Vector3 bob = _cam.up * (Mathf.Sin(t * Mathf.PI * 2f * hintBobHz) * hintBob * size);

            Color c = _hintColour;
            c.a *= SmoothStep(_hintFade) * (0.88f + 0.12f * breath);
            Draw(_hintRenderer, centre + bob, _cam.rotation * Quaternion.Euler(0f, 0f, _hintSpin), scale, c);
        }

        void TickLocked(float dt)
        {
            bool wanted = _mode == Mode.Locked;
            if (!wanted) _lockFade = Mathf.MoveTowards(_lockFade, 0f, dt / Mathf.Max(0.01f, lockFadeOutSeconds));
            if (_lockFade <= 0f)
            {
                _lockRenderer.enabled = false;
                _lockAlphaShown = 0f;
                return;
            }

            if (_lockInT < float.MaxValue) _lockInT += dt;
            float k = Mathf.Clamp01(_lockInT / Mathf.Max(0.01f, lockInSeconds));
            float glide = EaseOutCubic(k);
            _spin = (_spin + spinDegreesPerSecond * dt) % 360f;

            Vector3 anchor = _lockPoint + _lockFromOffset * (1f - glide);
            _lockShown = anchor;

            Vector3 toCam = _cam.position - anchor;
            float dist = toCam.magnitude;
            if (dist < 0.05f) { _lockRenderer.enabled = false; return; }

            // Size is chosen at the target's depth (so the ring frames the
            // body), clamped on screen, then re-expressed at the pulled-forward
            // depth so the pull does not make it look bigger.
            float pull = Mathf.Min(_radius * pullTowardCamera, dist * 0.5f);
            Vector3 position = anchor + toCam / dist * pull;
            float worldSize = 2f * _radius * ringPadding / LockRingRadius;
            float fraction = Mathf.Clamp(worldSize / ViewHeightAt(dist), lockScreenSize.x, lockScreenSize.y);
            float size = fraction * ViewHeightAt(dist - pull);

            // Clamp in from wide with a slight overshoot, so it bites onto the
            // target; a release swells outward as it fades.
            float scale = Mathf.LerpUnclamped(_lockInStartScale, 1f, EaseOutBack(k, 1.2f));
            if (!wanted) scale *= Mathf.Lerp(releaseScale, 1f, _lockFade);

            float fadeIn = Mathf.Clamp01(_lockInT / Mathf.Max(0.01f, lockInSeconds * 0.35f));
            _lockAlphaShown = Mathf.Lerp(_lockInAlphaFrom, 1f, fadeIn) * SmoothStep(_lockFade);

            // White flash peaking just as it lands.
            float flash = lockFlash * Mathf.Clamp01(1f - Mathf.Abs(_lockInT - lockInSeconds * 0.8f) / 0.12f);
            Color c = Color.Lerp(_lockColour, Color.white, flash);
            c.a = _lockColour.a * _lockAlphaShown;

            float turn = _spin + (1f - glide) * lockInSpinDegrees;
            Draw(_lockRenderer, position, _cam.rotation * Quaternion.Euler(0f, 0f, turn), size * scale, c);
        }

        void Draw(MeshRenderer r, Vector3 position, Quaternion rotation, float size, Color colour)
        {
            var t = r.transform;
            t.SetPositionAndRotation(position, rotation);
            t.localScale = new Vector3(size, size, size);
            r.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, colour);
            _block.SetColor(ColorId, colour);
            r.SetPropertyBlock(_block);
            r.enabled = true;
        }

        /// <summary>World height the camera sees at this distance, so sizes
        /// can be set as a fraction of the screen.</summary>
        float ViewHeightAt(float distance)
        {
            if (_camComponentFor != _cam)
            {
                _camComponentFor = _cam;
                _camComponent = _cam.GetComponent<Camera>();
            }
            if (_camComponent != null && _camComponent.orthographic) return 2f * _camComponent.orthographicSize;
            float fov = _camComponent != null ? _camComponent.fieldOfView : 60f;
            return 2f * Mathf.Max(0.01f, distance) * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        }

        static float EaseOutCubic(float k) { float u = 1f - k; return 1f - u * u * u; }

        static float EaseOutBack(float k, float overshoot)
        {
            float u = k - 1f;
            return 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
        }

        static float SmoothStep(float k) => k * k * (3f - 2f * k);

        // ------------------------------------------------------------ building

        MeshRenderer MakeLayer(string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            r.allowOcclusionWhenDynamic = false;
            r.enabled = false;
            return r;
        }

        static bool CanCarryTexture(Material m) =>
            m != null && m.shader != null && (m.HasProperty(BaseMapId) || m.HasProperty(MainTexId));

        /// <summary>
        /// A straight alpha-blended copy of the template (or a fresh URP unlit).
        /// Forced to alpha blend even if the template is additive, because the
        /// sprites' dark outline would vanish under additive blending. The URP
        /// properties are set by hand because the material inspector, which
        /// normally does it, never runs on a runtime material.
        /// </summary>
        Material MakeMaterial(Material template, Texture2D texture, string name, int queueOffset)
        {
            Material m;
            if (template != null) m = new Material(template);
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                             ?? Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    Debug.LogWarning("LockOnReticle: no unlit shader found; the reticle will render magenta.");
                    shader = Shader.Find("Hidden/InternalErrorShader");
                }
                m = new Material(shader);
            }
            m.name = name;

            if (m.HasProperty(BaseMapId)) m.SetTexture(BaseMapId, texture);
            if (m.HasProperty(MainTexId)) m.SetTexture(MainTexId, texture);
            m.SetColor(BaseColorId, Color.white);
            m.SetColor(ColorId, Color.white);

            m.SetFloat("_Surface", 1f);                                   // transparent
            m.SetFloat("_Blend", 0f);                                     // alpha
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetFloat("_AlphaClip", 0f);
            // Only honoured by shaders that expose _ZTest; the pull toward the
            // camera keeps the ring out of the body on those that do not.
            m.SetFloat("_ZTest", (float)(drawOverWorld ? CompareFunction.Always : CompareFunction.LessEqual));
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHAMODULATE_ON");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            // After the world's own transparents (VFX, beams), locked over hint.
            m.renderQueue = (int)RenderQueue.Transparent + 100 + queueOffset;
            return m;
        }

        static Mesh SharedQuad()
        {
            if (s_quad != null) return s_quad;
            // Faces -Z, i.e. toward a camera whose rotation it copies.
            s_quad = new Mesh { name = "LockOnReticleQuad" };
            s_quad.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            });
            s_quad.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            // Particle shaders multiply by vertex colour; without it some
            // platforms read black.
            s_quad.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
            s_quad.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            s_quad.SetTriangles(new[] { 0, 3, 2, 0, 2, 1 }, 0);
            s_quad.RecalculateBounds();
            return s_quad;
        }

        // ------------------------------------------------------------- sprites

        delegate float Shape(Vector2 p);

        static Texture2D SharedLockTexture()
        {
            if (s_lockTexture == null)
                s_lockTexture = Bake("LockOnLocked", LockTextureSize, LockedShape, 0f, 0f, 0.07f, 0.5f);
            return s_lockTexture;
        }

        static Texture2D SharedHintTexture()
        {
            if (s_hintTexture == null)
                s_hintTexture = Bake("LockOnHint", HintTextureSize, HintShape, HintRingRadius - 0.04f, 0.1f, 0.08f, 0.4f);
            return s_hintTexture;
        }

        /// <summary>
        /// Locked: a closed ring, three rounded triangles pointing in through
        /// it (tips inside the ring, so they pinch the target), and a short
        /// tick between each pair. Three-fold so the spin is visible.
        /// </summary>
        static float LockedShape(Vector2 p)
        {
            float d = Mathf.Abs(p.magnitude - LockRingRadius) - 0.032f;
            for (int i = 0; i < 3; i++)
            {
                float a = (90f + 120f * i) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var side = new Vector2(-dir.y, dir.x);
                d = Mathf.Min(d, SdTriangle(p, dir * 0.45f, dir * 0.84f + side * 0.16f, dir * 0.84f - side * 0.16f) - 0.018f);

                float b = a + 60f * Mathf.Deg2Rad;
                var tick = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                d = Mathf.Min(d, SdSegment(p, tick * 0.67f, tick * 0.76f) - 0.016f);
            }
            return d;
        }

        /// <summary>
        /// Hint: four thin arcs on the diagonals with open gaps between, like
        /// soft bracket corners. Open where the lock is closed, thin where it
        /// is bold, so the two never read as the same state.
        /// </summary>
        static float HintShape(Vector2 p)
        {
            float d = float.MaxValue;
            for (int i = 0; i < 4; i++)
                d = Mathf.Min(d, SdArc(p, 45f + 90f * i, 30f, HintRingRadius) - 0.028f);
            return d;
        }

        /// <summary>
        /// Rasterises a signed-distance shape into a white sprite with a 1px
        /// antialiased edge, an optional faint inner disc, and a soft dark
        /// outline underneath. The outline is baked black so tinting leaves it
        /// dark, which is what keeps a yellow marker readable on a pale sky.
        /// </summary>
        static Texture2D Bake(string name, int size, Shape shape, float fillRadius, float fillAlpha,
                              float shadowWidth, float shadowAlpha)
        {
            var pixels = new Color32[size * size];
            float px = 2f / size;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
                float d = shape(p);

                float core = Mathf.Clamp01(0.5f - d / px);
                float fill = fillAlpha > 0f
                    ? fillAlpha * Mathf.Clamp01(0.5f - (p.magnitude - fillRadius) / (px * 4f))
                    : 0f;
                float white = core + fill * (1f - core);
                float shadow = shadowAlpha * (1f - SmoothStep(Mathf.Clamp01(d / shadowWidth)));
                float a = white + shadow * (1f - white);

                // Straight alpha: colour is the white share of what is visible.
                // Fully clear pixels stay black so mips fade into the outline,
                // not into a pale fringe.
                byte rgb = a > 0.0001f ? (byte)Mathf.RoundToInt(255f * white / a) : (byte)0;
                pixels[y * size + x] = new Color32(rgb, rgb, rgb, (byte)Mathf.RoundToInt(255f * a));
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
            tex.SetPixels32(pixels);
            tex.Apply(true, true);   // mips for distance, then free the CPU copy
            return tex;
        }

        static float SdSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude;
        }

        /// <summary>Distance to a circular arc with round ends (no width).</summary>
        static float SdArc(Vector2 p, float centreDegrees, float halfSpanDegrees, float radius)
        {
            float theta = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
            float delta = Mathf.DeltaAngle(centreDegrees, theta);
            if (Mathf.Abs(delta) <= halfSpanDegrees) return Mathf.Abs(p.magnitude - radius);
            float end = (centreDegrees + Mathf.Sign(delta) * halfSpanDegrees) * Mathf.Deg2Rad;
            return (p - new Vector2(Mathf.Cos(end), Mathf.Sin(end)) * radius).magnitude;
        }

        /// <summary>Signed distance to a triangle (negative inside).</summary>
        static float SdTriangle(Vector2 p, Vector2 p0, Vector2 p1, Vector2 p2)
        {
            Vector2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
            Vector2 v0 = p - p0, v1 = p - p1, v2 = p - p2;
            Vector2 q0 = v0 - e0 * Mathf.Clamp01(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0));
            Vector2 q1 = v1 - e1 * Mathf.Clamp01(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1));
            Vector2 q2 = v2 - e2 * Mathf.Clamp01(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2));
            float s = Mathf.Sign(e0.x * e2.y - e0.y * e2.x);
            Vector2 d = Vector2.Min(Vector2.Min(
                    new Vector2(Vector2.Dot(q0, q0), s * (v0.x * e0.y - v0.y * e0.x)),
                    new Vector2(Vector2.Dot(q1, q1), s * (v1.x * e1.y - v1.y * e1.x))),
                    new Vector2(Vector2.Dot(q2, q2), s * (v2.x * e2.y - v2.y * e2.x)));
            return -Mathf.Sqrt(d.x) * Mathf.Sign(d.y);
        }
    }
}
