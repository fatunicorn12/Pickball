using UnityEngine;

namespace Pickleball.EditorTools.WorldPaint
{
    /// <summary>
    /// The six greybox surfaces and what paints them. Lawn, paving and stone have textures, which
    /// are the look under test. Roof, rock and sand are flat colour with soft patches, only so that
    /// the grid doesn't sit beside the painted surfaces in the test.
    /// </summary>
    public static class WorldPaintCatalog
    {
        /// <summary>The shader every painted material uses.</summary>
        public const string ShaderName = "Pickball/World Painterly";
        /// <summary>Where the greybox materials live.</summary>
        public const string GreyboxDir = "Assets/_Project/Art/World/Greybox";
        /// <summary>Where the painted materials live.</summary>
        public const string PaintedDir = "Assets/_Project/Art/World/Painted";
        /// <summary>Where the textures live. Replacing a PNG here under the same name updates every
        /// material that uses it, with no rebuild.</summary>
        public const string TextureDir = PaintedDir + "/Textures";

        /// <summary>Every surface the texture test replaces.</summary>
        public static readonly WorldPaintSurface[] Surfaces =
        {
            // The low-angle sun runs warm, so these tints pull yellow out of the green and the cream.
            new("GreyboxLawn", "Painted_Lawn", "Lawn", new Color(0.66f, 0.84f, 1f), 8f, 14f,
                new Color(0.86f, 0.92f, 0.9f), new Color(1.02f, 1.05f, 0.98f)),
            new("GreyboxPaving", "Painted_Paving", "Paving", new Color(0.95f, 0.96f, 1f), 4f, 9f,
                new Color(0.93f, 0.9f, 0.86f), new Color(1.03f, 1.02f, 0.98f)),
            new("GreyboxStone", "Painted_Marble", "Marble", new Color(0.94f, 0.96f, 1f), 6f, 7f,
                new Color(0.92f, 0.89f, 0.84f), new Color(1.03f, 1.01f, 0.97f)),
            new("GreyboxRoof", "Painted_Roof", null, new Color(0.72f, 0.42f, 0.32f), 4f, 3f,
                new Color(0.88f, 0.84f, 0.82f), new Color(1.08f, 1.04f, 1f)),
            new("GreyboxRock", "Painted_Rock", null, new Color(0.6f, 0.55f, 0.5f), 4f, 5f,
                new Color(0.84f, 0.84f, 0.88f), new Color(1.06f, 1.03f, 0.98f)),
            new("GreyboxSand", "Painted_Sand", null, new Color(0.86f, 0.78f, 0.62f), 4f, 4f,
                new Color(0.92f, 0.9f, 0.86f), new Color(1.04f, 1.03f, 0.98f)),
        };
    }
}
