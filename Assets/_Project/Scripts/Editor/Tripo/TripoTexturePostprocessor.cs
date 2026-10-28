using Pickleball.Art;
using UnityEditor;
using UnityEngine;

namespace Pickleball.EditorTools.Tripo
{
    /// <summary>
    /// Bakes a catalog row's <see cref="TripoTextureGrade"/> into its texture at
    /// import time. <see cref="TripoAssetImporter"/> writes the grade into the
    /// texture importer's userData, so changing it reimports the texture and the
    /// source JPG in the repo stays exactly as Tripo exported it.
    ///
    /// Applies only under <see cref="TripoAssetCatalog.ArtRoot"/> and only to
    /// textures carrying a grade; every other texture passes through untouched.
    /// Deterministic. Bump <see cref="GetVersion"/> when the grade maths changes.
    /// </summary>
    public sealed class TripoTexturePostprocessor : AssetPostprocessor
    {
        /// <inheritdoc/>
        public override uint GetVersion() => 1;

        void OnPostprocessTexture(Texture2D texture)
        {
            if (!assetPath.StartsWith(TripoAssetCatalog.ArtRoot + "/")) return;
            if (!TripoTextureGrade.TryDecode(assetImporter.userData, out var grade)) return;

            // Every mip level, so distant mips match the graded top level.
            for (int mip = 0; mip < texture.mipmapCount; mip++)
            {
                var pixels = texture.GetPixels(mip);
                for (int i = 0; i < pixels.Length; i++) pixels[i] = grade.Apply(pixels[i]);
                texture.SetPixels(pixels, mip);
            }
        }
    }
}
