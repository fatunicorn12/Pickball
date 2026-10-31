using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Pickleball.Cutscene.Actors;
using Pickleball.Sandbox.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pickleball.Cutscene.Editor.Actors
{
    /// <summary>
    /// The characters workstream's two setup steps.
    /// <list type="bullet">
    /// <item><b>1 Build Actors</b>: importer settings for the propeller, eye variants and Mixamo
    /// copies; the configs (seeded once); every hand-keyed clip; MC_Actor, Birdy_Actor and
    /// Propeller prefabs; both action libraries.</item>
    /// <item><b>4b Rig Scene Actors</b>: in the open scene, the gameplay Player becomes MC's slot
    /// and every placed actor prefab is re-rigged. This is also the model-swap path.</item>
    /// </list>
    /// Both are idempotent: a second run writes the same values (and the same files).
    /// </summary>
    public static class CutsceneActorSetup
    {
        private const string McModelChild = "ChibiModel";
        private const string BirdyModelChild = "BirdyModel";
        private static readonly Vector3 BirdyModelOffset = new Vector3(0f, 0.008f, 0f); // Birdy.prefab's ModelYOffset

        /// <summary>Menu 1.</summary>
        [MenuItem(CutsceneMenus.BuildActors)]
        public static void BuildActors()
        {
            var report = new StringBuilder("Cutscene ▸ 1 Build Actors\n");
            EnsureFolders();
            ConfigurePropellerImporter(report);
            ConfigureEyeTextures(report);
            ConfigureMixamoCopies(report);

            var propConfig = SeedAsset<PropellerConfig>(CutsceneActorPaths.PropellerConfig);
            var mcFlap = SeedAsset<MouthFlapConfig>(CutsceneActorPaths.McMouthFlap);
            var birdyFlap = SeedAsset<MouthFlapConfig>(CutsceneActorPaths.BirdyMouthFlap, cfg =>
            {
                var so = new SerializedObject(cfg);
                so.FindProperty("maxAngle").floatValue = 9f;      // a bird's whole-head beak flap is bigger
                so.FindProperty("attackRate").floatValue = 40f;
                so.ApplyModifiedPropertiesWithoutUndo();
            });

            var mcClips = BakeAll(McClips.All(), report);
            var birdyClips = BakeAll(BirdyClips.All(), report);
            // Reused clips play through cutscene-owned in-place copies; the sources (Movement's and
            // Birdy's clips) keep their own import settings and are never written.
            foreach (var c in BakeAll(InPlaceCopies(CutsceneClipCatalog.McReuse(), CutsceneActorPaths.ChibiModel,
                         CutsceneActorPaths.McClipDir), report)) mcClips[c.Key] = c.Value;
            foreach (var c in BakeAll(InPlaceCopies(CutsceneClipCatalog.BirdyReuse(), CutsceneActorPaths.BirdyModel,
                         CutsceneActorPaths.BirdyCutsceneClipDir), report)) birdyClips[c.Key] = c.Value;

            report.AppendLine(BuildActorPrefab(CutsceneActorPaths.McActorPrefab, "MC_Actor", CutsceneActorPaths.ChibiModel,
                McModelChild, Vector3.zero, CutsceneSpeakers.MC, mcFlap, propConfig));
            report.AppendLine(BuildActorPrefab(CutsceneActorPaths.BirdyActorPrefab, "Birdy_Actor", CutsceneActorPaths.BirdyModel,
                BirdyModelChild, BirdyModelOffset, CutsceneSpeakers.Birdy, birdyFlap, propConfig));
            report.AppendLine(BuildPropellerPrefab(propConfig));

            BuildLibrary(CutsceneActorPaths.McLibrary, CutsceneSpeakers.MC, CutsceneClipCatalog.McIdleVerb, "MC",
                McClips.All(), mcClips, CutsceneClipCatalog.McReuse(), CutsceneClipCatalog.McMissing, report);
            BuildLibrary(CutsceneActorPaths.BirdyLibrary, CutsceneSpeakers.Birdy, CutsceneClipCatalog.BirdyIdleVerb, "Birdy",
                BirdyClips.All(), birdyClips, CutsceneClipCatalog.BirdyReuse(), CutsceneClipCatalog.BirdyMissing, report);

            Debug.Log(report.ToString());
        }

        /// <summary>Menu 4b.</summary>
        [MenuItem(CutsceneMenus.RigSceneActors)]
        public static void RigSceneActors()
        {
            var report = new StringBuilder("Cutscene ▸ 4b Rig Scene Actors\n");
            var propConfig = AssetDatabase.LoadAssetAtPath<PropellerConfig>(CutsceneActorPaths.PropellerConfig);
            var mcFlap = AssetDatabase.LoadAssetAtPath<MouthFlapConfig>(CutsceneActorPaths.McMouthFlap);
            var birdyFlap = AssetDatabase.LoadAssetAtPath<MouthFlapConfig>(CutsceneActorPaths.BirdyMouthFlap);
            if (propConfig == null || mcFlap == null || birdyFlap == null)
            {
                Debug.LogError("4 Rig Scene Actors: run 1 Build Actors first (configs missing).");
                return;
            }

            // The gameplay Player plays MC in the academy scenes and the void. MovementMotor exists
            // only on it; found at editor time, never at runtime.
            var players = Object.FindObjectsByType<MovementMotor>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            foreach (MovementMotor motor in players)
            {
                var animator = motor.GetComponentInChildren<Animator>(true);
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                {
                    report.AppendLine($"{motor.name}: no humanoid Animator; skipped");
                    continue;
                }
                FindBlade(animator, out Transform bladeParent, out Vector3 bladePos, out Quaternion bladeRot);
                report.AppendLine(ActorRigger.Rig(motor.gameObject, animator, CutsceneSpeakers.MC, mcFlap, propConfig,
                    bladeParent, bladePos, bladeRot) + (bladeParent != null ? ", blade at the guard" : ", no blade found (Blade at the hand)"));
            }

            // Placed actor prefabs (and anything else with a slot that isn't the Player).
            foreach (CutsceneActor actor in Object.FindObjectsByType<CutsceneActor>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                if (actor.GetComponent<MovementMotor>() != null) continue;
                var animator = actor.GetComponentInChildren<Animator>(true);
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                {
                    report.AppendLine($"{actor.name}: no humanoid Animator; skipped");
                    continue;
                }
                bool birdy = actor.ActorId == CutsceneSpeakers.Birdy;
                report.AppendLine(ActorRigger.Rig(actor.gameObject, animator, actor.ActorId, birdy ? birdyFlap : mcFlap,
                    propConfig, null, Vector3.zero, Quaternion.identity));
            }

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log(report.ToString());
        }

        // ---------------------------------------------------------------- blade

        /// <summary>
        /// The compass-blade's guard: the far end of the handle from the grip, along the grip
        /// anchor's +Y (the blade direction), measured from the held weapon's mesh bounds.
        /// WeaponSetup names: R_Hand/WeaponSocket/WeaponVisual/GripAnchor and WeaponSocket/BladeBase.
        /// </summary>
        public static void FindBlade(Animator animator, out Transform parent, out Vector3 position, out Quaternion rotation)
        {
            parent = null;
            position = Vector3.zero;
            rotation = Quaternion.identity;
            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform socket = hand != null ? hand.Find("WeaponSocket") : null;
            if (socket == null) return;
            Transform grip = socket.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "GripAnchor");
            if (grip == null)
            {
                Transform bladeBase = socket.Find("BladeBase");
                if (bladeBase == null) return;
                parent = socket;
                position = bladeBase.position;
                rotation = bladeBase.rotation;
                return;
            }
            Transform visual = grip.parent;
            float minAlong = 0f;
            foreach (var r in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    float d = Vector3.Dot(r.transform.TransformPoint(c) - grip.position, grip.up);
                    minAlong = Mathf.Min(minAlong, d);
                }
            }
            // The fist closes on the middle of the handle, so the guard is as far above the grip
            // as the held end is below it.
            parent = socket;
            position = grip.position - grip.up * minAlong;
            rotation = grip.rotation;
        }

        // ---------------------------------------------------------------- prefabs

        private static string BuildActorPrefab(string path, string rootName, string modelPath, string modelChild,
            Vector3 modelOffset, string actorId, MouthFlapConfig flap, PropellerConfig prop)
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelAsset == null) return $"{rootName}: no model at {modelPath}";
            bool existing = File.Exists(path);
            GameObject root = existing ? PrefabUtility.LoadPrefabContents(path) : new GameObject(rootName);
            try
            {
                Transform model = root.transform.Find(modelChild);
                if (model != null && PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject) != modelAsset)
                {
                    Object.DestroyImmediate(model.gameObject);
                    model = null;
                }
                if (model == null)
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
                    inst.name = modelChild;
                    model = inst.transform;
                }
                model.localPosition = modelOffset;
                model.localRotation = Quaternion.identity;
                model.localScale = Vector3.one;

                var animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.gameObject.AddComponent<Animator>();
                if (animator.avatar == null) animator.avatar = modelAsset.GetComponent<Animator>()?.avatar;
                animator.runtimeAnimatorController = null; // the timeline drives it
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                string line = ActorRigger.Rig(root, animator, actorId, flap, prop, null, Vector3.zero, Quaternion.identity);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return line;
            }
            finally
            {
                if (existing) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
        }

        private static string BuildPropellerPrefab(PropellerConfig config)
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CutsceneActorPaths.PropellerModel);
            if (modelAsset == null) return "Propeller: no model (run Tools/Blender/cutscene_extract_propeller.py)";
            string path = CutsceneActorPaths.PropellerPrefab;
            bool existing = File.Exists(path);
            GameObject root = existing ? PrefabUtility.LoadPrefabContents(path) : new GameObject("Propeller");
            try
            {
                Transform spin = root.transform.Find("Spin");
                if (spin == null)
                {
                    spin = new GameObject("Spin").transform;
                    spin.SetParent(root.transform, false);
                }
                spin.localPosition = Vector3.zero;
                spin.localRotation = Quaternion.identity;
                Transform model = spin.Find("Model");
                if (model != null && PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject) != modelAsset)
                {
                    Object.DestroyImmediate(model.gameObject);
                    model = null;
                }
                if (model == null)
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, spin);
                    inst.name = "Model";
                    model = inst.transform;
                }
                model.localPosition = Vector3.zero;
                model.localScale = Vector3.one;
                // The FBX may carry an axis-conversion rotation on its root; keep the import's.
                model.localRotation = modelAsset.transform.localRotation;

                var prop = root.GetComponent<PropellerProp>();
                if (prop == null) prop = root.AddComponent<PropellerProp>();
                prop.Configure(config, spin);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Bounds b = ActorPoseSheet.BoundsOf(root);
                return $"Propeller: span {Mathf.Max(b.size.x, b.size.z):0.000} m, height {b.size.y:0.000} m";
            }
            finally
            {
                if (existing) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
        }

        // ---------------------------------------------------------------- clips and libraries

        private static List<PoseClipSpec> InPlaceCopies(List<CutsceneClipCatalog.Reuse> reuse, string model, string folder)
        {
            var specs = new List<PoseClipSpec>();
            var seen = new HashSet<string>();
            foreach (var r in reuse)
                if (!string.IsNullOrEmpty(r.CopyName) && seen.Add(r.CopyName))
                    specs.Add(CutsceneClipCatalog.InPlaceCopy(r, model, folder));
            return specs;
        }

        private static Dictionary<string, AnimationClip> BakeAll(List<PoseClipSpec> specs, StringBuilder report)
        {
            var clips = new Dictionary<string, AnimationClip>();
            foreach (PoseClipSpec spec in specs)
            {
                try
                {
                    clips[spec.Name] = PoseClipBaker.BakeAndSave(spec);
                }
                catch (System.Exception e)
                {
                    report.AppendLine($"clip {spec.Name} FAILED: {e.Message}");
                }
            }
            report.AppendLine($"baked {clips.Count}/{specs.Count} clips");
            return clips;
        }

        private static void BuildLibrary(string path, string actorId, string idleVerb, string character,
            List<PoseClipSpec> specs, Dictionary<string, AnimationClip> baked, List<CutsceneClipCatalog.Reuse> reuse,
            string[] missing, StringBuilder report)
        {
            var byVerb = new Dictionary<string, CutsceneActionLibrary.Entry>();
            var order = new List<string>();
            void Add(string verb, AnimationClip clip, bool loop, float blend)
            {
                if (clip == null || byVerb.ContainsKey(verb)) return;
                byVerb[verb] = new CutsceneActionLibrary.Entry { verb = verb, clip = clip, loop = loop, blendIn = blend };
                order.Add(verb);
            }
            foreach (PoseClipSpec s in specs)
                if (baked.TryGetValue(s.Name, out var c))
                    foreach (string v in s.Verbs) Add(v, c, s.Loop, s.BlendIn);
            foreach (var r in reuse)
            {
                if (baked.TryGetValue(r.CopyName ?? "", out var copy)) Add(r.Verb, copy, r.Loop, r.BlendIn);
                else report.AppendLine($"{character} '{r.Verb}': no in-place copy {r.CopyName}; left unmapped");
            }

            // Shot-list order first, then the extras (aliases).
            var shotVerbs = CutsceneVerbs.FromRepo(character);
            var entries = new List<CutsceneActionLibrary.Entry>();
            foreach (string v in shotVerbs)
                if (byVerb.TryGetValue(v, out var e)) entries.Add(e);
            foreach (string v in order)
                if (!shotVerbs.Contains(v)) entries.Add(byVerb[v]);

            var unmapped = shotVerbs.Where(v => !byVerb.ContainsKey(v) && !missing.Contains(v)).ToList();
            if (unmapped.Count > 0)
                Debug.LogWarning($"{character} verbs with no clip and not on the missing list: {string.Join(", ", unmapped)}");

            var lib = AssetDatabase.LoadAssetAtPath<CutsceneActionLibrary>(path);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<CutsceneActionLibrary>();
                lib.Configure(actorId, idleVerb, entries.ToArray());
                AssetDatabase.CreateAsset(lib, path);
            }
            else lib.Configure(actorId, idleVerb, entries.ToArray());
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssetIfDirty(lib);
            report.AppendLine($"{character} library: {entries.Count} entries for {shotVerbs.Count} shot-list verbs; " +
                              $"missing: {(missing.Length == 0 ? "none" : string.Join(", ", missing))}");
        }

        // ---------------------------------------------------------------- importers

        private static void ConfigurePropellerImporter(StringBuilder report)
        {
            var imp = AssetImporter.GetAtPath(CutsceneActorPaths.PropellerModel) as ModelImporter;
            var birdy = AssetImporter.GetAtPath(CutsceneActorPaths.BirdyModel) as ModelImporter;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(CutsceneActorPaths.BirdyMaterial);
            if (imp == null || birdy == null) return;
            bool changed = false;
            void Set<T>(T current, T wanted, System.Action<T> apply)
            {
                if (EqualityComparer<T>.Default.Equals(current, wanted)) return;
                apply(wanted);
                changed = true;
            }
            // Same scale as Birdy's import, so the prop is exactly his propeller's size.
            Set(imp.globalScale, birdy.globalScale, v => imp.globalScale = v);
            Set(imp.useFileScale, birdy.useFileScale, v => imp.useFileScale = v);
            Set(imp.animationType, ModelImporterAnimationType.None, v => imp.animationType = v);
            Set(imp.importAnimation, false, v => imp.importAnimation = v);
            Set(imp.importCameras, false, v => imp.importCameras = v);
            Set(imp.importLights, false, v => imp.importLights = v);
            Set(imp.isReadable, false, v => imp.isReadable = v);
            Set(imp.materialImportMode, ModelImporterMaterialImportMode.ImportViaMaterialDescription, v => imp.materialImportMode = v);
            if (mat != null)
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), "Material");
                imp.GetExternalObjectMap().TryGetValue(id, out Object current);
                if (current != mat)
                {
                    imp.AddRemap(id, mat);
                    changed = true;
                }
            }
            if (changed)
            {
                imp.SaveAndReimport();
                report.AppendLine("propeller importer updated");
            }
        }

        private static void ConfigureEyeTextures(StringBuilder report)
        {
            CopyTextureSettings(CutsceneActorPaths.ChibiBaseColor, CutsceneActorPaths.ChibiEyesClosed, report);
            CopyTextureSettings(CutsceneActorPaths.BirdyBaseColor, CutsceneActorPaths.BirdyEyesDrop, report);
        }

        /// <summary>The painted copy imports exactly like the original, so swapping never changes resolution or colour space.</summary>
        private static void CopyTextureSettings(string originalPath, string copyPath, StringBuilder report)
        {
            var src = AssetImporter.GetAtPath(originalPath) as TextureImporter;
            var dst = AssetImporter.GetAtPath(copyPath) as TextureImporter;
            if (src == null || dst == null) return;
            var want = new TextureImporterSettings();
            src.ReadTextureSettings(want);
            var have = new TextureImporterSettings();
            dst.ReadTextureSettings(have);
            bool changed = EditorJsonUtility.ToJson(want) != EditorJsonUtility.ToJson(have) ||
                           dst.maxTextureSize != src.maxTextureSize || dst.textureCompression != src.textureCompression ||
                           dst.crunchedCompression != src.crunchedCompression || dst.compressionQuality != src.compressionQuality;
            foreach (string platform in new[] { "Standalone", "Android", "iPhone" })
            {
                var a = src.GetPlatformTextureSettings(platform);
                var b = dst.GetPlatformTextureSettings(platform);
                a.name = b.name = platform;
                if (EditorJsonUtility.ToJson(a) != EditorJsonUtility.ToJson(b)) changed = true;
            }
            if (!changed) return;
            dst.SetTextureSettings(want);
            dst.maxTextureSize = src.maxTextureSize;
            dst.textureCompression = src.textureCompression;
            dst.crunchedCompression = src.crunchedCompression;
            dst.compressionQuality = src.compressionQuality;
            foreach (string platform in new[] { "Standalone", "Android", "iPhone" })
            {
                var a = src.GetPlatformTextureSettings(platform);
                a.name = platform;
                dst.SetPlatformTextureSettings(a);
            }
            dst.SaveAndReimport();
            report.AppendLine($"{Path.GetFileName(copyPath)} importer matched to {Path.GetFileName(originalPath)}");
        }

        private static void ConfigureMixamoCopies(StringBuilder report)
        {
            foreach (string path in CutsceneClipCatalog.MixamoCopies)
            {
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null)
                {
                    report.AppendLine($"missing Mixamo copy {path}");
                    continue;
                }
                bool changed = false;
                if (imp.animationType != ModelImporterAnimationType.Human) { imp.animationType = ModelImporterAnimationType.Human; changed = true; }
                if (imp.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; changed = true; }
                if (imp.materialImportMode != ModelImporterMaterialImportMode.None) { imp.materialImportMode = ModelImporterMaterialImportMode.None; changed = true; }
                if (changed)
                {
                    imp.SaveAndReimport(); // the default take only exists for a humanoid import once reimported
                    imp = (ModelImporter)AssetImporter.GetAtPath(path);
                }
                var defaults = imp.defaultClipAnimations;
                if (defaults.Length == 0) continue;
                ModelImporterClipAnimation clip = defaults[0];
                clip.name = Path.GetFileNameWithoutExtension(path);
                clip.loopTime = false;
                clip.loopPose = false;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
                clip.heightFromFeet = false;
                var current = imp.clipAnimations;
                if (current.Length == 1 && EditorJsonUtility.ToJson(current[0]) == EditorJsonUtility.ToJson(clip) && !changed) continue;
                imp.clipAnimations = new[] { clip };
                imp.SaveAndReimport();
                report.AppendLine($"{Path.GetFileName(path)}: Humanoid, root baked into pose");
            }
        }

        // ---------------------------------------------------------------- helpers

        private static T SeedAsset<T>(string path, System.Action<T> init = null) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            init?.Invoke(a);
            AssetDatabase.CreateAsset(a, path);
            AssetDatabase.SaveAssetIfDirty(a);
            return a;
        }

        private static void EnsureFolders()
        {
            foreach (string dir in new[]
                     {
                         CutsceneActorPaths.ArtDir + "/Chibi", CutsceneActorPaths.ArtDir + "/Birdy", CutsceneActorPaths.McClipDir,
                         CutsceneActorPaths.BirdyCutsceneClipDir, CutsceneActorPaths.PrefabDir, CutsceneActorPaths.DataDir,
                     })
                EnsureFolder(dir);
        }

        private static void EnsureFolder(string dir)
        {
            if (AssetDatabase.IsValidFolder(dir)) return;
            string parent = Path.GetDirectoryName(dir).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(dir));
        }
    }
}
