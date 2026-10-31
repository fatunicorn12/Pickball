using System.Collections.Generic;
using Pickleball.Cutscene.Sets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Pickleball.Cutscene.Editor.Sets
{
    /// <summary>
    /// Tools ▸ Pickleball ▸ Cutscene ▸ 2 Build Sets. Builds the opening's cutscene-only sets as
    /// prefabs under Prefabs/Cutscene/Sets: Void, OuterShrine, OlivePath, SouthCrest and
    /// AcademyVista, each with a <see cref="CutsceneSet"/> root listing its
    /// <see cref="CutsceneMarker"/>s. Every prefab's root is at the world origin and its content
    /// at world coordinates, so the scene builder instantiates them all at identity into its
    /// copy of AcademyGreybox, beside the greybox geometry without touching it.
    ///
    /// <b>Idempotent:</b> meshes, materials and the Greek-key texture are refilled in place (same
    /// GUIDs), the GoldenHour profile is rewritten from the greybox's lighting, the Void profile
    /// and the resolve config are created once and then left for live tuning, and each prefab is
    /// rebuilt from scratch in a preview scene. The open scene is never touched. Realtime
    /// lighting only: no lightmap or GI static flags are set.
    /// </summary>
    public static class CutsceneSetBuilder
    {
        private const string VoidProfilePath = SetLayout.ConfigDir + "/VoidLook.asset";
        private const string GoldenProfilePath = SetLayout.ConfigDir + "/GoldenHourLook.asset";
        private const string ResolveConfigPath = SetLayout.ConfigDir + "/ResolveConfig.asset";
        private const string VoidPostPath = SetLayout.ConfigDir + "/VoidPostFX.asset";

        /// <summary>Priority of the Void's post volume: above the greybox's World Look volume (1).</summary>
        public const float VoidPostPriority = 100f;

        private static Scene _preview;
        private static SetMaterials.Set _mats;

        /// <summary>Menu entry point.</summary>
        [MenuItem(CutsceneMenus.BuildSets)]
        public static void BuildMenu() => Build();

        /// <summary>Builds every set prefab and its assets.</summary>
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Leave Play mode before building the cutscene sets.");
                return;
            }

            foreach (string dir in new[] { SetLayout.PrefabDir, SetLayout.MeshDir, SetLayout.MaterialDir, SetLayout.TextureDir, SetLayout.ConfigDir })
                SetAssetIO.EnsureFolder(dir);

            _builtTrees.Clear();
            _mats = SetMaterials.Ensure();
            ResolveConfig config = SetAssetIO.LoadOrCreate<ResolveConfig>(ResolveConfigPath);
            var voidLook = AssetDatabase.LoadAssetAtPath<LookProfile>(VoidProfilePath);
            if (voidLook == null)
            {
                voidLook = SetAssetIO.LoadOrCreate<LookProfile>(VoidProfilePath);
                voidLook.Configure(VoidLookState());
                EditorUtility.SetDirty(voidLook);
            }
            var voidPost = EnsureVoidPost();
            var golden = SetAssetIO.LoadOrCreate<LookProfile>(GoldenProfilePath);
            golden.Configure(GoldenHourState());
            EditorUtility.SetDirty(golden);

            _preview = EditorSceneManager.NewPreviewScene();
            try
            {
                BuildVoid(voidLook, golden, config, voidPost);
                BuildOuterShrine(config);
                BuildOlivePath();
                BuildSouthCrest();
                BuildAcademyVista();
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(_preview);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Cutscene sets built: Void, OuterShrine, OlivePath, SouthCrest, AcademyVista in " + SetLayout.PrefabDir);
        }

        // ── Looks ─────────────────────────────────────────────────────────────
        /// <summary>The white void: white exponential fog (no horizon), flat white ambient with a
        /// slightly brighter sky (MC's soft top fill), the sun off.</summary>
        public static LookState VoidLookState() => new(
            FogMode.Exponential, Color.white, 0.06f, 0f, 60f, 0.06f,
            Color.white, new Color(0.96f, 0.96f, 0.96f), new Color(0.86f, 0.86f, 0.86f),
            0f, new Color(1f, 0.88f, 0.72f), 0f);

        /// <summary>
        /// Golden hour, equal to gameplay: AcademyGreyboxSetup.ApplyLighting (linear fog
        /// 300–2600 m in (0.86, 0.82, 0.74), trilight ambient, sun colour (1, 0.88, 0.72) at 1.35),
        /// the fog density and the sun's shadow strength (1) as saved in AcademyGreybox.unity.
        /// </summary>
        public static LookState GoldenHourState() => new(
            FogMode.Linear, new Color(0.86f, 0.82f, 0.74f), 0.01f, 300f, 2600f, 0.00035f,
            new Color(0.72f, 0.76f, 0.86f), new Color(0.74f, 0.68f, 0.6f), new Color(0.42f, 0.38f, 0.34f),
            1.35f, new Color(1f, 0.88f, 0.72f), 1f);

        /// <summary>
        /// The void's post overrides, created once and then left for tuning (like VoidLook): bloom
        /// off (the void's white is HDR, see <see cref="ResolveConfig.VoidWhite"/>, and would
        /// bloom over MC), white balance neutral and no vignette (the World Look volume warms and
        /// darkens the corners, which turns the white cream). Weighted 1 − blend by CutsceneLook.
        /// </summary>
        public static VolumeProfile EnsureVoidPost()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VoidPostPath);
            if (profile != null) return profile;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VoidPostPath);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0f);
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(0f);
            wb.tint.Override(0f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0f);
            foreach (var component in profile.components)
            {
                component.name = component.GetType().Name;
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);
            return profile;
        }

        // ── Void ──────────────────────────────────────────────────────────────
        private static void BuildVoid(LookProfile voidLook, LookProfile golden, ResolveConfig config, VolumeProfile voidPost)
        {
            var root = Root(SetLayout.VoidId);
            var centre = Child("FloodCentre", root.transform);
            centre.transform.position = SetLayout.ShrineCentre;

            var dome = MeshObject("VoidDome", root.transform, SetAssetIO.SaveMesh(PropMeshes.Sphere(32, 16), "VoidDomeSphere"),
                                  _mats.VoidDome, collide: false, shadows: false, isStatic: false);
            dome.transform.position = SetLayout.ShrineCentre;
            dome.transform.localScale = Vector3.one * SetLayout.DomeRadius;

            var disc = MeshObject("VoidDisc", root.transform, SetAssetIO.SaveMesh(PropMeshes.Disc(96), "VoidDisc"),
                                  _mats.VoidDisc, collide: false, shadows: false, isStatic: false);
            disc.transform.position = SetLayout.ShrineCentre + Vector3.up * SetLayout.DiscHeight;
            disc.transform.localScale = new Vector3(SetLayout.DomeRadius, 1f, SetLayout.DomeRadius);

            var look = root.AddComponent<CutsceneLook>();
            look.Configure(voidLook, golden, config, centre.transform,
                           new Renderer[] { dome.GetComponent<Renderer>(), disc.GetComponent<Renderer>() });

            var post = Child("VoidPost", root.transform).AddComponent<Volume>();
            post.isGlobal = true;
            post.priority = VoidPostPriority;
            post.weight = 0f;
            post.sharedProfile = voidPost;
            post.enabled = false;   // the prefab's blend is 1 (gameplay look)
            look.ConfigurePost(post);
            Finish(root, SetLayout.VoidId, new List<CutsceneMarker>());
        }

        /// <summary>
        /// Scene 6's fraying world edge: the Academy's own marble paving coming apart off its north
        /// edge (review round 3: a band of sheets above the rim read as a cluttered wall). Three
        /// zones at increasing distance and height, drifting north-east toward the black sun so the
        /// eye runs from the paving to the sun, the golden-hour haze layering the far ones:
        /// <list type="number">
        /// <item>z 61–90 (the paving ends at z ≈ 60), from the paving's height (y 0): cream marble slabs continuing the paving
        /// grid, the gaps opening, then lifting, tilting and breaking into floating cubes and
        /// triangles of shrinking size;</item>
        /// <item>z 88–118, y 4–20: the same shapes as unfilled black ink outlines (cube and
        /// tetrahedron edges), shrinking, then only hatching;</item>
        /// <item>z 116–160, y 10–40: blank parchment sheets, staggered in depth, fading from cream to
        /// white toward the sun.</item>
        /// </list>
        /// Vertex colour on the painterly material; no colliders; nothing on the walkable plateau.
        /// </summary>
        private static MeshBuffer FrayingRim()
        {
            var m = new MeshBuffer();
            var rng = new System.Random(6);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            Color marble = new(0.94f, 0.91f, 0.84f), veined = new(0.80f, 0.78f, 0.74f), ink = new(0.05f, 0.045f, 0.045f);
            Color parchment = new(0.96f, 0.91f, 0.78f), blank = new(1f, 1f, 0.98f);
            // The band's centre line drifts east with distance, toward the black sun (NNE of Crest_View).
            float CentreX(float z) => 12f + (z - 58f) * 0.45f;

            // 1. Marble: the paving grid carried on past the edge, breaking up with distance u.
            const float tile = 3f;
            for (float z = 61f; z < 90f; z += tile)
            {
                float u = Mathf.InverseLerp(61f, 90f, z);
                float half = Mathf.Lerp(40f, 55f, u);
                for (float x = CentreX(z) - half; x < CentreX(z) + half; x += tile)
                {
                    // Holes open up as the edge frays; near the paving most slabs are still there.
                    if (rng.NextDouble() < Mathf.Lerp(0.05f, 0.55f, u)) continue;
                    // Keep clear of the greybox's narrow north pier (x ≈ 0, to z 76).
                    if (Mathf.Abs(x - 0.5f) < 3.5f && z < 78f) continue;
                    float lift = u * u * 14f + R(0f, 2f) * u;
                    float size = tile * Mathf.Lerp(0.95f, 0.35f, u) * R(0.8f, 1.05f);
                    var p = new Vector3(x + R(-0.4f, 0.4f) * (1f + 4f * u), lift, z + R(-0.4f, 0.4f) * (1f + 4f * u));
                    var rot = Quaternion.Euler(R(-60f, 60f) * u * u, R(-15f, 15f) + R(0f, 360f) * u, R(-60f, 60f) * u * u);
                    Color c = Color.Lerp(veined, marble, R(0.4f, 1f));
                    if (u < 0.45f)
                        // Still slabs: flat, paving-thick.
                        m.Append(PropMeshes.Box(new Vector3(size, Mathf.Lerp(0.5f, 0.9f, u), size), c * 0.9f, c), Matrix4x4.TRS(p, rot, Vector3.one));
                    else if (rng.NextDouble() < 0.45)
                        Tetra(m, p + Vector3.up * size * 0.5f, size * 1.1f, rot, c);
                    else
                        m.Append(PropMeshes.Box(Vector3.one * size * 0.8f, c * 0.9f, c), Matrix4x4.TRS(p + Vector3.up * size * 0.4f, rot, Vector3.one));
                }
            }

            // 2. Ink: unfilled outlines of the same shapes, then hatching alone.
            for (int i = 0; i < 90; i++)
            {
                float u = (float)rng.NextDouble();
                float z = Mathf.Lerp(88f, 108f, u);
                var p = new Vector3(CentreX(z) + R(-45f, 45f), Mathf.Lerp(5f, 16f, u) + R(-1f, 4f), z);
                float size = Mathf.Lerp(2.6f, 1.2f, u) * R(0.8f, 1.2f);
                var rot = Quaternion.Euler(R(-40f, 40f), R(0f, 360f), R(-40f, 40f));
                float w = Mathf.Lerp(0.2f, 0.14f, u);
                if (rng.NextDouble() < 0.5) OutlineCube(m, p, size, rot, w, ink);
                else OutlineTetra(m, p, size * 1.2f, rot, w, ink);
            }
            for (int g = 0; g < 40; g++)
            {
                float z = R(104f, 118f);
                var c = new Vector3(CentreX(z) + R(-45f, 45f), R(8f, 22f), z);
                float tilt = R(30f, 60f), len = R(2f, 4f);
                int strokes = rng.Next(4, 8);
                for (int k = 0; k < strokes; k++)
                    m.Append(PropMeshes.Box(new Vector3(0.14f, 0.14f, len), ink, ink),
                             Matrix4x4.TRS(c + new Vector3(k * 0.7f, 0f, 0f), Quaternion.Euler(tilt, 90f, 0f), Vector3.one));
            }

            // 3. Blank parchment: staggered sheets, cream to white with distance and toward the sun.
            for (int i = 0; i < 46; i++)
            {
                float u = (float)rng.NextDouble();
                float z = Mathf.Lerp(116f, 160f, u);
                var p = new Vector3(CentreX(z) + R(-40f, 40f), Mathf.Lerp(10f, 32f, u) + R(-3f, 6f), z);
                Color c = Color.Lerp(parchment, blank, Mathf.Clamp01(u * 1.2f));
                m.Append(PropMeshes.Box(new Vector3(R(6f, 14f), R(5f, 10f), 0.2f), c, c),
                         Matrix4x4.TRS(p, Quaternion.Euler(R(-12f, 12f), R(-30f, 30f), R(-10f, 10f)), Vector3.one));
            }
            return m;
        }

        // An ink stroke from a to b, a square bar of the given width.
        private static void Edge(MeshBuffer m, Vector3 a, Vector3 b, float width, Color c)
        {
            Vector3 d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            m.Append(PropMeshes.Box(new Vector3(width, width, d.magnitude + width), c, c),
                     Matrix4x4.TRS((a + b) * 0.5f, Quaternion.LookRotation(d), Vector3.one));
        }

        // The 12 edges of a cube, unfilled.
        private static void OutlineCube(MeshBuffer m, Vector3 centre, float size, Quaternion rot, float width, Color c)
        {
            float h = size * 0.5f;
            var v = new Vector3[8];
            for (int i = 0; i < 8; i++)
                v[i] = centre + rot * new Vector3((i & 1) == 0 ? -h : h, (i & 2) == 0 ? -h : h, (i & 4) == 0 ? -h : h);
            for (int i = 0; i < 8; i++)
                for (int bit = 1; bit < 8; bit <<= 1)
                    if ((i & bit) == 0) Edge(m, v[i], v[i | bit], width, c);
        }

        // The 6 edges of a tetrahedron, unfilled.
        private static void OutlineTetra(MeshBuffer m, Vector3 centre, float size, Quaternion rot, float width, Color c)
        {
            float h = size * 0.6f;
            Vector3[] v = { new(h, -h * 0.6f, -h), new(-h, -h * 0.6f, -h), new(0f, -h * 0.6f, h), new(0f, h, 0f) };
            for (int i = 0; i < 4; i++) v[i] = centre + rot * v[i];
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++) Edge(m, v[i], v[j], width, c);
        }

        // A tetrahedron of edge ~size, flat shaded.
        private static void Tetra(MeshBuffer m, Vector3 centre, float size, Quaternion rot, Color c)
        {
            float h = size * 0.6f;
            Vector3[] v =
            {
                new(h, -h * 0.6f, -h), new(-h, -h * 0.6f, -h), new(0f, -h * 0.6f, h), new(0f, h, 0f),
            };
            for (int i = 0; i < 4; i++) v[i] = centre + rot * v[i];
            int[,] faces = { { 0, 1, 2 }, { 0, 3, 1 }, { 1, 3, 2 }, { 2, 3, 0 } };
            Vector3 mid = (v[0] + v[1] + v[2] + v[3]) * 0.25f;
            for (int f = 0; f < 4; f++)
            {
                Vector3 a = v[faces[f, 0]], b = v[faces[f, 1]], d = v[faces[f, 2]];
                Vector3 n = Vector3.Cross(b - a, d - a).normalized;
                if (Vector3.Dot(n, (a + b + d) / 3f - mid) < 0f) n = -n;
                int i0 = m.Add(a, n, c, Vector2.zero), i1 = m.Add(b, n, c, Vector2.right), i2 = m.Add(d, n, c, Vector2.up);
                m.AddFacing(i0, i1, i2, n);
            }
        }

        // ── Outer Shrine ──────────────────────────────────────────────────────
        private static void BuildOuterShrine(ResolveConfig config)
        {
            var root = Root(SetLayout.OuterShrineId);
            Ground(root.transform, "Shrine", float.NegativeInfinity, SouthIslandTerrain.ShrineBandMaxZ);

            Vector3 c = SetLayout.ShrineCentre;
            var lawn = MeshObject("Lawn", root.transform, SetAssetIO.SaveMesh(PropMeshes.Lawn(SetLayout.LawnRadius, 0.02f), "ShrineLawn"),
                                  _mats.Painterly, collide: false);
            lawn.transform.position = c;
            var kerb = MeshObject("Kerb", root.transform,
                                  SetAssetIO.SaveMesh(PropMeshes.Kerb(SetLayout.LawnRadius, SetLayout.KerbOuterRadius, SetLayout.KerbHeight), "ShrineKerb"),
                                  _mats.Painterly, collide: true);
            kerb.transform.position = c;

            // The broken ring of columns.
            var columns = Child("Columns", root.transform);
            Mesh intact = SetAssetIO.SaveMesh(PropMeshes.Column(0f, 0), "Column_Intact");
            for (int i = 0; i < SetLayout.Columns.Length; i++)
            {
                var (bearing, broken) = SetLayout.Columns[i];
                Mesh mesh = broken > 0f ? SetAssetIO.SaveMesh(PropMeshes.Column(broken, 100 + i), $"Column_Broken_{i}") : intact;
                var col = MeshObject($"Column_{bearing:0}", columns.transform, mesh, _mats.Painterly, collide: true, convex: true);
                col.transform.SetPositionAndRotation(c + Dir(bearing) * SetLayout.ColumnRingRadius,
                                                     Quaternion.Euler(0f, bearing + 15f * SouthIslandTerrain.Hash(i, 9), 0f));
            }

            // Terracotta pots by the columns.
            var pots = Child("Pots", root.transform);
            Mesh pot = SetAssetIO.SaveMesh(PropMeshes.Pot(1f), "Amphora");
            float[] potBearings = { 128f, 160f, -136f };
            for (int i = 0; i < potBearings.Length; i++)
            {
                var p = MeshObject($"Amphora_{i}", pots.transform, pot, _mats.Painterly, collide: true, convex: true);
                p.transform.SetPositionAndRotation(c + Dir(potBearings[i]) * (SetLayout.ColumnRingRadius - 0.1f),
                                                   Quaternion.Euler(0f, 40f * i, i == 2 ? 78f : 0f));
                if (i == 2) p.transform.position += Vector3.up * 0.3f;   // one lies tipped over
                p.transform.localScale = Vector3.one * (0.85f + 0.15f * i);
            }

            BuildPropylon(root.transform);

            var trees = Child("OliveTrees", root.transform);
            Tree(trees.transform, 0, new Vector2(-15.5f, -78.5f), 11, 1.8f);
            Tree(trees.transform, 1, new Vector2(5.2f, -87.5f), 23, 1.7f);

            BuildConstructionLines(root.transform, config);

            var markers = new List<CutsceneMarker>
            {
                Marker(root, CutsceneMarkerIds.ShrineCenter, c, Quaternion.identity),
                Marker(root, CutsceneMarkerIds.MCStart, c, Quaternion.identity),
                Marker(root, CutsceneMarkerIds.BirdySkyEntry, SetLayout.BirdySkyEntry,
                       Quaternion.LookRotation(SetLayout.BirdyLand - SetLayout.BirdySkyEntry)),
                Marker(root, CutsceneMarkerIds.PropSeparation, SetLayout.PropSeparation,
                       Quaternion.LookRotation(SetLayout.PropellerLedge - SetLayout.PropSeparation)),
                Marker(root, CutsceneMarkerIds.BirdyLand, SetLayout.BirdyLand, Facing(SetLayout.BirdyLand, c)),
                Marker(root, CutsceneMarkerIds.PropellerLedge, SetLayout.PropellerLedge, Facing(SetLayout.PropellerLedge, c)),
                Marker(root, CutsceneMarkerIds.LedgeView, SetLayout.LedgeView,
                       Quaternion.LookRotation(SetLayout.PropellerLedge - SetLayout.LedgeView)),
                Marker(root, CutsceneMarkerIds.MCHandover, SetLayout.MCHandover, Quaternion.LookRotation(Vector3.right)),
                Marker(root, CutsceneMarkerIds.PlaySpawn, SetLayout.PlaySpawn, Facing(SetLayout.PlaySpawn, SetLayout.PropellerLedge)),
            };
            string[] stepIds = { CutsceneMarkerIds.LedgeStep1, CutsceneMarkerIds.LedgeStep2, CutsceneMarkerIds.LedgeStep3 };
            for (int i = 0; i < stepIds.Length; i++)
            {
                Vector3 next = i + 1 < stepIds.Length ? SetLayout.LedgeSteps[i + 1] : SetLayout.PropellerLedge;
                markers.Add(Marker(root, stepIds[i], SetLayout.LedgeSteps[i], Facing(SetLayout.LedgeSteps[i], next)));
            }
            Finish(root, SetLayout.OuterShrineId, markers);
        }

        // The broken propylon: a 5 m pier whose top is the ledge, a shorter broken pier, a fallen
        // lintel, and three 1.25 m steps along the pier's south face. Every standable top wears
        // the cream Greek-key trim (TheAcademy.md §2).
        private static void BuildPropylon(Transform parent)
        {
            var group = Child("Propylon", parent);
            const float keyRepeat = 0.8f, trimHeight = 0.3f, trimProud = 0.04f;

            var pier = MeshObject("Pier", group.transform,
                                  SetAssetIO.SaveMesh(PropMeshes.Box(new Vector3(SetLayout.PierSize.x, SetLayout.LedgeTop, SetLayout.PierSize.y),
                                                                     PropMeshes.Sandstone * 0.82f, PropMeshes.Sandstone), "PropylonPier"),
                                  _mats.Painterly, collide: false);
            pier.transform.position = new Vector3(SetLayout.PierCentre.x, 0f, SetLayout.PierCentre.y);
            BoxCollider(pier, new Vector3(SetLayout.PierSize.x, SetLayout.LedgeTop, SetLayout.PierSize.y));
            var pierTrim = MeshObject("PierTrim", pier.transform,
                                      SetAssetIO.SaveMesh(PropMeshes.TrimBand(SetLayout.PierSize, trimHeight, trimProud, keyRepeat), "PropylonPierTrim"),
                                      _mats.GreekKey, collide: false);
            pierTrim.transform.localPosition = Vector3.up * SetLayout.LedgeTop;

            var second = MeshObject("BrokenPier", group.transform,
                                    SetAssetIO.SaveMesh(PropMeshes.Box(new Vector3(SetLayout.SecondPierSize.x, SetLayout.SecondPierHeight, SetLayout.SecondPierSize.y),
                                                                       PropMeshes.Sandstone * 0.8f, PropMeshes.Sandstone * 0.97f), "PropylonBrokenPier"),
                                    _mats.Painterly, collide: false);
            second.transform.position = new Vector3(SetLayout.SecondPierCentre.x, 0f, SetLayout.SecondPierCentre.y);
            BoxCollider(second, new Vector3(SetLayout.SecondPierSize.x, SetLayout.SecondPierHeight, SetLayout.SecondPierSize.y));

            var lintel = MeshObject("FallenLintel", group.transform,
                                    SetAssetIO.SaveMesh(PropMeshes.Box(new Vector3(4.5f, 0.9f, 1f), PropMeshes.Sandstone * 0.8f, PropMeshes.Sandstone),
                                                        "PropylonLintel"),
                                    _mats.Painterly, collide: false);
            lintel.transform.SetPositionAndRotation(SetLayout.Ground(-12.2f, -79.6f) + Vector3.down * 0.15f,
                                                    Quaternion.Euler(0f, 22f, 0f));
            BoxCollider(lintel, new Vector3(4.5f, 0.9f, 1f));

            Mesh stepTrim = SetAssetIO.SaveMesh(PropMeshes.TrimBand(SetLayout.StepSize, trimHeight * 0.8f, trimProud, keyRepeat), "PropylonStepTrim");
            for (int i = 0; i < SetLayout.LedgeSteps.Length; i++)
            {
                Vector3 top = SetLayout.LedgeSteps[i];
                var size = new Vector3(SetLayout.StepSize.x, top.y, SetLayout.StepSize.y);
                var step = MeshObject($"Step_{i + 1}", group.transform,
                                      SetAssetIO.SaveMesh(PropMeshes.Box(size, PropMeshes.Sandstone * 0.78f, PropMeshes.Sandstone * 0.95f), $"PropylonStep_{i + 1}"),
                                      _mats.Painterly, collide: false);
                step.transform.position = new Vector3(top.x, 0f, top.z);
                BoxCollider(step, size);
                // Unique names keep the prefab's file IDs stable from run to run.
                var trim = MeshObject($"StepTrim_{i + 1}", step.transform, stepTrim, _mats.GreekKey, collide: false);
                trim.transform.localPosition = Vector3.up * top.y;
            }
        }

        private static void BuildConstructionLines(Transform parent, ResolveConfig config)
        {
            List<ConstructionStrokes.Stroke> strokes = ConstructionStrokes.Build();
            if (strokes.Count > ConstructionLineResolve.MaxStrokes)
                Debug.LogError($"{strokes.Count} construction strokes exceed the line shader's {ConstructionLineResolve.MaxStrokes}.");
            Mesh mesh = SetAssetIO.SaveMesh(ConstructionStrokes.BakeMesh(strokes), "ConstructionLines", boundsPadding: 1f);
            var go = MeshObject("ConstructionLines", parent, mesh, _mats.Line, collide: false, shadows: false, isStatic: false);
            var groups = new StrokeGroup[strokes.Count];
            var orders = new float[strokes.Count];
            for (int i = 0; i < strokes.Count; i++)
            {
                groups[i] = strokes[i].Group;
                orders[i] = strokes[i].Order;
            }
            go.AddComponent<ConstructionLineResolve>().Configure(config, go.GetComponent<Renderer>(), groups, orders);
        }

        // ── Olive path ────────────────────────────────────────────────────────
        private static void BuildOlivePath()
        {
            var root = Root(SetLayout.OlivePathId);
            Ground(root.transform, "Path", SouthIslandTerrain.ShrineBandMaxZ, SouthIslandTerrain.PathBandMaxZ);
            MeshObject("StonePath", root.transform,
                       SetAssetIO.SaveMesh(PropMeshes.PathRibbon(SetLayout.PathPoints, SetLayout.PathWidth, 2f, -91f, SouthIslandTerrain.PathBandMaxZ),
                                           "OlivePathStones"),
                       _mats.Painterly, collide: false);
            var rock = MeshObject("UndersideRock", root.transform, SetAssetIO.SaveMesh(PropMeshes.Rock(new Vector3(10f, 7f, 14f), 5), "IslandRockSouth"),
                                  _mats.Painterly, collide: false);
            rock.transform.position = new Vector3(-5f, -8.5f, -70f);

            Vector3 a = SetLayout.Ground(SetLayout.PathTrackAXZ), b = SetLayout.Ground(SetLayout.PathTrackBXZ);
            var trees = Child("OliveTrees", root.transform);
            int n = 0;
            // Foreground trunks between the scene-4 camera track and the path (see SetLayout.TrunkStations).
            float fromPath = SetLayout.CamTrackSideH * SetLayout.H - SetLayout.TrunkFromCamera;
            foreach (float s in SetLayout.TrunkStations)
            {
                Vector2 xz = OnTrack(a, b, s) + new Vector2(SetLayout.TrackRight.x, SetLayout.TrackRight.z) * fromPath;
                Tree(trees.transform, n++, xz, 53, 2.3f);
            }
            // Background rows to the west, and a few beyond the camera line to the east.
            float[] row1 = { -2f, 4f, 10f, 16f, 22f, 28f }, row2 = { 1f, 7f, 13f, 19f, 25f };
            foreach (float s in row1) TryTree(trees.transform, ref n, OnTrack(a, b, s) - Right2() * 5.5f, 11);
            foreach (float s in row2) TryTree(trees.transform, ref n, OnTrack(a, b, s) - Right2() * 10.5f, 23);
            foreach (float s in new[] { 3f, 12f, 21f }) TryTree(trees.transform, ref n, OnTrack(a, b, s) + Right2() * 8.6f, 37);

            Vector3 pathStart = SetLayout.Ground(SetLayout.PathStartXZ), pathEnd = SetLayout.Ground(SetLayout.PathEndXZ);
            var markers = new List<CutsceneMarker>
            {
                Marker(root, CutsceneMarkerIds.PathStart, pathStart, Facing(pathStart, a)),
                Marker(root, CutsceneMarkerIds.PathTrackA, a, Facing(a, b)),
                Marker(root, CutsceneMarkerIds.PathTrackB, b, Facing(b, pathEnd)),
                Marker(root, CutsceneMarkerIds.CamTrackS4Start, SetLayout.CamTrackAbeam(a), Quaternion.LookRotation(-SetLayout.TrackRight)),
                Marker(root, CutsceneMarkerIds.CamTrackS4End, SetLayout.CamTrackAbeam(b), Quaternion.LookRotation(-SetLayout.TrackRight)),
                Marker(root, CutsceneMarkerIds.PathEnd, pathEnd, Facing(pathEnd, SetLayout.Ground(SetLayout.CrestViewXZ))),
            };
            Finish(root, SetLayout.OlivePathId, markers);
        }

        private static Vector2 Right2() => new(SetLayout.TrackRight.x, SetLayout.TrackRight.z);

        // A point s metres of 3-D path length from A along the straight A→B walk (extended past B).
        private static Vector2 OnTrack(Vector3 a, Vector3 b, float s)
        {
            float t = s / (b - a).magnitude;
            Vector3 p = Vector3.LerpUnclamped(a, b, t);
            return new Vector2(p.x, p.z);
        }

        private static void TryTree(Transform parent, ref int n, Vector2 xz, int seed)
        {
            if (SouthIslandTerrain.SignedDistance(xz.x, xz.y) > -1.5f) return;
            if (Mathf.Abs(xz.x - SouthIslandTerrain.PathX(xz.y)) < 2.2f) return;
            if (xz.y > -60f && xz.x > -11f && xz.x < 4f) return;   // keep the crest's views open
            if (xz.y < -76f && xz.x < -3f) return;                 // the propylon, its steps and Ledge_View's view
            if (Vector2.Distance(xz, new Vector2(SetLayout.ShrineCentre.x, SetLayout.ShrineCentre.z)) < 11f) return;
            Tree(parent, n++, xz, seed, 1.6f + 0.4f * SouthIslandTerrain.Hash(n, 4));
        }

        private static void Tree(Transform parent, int index, Vector2 xz, int seed, float trunkHeight)
        {
            string meshName = $"OliveTree_{seed}_{Mathf.RoundToInt(trunkHeight * 10f)}";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{SetLayout.MeshDir}/{meshName}.asset");
            if (mesh == null || !_builtTrees.Contains(meshName))
            {
                mesh = SetAssetIO.SaveMesh(PropMeshes.OliveTree(seed, trunkHeight), meshName);
                _builtTrees.Add(meshName);
            }
            var tree = MeshObject($"Olive_{index}", parent, mesh, _mats.Painterly, collide: false);
            tree.transform.SetPositionAndRotation(SetLayout.Ground(xz.x, xz.y) - Vector3.up * 0.05f,
                                                  Quaternion.Euler(0f, 360f * SouthIslandTerrain.Hash(index, seed), 0f));
            var trunk = tree.AddComponent<CapsuleCollider>();
            trunk.center = Vector3.up * (trunkHeight * 0.5f + 0.1f);
            trunk.radius = 0.3f;
            trunk.height = trunkHeight + 0.4f;
        }

        private static readonly HashSet<string> _builtTrees = new();

        // ── South crest ───────────────────────────────────────────────────────
        private static void BuildSouthCrest()
        {
            var root = Root(SetLayout.SouthCrestId);
            Ground(root.transform, "Crest", SouthIslandTerrain.PathBandMaxZ, float.PositiveInfinity);
            MeshObject("StonePath", root.transform,
                       SetAssetIO.SaveMesh(PropMeshes.PathRibbon(SetLayout.PathPoints, SetLayout.PathWidth, 2f, SouthIslandTerrain.PathBandMaxZ, -50f),
                                           "CrestPathStones"),
                       _mats.Painterly, collide: false);
            MeshObject("DescentPath", root.transform,
                       SetAssetIO.SaveMesh(PropMeshes.PathRibbon(SetLayout.DescentPoints, 1.5f, 2f, -60f, -50f), "CrestDescentStones"),
                       _mats.Painterly, collide: false);
            var rock = MeshObject("UndersideRock", root.transform, SetAssetIO.SaveMesh(PropMeshes.Rock(new Vector3(9f, 5f, 3f), 8), "IslandRockCrest"),
                                  _mats.Painterly, collide: false);
            rock.transform.position = new Vector3(-3f, -6f, -54.2f);

            Vector3 crest = SetLayout.Ground(SetLayout.CrestViewXZ);
            var markers = new List<CutsceneMarker>
            {
                Marker(root, CutsceneMarkerIds.CrestView, crest, Quaternion.identity),
                Marker(root, CutsceneMarkerIds.ZipPath, SetLayout.ZipPath, Facing(crest, SetLayout.ZipPath)),
            };
            Finish(root, SetLayout.SouthCrestId, markers);
        }

        // ── Academy vista ─────────────────────────────────────────────────────
        private static void BuildAcademyVista()
        {
            var root = Root(SetLayout.AcademyVistaId);
            Vector3 eye = SetLayout.Ground(SetLayout.CrestViewXZ) + Vector3.up * 1.45f;
            var markers = new List<CutsceneMarker>
            {
                Marker(root, CutsceneMarkerIds.GnomonAim, SetLayout.GnomonAim, Quaternion.LookRotation(eye - SetLayout.GnomonAim)),
                Marker(root, CutsceneMarkerIds.RimGaze, SetLayout.RimGaze, Quaternion.LookRotation(eye - SetLayout.RimGaze)),
                Marker(root, CutsceneMarkerIds.BlackSun, SetLayout.BlackSun, Quaternion.LookRotation(eye - SetLayout.BlackSun)),
            };
            MeshObject("FrayingRim", root.transform, SetAssetIO.SaveMesh(FrayingRim(), "FrayingRim", 2f),
                       _mats.Painterly, collide: false, shadows: false, isStatic: true);
            Finish(root, SetLayout.AcademyVistaId, markers);
        }

        // ── Shared pieces ─────────────────────────────────────────────────────
        // One band of the island: ground (collider), skirt (collider), underside, rim wall, fence.
        private static void Ground(Transform parent, string name, float zMin, float zMax)
        {
            IslandMeshes.Band band = IslandMeshes.BuildBand(zMin, zMax);
            var group = Child("Ground", parent);
            MeshObject("Walkable", group.transform, SetAssetIO.SaveMesh(band.Ground, $"{name}Ground"), _mats.Painterly, collide: true);
            MeshObject("RockSkirt", group.transform, SetAssetIO.SaveMesh(band.Skirt, $"{name}Skirt"), _mats.Painterly, collide: true);
            MeshObject("Underside", group.transform, SetAssetIO.SaveMesh(band.Cap, $"{name}Underside"), _mats.Painterly, collide: false, shadows: false);
            MeshObject("RimWall", group.transform, SetAssetIO.SaveMesh(band.RimWall, $"{name}RimWall"), _mats.Painterly, collide: false);
            var fence = Child("Fence", group.transform);
            fence.AddComponent<MeshCollider>().sharedMesh = SetAssetIO.SaveMesh(band.Fence, $"{name}Fence");
        }

        private static GameObject Root(string setId)
        {
            var root = new GameObject(setId);
            SceneManager.MoveGameObjectToScene(root, _preview);
            return root;
        }

        private static GameObject Child(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material material, bool collide,
                                             bool shadows = true, bool convex = false, bool isStatic = true)
        {
            var go = Child(name, parent);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = shadows;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (collide)
            {
                var col = go.AddComponent<MeshCollider>();
                col.sharedMesh = mesh;
                col.convex = convex;
            }
            // Realtime only: batching and culling flags, never ContributeGI (no lightmaps).
            if (isStatic)
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        private static void BoxCollider(GameObject go, Vector3 size)
        {
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            box.center = Vector3.up * (size.y * 0.5f);
        }

        private static CutsceneMarker Marker(GameObject root, string id, Vector3 position, Quaternion rotation)
        {
            Transform group = root.transform.Find("Markers");
            if (group == null) group = Child("Markers", root.transform).transform;
            var go = Child(id, group);
            go.transform.SetPositionAndRotation(position, rotation);
            var marker = go.AddComponent<CutsceneMarker>();
            marker.Configure(id);
            return marker;
        }

        private static void Finish(GameObject root, string setId, List<CutsceneMarker> markers)
        {
            root.AddComponent<CutsceneSet>().Configure(setId, markers.ToArray());
            PrefabUtility.SaveAsPrefabAsset(root, SetLayout.PrefabPath(setId));
            Object.DestroyImmediate(root);
        }

        private static Quaternion Facing(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            d.y = 0f;
            return d.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(d) : Quaternion.identity;
        }

        private static Vector3 Dir(float bearing)
        {
            float r = bearing * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
        }
    }
}
