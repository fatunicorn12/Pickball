using UnityEngine;
using UnityEngine.Rendering;

namespace Pickleball.Cutscene.Sets
{
    /// <summary>
    /// The scene-1 void as a look over the real Outer Shrine, not a separate place. One float,
    /// <see cref="Blend"/>, takes the whole scene from the white void (0) to golden hour (1):
    /// RenderSettings fog and trilight ambient, the sun's intensity, colour and shadow strength,
    /// and the radial colour flood that dissolves the void dome, the void disc and the white-out
    /// on every set's painterly material (see <see cref="VoidFlood"/>).
    ///
    /// <b>Timeline:</b> animate the serialized <c>blend</c> field with an Animation track bound to
    /// this GameObject (0 through scene 1, 0→1 over S1_SH07's 4.0–6.0 s). It is applied in
    /// LateUpdate when it changes, and immediately when Timeline previews it
    /// (OnDidApplyAnimationProperties). Nothing allocates per frame.
    ///
    /// <b>End state:</b> at blend 1 every value equals the GoldenHour profile, which equals the
    /// greybox's gameplay lighting, and the void renderers are switched off. Disabling this
    /// component switches the flood off (the void vanishes) but leaves the last applied fog,
    /// ambient and sun as they were; set blend to 1 first.
    ///
    /// The sun is the serialized Light, or RenderSettings.sun when none is wired (the greybox sets
    /// it), so the prefab works dropped into any copy of the greybox.
    ///
    /// <b>Pure white:</b> the gameplay post stack tonemaps (Neutral, from the pipeline asset's
    /// volume profile), which maps a white of 1.0 to light grey. So the void writes its white at
    /// <see cref="ResolveConfig.VoidWhite"/> (HDR, above the tonemapper's white level) through
    /// the <c>_CutsceneVoidWhite</c> global, and the optional <c>voidPost</c> volume (bloom off,
    /// white balance neutral) is weighted 1 − blend so that HDR white neither blooms over MC nor
    /// turns warm. At blend 1 the volume is off and gameplay post is untouched.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class CutsceneLook : MonoBehaviour
    {
        [Header("Profiles")]
        [Tooltip("The look at blend 0: the white void.")]
        [SerializeField] private LookProfile voidProfile;

        [Tooltip("The look at blend 1: golden hour, equal to gameplay lighting.")]
        [SerializeField] private LookProfile goldenHourProfile;

        [Header("Blend")]
        [Tooltip("0 = white void, 1 = golden hour. Animate this from Timeline. The colour flood, fog, " +
                 "ambient and sun all follow it.")]
        [Range(0f, 1f)] [SerializeField] private float blend = 1f;

        [Header("Scene")]
        [Tooltip("The sun to dim and restore. Empty uses RenderSettings.sun.")]
        [SerializeField] private Light sun;

        [Tooltip("Where the colour flood starts (MC_Start). Empty uses this transform.")]
        [SerializeField] private Transform floodCentre;

        [Tooltip("Void-only renderers (the dome and the disc). Enabled while blend < 1, off at 1.")]
        [SerializeField] private Renderer[] voidRenderers = new Renderer[0];

        [Header("Flood")]
        [Tooltip("Flood radius, front and rise tuning, shared with the construction lines.")]
        [SerializeField] private ResolveConfig config;

        [Header("Post")]
        [Tooltip("Void-only post volume (bloom off, neutral white balance), weighted 1 − blend and " +
                 "disabled at blend 1 so gameplay post is unchanged. Empty: the void's HDR white blooms.")]
        [SerializeField] private Volume voidPost;

        private float _appliedBlend = float.NaN;

        /// <summary>The current blend, 0 (void) .. 1 (golden hour). Setting it applies on the next
        /// LateUpdate.</summary>
        public float Blend
        {
            get => blend;
            set => blend = Mathf.Clamp01(value);
        }

        /// <summary>Setup-menu entry point.</summary>
        public void Configure(LookProfile voidLook, LookProfile goldenHour, ResolveConfig resolveConfig,
                              Transform centre, Renderer[] renderers)
        {
            voidProfile = voidLook;
            goldenHourProfile = goldenHour;
            config = resolveConfig;
            floodCentre = centre;
            voidRenderers = renderers ?? new Renderer[0];
        }

        /// <summary>Wires the void-only post volume (setup menu). Null removes it.</summary>
        public void ConfigurePost(Volume post)
        {
            voidPost = post;
        }

        private void OnEnable()
        {
            _appliedBlend = float.NaN;
            Apply();
        }

        private void OnDisable()
        {
            _appliedBlend = float.NaN;
            if (CanApply()) Shader.SetGlobalVector(VoidFlood.ParamsId, Vector4.zero);
            if (voidPost != null) voidPost.enabled = false;
        }

        private void LateUpdate()
        {
            if (blend != _appliedBlend) Apply();
        }

        // Timeline preview (and Animation window) writes the field without a LateUpdate in edit mode.
        private void OnDidApplyAnimationProperties()
        {
            Apply();
        }

        // Inspector edits re-apply on the next LateUpdate (renderers can't be toggled inside OnValidate).
        private void OnValidate()
        {
            _appliedBlend = float.NaN;
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        /// <summary>
        /// Writes the look for the current blend into RenderSettings, the sun and the flood
        /// globals. Called automatically; public so a sequencer can force it after a jump.
        /// </summary>
        public void Apply()
        {
            if (!CanApply() || voidProfile == null || goldenHourProfile == null) return;

            LookState look = LookBlend.Evaluate(voidProfile.State, goldenHourProfile.State, blend);
            RenderSettings.fog = true;
            RenderSettings.fogMode = look.FogMode;
            RenderSettings.fogColor = look.FogColor;
            RenderSettings.fogDensity = look.FogDensity;
            RenderSettings.fogStartDistance = look.FogStart;
            RenderSettings.fogEndDistance = look.FogEnd;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = look.AmbientSky;
            RenderSettings.ambientEquatorColor = look.AmbientEquator;
            RenderSettings.ambientGroundColor = look.AmbientGround;

            Light light = sun != null ? sun : RenderSettings.sun;
            if (light != null)
            {
                light.intensity = look.SunIntensity;
                light.color = look.SunColor;
                light.shadowStrength = look.SunShadowStrength;
            }

            bool voidUp = blend < 1f;
            if (config != null)
            {
                Vector3 centre = floodCentre != null ? floodCentre.position : transform.position;
                float radius = VoidFlood.Radius(blend, config.FloodMaxRadius, config.FloodExponent, config.FloodFront);
                Shader.SetGlobalVector(VoidFlood.CentreId, new Vector4(centre.x, centre.y, centre.z, 1f));
                Shader.SetGlobalVector(VoidFlood.ParamsId,
                                       new Vector4(radius, config.FloodFront, voidUp ? 1f : 0f, config.FloodRise));
                Shader.SetGlobalColor(VoidFlood.EdgeColourId, config.FloodFrontColour);
                Shader.SetGlobalFloat(VoidFlood.WhiteId, config.VoidWhite);
                Shader.SetGlobalFloat(VoidFlood.SkyId, VoidFlood.SkyFade(blend));
            }

            if (voidPost != null)
            {
                voidPost.weight = 1f - blend;
                voidPost.enabled = voidUp;
            }

            if (voidRenderers != null)
            {
                for (int i = 0; i < voidRenderers.Length; i++)
                {
                    if (voidRenderers[i] != null) voidRenderers[i].enabled = voidUp;
                }
            }

            _appliedBlend = blend;
        }

        // Prefab assets and prefab-mode previews must not rewrite the open scene's lighting.
        private bool CanApply()
        {
            if (!gameObject.scene.IsValid()) return false;
#if UNITY_EDITOR
            if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(gameObject.scene)) return false;
#endif
            return true;
        }
    }
}
