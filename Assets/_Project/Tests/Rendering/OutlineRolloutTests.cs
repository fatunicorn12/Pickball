using NUnit.Framework;
using Pickleball.Rendering;
using UnityEditor;
using UnityEngine.Rendering.Universal;

namespace Pickleball.Tests.Rendering
{
    /// <summary>
    /// Pins the outline's place in the project renderers. A feature that went missing, or a second one
    /// that drew every line twice, would show up only as a change in the look.
    /// </summary>
    public class OutlineRolloutTests
    {
        [TestCase("Assets/Settings/PC_Renderer.asset")]
        [TestCase("Assets/Settings/Mobile_Renderer.asset")]
        public void Renderer_HasExactlyOneWorkingOutline(string path)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            Assert.IsNotNull(renderer, $"{path} is missing.");

            OutlineRendererFeature outline = null;
            int count = 0;
            foreach (var feature in renderer.rendererFeatures)
            {
                Assert.IsNotNull(feature, $"{path} has a broken feature entry.");
                if (feature is OutlineRendererFeature found)
                {
                    outline = found;
                    count++;
                }
            }

            Assert.AreEqual(1, count, "Expected one outline feature; run Tools ▸ Pickleball ▸ Setup Outline.");
            Assert.IsTrue(outline.isActive, "The outline is ticked off on this renderer.");
            Assert.IsNotNull(outline.Settings, "The outline has no settings asset.");
            Assert.IsNotNull(outline.Settings.Shader, "The settings asset has no shader, so nothing would draw.");
        }
    }
}
