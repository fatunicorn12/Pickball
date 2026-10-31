namespace Pickleball.Cutscene
{
    /// <summary>
    /// Every marker id the opening sequence uses (Docs/Story/OpeningCutscenes_ShotList.md §2).
    /// A set that replaces one of ours must provide the same ids. Derived points such as
    /// Birdy_Handover or Birdy_Guide are computed from actors, not placed.
    /// </summary>
    public static class CutsceneMarkerIds
    {
        // Outer Shrine (scenes 1-3)
        /// <summary>Centre of the shrine's lawn circle.</summary>
        public const string ShrineCenter = "Shrine_Center";
        /// <summary>Where MC lies in the void, head toward the marker's forward.</summary>
        public const string MCStart = "MC_Start";
        /// <summary>Where Birdy enters the sky in scene 2.</summary>
        public const string BirdySkyEntry = "Birdy_SkyEntry";
        /// <summary>Where the propeller pops off Birdy mid-tumble.</summary>
        public const string PropSeparation = "Prop_Separation";
        /// <summary>Where Birdy bounces on the lawn.</summary>
        public const string BirdyLand = "Birdy_Land";
        /// <summary>The ledge the propeller lands on: the platforming goal.</summary>
        public const string PropellerLedge = "Propeller_Ledge";
        /// <summary>Camera settle point for the player's-eye view of the ledge.</summary>
        public const string LedgeView = "Ledge_View";
        /// <summary>MC's mark at the ledge foot when scene 3 starts.</summary>
        public const string MCHandover = "MC_Handover";
        /// <summary>Gameplay respawn and spawn point for the platforming section.</summary>
        public const string PlaySpawn = "Play_Spawn";
        /// <summary>First of the steps up to the ledge (lowest).</summary>
        public const string LedgeStep1 = "Ledge_Step_1";
        /// <summary>Second step up to the ledge.</summary>
        public const string LedgeStep2 = "Ledge_Step_2";
        /// <summary>Third step up to the ledge (highest).</summary>
        public const string LedgeStep3 = "Ledge_Step_3";

        // Olive path (scene 4)
        /// <summary>Where the path leaves the shrine.</summary>
        public const string PathStart = "Path_Start";
        /// <summary>Start of the stretch scene 4 walks.</summary>
        public const string PathTrackA = "Path_Track_A";
        /// <summary>End of the stretch scene 4 walks.</summary>
        public const string PathTrackB = "Path_Track_B";
        /// <summary>Where the path meets the crest.</summary>
        public const string PathEnd = "Path_End";
        /// <summary>Start of scene 4's side-on camera track (abeam Path_Track_A, to the walkers' east).</summary>
        public const string CamTrackS4Start = "Cam_Track_S4_Start";
        /// <summary>End of scene 4's side-on camera track (abeam Path_Track_B).</summary>
        public const string CamTrackS4End = "Cam_Track_S4_End";

        // South crest and the Academy vista (scenes 5-6)
        /// <summary>MC's stop mark on the crest, facing north.</summary>
        public const string CrestView = "Crest_View";
        /// <summary>Where Birdy zips off to at the end of scene 6.</summary>
        public const string ZipPath = "Zip_Path";
        /// <summary>The gnomon's mid-height, the crane reveal's aim point.</summary>
        public const string GnomonAim = "Gnomon_Aim";
        /// <summary>The fraying shards above the Overlook, the scene-6 gaze target.</summary>
        public const string RimGaze = "Rim_Gaze";
        /// <summary>The black sun's centre.</summary>
        public const string BlackSun = "BlackSun";
    }
}
