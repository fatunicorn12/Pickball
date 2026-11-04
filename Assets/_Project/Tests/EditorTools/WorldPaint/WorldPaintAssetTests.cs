using NUnit.Framework;
using Pickleball.EditorTools.WorldPaint;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.EditorTools.WorldPaint
{
    /// <summary>Read-only checks on the shipped assets: the catalog must match what's on disk, and a
    /// texture must be set up to tile. An AI texture dropped in by hand is the likely way to break
    /// these.</summary>
    [Category("Assets")]
    public class WorldPaintAssetTests
    {
        [Test]
        public void Shader_ExistsAndCompiles()
        {
            var shader = Shader.Find(WorldPaintCatalog.ShaderName);
            Assert.That(shader, Is.Not.Null);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
        }

        [Test]
        public void Catalog_NamesEveryGreyboxMaterialThatExists()
        {
            foreach (var surface in WorldPaintCatalog.Surfaces)
            {
                string path = $"{WorldPaintCatalog.GreyboxDir}/{surface.GreyboxMaterial}.mat";
                Assert.That(AssetDatabase.LoadAssetAtPath<Material>(path), Is.Not.Null, path);
            }
        }

        [Test]
        public void Catalog_TileAndPatchSizesArePositive()
        {
            foreach (var surface in WorldPaintCatalog.Surfaces)
            {
                Assert.That(surface.TileSize, Is.GreaterThan(0f), surface.PaintedMaterial);
                Assert.That(surface.PatchSize, Is.GreaterThan(0f), surface.PaintedMaterial);
            }
        }

        [Test]
        public void Textures_AreSetUpToTile()
        {
            foreach (var surface in WorldPaintCatalog.Surfaces)
            {
                if (surface.Texture == null) continue;
                string path = $"{WorldPaintCatalog.TextureDir}/{surface.Texture}.png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Repeat), path);
                Assert.That(importer.mipmapEnabled, Is.True, path);
                Assert.That(importer.sRGBTexture, Is.True, path);
                Assert.That(importer.anisoLevel, Is.GreaterThan(1), $"{path} smears at low camera angles");
            }
        }
    }
}
