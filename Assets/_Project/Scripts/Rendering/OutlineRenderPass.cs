using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Pickleball.Rendering
{
    /// <summary>
    /// Draws the outline over the opaque image, from the camera's depth and normals. It runs before
    /// transparents so that fire, pulses and other effects draw over the lines and get none themselves.
    ///
    /// It blends black lines straight onto the camera colour instead of reading that colour, which is
    /// why it doesn't need a copy of the colour texture, and why this is a raster pass and not a blit.
    /// </summary>
    public sealed class OutlineRenderPass : ScriptableRenderPass
    {
        static readonly int DepthId = Shader.PropertyToID("_OutlineDepth");
        static readonly int NormalsId = Shader.PropertyToID("_OutlineNormals");
        static readonly int RadiusId = Shader.PropertyToID("_OutlineRadius");
        static readonly int ColorId = Shader.PropertyToID("_OutlineColor");
        static readonly int DepthThresholdsId = Shader.PropertyToID("_OutlineDepthThresholds");
        static readonly int NormalThresholdId = Shader.PropertyToID("_OutlineNormalThreshold");
        static readonly int FadeId = Shader.PropertyToID("_OutlineFade");

        OutlineSettings _settings;
        Material _material;

        /// <summary>Sets up when the pass runs and what the pipeline must render for it.</summary>
        public OutlineRenderPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            // Without these two the depth and normals textures may not exist this frame.
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            // Keeps the colour target the same way up as the depth and normals it is drawn from.
            requiresIntermediateTexture = true;
        }

        /// <summary>Gives the pass what it draws with. Call this before enqueuing it.</summary>
        /// <param name="settings">Read each frame, so tuning the asset takes effect live.</param>
        /// <param name="material">Owned by the caller. The pass only sets properties on it.</param>
        public void Setup(OutlineSettings settings, Material material)
        {
            _settings = settings;
            _material = material;
        }

        /// <inheritdoc />
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_settings == null || _material == null) return;

            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;
            if (!resources.cameraDepthTexture.IsValid() || !resources.cameraNormalsTexture.IsValid()) return;

            var camera = frameData.Get<UniversalCameraData>();
            ApplySettings(_material, _settings);

            using var builder = renderGraph.AddRasterRenderPass<PassData>("Screen Outline", out var data);
            data.Material = _material;
            data.Depth = resources.cameraDepthTexture;
            data.Normals = resources.cameraNormalsTexture;
            data.RadiusPixels = OutlineMath.RadiusPixels(_settings.WidthAt1080p, camera.scaledHeight);

            builder.UseTexture(data.Depth);
            builder.UseTexture(data.Normals);
            builder.SetRenderAttachment(resources.activeColorTexture, 0);
            builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) => Execute(passData, context));
        }

        static void ApplySettings(Material material, OutlineSettings settings)
        {
            Vector2 fade = OutlineMath.FadeRange(settings.FadeStart, settings.FadeEnd);
            material.SetColor(ColorId, settings.Color);
            material.SetVector(DepthThresholdsId, new Vector4(settings.DepthThreshold, settings.DepthThresholdPerMetre));
            material.SetFloat(NormalThresholdId, settings.NormalThreshold);
            material.SetVector(FadeId, new Vector4(fade.x, fade.y));
        }

        static void Execute(PassData data, RasterGraphContext context)
        {
            data.Material.SetTexture(DepthId, data.Depth);
            data.Material.SetTexture(NormalsId, data.Normals);
            data.Material.SetFloat(RadiusId, data.RadiusPixels);
            // One triangle covering the screen; the shader builds it from the vertex index.
            context.cmd.DrawProcedural(Matrix4x4.identity, data.Material, 0, MeshTopology.Triangles, 3);
        }

        sealed class PassData
        {
            public Material Material;
            public TextureHandle Depth;
            public TextureHandle Normals;
            public float RadiusPixels;
        }
    }
}
