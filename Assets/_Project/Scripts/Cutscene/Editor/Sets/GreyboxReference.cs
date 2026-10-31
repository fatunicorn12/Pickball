using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Cutscene.Editor.Sets
{
    /// <summary>
    /// The greybox geometry the cutscene sets have to sit beside and see past, copied from
    /// <c>Scripts/Editor/AcademyGreyboxSetup.cs</c> (read-only for this workstream). Every
    /// number names the greybox method it comes from; when the greybox changes, update these
    /// and re-run the set tests, which check the crest's sightlines and the island's clearance
    /// against them. The in-Editor check (SetVerification) raycasts the real scene as well.
    /// </summary>
    public static class GreyboxReference
    {
        /// <summary>BuildVistas: the black sun's bearing, distance and height.</summary>
        public const float BlackSunBearing = 24f, BlackSunDistance = 450f, BlackSunHeight = 130f, BlackSunRadius = 45f;

        /// <summary>The black sun's centre, as BuildVistas places it.</summary>
        public static Vector3 BlackSunCentre
        {
            get
            {
                float r = BlackSunBearing * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(r) * BlackSunDistance, BlackSunHeight, Mathf.Cos(r) * BlackSunDistance);
            }
        }

        /// <summary>BuildCourtyard: the core plateau's extents (x, z) and its south parapet.</summary>
        public const float PlateauMinX = -36f, PlateauMaxX = 40f, PlateauMinZ = -50f, PlateauMaxZ = 50f;
        /// <summary>BuildGymnasium: ParapetSouth spans z -50.4..-50, 0.9 m tall.</summary>
        public const float ParapetSouthZ = -50.4f, ParapetTop = 0.9f;

        /// <summary>BuildDelta: the four islets (x0, x1, z0, z1) with their tops at y -8.</summary>
        public static readonly (float x0, float x1, float z0, float z1)[] DeltaIslets =
        {
            (-80f, -64f, -74f, -60f), (-48f, -36f, -92f, -80f), (-20f, -8f, -104f, -92f), (8f, 26f, -100f, -82f),
        };
        /// <summary>BuildDelta: islet tops.</summary>
        public const float DeltaTop = -8f;

        /// <summary>
        /// Boxes (min, max) that can block a view from the south crest north over the Academy:
        /// the plateau, the stoa walls and roofs, Birdy's bell tower, the gnomon's bounding box,
        /// the plinths, the Overlook and its gates, the Mirror Court walls and the Nautilus Tower.
        /// </summary>
        public static List<(Vector3 min, Vector3 max, string name)> Occluders()
        {
            var list = new List<(Vector3, Vector3, string)>
            {
                // BuildCourtyard
                (new(PlateauMinX, -4f, PlateauMinZ), new(PlateauMaxX, 0f, PlateauMaxZ), "Core plateau"),
                (new(-19f, 0f, 15f), new(19f, 4.5f, 16f), "Stoa north wall"),
                (new(-19f, 0f, -16f), new(19f, 4.5f, -15f), "Stoa south wall"),
                (new(-19f, 0f, -15f), new(-18f, 4.5f, 15f), "Stoa west wall"),
                (new(18f, 0f, -15f), new(19f, 4.5f, 15f), "Stoa east wall"),
                (new(-19f, 4.5f, 11f), new(19f, 5f, 16f), "RoofNorth"),
                (new(-19f, 4.5f, -16f), new(19f, 5f, -11f), "RoofSouth"),
                (new(-19f, 4.5f, -11f), new(-14f, 5f, 11f), "RoofWest"),
                (new(14f, 4.5f, -11f), new(19f, 5f, 11f), "RoofEast"),
                (new(14.5f, 0f, 11.5f), new(19f, 12f, 16f), "BellTower"),
                // BuildGymnasium
                (new(-36f, 0f, -50.4f), new(40f, 0.9f, -50f), "ParapetSouth"),
                (new(11f, 0f, -48.5f), new(17f, 4.5f, -47.5f), "SealWall"),
                (new(-34f, 0f, -48f), new(-30f, 5f, -44f), "AqueductHead"),
                // BuildPlaza: the gnomon wedge's bounding box (x -6..6, rising 1..17 m)
                (new(-6f, 1f, 35.3f), new(6f, 17f, 36.7f), "Gnomon"),
                (new(-15f, 0f, 21f), new(15f, 1f, 51f), "Plaza"),
                // BuildOverlook
                (new(-36f, -3f, 50f), new(40f, 0f, 60f), "Overlook"),
                (new(-36f, 0f, 59.6f), new(-2.1f, 0.9f, 60f), "ParapetNorthW"),
                (new(2.1f, 0f, 59.6f), new(40f, 0.9f, 60f), "ParapetNorthE"),
                (new(-2.4f, 0f, 59.3f), new(2.4f, 5.7f, 60.3f), "ZenoGate"),
                (new(-35.6f, 0f, 54.5f), new(-26.4f, 8.8f, 55.5f), "SurveyorsGate"),
                // BuildEast
                (new(54.8f, 0f, -14f), new(70f, 12f, 14f), "Mirror Court walls"),
                (new(52f, 0f, -44f), new(60f, 26.85f, -36f), "Nautilus Tower"),
                (new(44f, 10.75f, -52f), new(68f, 11.35f, -28f), "Nautilus overhang"),
            };
            // BuildPlaza: eight plinths on an 11 m circle round (0, 36), 1 m plaza + 1.5 m.
            for (int i = 0; i < 8; i++)
            {
                float a = (202.5f - 45f * i) * Mathf.Deg2Rad;
                var at = new Vector3(Mathf.Cos(a) * 11f, 0f, 36f + Mathf.Sin(a) * 11f);
                list.Add((at + new Vector3(-1f, 1f, -1f), at + new Vector3(1f, 2.5f, 1f), $"Plinth_{i}"));
            }
            return list;
        }
    }
}
