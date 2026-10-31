using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Cutscene.Editor.Pipeline
{
    /// <summary>
    /// Stand-in sets for when the sets workstream's prefabs aren't present: one CutsceneSet root per
    /// set with its markers at the shot list §2 coordinates, plus just enough greybox (a meadow disc,
    /// the ledge and its steps, a path ramp, the crest) to walk on. They double as the swap test's
    /// "replacement sets": same ids, different geometry. The Void is a look, so its placeholder has
    /// no markers.
    /// </summary>
    public static class PlaceholderSets
    {
        /// <summary>Marker spec: id, position, and the point it faces (or null for +Z).</summary>
        public readonly struct MarkerSpec
        {
            /// <summary>Marker id.</summary>
            public readonly string Id;
            /// <summary>World position.</summary>
            public readonly Vector3 Position;
            /// <summary>Yaw in degrees (0 = +Z, 90 = +X).</summary>
            public readonly float Yaw;

            /// <summary>Creates a spec.</summary>
            public MarkerSpec(string id, Vector3 position, float yaw)
            {
                Id = id;
                Position = position;
                Yaw = yaw;
            }
        }

        static float YawTo(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        /// <summary>The markers of one set, at the shot list §2 positions.</summary>
        public static List<MarkerSpec> Markers(string setId)
        {
            var list = new List<MarkerSpec>();
            switch (setId)
            {
                case "OuterShrine":
                {
                    var ledge = new Vector3(-13f, 5f, -88f);
                    var mcStart = new Vector3(-4f, 0f, -100f);
                    list.Add(new MarkerSpec(CutsceneMarkerIds.ShrineCenter, new Vector3(-4f, 0f, -100f), 0f));
                    list.Add(new MarkerSpec(CutsceneMarkerIds.MCStart, mcStart, 0f));
                    list.Add(new MarkerSpec(CutsceneMarkerIds.BirdySkyEntry, new Vector3(4f, 14f, -88f), 0f));
                    list.Add(new MarkerSpec(CutsceneMarkerIds.PropSeparation, new Vector3(1f, 9f, -92f), 0f));
                    list.Add(new MarkerSpec(CutsceneMarkerIds.BirdyLand, new Vector3(0f, 0f, -95f), YawTo(new Vector3(0f, 0f, -95f), mcStart)));
                    list.Add(new MarkerSpec(CutsceneMarkerIds.PropellerLedge, ledge, 180f));
                    list.Add(new MarkerSpec(CutsceneMarkerIds.LedgeView, new Vector3(-6f, 2.2f, -96f), YawTo(new Vector3(-6f, 2.2f, -96f), ledge)));
                    list.Add(new MarkerSpec(CutsceneMarkerIds.MCHandover, new Vector3(-11f, 0f, -91f), 90f));
                    list.Add(new MarkerSpec(CutsceneMarkerIds.PlaySpawn, mcStart, YawTo(mcStart, ledge)));
                    list.Add(new MarkerSpec(CutsceneMarkerIds.LedgeStep1, new Vector3(-9f, 1.4f, -91f), 0f));
                    list.Add(new MarkerSpec(CutsceneMarkerIds.LedgeStep2, new Vector3(-9f, 2.8f, -88f), 0f));
                    list.Add(new MarkerSpec(CutsceneMarkerIds.LedgeStep3, new Vector3(-11f, 4.0f, -86f), 0f));
                    break;
                }
                case "OlivePath":
                {
                    var a = new Vector3(0f, 1f, -84f);
                    var b = new Vector3(-3f, 8f, -60f);
                    float walk = YawTo(a, b);
                    // Cam_Track_S4: 3.2·H east of the walkers and 0.75·H up, at H = 1.6.
                    Vector3 side = Quaternion.Euler(0f, walk, 0f) * Vector3.right * 5.12f + Vector3.up * 1.2f;
                    list.Add(new MarkerSpec(CutsceneMarkerIds.PathStart, new Vector3(0f, 0f, -88f), walk));
                    list.Add(new MarkerSpec(CutsceneMarkerIds.PathTrackA, a, walk));
                    list.Add(new MarkerSpec(CutsceneMarkerIds.PathTrackB, b, walk));
                    list.Add(new MarkerSpec(CutsceneMarkerIds.PathEnd, new Vector3(-4f, 9.5f, -56f), walk));
                    list.Add(new MarkerSpec(CutsceneMarkerIds.CamTrackS4Start, a + side, walk));
                    list.Add(new MarkerSpec(CutsceneMarkerIds.CamTrackS4End, b + side, walk));
                    break;
                }
                case "SouthCrest":
                    list.Add(new MarkerSpec(CutsceneMarkerIds.CrestView, new Vector3(-4f, 10f, -53f), 0f));
                    list.Add(new MarkerSpec(CutsceneMarkerIds.ZipPath, new Vector3(8f, 2f, -30f), 27f));
                    break;
                case "AcademyVista":
                    list.Add(new MarkerSpec(CutsceneMarkerIds.GnomonAim, new Vector3(0f, 8f, 36f), 0f));
                    list.Add(new MarkerSpec(CutsceneMarkerIds.RimGaze, new Vector3(30f, 8f, 64f), 0f));
                    list.Add(new MarkerSpec(CutsceneMarkerIds.BlackSun, new Vector3(183f, 130f, 411f), 0f));
                    break;
            }
            return list;
        }

        /// <summary>Builds one placeholder set under <paramref name="parent"/>.</summary>
        public static CutsceneSet Build(string setId, Transform parent)
        {
            var root = new GameObject($"Set_{setId} (placeholder)");
            root.transform.SetParent(parent, false);
            var set = root.AddComponent<CutsceneSet>();

            var markers = new List<CutsceneMarker>();
            foreach (var spec in Markers(setId))
            {
                var go = new GameObject(spec.Id);
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(spec.Position, Quaternion.Euler(0f, spec.Yaw, 0f));
                var marker = go.AddComponent<CutsceneMarker>();
                marker.Configure(spec.Id);
                markers.Add(marker);
            }
            set.Configure(setId, markers.ToArray());
            BuildGround(setId, root.transform);
            return set;
        }

        static void BuildGround(string setId, Transform root)
        {
            var geo = new GameObject("Greybox").transform;
            geo.SetParent(root, false);
            switch (setId)
            {
                case "OuterShrine":
                    Block(geo, "Meadow", PrimitiveType.Cylinder, new Vector3(-4f, -0.5f, -95f), new Vector3(44f, 0.5f, 44f), 0f);
                    Block(geo, "Ledge", PrimitiveType.Cube, new Vector3(-13f, 2.5f, -88f), new Vector3(3f, 5f, 3f), 0f);
                    Block(geo, "Step1", PrimitiveType.Cube, new Vector3(-9f, 0.7f, -91f), new Vector3(2f, 1.4f, 2f), 0f);
                    Block(geo, "Step2", PrimitiveType.Cube, new Vector3(-9f, 1.4f, -88f), new Vector3(2f, 2.8f, 2f), 0f);
                    Block(geo, "Step3", PrimitiveType.Cube, new Vector3(-11f, 2.0f, -86f), new Vector3(2f, 4.0f, 2f), 0f);
                    break;
                case "OlivePath":
                {
                    var from = new Vector3(0f, 0f, -88f);
                    var to = new Vector3(-4f, 9.5f, -56f);
                    Vector3 d = to - from;
                    var ramp = Block(geo, "PathRamp", PrimitiveType.Cube, (from + to) * 0.5f + Vector3.down * 0.25f,
                        new Vector3(3f, 0.5f, d.magnitude + 1f), 0f);
                    ramp.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
                    break;
                }
                case "SouthCrest":
                    Block(geo, "Crest", PrimitiveType.Cube, new Vector3(-4f, 9.5f, -52f), new Vector3(10f, 1f, 8f), 0f);
                    break;
            }
        }

        static Transform Block(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, float yaw)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = scale;
            if (type == PrimitiveType.Cylinder)
            {
                // A cylinder's capsule collider rounds the edges; a box is a flat floor.
                Object.DestroyImmediate(go.GetComponent<Collider>());
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(1f, 2f, 1f);
            }
            return go.transform;
        }
    }
}
