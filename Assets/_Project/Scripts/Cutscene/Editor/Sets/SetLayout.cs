using UnityEngine;

namespace Pickleball.Cutscene.Editor.Sets
{
    /// <summary>
    /// Where everything in the cutscene sets goes, in world metres (X east, Z north, Y up, the
    /// Academy lawn at y = 0). The source of truth for the set builder and its tests; the
    /// changes from the shot list (§2) are explained in Docs/Art/CutsceneSets.md.
    ///
    /// <b>Why the shrine moved:</b> the greybox's Delta Pylons (AcademyGreyboxSetup.BuildDelta)
    /// sit 8 m below the shot list's meadow, so the south island is fitted between them: the
    /// shrine lobe lies in the 16 m gap between islets 2 and 3, and every walkable edge near an
    /// islet carries an invisible fence, so nobody drops onto the Network Step gate's islets.
    /// Everything is built at z ≤ -50.45, clear of the plateau and its south parapet.
    /// </summary>
    public static class SetLayout
    {
        // ── Asset paths ──────────────────────────────────────────────────────
        /// <summary>Set prefabs.</summary>
        public const string PrefabDir = "Assets/_Project/Prefabs/Cutscene/Sets";
        /// <summary>Materials, meshes and textures.</summary>
        public const string ArtDir = "Assets/_Project/Art/Cutscene/Sets";
        /// <summary>Generated meshes.</summary>
        public const string MeshDir = ArtDir + "/Meshes";
        /// <summary>Materials.</summary>
        public const string MaterialDir = ArtDir + "/Materials";
        /// <summary>Generated textures.</summary>
        public const string TextureDir = ArtDir + "/Textures";
        /// <summary>Look profiles and the resolve config.</summary>
        public const string ConfigDir = "Assets/_Project/ScriptableObjects/Cutscene/Sets";

        /// <summary>Set ids, also the prefab file names.</summary>
        public const string VoidId = "Void", OuterShrineId = "OuterShrine", OlivePathId = "OlivePath",
                            SouthCrestId = "SouthCrest", AcademyVistaId = "AcademyVista";

        /// <summary>The prefab path for a set id.</summary>
        public static string PrefabPath(string setId) => $"{PrefabDir}/{setId}.prefab";

        /// <summary>Every set id, in build order.</summary>
        public static string[] SetIds() => new[] { VoidId, OuterShrineId, OlivePathId, SouthCrestId, AcademyVistaId };

        /// <summary>The marker ids a set must list (CutsceneMarkerIds). Each id belongs to exactly one set.</summary>
        public static string[] RequiredMarkers(string setId)
        {
            switch (setId)
            {
                case OuterShrineId:
                    return new[]
                    {
                        CutsceneMarkerIds.ShrineCenter, CutsceneMarkerIds.MCStart, CutsceneMarkerIds.BirdySkyEntry,
                        CutsceneMarkerIds.PropSeparation, CutsceneMarkerIds.BirdyLand, CutsceneMarkerIds.PropellerLedge,
                        CutsceneMarkerIds.LedgeView, CutsceneMarkerIds.MCHandover, CutsceneMarkerIds.PlaySpawn,
                        CutsceneMarkerIds.LedgeStep1, CutsceneMarkerIds.LedgeStep2, CutsceneMarkerIds.LedgeStep3,
                    };
                case OlivePathId:
                    return new[]
                    {
                        CutsceneMarkerIds.PathStart, CutsceneMarkerIds.PathTrackA, CutsceneMarkerIds.PathTrackB,
                        CutsceneMarkerIds.CamTrackS4Start, CutsceneMarkerIds.CamTrackS4End, CutsceneMarkerIds.PathEnd,
                    };
                case SouthCrestId:
                    return new[] { CutsceneMarkerIds.CrestView, CutsceneMarkerIds.ZipPath };
                case AcademyVistaId:
                    return new[] { CutsceneMarkerIds.GnomonAim, CutsceneMarkerIds.RimGaze, CutsceneMarkerIds.BlackSun };
                default:
                    return new string[0];
            }
        }

        // ── Characters and cameras ───────────────────────────────────────────
        /// <summary>MC's nominal height (shot list §1), metres.</summary>
        public const float H = 1.6f;

        // ── Outer Shrine (scenes 1-3) ────────────────────────────────────────
        /// <summary>Centre of the lawn circle; MC lies here. Shot list (-4, 0, -100), moved (+4, +4)
        /// into the gap between Delta islets 2 and 3.</summary>
        public static readonly Vector3 ShrineCentre = new(0f, 0f, -96f);
        /// <summary>Radius of the saturated lawn circle, metres.</summary>
        public const float LawnRadius = 5.8f;
        /// <summary>Outer radius of the marble kerb round the lawn, metres.</summary>
        public const float KerbOuterRadius = 6.15f;
        /// <summary>Kerb height, metres (under the controller's 0.3 m step offset).</summary>
        public const float KerbHeight = 0.14f;
        /// <summary>Radius of the column ring, metres.</summary>
        public const float ColumnRingRadius = 6.6f;

        /// <summary>The ring's columns: bearing from the shrine centre (degrees east of north) and
        /// height (0 = intact). The gaps keep MC_Start's views open to the ledge (-37°), the path
        /// (0-7°) and Birdy_Land (30°).</summary>
        public static readonly (float bearing, float brokenHeight)[] Columns =
        {
            (-150f, 1.6f), (-112f, 0f), (-78f, 2.4f), (-22f, 1.1f), (48f, 2.0f),
            (82f, 0f), (116f, 1.4f), (150f, 2.6f), (180f, 0f),
        };

        /// <summary>Intact column height (plinth to abacus top), metres: squat, chibi-scaled.</summary>
        public const float ColumnHeight = 3.3f;
        /// <summary>Column shaft radius, metres.</summary>
        public const float ColumnRadius = 0.45f;

        /// <summary>Where Birdy enters the sky: north-west of the shrine, so S2_SH01's tilt up to him looks away from the black sun.</summary>
        public static readonly Vector3 BirdySkyEntry = new(-7f, 14f, -82f);
        /// <summary>Where the propeller pops off, on his way down from the north-west.</summary>
        public static readonly Vector3 PropSeparation = new(-3f, 9f, -88f);
        /// <summary>Where Birdy bounces: 5 m from MC_Start at a bearing of 30° (the shot list's
        /// "≈ 5 m NNE"; its coordinates were 6.4 m away, in the column ring).</summary>
        public static readonly Vector3 BirdyLand = new(2.5f, 0f, -91.67f);

        /// <summary>The broken propylon whose top is the ledge: footprint centre (x, z).</summary>
        public static readonly Vector2 PierCentre = new(-9f, -84f);
        /// <summary>The pier's footprint, metres (x by z).</summary>
        public static readonly Vector2 PierSize = new(3.5f, 3f);
        /// <summary>The ledge top, metres above the meadow.</summary>
        public const float LedgeTop = 5f;
        /// <summary>The second, broken pier of the propylon: centre (x, z), footprint, height.</summary>
        public static readonly Vector2 SecondPierCentre = new(-15f, -84f);
        /// <summary>Second pier footprint, metres.</summary>
        public static readonly Vector2 SecondPierSize = new(3f, 3f);
        /// <summary>Second pier height, metres.</summary>
        public const float SecondPierHeight = 2.2f;

        /// <summary>The three steps up to the ledge along the pier's south face: top-centre of
        /// each, lowest first. Equal 1.25 m rises (ground, 1.25, 2.5, 3.75, ledge 5), each a
        /// single jump like the greybox's crates.</summary>
        public static readonly Vector3[] LedgeSteps =
        {
            new(-13.75f, 1.25f, -86.5f), new(-11.75f, 2.5f, -86.5f), new(-9.75f, 3.75f, -86.5f),
        };
        /// <summary>Each step's footprint, metres.</summary>
        public static readonly Vector2 StepSize = new(2f, 2f);

        /// <summary>
        /// The ledge's pickup point: on the top, 0.6 m in from the south face and 0.75 m from the
        /// east face, the corner nearest the shrine. From below, the pier's own edge hides the
        /// middle of its top; here the propeller shows over the edge from MC_Start and Ledge_View.
        /// </summary>
        public static Vector3 PropellerLedge => new(PierCentre.x + PierSize.x * 0.5f - 0.75f, LedgeTop,
                                                    PierCentre.y - PierSize.y * 0.5f + 0.6f);
        /// <summary>S2_SH02's settle point (shot list + (4, 0, 4)).</summary>
        public static readonly Vector3 LedgeView = new(-2f, 2.2f, -92f);
        /// <summary>MC's mark at the ledge foot for scene 3, facing +X (shot list + (4, 0, 4)).</summary>
        public static readonly Vector3 MCHandover = new(-5.5f, 0f, -88f);
        /// <summary>Gameplay spawn for the platforming: MC_Start, facing the ledge.</summary>
        public static readonly Vector3 PlaySpawn = new(0f, 0.05f, -96f);

        // ── Olive path (scene 4) ─────────────────────────────────────────────
        /// <summary>The path's centreline control points (x, z), south to north. Straight
        /// between Path_Track_A and Path_Track_B, the stretch scene 4 walks.</summary>
        public static readonly Vector2[] PathPoints =
        {
            new(0f, -90.3f), new(0f, -88f), new(0f, -84f), new(-1.5f, -72f), new(-3f, -60f),
            new(-4f, -56f), new(-4f, -53f), new(-4f, -51.6f),
        };
        /// <summary>The way down from the crest: east along the rim to the ridge foot.</summary>
        public static readonly Vector2[] DescentPoints =
        {
            new(-4f, -53f), new(2f, -52.6f), new(10f, -52.4f), new(18f, -52.3f), new(24.5f, -51.8f),
        };
        /// <summary>Path ribbon width, metres.</summary>
        public const float PathWidth = 1.8f;

        /// <summary>Path_Start (x, z): where the path leaves the shrine.</summary>
        public static readonly Vector2 PathStartXZ = new(0f, -88f);
        /// <summary>Path_Track_A (x, z).</summary>
        public static readonly Vector2 PathTrackAXZ = new(0f, -84f);
        /// <summary>Path_Track_B (x, z).</summary>
        public static readonly Vector2 PathTrackBXZ = new(-3f, -60f);
        /// <summary>Path_End (x, z).</summary>
        public static readonly Vector2 PathEndXZ = new(-4f, -56f);

        /// <summary>The scene-4 camera track: this far to the walkers' right (east), in H.</summary>
        public const float CamTrackSideH = 3.2f;
        /// <summary>The scene-4 camera track: this far above the path, in H.</summary>
        public const float CamTrackUpH = 0.75f;
        /// <summary>Foreground trunks: distance from the camera line toward the path, metres.</summary>
        public const float TrunkFromCamera = 1.5f;

        /// <summary>
        /// Foreground trunk stations: path distance from Path_Track_A, metres. Spaced 4.5–5 m and
        /// placed so that, at the walk's 1.04 m/s and the placeholder timing of scene 4, no trunk
        /// crosses frame during S4_SH01's 0.8 s hold (8.2–9.0 s) or from OC_S4_07 to the end
        /// (17.0–24.1 s). A trunk 1.5 m from a 35° lens is in frame for ±1.15 m of camera travel.
        /// Re-check these when the real line audio changes the timing.
        /// </summary>
        public static readonly float[] TrunkStations = { 2f, 6.5f, 11f, 16f, 26f };

        // ── South crest (scenes 5-6) ─────────────────────────────────────────
        /// <summary>The crest's summit height, metres. The shot list's ≈ 10 raised to 11 so the eye
        /// line (12.45 m) clears the north stoa roof to the Overlook's parapet.</summary>
        public const float CrestHeight = 11f;
        /// <summary>The ridge foot east of the crest, level with the parapet top (0.9 m), where the
        /// way down steps onto the parapet and into the Gymnasium.</summary>
        public const float RidgeFootHeight = 0.9f;
        /// <summary>Crest_View (x, z): MC's stop mark, facing +Z.</summary>
        public static readonly Vector2 CrestViewXZ = new(-4f, -53f);
        /// <summary>Where Birdy zips off to (unchanged from the shot list).</summary>
        public static readonly Vector3 ZipPath = new(8f, 2f, -30f);

        // ── Academy vista (the greybox's real positions) ─────────────────────
        /// <summary>The gnomon's mid-height: its wedge rises 16 m from the 1 m plaza at (0, 36),
        /// so the middle is y 9 (the shot list said 8).</summary>
        public static readonly Vector3 GnomonAim = new(0f, 9f, 36f);
        /// <summary>The scene-6 gaze point in the fraying shards. Shot list (30, 8, 64) moved to
        /// (20, 8, 66): from Crest_View the old line ran through Birdy's bell tower.</summary>
        public static readonly Vector3 RimGaze = new(20f, 8f, 66f);

        /// <summary>The black sun's centre, from AcademyGreyboxSetup's BlackSunBearing (24°),
        /// BlackSunDistance (450 m) and BlackSunHeight (130 m).</summary>
        public static Vector3 BlackSun => GreyboxReference.BlackSunCentre;

        // ── The void ─────────────────────────────────────────────────────────
        /// <summary>Void dome radius, metres, centred on MC_Start. Every scene-1 camera is within 6 m.</summary>
        public const float DomeRadius = 40f;
        /// <summary>The dome's flood rise weight: its upper part dissolves after the horizon.</summary>
        public const float DomeRise = 1.5f;
        /// <summary>The void disc's height: just under the meadow, so it hides the delta below
        /// without z-fighting the set's own (whitened) ground.</summary>
        public const float DiscHeight = -0.05f;

        // ── Helpers ──────────────────────────────────────────────────────────
        /// <summary>A point on the island's ground.</summary>
        public static Vector3 Ground(float x, float z) => new(x, SouthIslandTerrain.Height(x, z), z);

        /// <summary>A point on the island's ground.</summary>
        public static Vector3 Ground(Vector2 xz) => Ground(xz.x, xz.y);

        /// <summary>Unit vector to the east of the A→B walk, horizontal.</summary>
        public static Vector3 TrackRight
        {
            get
            {
                Vector2 d = (PathTrackBXZ - PathTrackAXZ).normalized;
                return new Vector3(d.y, 0f, -d.x);
            }
        }

        /// <summary>The scene-4 camera track point abeam a path point.</summary>
        public static Vector3 CamTrackAbeam(Vector3 pathPoint) =>
            pathPoint + TrackRight * (CamTrackSideH * H) + Vector3.up * (CamTrackUpH * H);
    }
}
