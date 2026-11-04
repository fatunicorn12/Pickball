namespace Pickleball.Cutscene.Editor.Actors
{
    /// <summary>
    /// The painted face textures per model, keyed by the base-colour texture the model's material
    /// uses. A model whose texture isn't listed gets no EyeSwap, no MouthSwap, and the head-nod
    /// mouth flap (the swap-a-model fallback). Painted by Tools/Blender/cutscene_paint_eyes.py.
    /// </summary>
    public static class FacePaintSets
    {
        /// <summary>One model's painted faces.</summary>
        public sealed class Set
        {
            /// <summary>The original base-colour texture the material uses (the key).</summary>
            public string Original;

            /// <summary>The eye variant's name ("Closed", "Drop").</summary>
            public string EyeName;

            /// <summary>Original eyes, model's own closed mouth replaced: [half, open].</summary>
            public string[] Mouths;

            /// <summary>The eye variant with the model's own mouth.</summary>
            public string Eyes;

            /// <summary>The eye variant with [half, open] mouths.</summary>
            public string[] EyesMouths;

            /// <summary>Every texture path of the set (for importer settings and the prefab test).</summary>
            public string[] All => new[] { Eyes, Mouths[0], Mouths[1], EyesMouths[0], EyesMouths[1] };
        }

        private const string Chibi = CutsceneActorPaths.ArtDir + "/Chibi/";
        private const string Birdy = CutsceneActorPaths.ArtDir + "/Birdy/";

        /// <summary>MC: closed eyes; mouth half and open (an oval opening, tongue when open).</summary>
        public static readonly Set Mc = new Set
        {
            Original = CutsceneActorPaths.ChibiBaseColor,
            EyeName = "Closed",
            Eyes = CutsceneActorPaths.ChibiEyesClosed,
            Mouths = new[] { Chibi + "Chibi_Mouth_Half.jpg", Chibi + "Chibi_Mouth_Open.jpg" },
            EyesMouths = new[] { Chibi + "Chibi_EyesClosed_Mouth_Half.jpg", Chibi + "Chibi_EyesClosed_Mouth_Open.jpg" },
        };

        /// <summary>Birdy: dropped lids; beak half and open (a dark-red gape opening along the beak line).</summary>
        public static readonly Set BirdySet = new Set
        {
            Original = CutsceneActorPaths.BirdyBaseColor,
            EyeName = "Drop",
            Eyes = CutsceneActorPaths.BirdyEyesDrop,
            Mouths = new[] { Birdy + "Birdy_Beak_Half.jpg", Birdy + "Birdy_Beak_Open.jpg" },
            EyesMouths = new[] { Birdy + "Birdy_EyesDrop_Beak_Half.jpg", Birdy + "Birdy_EyesDrop_Beak_Open.jpg" },
        };

        /// <summary>Every set.</summary>
        public static readonly Set[] All = { Mc, BirdySet };

        /// <summary>Mouth shapes per set, counting the model's own closed mouth.</summary>
        public const int MouthShapes = 3;
    }
}
