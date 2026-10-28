using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Pickleball.Art;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Pickleball.EditorTools.Tripo
{
    /// <summary>
    /// Turns Tripo exports into game-ready prefabs, driven entirely by the rows of
    /// the <see cref="TripoAssetCatalog"/>. Tools ▸ Pickleball ▸ Tripo ▸ Import
    /// Tripo Assets, or headless:
    /// <c>-executeMethod Pickleball.EditorTools.Tripo.TripoAssetImporter.Batch [-tripoSource &lt;extracted dir&gt;]</c>.
    ///
    /// Per row it:
    /// 1. <b>Stages</b> (only when given an extracted-downloads folder): copies the
    ///    newest extraction of <c>&lt;source&gt;/&lt;id&gt;/</c> to
    ///    <c>Art/Tripo/&lt;Category&gt;/&lt;Name&gt;/&lt;Name&gt;.fbx</c> and
    ///    <c>&lt;Name&gt;_BaseColor.jpg</c>. Only FBX and image files are accepted.
    /// 2. <b>Imports the textures</b>: mip maps, the row's size cap, ASTC on Android and iOS.
    /// 3. <b>Imports the model</b>: Read/Write off, imported normals, tangents only with a
    ///    normal map, no animation, cameras, lights or colliders, and an import scale
    ///    fitted so the model measures the row's target size.
    /// 4. <b>Builds one URP Lit material</b> and remaps every material slot of the FBX to
    ///    it, so code that instantiates the FBX directly (NewtonFightArt's apple) gets
    ///    it too.
    /// 5. <b>Builds the prefab</b> in Prefabs/Tripo: the model under a "Model" child whose
    ///    offset puts the origin at the row's pivot, the row's shadow mode, and a box
    ///    collider only for solid props.
    /// 6. <b>Checks the budget</b> and writes a report to Logs/TripoImport.md.
    ///
    /// Idempotent: every step compares before it writes, so a second run changes
    /// no file. It never opens, edits or saves a scene and touches nothing outside
    /// Art/Tripo, Prefabs/Tripo and the catalog.
    /// </summary>
    public static class TripoAssetImporter
    {
        const string MenuPath = "Tools/Pickleball/Tripo/Import Tripo Assets…";
        const string LitShader = "Universal Render Pipeline/Lit";
        const string ParticlesLitShader = "Universal Render Pipeline/Particles/Lit";
        const string SourcePrefKey = "Pickleball.Tripo.LastSourceFolder";
        const string ModelChild = "Model";
        static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png" };
        static readonly string[] MobilePlatforms = { "Android", "iPhone" };
        static readonly Regex VersionSuffix = new(@"_v(\d+)$", RegexOptions.IgnoreCase);

        /// <summary>One asset's outcome, for the log and the report file.</summary>
        public sealed class AssetReport
        {
            /// <summary>The catalog row.</summary>
            public TripoAssetSpec Spec;
            /// <summary>Whether a model was found and processed.</summary>
            public bool Imported;
            /// <summary>Triangles in the imported model.</summary>
            public int Triangles;
            /// <summary>Prefab-space bounds size, metres.</summary>
            public Vector3 Size;
            /// <summary>Imported base colour texture size in pixels, 0 if none.</summary>
            public int TextureSize;
            /// <summary>The model's import scale factor.</summary>
            public float ImportScale;
            /// <summary>Budget and pipeline problems for this asset.</summary>
            public readonly List<string> Notes = new();
            /// <summary>Whether a note is an error (over the hard cap, missing input).</summary>
            public bool HasError;
        }

        // ---------------------------------------------------------------- entry points

        [MenuItem(MenuPath)]
        static void Menu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Tripo import: leave Play mode first.");
                return;
            }
            string last = EditorPrefs.GetString(SourcePrefKey, "");
            string source = EditorUtility.OpenFolderPanel(
                "Extracted Tripo downloads (Cancel = re-process what is already imported)", last, "");
            if (!string.IsNullOrEmpty(source)) EditorPrefs.SetString(SourcePrefKey, source);
            ImportAll(string.IsNullOrEmpty(source) ? null : source);
        }

        /// <summary>
        /// Headless entry. Reads <c>-tripoSource &lt;dir&gt;</c> (optional) and exits 1
        /// when any asset reported an error, so a batch run's exit code means
        /// something (unlike the older setup menus).
        /// </summary>
        public static void Batch()
        {
            int errors;
            try
            {
                errors = ImportAll(CommandLineValue("-tripoSource")).Count(r => r.HasError);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                errors = 1;
            }
            EditorApplication.Exit(errors > 0 ? 1 : 0);
        }

        /// <summary>
        /// Runs the whole pipeline over every catalog row. <paramref name="sourceRoot"/>
        /// is the extracted-downloads folder to stage from, or null to re-process
        /// what is already in Art/Tripo.
        /// </summary>
        public static List<AssetReport> ImportAll(string sourceRoot)
        {
            var catalog = EnsureCatalog();
            var reports = catalog.Assets.Where(s => s != null)
                                 .Select(s => new AssetReport { Spec = s }).ToList();

            if (!string.IsNullOrEmpty(sourceRoot))
            {
                if (!Directory.Exists(sourceRoot))
                    throw new DirectoryNotFoundException($"Tripo source folder not found: {sourceRoot}");
                bool sourceChanged = false;
                foreach (var report in reports) sourceChanged |= Stage(sourceRoot, report);
                if (sourceChanged)
                {
                    EditorUtility.SetDirty(catalog);
                    AssetDatabase.SaveAssetIfDirty(catalog);
                }
                // Files were copied behind the AssetDatabase's back; a no-op if none were.
                AssetDatabase.Refresh();
            }

            foreach (var report in reports)
            {
                try
                {
                    Process(report);
                }
                catch (Exception e)
                {
                    report.HasError = true;
                    report.Notes.Add($"import failed: {e.Message}");
                    Debug.LogException(e);
                }
            }

            AssetDatabase.SaveAssets();
            WriteReport(reports);
            return reports;
        }

        // ---------------------------------------------------------------- catalog

        /// <summary>Loads the catalog, creating it on first use, and appends any
        /// seeded row it lacks. Existing rows are never changed.</summary>
        public static TripoAssetCatalog EnsureCatalog()
        {
            EnsureFolder(TripoAssetCatalog.ArtRoot);
            var catalog = AssetDatabase.LoadAssetAtPath<TripoAssetCatalog>(TripoAssetCatalog.AssetPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<TripoAssetCatalog>();
                AssetDatabase.CreateAsset(catalog, TripoAssetCatalog.AssetPath);
            }
            bool added = false;
            foreach (var row in TripoCatalogSeed.All()) added |= catalog.AddIfMissing(row);
            if (added)
            {
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssetIfDirty(catalog);
            }
            return catalog;
        }

        // ---------------------------------------------------------------- 1. staging

        /// <summary>Copies the newest extraction for this row into its art folder.
        /// Returns whether the catalog row changed (its recorded source).</summary>
        static bool Stage(string sourceRoot, AssetReport report)
        {
            var spec = report.Spec;
            string dir = Path.Combine(sourceRoot, spec.Id);
            if (!Directory.Exists(dir)) return false;

            string chosen = NewestExtraction(dir);
            if (chosen == null)
            {
                report.Notes.Add($"no FBX under {dir}");
                return false;
            }

            var files = Directory.GetFiles(chosen, "*", SearchOption.AllDirectories);
            var strays = files.Where(f => !IsModel(f) && !IsImage(f)).ToArray();
            if (strays.Length > 0)
            {
                // Downloads are untrusted: anything that is not a model or a picture
                // means the export is not what we think it is.
                report.HasError = true;
                report.Notes.Add("download holds unexpected files, not staged: " +
                                 string.Join(", ", strays.Select(Path.GetFileName)));
                return false;
            }

            var models = files.Where(IsModel).ToArray();
            if (models.Length != 1)
            {
                report.HasError = true;
                report.Notes.Add($"expected one FBX in {chosen}, found {models.Length}");
                return false;
            }

            var images = files.Where(IsImage).ToArray();
            var normal = images.FirstOrDefault(f => Path.GetFileName(f).ToLowerInvariant().Contains("normal"));
            var colours = images.Where(f => f != normal).ToArray();
            if (colours.Length > 1)
            {
                report.HasError = true;
                report.Notes.Add($"{colours.Length} colour textures: a multi-part export. Merge the parts " +
                                 "into one mesh and one texture before importing (asset plan 3.4).");
                return false;
            }

            foreach (var f in images.Append(models[0]))
            {
                if (!HasExpectedSignature(f))
                {
                    report.HasError = true;
                    report.Notes.Add($"{Path.GetFileName(f)} is not the file type its name says; not staged");
                    return false;
                }
            }

            EnsureFolder(spec.Folder);
            CopyIfChanged(models[0], spec.ModelPath);
            if (spec.HasTexture)
            {
                if (colours.Length == 0)
                {
                    report.HasError = true;
                    report.Notes.Add("the row expects a texture but the download has none");
                }
                else
                {
                    CopyIfChanged(colours[0], $"{spec.Folder}/{spec.BaseColorStem}{Path.GetExtension(colours[0]).ToLowerInvariant()}");
                }
            }
            else if (colours.Length > 0)
            {
                report.Notes.Add("download has a texture the row does not use (Texture Max Size 0)");
            }
            if (normal != null)
                CopyIfChanged(normal, $"{spec.Folder}/{spec.NormalStem}{Path.GetExtension(normal).ToLowerInvariant()}");

            string source = Path.GetFileName(chosen.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (spec.Source == source) return false;
            spec.SetSource(source);
            return true;
        }

        /// <summary>The generator re-exports retries as name_v2, name_v3…; each zip is
        /// extracted to its own folder. The highest version wins. A folder that holds
        /// an FBX directly is used as is.</summary>
        static string NewestExtraction(string idDir)
        {
            if (Directory.GetFiles(idDir, "*.fbx").Length > 0) return idDir;
            return Directory.GetDirectories(idDir)
                            .Where(d => Directory.GetFiles(d, "*.fbx", SearchOption.TopDirectoryOnly).Length > 0)
                            .OrderBy(d => Version(Path.GetFileName(d)))
                            .ThenBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                            .LastOrDefault();
        }

        static int Version(string name)
        {
            var m = VersionSuffix.Match(name);
            return m.Success ? int.Parse(m.Groups[1].Value) : 1;
        }

        static bool IsModel(string path) => Path.GetExtension(path).Equals(".fbx", StringComparison.OrdinalIgnoreCase);

        static bool IsImage(string path) =>
            ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        static bool HasExpectedSignature(string path)
        {
            var head = new byte[20];
            using (var s = File.OpenRead(path)) s.Read(head, 0, head.Length);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".fbx" => Encoding.ASCII.GetString(head, 0, 18) == "Kaydara FBX Binary"
                          || Encoding.ASCII.GetString(head).TrimStart().StartsWith(";"),
                ".png" => head[0] == 0x89 && head[1] == (byte)'P' && head[2] == (byte)'N' && head[3] == (byte)'G',
                _ => head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF,
            };
        }

        static void CopyIfChanged(string from, string assetPath)
        {
            string to = FullPath(assetPath);
            if (File.Exists(to) && File.ReadAllBytes(to).SequenceEqual(File.ReadAllBytes(from)))
                return;
            File.Copy(from, to, overwrite: true);
        }

        // ---------------------------------------------------------------- 2-6. processing

        static void Process(AssetReport report)
        {
            var spec = report.Spec;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath) == null)
            {
                report.HasError = true;
                report.Notes.Add($"no model at {spec.ModelPath} (stage it from the extracted downloads)");
                return;
            }

            var baseMap = ConfigureTexture(spec, spec.BaseColorStem, isNormal: false);
            var normalMap = ConfigureTexture(spec, spec.NormalStem, isNormal: true);
            if (spec.HasTexture && baseMap == null)
            {
                report.HasError = true;
                report.Notes.Add($"no {spec.BaseColorStem} texture in {spec.Folder}");
            }

            ConfigureModel(spec, normalMap != null);
            report.ImportScale = FitScale(spec);
            var material = BuildMaterial(spec, baseMap, normalMap);
            RemapMaterials(spec, material);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
            var bounds = ModelBounds(spec, model);
            BuildPrefab(spec, model, material, bounds);

            report.Imported = true;
            report.Triangles = TripoFit.CountTriangles(model);
            report.Size = bounds.size;
            report.TextureSize = baseMap != null ? Mathf.Max(baseMap.width, baseMap.height) : 0;

            if (report.Triangles > spec.TriangleCap)
            {
                report.HasError = true;
                report.Notes.Add($"{report.Triangles} triangles is over the hard cap of {spec.TriangleCap}: regenerate at a lower polycount");
            }
            else if (report.Triangles > spec.TriangleBudget)
            {
                report.Notes.Add($"{report.Triangles} triangles is over the {spec.TriangleBudget} budget (under the {spec.TriangleCap} cap)");
            }
            if (spec.HasTexture && report.TextureSize > spec.TextureMaxSize)
                report.Notes.Add($"texture imports at {report.TextureSize}, above the {spec.TextureMaxSize} cap");
            if (spec.Rig == TripoRig.Humanoid) CheckAvatar(spec, report);
            if (spec.GenerateMeshLods) ReportMeshLods(model, report);
        }

        /// <summary>Lists the triangles of each generated Mesh LOD level, the number
        /// that matters for meshes spawned in crowds.</summary>
        static void ReportMeshLods(GameObject model, AssetReport report)
        {
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                var levels = new List<string>();
                for (int lod = 0; lod < mesh.lodCount; lod++)
                {
                    int tris = 0;
                    for (int s = 0; s < mesh.subMeshCount; s++) tris += (int)(mesh.GetLod(s, lod).indexCount / 3);
                    levels.Add(tris.ToString());
                }
                report.Notes.Add(mesh.lodCount > 1
                    ? $"Mesh LODs: {string.Join(" / ", levels)} triangles"
                    : "Mesh LODs requested but none generated");
            }
        }

        /// <summary>A humanoid row must import a valid human avatar with every
        /// required bone mapped; the mapping count goes in the report.</summary>
        static void CheckAvatar(TripoAssetSpec spec, AssetReport report)
        {
            var avatar = AssetDatabase.LoadAllAssetsAtPath(spec.ModelPath).OfType<Avatar>().FirstOrDefault();
            var mapped = avatar != null ? avatar.humanDescription.human ?? Array.Empty<HumanBone>()
                                        : Array.Empty<HumanBone>();
            var names = new HashSet<string>(mapped.Select(b => b.humanName));
            var missing = HumanTrait.BoneName.Where((n, i) => HumanTrait.RequiredBone(i) && !names.Contains(n)).ToArray();
            if (avatar == null || !avatar.isValid || !avatar.isHuman || missing.Length > 0)
            {
                report.HasError = true;
                report.Notes.Add($"humanoid avatar invalid (missing: {string.Join(", ", missing)})");
                return;
            }
            int fingers = names.Count(n => n.Contains("Proximal") || n.Contains("Intermediate") || n.Contains("Distal"));
            report.Notes.Add($"humanoid avatar valid: {names.Count} bones mapped ({fingers} finger bones)");
        }

        /// <summary>Base colour (sRGB) or normal map: mip maps on, Read/Write off,
        /// capped at the row's size, block-compressed on desktop and ASTC 6x6 on
        /// Android and iOS. Returns null when the folder has no such texture.</summary>
        static Texture2D ConfigureTexture(TripoAssetSpec spec, string stem, bool isNormal)
        {
            string path = ImageExtensions.Select(e => $"{spec.Folder}/{stem}{e}")
                                         .FirstOrDefault(p => AssetImporter.GetAtPath(p) is TextureImporter);
            if (path == null) return null;

            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            int max = Mathf.Max(32, spec.HasTexture ? spec.TextureMaxSize : 512);
            bool changed = false;
            changed |= Set(ti.textureType, isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default, v => ti.textureType = v);
            changed |= Set(ti.sRGBTexture, !isNormal, v => ti.sRGBTexture = v);
            changed |= Set(ti.alphaSource, TextureImporterAlphaSource.None, v => ti.alphaSource = v);
            changed |= Set(ti.mipmapEnabled, true, v => ti.mipmapEnabled = v);
            changed |= Set(ti.isReadable, false, v => ti.isReadable = v);
            changed |= Set(ti.maxTextureSize, max, v => ti.maxTextureSize = v);
            changed |= Set(ti.textureCompression, TextureImporterCompression.Compressed, v => ti.textureCompression = v);
            // The grade is read back by TripoTexturePostprocessor; normal maps are never graded.
            string grade = isNormal ? "" : spec.TextureGrade.Encode();
            changed |= Set(ti.userData ?? "", grade, v => ti.userData = v);

            foreach (string platform in MobilePlatforms)
            {
                var s = ti.GetPlatformTextureSettings(platform);
                if (s.overridden && s.maxTextureSize == max && s.format == TextureImporterFormat.ASTC_6x6) continue;
                s.overridden = true;
                s.maxTextureSize = max;
                s.format = TextureImporterFormat.ASTC_6x6;
                s.compressionQuality = 50;
                ti.SetPlatformTextureSettings(s);
                changed = true;
            }

            if (changed) ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Static-prop import settings. The scale factor is left to <see cref="FitScale"/>.</summary>
        static void ConfigureModel(TripoAssetSpec spec, bool hasNormalMap)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(spec.ModelPath);
            bool changed = false;
            changed |= Set(mi.useFileScale, true, v => mi.useFileScale = v);
            // Bake the Blender-preset axis conversion into the mesh, so the model's
            // root has an identity rotation and "up" is Y in every child.
            changed |= Set(mi.bakeAxisConversion, true, v => mi.bakeAxisConversion = v);
            changed |= Set(mi.isReadable, false, v => mi.isReadable = v);
            changed |= Set(mi.importNormals, ModelImporterNormals.Import, v => mi.importNormals = v);
            // Tangents only feed normal mapping; without a normal map they are memory for nothing.
            changed |= Set(mi.importTangents, hasNormalMap ? ModelImporterTangents.CalculateMikk : ModelImporterTangents.None,
                           v => mi.importTangents = v);
            changed |= Set(mi.meshCompression, ModelImporterMeshCompression.Off, v => mi.meshCompression = v);
            changed |= Set(mi.optimizeMeshPolygons, true, v => mi.optimizeMeshPolygons = v);
            changed |= Set(mi.optimizeMeshVertices, true, v => mi.optimizeMeshVertices = v);
            changed |= Set(mi.weldVertices, true, v => mi.weldVertices = v);
            changed |= Set(mi.importBlendShapes, false, v => mi.importBlendShapes = v);
            changed |= Set(mi.importVisibility, false, v => mi.importVisibility = v);
            changed |= Set(mi.importCameras, false, v => mi.importCameras = v);
            changed |= Set(mi.importLights, false, v => mi.importLights = v);
            changed |= Set(mi.addCollider, false, v => mi.addCollider = v);
            changed |= Set(mi.generateSecondaryUV, false, v => mi.generateSecondaryUV = v);
            changed |= Set(mi.importAnimation, false, v => mi.importAnimation = v);
            changed |= Set(mi.generateMeshLods, spec.GenerateMeshLods, v => mi.generateMeshLods = v);
            // Read back by TripoModelPostprocessor, which bakes the gradient into vertex colours.
            changed |= Set(mi.userData ?? "", spec.VertexGradient.Encode(), v => mi.userData = v);
            if (spec.Rig == TripoRig.Humanoid)
            {
                // Unity's auto-mapper recognises Tripo's Mixamo rig preset by name.
                changed |= Set(mi.animationType, ModelImporterAnimationType.Human, v => mi.animationType = v);
                changed |= Set(mi.avatarSetup, ModelImporterAvatarSetup.CreateFromThisModel, v => mi.avatarSetup = v);
                changed |= Set(mi.optimizeGameObjects, false, v => mi.optimizeGameObjects = v);
                changed |= Set(mi.skinWeights, ModelImporterSkinWeights.Standard, v => mi.skinWeights = v);
            }
            else
            {
                changed |= Set(mi.animationType, ModelImporterAnimationType.None, v => mi.animationType = v);
            }
            // Materials must be imported for the remap in RemapMaterials to have slots to map.
            if (mi.materialImportMode == ModelImporterMaterialImportMode.None)
            {
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                changed = true;
            }
            if (changed) mi.SaveAndReimport();
        }

        /// <summary>Sets the scale factor so the model measures the row's target
        /// size along its axis. Scale is linear, so one measure-and-correct pass is
        /// exact; the next run measures the target and changes nothing.</summary>
        static float FitScale(TripoAssetSpec spec)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(spec.ModelPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
            float measured = TripoFit.Measure(ModelBounds(spec, model).size, spec.SizeAxis);
            float wanted = TripoFit.ImportScale(mi.globalScale, measured, spec.TargetSize);
            if (Mathf.Abs(wanted - mi.globalScale) > mi.globalScale * 1e-5f)
            {
                mi.globalScale = wanted;
                mi.SaveAndReimport();
            }
            return mi.globalScale;
        }

        /// <summary>Bounds of the model asset after the row's rotation, in the space
        /// of the prefab's "Model" holder before its offset.</summary>
        static Bounds ModelBounds(TripoAssetSpec spec, GameObject model) =>
            TripoFit.MeshBounds(model, Matrix4x4.Rotate(Quaternion.Euler(spec.ModelRotation)));

        /// <summary>URP Lit, or URP Particles/Lit (opaque) for a row with a vertex
        /// gradient: it multiplies vertex colour into the albedo, URP Lit does not.</summary>
        static Material BuildMaterial(TripoAssetSpec spec, Texture2D baseMap, Texture2D normalMap)
        {
            bool gradient = !spec.VertexGradient.IsNeutral;
            string shaderName = gradient ? ParticlesLitShader : LitShader;
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException($"Shader '{shaderName}' not found; is URP installed?");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(spec.MaterialPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = spec.AssetName };
                AssetDatabase.CreateAsset(mat, spec.MaterialPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            mat.SetTexture("_BaseMap", baseMap);
            mat.SetColor("_BaseColor", spec.BaseColor);
            mat.SetFloat("_Smoothness", spec.Smoothness);
            mat.SetFloat("_Metallic", spec.Metallic);
            if (gradient)
            {
                // Opaque, back-face culled, vertex colour multiplied: the particle
                // shader's defaults are transparent and two-sided.
                mat.SetFloat("_Surface", 0f);
                mat.SetFloat("_ColorMode", 0f);
                mat.SetFloat("_Cull", (float)CullMode.Back);
                mat.SetFloat("_ZWrite", 1f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
                mat.SetOverrideTag("RenderType", "Opaque");
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)RenderQueue.Geometry;
            }

            mat.SetTexture("_BumpMap", normalMap);
            if (normalMap != null) mat.EnableKeyword("_NORMALMAP");
            else mat.DisableKeyword("_NORMALMAP");

            if (spec.Emits)
            {
                mat.SetColor("_EmissionColor", spec.EmissionColor);
                mat.SetTexture("_EmissionMap", baseMap);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.SetColor("_EmissionColor", Color.black);
                mat.SetTexture("_EmissionMap", null);
                mat.DisableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
            return mat;
        }

        /// <summary>Points every material slot of the FBX at the asset's material.</summary>
        static void RemapMaterials(TripoAssetSpec spec, Material material)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(spec.ModelPath);
            bool changed = false;
            foreach (var pair in mi.GetExternalObjectMap())
            {
                if (pair.Key.type != typeof(Material) || pair.Value == material) continue;
                mi.AddRemap(pair.Key, material);
                changed = true;
            }
            foreach (var embedded in AssetDatabase.LoadAllAssetsAtPath(spec.ModelPath).OfType<Material>())
            {
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), material);
                changed = true;
            }
            if (changed) mi.SaveAndReimport();
        }

        /// <summary>
        /// Writes or updates the prefab in place. Updating an existing prefab through
        /// LoadPrefabContents keeps every object's file id, so an unchanged asset
        /// saves byte-identical YAML and re-runs leave git clean.
        /// </summary>
        static void BuildPrefab(TripoAssetSpec spec, GameObject model, Material material, Bounds bounds)
        {
            EnsureFolder(TripoAssetCatalog.PrefabRoot);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath) == null)
            {
                var seed = new GameObject(spec.AssetName);
                PrefabUtility.SaveAsPrefabAsset(seed, spec.PrefabPath);
                Object.DestroyImmediate(seed);
            }

            var root = PrefabUtility.LoadPrefabContents(spec.PrefabPath);
            try
            {
                // Holder: carries the pivot offset and the row's rotation, so the
                // model instance itself keeps the FBX's own transform untouched.
                var holder = root.transform.Find(ModelChild);
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                {
                    var child = root.transform.GetChild(i);
                    if (child != holder) Object.DestroyImmediate(child.gameObject);
                }
                if (holder == null)
                {
                    holder = new GameObject(ModelChild).transform;
                    holder.SetParent(root.transform, false);
                }
                var offset = TripoFit.PivotOffset(bounds, spec.Pivot);
                holder.localPosition = offset;
                holder.localRotation = Quaternion.Euler(spec.ModelRotation);
                holder.localScale = Vector3.one;

                Transform instance = holder.childCount == 1 ? holder.GetChild(0) : null;
                if (instance == null ||
                    PrefabUtility.GetCorrespondingObjectFromOriginalSource(instance.gameObject) != model)
                {
                    for (int i = holder.childCount - 1; i >= 0; i--)
                        Object.DestroyImmediate(holder.GetChild(i).gameObject);
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(model, holder);
                    instance = go.transform;
                }
                instance.localPosition = model.transform.localPosition;
                instance.localRotation = model.transform.localRotation;
                instance.localScale = model.transform.localScale;

                foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
                {
                    var mode = spec.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    if (r.shadowCastingMode != mode) r.shadowCastingMode = mode;
                    if (!r.receiveShadows) r.receiveShadows = true;
                    var mats = r.sharedMaterials;
                    if (mats.Any(m => m != material))
                        r.sharedMaterials = Enumerable.Repeat(material, Mathf.Max(1, mats.Length)).ToArray();
                }

                ApplyCollider(spec, root, bounds, offset);
                PrefabUtility.SaveAsPrefabAsset(root, spec.PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void ApplyCollider(TripoAssetSpec spec, GameObject root, Bounds bounds, Vector3 offset)
        {
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
            {
                bool keep = spec.ColliderShape == TripoColliderShape.Box && c is BoxCollider && c.gameObject == root;
                if (!keep) Object.DestroyImmediate(c);
            }
            if (spec.ColliderShape != TripoColliderShape.Box) return;

            if (!root.TryGetComponent(out BoxCollider box)) box = root.AddComponent<BoxCollider>();
            var center = bounds.center + offset;
            if (box.center != center) box.center = center;
            if (box.size != bounds.size) box.size = bounds.size;
        }

        // ---------------------------------------------------------------- report

        static void WriteReport(List<AssetReport> reports)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Tripo import report");
            sb.AppendLine();
            sb.AppendLine("| Id | Asset | Source | Triangles (budget / cap) | Size W×H×D (m) | Texture | Scale | Notes |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var r in reports)
            {
                var s = r.Spec;
                string tris = r.Imported ? $"{r.Triangles:N0} ({s.TriangleBudget:N0} / {s.TriangleCap:N0})" : "—";
                string size = r.Imported ? $"{r.Size.x:0.###} × {r.Size.y:0.###} × {r.Size.z:0.###}" : "—";
                string tex = r.TextureSize > 0 ? r.TextureSize.ToString() : "none";
                string notes = r.Notes.Count == 0 ? "ok" : string.Join("; ", r.Notes);
                sb.AppendLine($"| {s.Id} | {s.AssetName} | {s.Source} | {tris} | {size} | {tex} | {r.ImportScale:0.#####} | {(r.HasError ? "**ERROR** " : "")}{notes} |");
            }
            string text = sb.ToString();
            string path = Path.Combine(ProjectRoot, "Logs", "TripoImport.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);

            bool error = reports.Any(r => r.HasError);
            string message = $"Tripo import: {reports.Count(r => r.Imported)}/{reports.Count} assets processed. Report: {path}\n{text}";
            if (error) Debug.LogError(message);
            else Debug.Log(message);
        }

        // ---------------------------------------------------------------- helpers

        static string ProjectRoot => Directory.GetParent(Application.dataPath)!.FullName;

        static string FullPath(string assetPath) => Path.Combine(ProjectRoot, assetPath);

        static bool Set<T>(T current, T wanted, Action<T> apply)
        {
            if (EqualityComparer<T>.Default.Equals(current, wanted)) return false;
            apply(wanted);
            return true;
        }

        static void EnsureFolder(string dir)
        {
            if (AssetDatabase.IsValidFolder(dir)) return;
            string parent = Path.GetDirectoryName(dir)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(dir));
        }

        /// <summary>The value after a command-line flag, or null.</summary>
        public static string CommandLineValue(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
