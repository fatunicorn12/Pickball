using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Pickleball.Rendering
{
    /// <summary>
    /// Puts the screen outline in a renderer, so every scene and every model that renderer draws gets
    /// lines with no per-scene or per-material work. Added to both project renderers by
    /// Tools ▸ Pickleball ▸ Setup Outline. Untick it on a renderer asset to turn the outline off at
    /// that quality level.
    /// </summary>
    public sealed class OutlineRendererFeature : ScriptableRendererFeature
    {
        [Tooltip("The look to draw. Edit the asset, live in Play mode if you like, to tune the outline.")]
        [SerializeField] private OutlineSettings _settings;

        OutlineRenderPass _pass;
        Material _material;

        /// <summary>The look this feature draws; null means it draws nothing.</summary>
        public OutlineSettings Settings => _settings;

        /// <inheritdoc />
        public override void Create()
        {
            // Unity calls this again on every change to the feature and after a script reload.
            CoreUtils.Destroy(_material);
            _material = null;
            _pass = null;
            if (_settings == null || _settings.Shader == null) return;

            _material = CoreUtils.CreateEngineMaterial(_settings.Shader);
            _pass = new OutlineRenderPass();
            _pass.Setup(_settings, _material);
        }

        /// <inheritdoc />
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass == null) return;
            // Skips Scene view and asset preview cameras, whose lines would only get in the way.
            if (renderingData.cameraData.cameraType != CameraType.Game) return;
            // An overlay camera draws on top of its base camera, which has already been outlined.
            if (renderingData.cameraData.renderType == CameraRenderType.Overlay) return;
            renderer.EnqueuePass(_pass);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
            _pass = null;
        }
    }
}
