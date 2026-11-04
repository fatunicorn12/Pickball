using UnityEngine;

namespace Pickleball.EditorTools.WorldPaint
{
    /// <summary>
    /// One row of the world-texturing table: which greybox material it replaces, and the first
    /// values of the painted material that replaces it. These only seed a new material; after that
    /// the material asset owns them, so values tuned in the Inspector survive a rebuild.
    /// </summary>
    public readonly struct WorldPaintSurface
    {
        /// <summary>The greybox material this surface replaces, by asset name.</summary>
        public readonly string GreyboxMaterial;
        /// <summary>The painted material's asset name.</summary>
        public readonly string PaintedMaterial;
        /// <summary>The PNG's name under <c>Painted/Textures</c>, or null for flat colour with patches only.</summary>
        public readonly string Texture;
        /// <summary>Multiplied over the texture.</summary>
        public readonly Color Tint;
        /// <summary>Metres per repeat of the texture.</summary>
        public readonly float TileSize;
        /// <summary>Metres across a soft colour patch.</summary>
        public readonly float PatchSize;
        /// <summary>The tint at the dark end of a patch.</summary>
        public readonly Color PatchDark;
        /// <summary>The tint at the light end of a patch.</summary>
        public readonly Color PatchLight;

        /// <summary>Creates a row.</summary>
        public WorldPaintSurface(string greyboxMaterial, string paintedMaterial, string texture, Color tint,
                                 float tileSize, float patchSize, Color patchDark, Color patchLight)
        {
            GreyboxMaterial = greyboxMaterial;
            PaintedMaterial = paintedMaterial;
            Texture = texture;
            Tint = tint;
            TileSize = tileSize;
            PatchSize = patchSize;
            PatchDark = patchDark;
            PatchLight = patchLight;
        }
    }
}
