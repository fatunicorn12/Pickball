using System.Collections.Generic;
using System.IO;
using Pickleball.Sandbox.Hands;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Turns a weapon model into something a hand can hold: measures its handle
/// and blade from the mesh and gives it a <see cref="HeldItem"/> with a grip
/// anchor, blade markers and a handle radius.
///
/// Two ways in:
///  - <see cref="PrepareInPlace"/> readies a model already hanging in a hand
///    (Setup Weapon's compass, which lives inside the Chibi prefab instance);
///  - <see cref="BuildPrefab"/> writes a held-weapon prefab under
///    Prefabs/Weapons, ready for an equipper to instantiate and hand to
///    <see cref="HandGrip.Hold"/>. Tools ▸ Pickleball ▸ Build Held Weapons
///    builds both shipped ones (Held_Compass, Held_Ruler).
///
/// A held-weapon prefab's root IS the grip frame (<see cref="GripFrame"/>):
/// its origin is the centre of the fist, +Y runs up the blade and +Z points at
/// the leading edge. The model hangs under it, scaled and turned so its
/// measured handle lands there, and the markers are plain children:
/// <code>
/// Held_Ruler      HeldItem (grip anchor, blade markers, grip radius)
///   Model         the FBX, scaled to length, posed so the handle is at the origin
///   GripAnchor    identity: the centre of the fist
///   BladeBase     identity: where WeaponClearance pivots and the hit sweep starts
///   BladeTip      (0, length past the fist, 0): the far end
/// </code>
/// So a seated item sits at identity on the grab point, and the markers have
/// the same local poses the hand's own BladeBase / BladeTip have always had.
///
/// Every number is measured, not typed: the length axis and the handle from
/// the mesh (<see cref="GripSeat.HandleLength"/>), the edge from the texture
/// when the shape can't say (<see cref="BladeEdge"/>). Re-running gives the
/// same prefab: an existing one is updated in place, so file ids don't churn.
/// </summary>
public static class HeldWeaponBuilder
{
    public const string PrefabDir = "Assets/_Project/Prefabs/Weapons";
    public const string CompassPrefabPath = PrefabDir + "/Held_Compass.prefab";
    public const string RulerPrefabPath = PrefabDir + "/Held_Ruler.prefab";

    const string CompassModelPath = "Assets/_Project/Art/Props/compass-weapon/compass-weapon.fbx";
    const string RulerDir = "Assets/_Project/Art/Props/ruler-sword";
    public const string RulerModelPath = RulerDir + "/ruler-sword.fbx";
    public const string RulerTexturePath = RulerDir + "/ruler-sword_basecolor.jpg";
    const string RulerMaterialPath = RulerDir + "/ruler-sword.mat";
    const string LitShader = "Universal Render Pipeline/Lit";

    /// <summary>Names the grip anchor and markers go by, on a prefab and on
    /// an in-place weapon alike.</summary>
    public const string GripAnchorName = "GripAnchor";
    const string ModelName = "Model";
    const string BladeBaseName = "BladeBase";
    const string BladeTipName = "BladeTip";

    /// <summary>
    /// Handle measurement (see <see cref="GripSeat.HandleLength"/>): the weapon
    /// is cut into slices of this fraction of its length, starting at the held
    /// end. 2% of 0.85 m is 1.7 cm, finer than any guard or knob.
    /// </summary>
    const float HandleSliceFraction = 0.02f;

    /// <summary>How far along the weapon a handle may run, as a fraction of its
    /// length. Past this a widening is part of the weapon, not the end of its
    /// handle.</summary>
    const float HandleSearchFraction = 0.4f;

    /// <summary>How much wider than the handle a slice must be to end it. The
    /// compass's hinge disc is about 1.9 times its knob; a sword's crossguard is
    /// several times its grip.</summary>
    const float HandleWidenRatio = 1.5f;

    /// <summary>Half the width of the chibi's fist across the handle, m: the
    /// stretch of handle whose radius the fist closes on.</summary>
    const float FistHalfWidth = 0.035f;

    /// <summary>Which way the anchor's +Z (the leading edge) points.</summary>
    public enum EdgeRule
    {
        /// <summary>Along the wider girth axis, positive direction. The compass:
        /// the plane its legs splay in, the roll it has always been held at.</summary>
        WideAxisPositive,
        /// <summary>Along the wider girth axis, negative direction.</summary>
        WideAxisNegative,
        /// <summary>Along the wider girth axis, toward whichever side the
        /// texture shows as bare steel (<see cref="BladeEdge"/>).</summary>
        SteelFromTexture,
    }

    /// <summary>Everything that differs between weapons. All lengths in metres
    /// at game scale.</summary>
    public sealed class Spec
    {
        /// <summary>Prefab root name.</summary>
        public string Name;
        /// <summary>The model asset.</summary>
        public string ModelPath;
        /// <summary>Length after scaling, whatever the authored size.</summary>
        public float Length;
        /// <summary>True when the handle is at the high end of the model's long
        /// axis. Tripo models don't agree: the compass's origin is at its leg
        /// tips, the ruler's at its pommel.</summary>
        public bool GripFarEnd;
        /// <summary>How the leading edge is found.</summary>
        public EdgeRule Edge;
        /// <summary>Texture sampled for <see cref="EdgeRule.SteelFromTexture"/>.</summary>
        public string EdgeTexturePath;
        /// <summary>Close the fist round the handle's own axis rather than the
        /// model's bounds centre line (see <see cref="GripSeat.HandleAxisPoint"/>).</summary>
        public bool CentreOnHandle;
        /// <summary>Centre of the fist this far below the guard, m; 0 for the
        /// middle of the handle (see <see cref="GripSeat.GripPosition"/>).</summary>
        public float GripBelowGuard;
        /// <summary>Measure the handle's radius where the fist closes and store
        /// it on the <see cref="HeldItem"/>, so seating and the finger curl
        /// follow it. Off keeps the hand's own size.</summary>
        public bool MeasureGripRadius;
    }

    /// <summary>
    /// The compass, exactly as Setup Weapon has always prepared it: 0.85 m,
    /// held by its knob end, gripped in the middle of the knob and neck on the
    /// bounds centre line, edge along +wide, the hand's own grip radius. The
    /// grip frame this gives is the one every saved scene already holds it in.
    /// </summary>
    public static readonly Spec Compass = new()
    {
        Name = "Held_Compass",
        ModelPath = CompassModelPath,
        Length = 0.85f,
        GripFarEnd = true,
        Edge = EdgeRule.WideAxisPositive,
    };

    /// <summary>The primitive stand-in Setup Weapon uses with no compass
    /// model: a cylinder has no far end to prefer.</summary>
    public static readonly Spec StandIn = new()
    {
        Name = "StandIn",
        Length = 0.85f,
        GripFarEnd = false,
        Edge = EdgeRule.WideAxisPositive,
    };

    /// <summary>
    /// The ruler sword. 1.05 m: about 1.25 times the compass and three
    /// fifths of the chibi's height, which with the 22 cm-wide blade reads as
    /// a buster sword, while the hand stays at its end. Longer and the Mixamo
    /// swings, authored round a 0.8 m blade, start burying the tip in the
    /// ground and sweeping it through the big head; WeaponClearance only lifts
    /// the arm so far. See Docs/Systems/Hands.md for the trade-off.
    ///
    /// Held at its pommel end (the model's low end), with the fist 4.5 cm
    /// below the guard so the guard sits just above it, the edge on the steel
    /// side found from the texture, and the measured 1.9 cm handle radius.
    /// </summary>
    public static readonly Spec Ruler = new()
    {
        Name = "Held_Ruler",
        ModelPath = RulerModelPath,
        Length = 1.05f,
        GripFarEnd = false,
        Edge = EdgeRule.SteelFromTexture,
        EdgeTexturePath = RulerTexturePath,
        CentreOnHandle = true,
        GripBelowGuard = 0.045f,
        MeasureGripRadius = true,
    };

    /// <summary>What <see cref="Measure"/> found, in the model's own space
    /// (its local axes, unscaled) unless stated.</summary>
    public struct Measurement
    {
        /// <summary>Uniform scale that gives the spec's length.</summary>
        public float Scale;
        /// <summary>Centre of the fist on the handle.</summary>
        public Vector3 AnchorInModel;
        /// <summary>The grip frame at the anchor: +Y to the far end, +Z the edge.</summary>
        public Quaternion AnchorRotationInModel;
        /// <summary>The far end, on the handle's axis.</summary>
        public Vector3 TipInModel;
        /// <summary>Handle length from the held end, model units; 0 if none.</summary>
        public float HandleLength;
        /// <summary>Handle radius where the fist closes, m at game scale; 0
        /// when not measured.</summary>
        public float GripRadius;
        /// <summary>Which way along the wide axis the edge points: +1 or −1.</summary>
        public int EdgeSign;
        /// <summary>Human-readable summary for the log.</summary>
        public string Notes;
    }

    // ---------------------------------------------------------------- menus

    /// <summary>
    /// Imports the ruler sword (settings, material) and builds both held-weapon
    /// prefabs and the ruler's clearance profile. Touches no scene.
    /// </summary>
    [MenuItem("Tools/Pickleball/Build Held Weapons")]
    public static void BuildAll()
    {
        EnsureRulerImport();
        BuildPrefab(Compass, CompassPrefabPath);
        BuildPrefab(Ruler, RulerPrefabPath);
        WeaponClearanceSetup.LoadOrCreateRulerProfile();
        AssetDatabase.SaveAssets();
        Debug.Log($"Held weapons built: {CompassPrefabPath}, {RulerPrefabPath}.");
    }

    // ------------------------------------------------------------ measuring

    /// <summary>
    /// Measures a weapon model in its own space: its length axis, the handle at
    /// the held end, where the fist closes, the edge, and the handle's radius
    /// there. Reads the model in place through the transform matrices, so it
    /// works on a prefab instance that can't be moved (the Chibi's compass).
    /// </summary>
    public static Measurement Measure(GameObject model, Spec spec)
    {
        var local = MeasureModelBounds(model);

        // Longest dimension is the weapon's length; the other two are its girth.
        int axis = 0;
        if (local.size.y > local.size[axis]) axis = 1;
        if (local.size.z > local.size[axis]) axis = 2;
        float span = local.size[axis];
        float scale = span > 0.0001f ? spec.Length / span : 1f;

        // The held end and the direction from it to the far end, on the
        // bounds' centre line.
        var gripEnd = local.center;
        gripEnd[axis] = spec.GripFarEnd ? local.max[axis] : local.min[axis];
        var along = Vector3.zero;
        along[axis] = spec.GripFarEnd ? -1f : 1f;

        int g1 = (axis + 1) % 3, g2 = (axis + 2) % 3;
        int wideIndex = local.size[g1] >= local.size[g2] ? g1 : g2;
        var wide = Vector3.zero;
        wide[wideIndex] = 1f;
        Vector3 u = Vector3.zero, v = Vector3.zero;
        u[g1] = 1f;
        v[g2] = 1f;

        var points = ModelVertices(model, out var meshes);
        float slice = span * HandleSliceFraction;
        int count = Mathf.CeilToInt(HandleSearchFraction / HandleSliceFraction);
        float handle = GripSeat.HandleLength(GripSeat.SliceRadii(points, gripEnd, along, local.center, slice, count),
                                             slice, HandleWidenRatio);

        Vector3 axisPoint = spec.CentreOnHandle
            ? GripSeat.HandleAxisPoint(points, gripEnd, along, u, v, handle, gripEnd)
            : gripEnd;
        float grip = GripSeat.GripPosition(handle, spec.GripBelowGuard / scale);

        int sign = spec.Edge == EdgeRule.WideAxisNegative ? -1 : 1;
        string edgeNote = $"edge along {(sign > 0 ? "+" : "-")}{"XYZ"[wideIndex]}";
        if (spec.Edge == EdgeRule.SteelFromTexture)
        {
            int found = SteelSide(meshes, model, spec.EdgeTexturePath, gripEnd, along, axisPoint, wide,
                                  handle, span, out string why);
            if (found != 0) sign = found;
            edgeNote = found != 0
                ? $"steel edge found on the {(sign > 0 ? "+" : "-")}{"XYZ"[wideIndex]} side ({why})"
                : $"steel edge NOT found ({why}); fell back to +{"XYZ"[wideIndex]}";
            if (found == 0) Debug.LogWarning($"{spec.Name}: {edgeNote}");
        }

        float gripRadius = 0f;
        if (spec.MeasureGripRadius && handle > 0f)
        {
            var radii = GripSeat.SliceRadii(points, gripEnd, along, axisPoint, slice, count);
            float window = FistHalfWidth / scale;
            gripRadius = GripSeat.MeanRadius(radii, slice, grip - window, grip + window) * scale;
        }

        return new Measurement
        {
            Scale = scale,
            AnchorInModel = axisPoint + along * grip,
            AnchorRotationInModel = Quaternion.LookRotation(wide * sign, along),
            TipInModel = axisPoint + along * span,
            HandleLength = handle,
            GripRadius = gripRadius,
            EdgeSign = sign,
            Notes = handle > 0f
                ? $"{span * scale:0.000} m long (x{scale:0.0000}); handle {handle * scale:0.000} m from the held " +
                  $"end, fist at {grip * scale:0.000} m; {edgeNote}" +
                  (gripRadius > 0f ? $"; handle radius {gripRadius:0.0000} m" : "")
                : $"{span * scale:0.000} m long; no distinct handle (it never widens near the held end), " +
                  $"so the fist closes on the end; {edgeNote}",
        };
    }

    /// <summary>
    /// Readies a weapon model that already hangs in a hand: scales it to the
    /// spec's length and gives it a <see cref="HeldItem"/> with a grip anchor
    /// at the measured handle. Used by Setup Weapon and Setup Weapon Grip on the
    /// compass inside the Chibi prefab instance: it adds a child and a
    /// component (allowed there) and never reparents or destroys anything.
    ///
    /// The old code assumed the model's own origin WAS the handle and just
    /// parented it at zero, then gripped the very end, so the compass's 8 cm
    /// knob stuck out of the hand instead of sitting in it. Measuring the
    /// handle puts the fist round the knob and neck, up to the hinge disc.
    /// </summary>
    /// <param name="tipInModel">The far end, in the model's own (unscaled)
    /// space, for the hand's BladeTip marker.</param>
    public static HeldItem PrepareInPlace(GameObject visual, Spec spec, out Vector3 tipInModel)
    {
        var tr = visual.transform;
        var m = Measure(visual, spec);
        tipInModel = m.TipInModel;
        tr.localScale = Vector3.one * m.Scale;

        // A new child and a new component are allowed on a prefab instance
        // (they become added-object overrides); only reparenting or destroying
        // what the prefab owns is not.
        var anchor = tr.Find(GripAnchorName);
        if (anchor == null)
        {
            anchor = new GameObject(GripAnchorName).transform;
            anchor.SetParent(tr, false);
        }
        anchor.SetLocalPositionAndRotation(m.AnchorInModel, m.AnchorRotationInModel);
        anchor.localScale = Vector3.one;

        if (!visual.TryGetComponent(out HeldItem item))
            item = visual.AddComponent<HeldItem>();
        item.SetGripAnchor(anchor);
        item.SetGripRadius(m.GripRadius);
        RecordInstanceChanges(tr, anchor, item);

        Debug.Log($"Weapon {visual.name}: {m.Notes}.");
        return item;
    }

    /// <summary>
    /// Builds or updates a held-weapon prefab (see the class summary for its
    /// layout). An existing prefab is opened and updated in place, so
    /// re-running keeps every file id and an unchanged weapon saves the same
    /// YAML. Returns the prefab's <see cref="HeldItem"/>, or null if the model
    /// is missing.
    /// </summary>
    public static HeldItem BuildPrefab(Spec spec, string prefabPath)
    {
        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
        if (modelAsset == null)
        {
            Debug.LogError($"{spec.Name}: no model at {spec.ModelPath}.");
            return null;
        }
        ChibiSandboxSetup.EnsureFolderPublic(PrefabDir);

        bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
        var root = existing ? PrefabUtility.LoadPrefabContents(prefabPath) : new GameObject(spec.Name);
        try
        {
            var model = root.transform.Find(ModelName);
            if (model != null && PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject) != modelAsset)
            {
                Object.DestroyImmediate(model.gameObject);
                model = null;
            }
            if (model == null)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
                go.name = ModelName;
                model = go.transform;
            }
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

            var m = Measure(model.gameObject, spec);
            model.localScale = Vector3.one * m.Scale;
            // The model goes where seating would put it if the root were the
            // grab point: its anchor on the root's origin, axes and all.
            GripSeat.ItemPose(m.AnchorInModel * m.Scale, m.AnchorRotationInModel,
                              out Vector3 modelPosition, out Quaternion modelRotation);
            model.SetLocalPositionAndRotation(modelPosition, modelRotation);

            Vector3 tip = modelPosition + modelRotation * (m.TipInModel * m.Scale);
            var anchor = Child(root.transform, GripAnchorName, Vector3.zero);
            var bladeBase = Child(root.transform, BladeBaseName, Vector3.zero);
            var bladeTip = Child(root.transform, BladeTipName, tip);

            if (!root.TryGetComponent(out HeldItem item)) item = root.AddComponent<HeldItem>();
            item.SetGripAnchor(anchor);
            item.SetBladeMarkers(bladeBase, bladeTip);
            item.SetGripRadius(m.GripRadius);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"{prefabPath}: {m.Notes}; blade tip {tip.y:0.000} m past the fist.");
        }
        finally
        {
            if (existing) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
        return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).GetComponent<HeldItem>();
    }

    // -------------------------------------------------------------- importing

    /// <summary>
    /// Import settings for the ruler sword, and a URP Lit material showing its
    /// texture, mapped onto the FBX's material slot.
    ///
    /// Settings follow the compass (unit scale, file units, no axis bake, not
    /// readable at runtime) except that the ruler imports with no rig: the
    /// compass's Generic rig only adds an empty Animator to a prop that never
    /// animates, and a held weapon sits inside the chibi's own animated
    /// hierarchy. The FBX's own material can't be trusted to find its texture:
    /// it names Tripo's original JPG, which the model's folder doesn't keep.
    /// </summary>
    public static void EnsureRulerImport()
    {
        if (AssetImporter.GetAtPath(RulerTexturePath) is TextureImporter ti)
        {
            bool changed = false;
            if (ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; changed = true; }
            if (!ti.sRGBTexture) { ti.sRGBTexture = true; changed = true; }
            if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; changed = true; }
            if (ti.isReadable) { ti.isReadable = false; changed = true; }
            if (changed) ti.SaveAndReimport();
        }

        if (AssetImporter.GetAtPath(RulerModelPath) is not ModelImporter mi)
        {
            Debug.LogError($"No ruler sword model at {RulerModelPath}.");
            return;
        }
        bool reimport = false;
        void Set<T>(T current, T wanted, System.Action<T> apply)
        {
            if (EqualityComparer<T>.Default.Equals(current, wanted)) return;
            apply(wanted);
            reimport = true;
        }
        Set(mi.globalScale, 1f, x => mi.globalScale = x);
        Set(mi.useFileUnits, true, x => mi.useFileUnits = x);
        Set(mi.useFileScale, true, x => mi.useFileScale = x);
        Set(mi.bakeAxisConversion, false, x => mi.bakeAxisConversion = x);
        Set(mi.isReadable, false, x => mi.isReadable = x);
        Set(mi.animationType, ModelImporterAnimationType.None, x => mi.animationType = x);
        Set(mi.importAnimation, false, x => mi.importAnimation = x);
        Set(mi.importCameras, false, x => mi.importCameras = x);
        Set(mi.importLights, false, x => mi.importLights = x);
        Set(mi.addCollider, false, x => mi.addCollider = x);
        Set(mi.materialImportMode, ModelImporterMaterialImportMode.ImportViaMaterialDescription,
            x => mi.materialImportMode = x);
        Set(mi.materialLocation, ModelImporterMaterialLocation.InPrefab, x => mi.materialLocation = x);
        if (reimport) mi.SaveAndReimport();

        var shader = Shader.Find(LitShader);
        if (shader == null)
        {
            Debug.LogError($"Shader '{LitShader}' not found; is URP installed?");
            return;
        }
        var mat = AssetDatabase.LoadAssetAtPath<Material>(RulerMaterialPath);
        if (mat == null)
        {
            mat = new Material(shader) { name = "ruler-sword" };
            AssetDatabase.CreateAsset(mat, RulerMaterialPath);
        }
        else if (mat.shader != shader)
        {
            mat.shader = shader;
        }
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(RulerTexturePath));
        mat.SetColor("_BaseColor", Color.white);
        // Painted wood-and-steel, not polished metal: a soft highlight only.
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 0.3f);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssetIfDirty(mat);

        bool remapped = false;
        var map = mi.GetExternalObjectMap();
        foreach (var embedded in AssetDatabase.LoadAllAssetsAtPath(RulerModelPath))
        {
            if (embedded is not Material em) continue;
            var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), em.name);
            if (map.TryGetValue(id, out var current) && current == mat) continue;
            mi.AddRemap(id, mat);
            remapped = true;
        }
        if (remapped) mi.SaveAndReimport();
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Which side of the blade is steel, from the texture at the blade's
    /// vertices: +1 or −1 along <paramref name="wide"/>, 0 if it can't tell.
    /// Only the blade proper is sampled, from a tenth of the length past the
    /// handle (clear of the guard) to 95% (short of the tip's bevel).
    /// </summary>
    static int SteelSide(List<(Mesh mesh, Matrix4x4 toModel)> meshes, GameObject model, string texturePath,
                         Vector3 gripEnd, Vector3 along, Vector3 axisPoint, Vector3 wide,
                         float handle, float span, out string why)
    {
        if (string.IsNullOrEmpty(texturePath) || !File.Exists(texturePath))
        {
            why = $"no texture at {texturePath}";
            return 0;
        }
        // Decoded from the file, so the import can stay unreadable at runtime.
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!tex.LoadImage(File.ReadAllBytes(texturePath)))
            {
                why = $"could not decode {texturePath}";
                return 0;
            }
            var offsets = new List<float>();
            var colours = new List<Color>();
            float from = handle + 0.1f * span, to = 0.95f * span;
            foreach (var (mesh, toModel) in meshes)
            {
                var verts = mesh.vertices;
                var uvs = mesh.uv;
                if (uvs.Length != verts.Length) continue;
                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 p = toModel.MultiplyPoint3x4(verts[i]);
                    float t = Vector3.Dot(p - gripEnd, along);
                    if (t < from || t > to) continue;
                    offsets.Add(Vector3.Dot(p - axisPoint, wide));
                    colours.Add(tex.GetPixelBilinear(uvs[i].x, uvs[i].y));
                }
            }
            BladeEdge.Score(offsets, colours, 0.15f, out float low, out float high);
            why = $"{offsets.Count} blade vertices, steeliness {low:0.00} on the - side, {high:0.00} on the + side";
            return BladeEdge.SteelSide(offsets, colours);
        }
        finally
        {
            Object.DestroyImmediate(tex);
        }
    }

    /// <summary>Every vertex the model's mesh filters draw, in the model
    /// root's own space, plus each mesh with its transform into that space.</summary>
    static List<Vector3> ModelVertices(GameObject model, out List<(Mesh, Matrix4x4)> meshes)
    {
        var points = new List<Vector3>();
        meshes = new List<(Mesh, Matrix4x4)>();
        var toModel = model.transform.worldToLocalMatrix;
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var meshToModel = toModel * mf.transform.localToWorldMatrix;
            meshes.Add((mf.sharedMesh, meshToModel));
            foreach (var v in mf.sharedMesh.vertices) points.Add(meshToModel.MultiplyPoint3x4(v));
        }
        return points;
    }

    /// <summary>
    /// Axis-aligned bounds of everything a model renders, in the model root's
    /// own space (its local axes, unscaled), wherever the root sits and however
    /// it is scaled. Each mesh's bounds box is carried into root space through
    /// the transform matrices, which is exactly what Renderer.bounds gives for
    /// a root at the world origin, without having to move the root there.
    /// </summary>
    static Bounds MeasureModelBounds(GameObject root)
    {
        var toRoot = root.transform.worldToLocalMatrix;
        var bounds = new Bounds();
        bool any = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            Mesh mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                : r.TryGetComponent(out MeshFilter mf) ? mf.sharedMesh : null;
            if (mesh == null) continue;
            var meshToRoot = toRoot * r.transform.localToWorldMatrix;
            var mb = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                var p = meshToRoot.MultiplyPoint3x4(corner);
                if (any) bounds.Encapsulate(p);
                else { bounds = new Bounds(p, Vector3.zero); any = true; }
            }
        }
        return bounds;
    }

    /// <summary>A named child at a local position with no rotation or scale,
    /// created if missing.</summary>
    static Transform Child(Transform parent, string name, Vector3 localPosition)
    {
        var t = parent.Find(name);
        if (t == null)
        {
            t = new GameObject(name).transform;
            t.SetParent(parent, false);
        }
        t.SetLocalPositionAndRotation(localPosition, Quaternion.identity);
        t.localScale = Vector3.one;
        return t;
    }

    /// <summary>
    /// Marks objects changed through their C# API as dirty and, when they
    /// belong to a prefab instance (the Chibi in every saved scene), records
    /// the change as an instance override. Without the record, an edit to a
    /// prefab-owned object made outside a SerializedObject can be dropped on
    /// the next save or reload. Harmless on objects outside a prefab.
    /// </summary>
    public static void RecordInstanceChanges(params Object[] objects)
    {
        foreach (var o in objects)
        {
            if (o == null) continue;
            PrefabUtility.RecordPrefabInstancePropertyModifications(o);
            EditorUtility.SetDirty(o);
        }
    }
}
