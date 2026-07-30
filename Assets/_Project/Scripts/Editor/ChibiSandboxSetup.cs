using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pickleball.Sandbox;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click setup: materials, Humanoid rigs, a locomotion blend tree, and a
/// WASD sandbox scene.
///
/// Tools â–¸ Pickleball â–¸ Setup Chibi Sandbox
///
/// Everything it creates lives outside the pickleball game systems, and it
/// writes a NEW scene rather than touching GameScene.
/// </summary>
public static class ChibiSandboxSetup
{
    // Canonical layout. Organize() moves assets here before anything else runs,
    // via AssetDatabase.MoveAsset so GUIDs â€” and therefore every scene and
    // prefab reference â€” survive the reshuffle untouched.
    const string CharacterDir = "Assets/_Project/Art/Characters/Chibi";
    const string PropsDir = "Assets/_Project/Art/Props";
    const string WorldDir = "Assets/_Project/Art/World";
    const string ClipDir = "Assets/_Project/Animation/Chibi";
    const string AnimDir = "Assets/_Project/Animation/Controllers";

    /// <summary>The Mixamo sword-and-shield pack. Base locomotion draws its
    /// idle/walk/run from here now that the character carries a weapon, so
    /// Organize() files the pack itself (WeaponSetup used to be the only mover;
    /// whichever runs first now wins and the other no-ops).</summary>
    const string PackDir = ClipDir + "/SwordShield";
    const string PackSourceDir = "Assets/Sword and Shield Pack";

    const string ModelPath = CharacterDir + "/chibi_unity.fbx";
    const string ArtDir = CharacterDir;
    const string ControllerPath = AnimDir + "/ChibiLocomotion.controller";
    const string ScenePath = "Assets/_Project/Scenes/ChibiSandbox.unity";

    /// <summary>Scratch working directory dragged into Assets by accident. It
    /// holds a DUPLICATE chibi_unity.fbx competing with the real one, the
    /// 68-part rigged source plus all 68 of its textures, a .py script and a
    /// .claude folder â€” none of which belong in a Unity project.</summary>
    const string StrayWorkDir = "Assets/chibi character 3d model";

    /// <summary>Model is authored ~1 unit tall; scale it to human height so it
    /// sits correctly in a human-scale world and the retargeted Mixamo stride
    /// reads naturally.</summary>
    const float TargetHeight = 1.7f;

    // Blend thresholds are the clips' MEASURED authored speeds (m/s), taken off
    // the source FBXs. Matching them is what stops the feet sliding.
    //
    // The armed clips were measured off the hip bone in Blender, same as the
    // unarmed originals (method re-validated: Walking.fbx re-measured 0.726,
    // Running.fbx 4.749 against the old 0.73/4.75 constants). One subtlety:
    // the pack's rig rests its hips at 0.3634 while Running.fbx's rig rests at
    // 1.0427, and Unity's humanoid retargeting normalises hip travel by rig
    // scale — so raw m/s only compares between same-sized rigs.
    //   walk: raw 0.607 on the SAME rig as Walking.fbx (0.726) -> use as-is.
    //   run:  raw 1.686, scale-normalised to Running.fbx's rig:
    //         1.686 x (1.0427/0.3634) = 4.84 — within 2% of the old 4.75, so
    //         the armed run strides at effectively the same rate as the old
    //         run and sprint speed barely changes.
    const float WalkSpeed = 0.61f;
    const float RunSpeed = 4.84f;
    /// <summary>Authored speed of Crouched Walking â€” much slower than the
    /// standing walk, so the crouch tree needs its own threshold.</summary>
    const float CrouchWalkSpeed = 0.39f;
    /// <summary>Default travel speed: between walk and run so the blend tree
    /// crossfades the two.</summary>
    const float MoveSpeed = 2.8f;

    /// <summary>Hand-tuned in the scene, kept here so re-running the setup does
    /// not reset it. Large because the chibi's legs are far shorter than the
    /// Mixamo source rig's, so retargeting seats the hips well below where a
    /// 1.7m human's would be.</summary>
    const float ModelYOffset = 0.64f;

    /// <summary>Ride speed. Far above the clips' authored 0.95 m/s, which is
    /// fine here and nowhere else: the feet stay planted on the deck, so there
    /// is no footfall to desynchronise from the ground.</summary>
    const float SkateSpeed = 6.2f;

    const string PickupDir = "Assets/_Project/Prefabs/Pickups";
    const string PrefabDir = "Assets/_Project/Prefabs";
    const string VfxDir = "Assets/_Project/Art/VFX";

    /// <summary>Front Flip is aloft for 1.20s of its 1.67s. The state is time
    /// scaled at runtime so that window matches the actual airtime â€” matching
    /// it by jump height alone would need a 3.24m leap at current gravity.</summary>
    const float FlipAirborneSeconds = 1.20f;

    /// <summary>The full-quality World Labs export: 589k tris, one 8192x8192
    /// texture. Used for visuals AND collision, so the collision surface is
    /// exactly the surface you can see.</summary>
    const string WorldFile = "Ancient Greek Scholarly Academy_texture_mesh.glb";
    const string ColliderFile = "Ancient Greek Scholarly Academy_collider.glb";
    const string WorldPath = WorldDir + "/" + WorldFile;
    /// <summary>Superseded assets from earlier iterations.</summary>
    const string OldWorldFbx = WorldDir + "/AcademyWorld.fbx";

    /// <summary>Fallback only â€” the spawn is normally raycast off the real
    /// collider, which avoids hand-converting between glTF, Blender and Unity
    /// axis conventions (three different handednesses, easy to get a sign
    /// wrong). The origin sits inside a building, so never spawn at zero.</summary>
    static readonly Vector3 FallbackSpawn = new(10f, 2f, 25f);

    /// <summary>
    /// role -> (asset path, loops, firstFrame, lastFrame).
    /// Frame range -1/-1 means "use the whole take".
    ///
    /// Jumping.fbx is 58 frames but spends its first 0.63s winding up before
    /// the feet leave the ground; starting the state there would float the
    /// character while the clip is still crouching. So it is split: the jump
    /// state uses frames 15-31 (short anticipation, launch, air) and the
    /// landing recovery becomes its own clip.
    ///
    /// Falling To Landing is likewise split into a loopable airborne portion
    /// and the landing itself.
    /// </summary>
    static readonly (string role, string file, bool loop, int first, int last)[] ClipDefs =
    {
        // Base locomotion is the ARMED set from the sword-and-shield pack, so
        // the character does not sprint around holding a weapon while animated
        // empty-handed. Chosen by measurement, not filename:
        //   idle (3): best loop of the lively idles (pose delta 0.0002 mean /
        //             0.001 max) and 8.67s — nearly the same length as the old
        //             8.33s Idle, so the blend tree keeps its timing structure.
        //             idle.fbx pops at the seam (0.0155); idle (4) loops but is
        //             a 2.5s statue.
        //   walk:     the only FORWARD armed walk — walk (2) travels backwards
        //             (travel.forward dot -0.98). Its seam is a 0.057 pop on
        //             LeftToe_End only (body mean 0.0085); no sub-window loops
        //             cleaner, so the full take stays.
        //   run:      the only FORWARD armed run — run (2) is also backwards
        //             (dot -0.88). Loops perfectly (pose delta 0.0).
        ("idle",   PackDir + "/sword and shield idle (3).fbx", true,  -1, -1),
        ("walk",   PackDir + "/sword and shield walk.fbx",     true,  -1, -1),
        ("run",    PackDir + "/sword and shield run.fbx",      true,  -1, -1),
        ("jump",   ClipDir + "/Jumping.fbx",                 false, 15, 31),
        // Running Jump needs no front trim -- the feet leave the ground at
        // frame 2. Frames past 17 are its own landing, which the Land state
        // already covers, so they are cut.
        ("runJump", ClipDir + "/Running Jump.fbx",           false,  1, 17),
        // Dash pose: frames 9-13 of Running Jump are the airborne stretch â€”
        // feet up, hips high, body leaning into travel. Non-looping, so it
        // holds that pose for the whole 0.22s dash. No new download needed.
        ("dashPose", ClipDir + "/Running Jump.fbx",          false,  9, 13),
        // Run To Stop is 1.80s but the character is at rest by 54%; the state
        // exits around there rather than playing the tail.
        ("stop",   ClipDir + "/Run To Stop.fbx",             false, -1, -1),
        // Falling Idle is a proper looping airborne clip, so the fall state no
        // longer borrows a 0.2s snippet off the front of the landing clip.
        ("fall",   ClipDir + "/Falling Idle.fbx",            true,  -1, -1),
        ("land",   ClipDir + "/Falling To Landing.fbx",      false,  8, 33),
        ("crouch", ClipDir + "/Standing To Crouched.fbx",    false, -1, -1),
        ("crouchIdle", ClipDir + "/Crouching Idle.fbx",      true,  -1, -1),
        ("crouchWalk", ClipDir + "/Crouched Walking.fbx",    true,  -1, -1),
        // Front Flip: 1.67s, hips rise 0.705 -- a one-shot trick, not a loop.
        ("flip",   ClipDir + "/Front Flip.fbx",              false, -1, -1),
        // Frames 5-41 are the airborne 1.20s, with the ground wind-up and
        // recovery cut off. Used for the double jump, where the character is
        // already in the air and only the flip itself should play.
        ("flipAir", ClipDir + "/Front Flip.fbx",             false,  5, 41),
        // Skateboarding loops (0.95 and 0.93 m/s). Imported and ready, but not
        // yet placed in the state machine: without a board mesh he would ride
        // thin air. Wired up once the skateboard prop exists.
        // 2.60s and loops cleanly (pose delta 0.0002) â€” used while tethered.
        ("hang",   ClipDir + "/Hanging.fbx",                 true,  -1, -1),
        // Throw.fbx is 168 frames (5.57s) and spends most of it standing still:
        // idle to ~30, wind-up to ~50, the actual throw 50-85, then idle again
        // to the end. Frames 55-80 are the forward drive through peak extension
        // (frame 67) into the start of the follow-through â€” no wind-up, which is
        // what a hookshot wants since it fires instantly.
        ("throw",  ClipDir + "/Throw.fbx",                   false, 55, 80),
        ("skate",     ClipDir + "/Skateboarding.fbx",        true,  -1, -1),
        ("skatePush", ClipDir + "/Skateboarding (1).fbx",    true,  -1, -1),
        // Standing Sprint Forward is deliberately unused: the direct walk->run
        // crossfade reads better than that clip did.
    };

    /// <summary>Loose files that belong in a canonical folder. Anything not
    /// found is skipped, so this is safe to re-run and safe when assets are
    /// already in place.</summary>
    static readonly (string file, string dir)[] AssetHomes =
    {
        ("chibi_unity.fbx", CharacterDir),
        ("Idle.fbx", ClipDir), ("Walking.fbx", ClipDir), ("Running.fbx", ClipDir),
        ("Jumping.fbx", ClipDir), ("Running Jump.fbx", ClipDir),
        ("Run To Stop.fbx", ClipDir), ("Falling Idle.fbx", ClipDir),
        ("Falling To Landing.fbx", ClipDir), ("Standing To Crouched.fbx", ClipDir),
        ("Crouching Idle.fbx", ClipDir), ("Crouched Walking.fbx", ClipDir),
        ("Front Flip.fbx", ClipDir), ("Skateboarding.fbx", ClipDir),
        // Listed explicitly so the loose-model sweep does not mistake this
        // animation for a prop and file it under Art/Props.
        ("Hanging.fbx", ClipDir), ("Throw.fbx", ClipDir),
        ("Skateboarding (1).fbx", ClipDir),
        ("Standing Sprint Forward.fbx", ClipDir), ("Sprint Turn.fbx", ClipDir),
        ("Crouched To Sprinting.fbx", ClipDir),
        (WorldFile, WorldDir), (ColliderFile, WorldDir),
    };

    static readonly Dictionary<string, string> BoneMap = new()
    {
        { "Hips", "Hip" }, { "Spine", "Waist" }, { "Chest", "Spine01" },
        { "UpperChest", "Spine02" }, { "Neck", "NeckTwist01" }, { "Head", "Head" },
        { "LeftShoulder", "L_Clavicle" }, { "LeftUpperArm", "L_Upperarm" },
        { "LeftLowerArm", "L_Forearm" }, { "LeftHand", "L_Hand" },
        { "RightShoulder", "R_Clavicle" }, { "RightUpperArm", "R_Upperarm" },
        { "RightLowerArm", "R_Forearm" }, { "RightHand", "R_Hand" },
        { "LeftUpperLeg", "L_Thigh" }, { "LeftLowerLeg", "L_Calf" },
        { "LeftFoot", "L_Foot" }, { "LeftToes", "L_ToeBase" },
        { "RightUpperLeg", "R_Thigh" }, { "RightLowerLeg", "R_Calf" },
        { "RightFoot", "R_Foot" }, { "RightToes", "R_ToeBase" },
    };

    // Leftovers from the earlier "Animation Preview" version of this script.
    const string OldControllerPath = AnimDir + "/ChibiPreview.controller";
    const string OldScenePath = "Assets/_Project/Scenes/AnimationPreview.unity";

    const string AutoRunKey = "Pickleball.ChibiSandbox.AutoRunToken";
    const string AutoRunToken = "lazy-layer-lookup+trail-sweep1";

    /// <summary>
    /// Runs the setup once, automatically, the first time this version of the
    /// script is loaded â€” the Editor holds the project lock, so it cannot be
    /// driven from outside. Guarded by an EditorPrefs token so it fires exactly
    /// once and never again on subsequent recompiles.
    /// </summary>
    [InitializeOnLoadMethod]
    static void AutoRunOnce()
    {
        if (EditorPrefs.GetString(AutoRunKey, "") == AutoRunToken) return;
        EditorPrefs.SetString(AutoRunKey, AutoRunToken);
        EditorApplication.delayCall += () =>
        {
            Debug.Log("Chibi sandbox: running setup automatically (one time).");
            Run();
        };
    }

    [MenuItem("Tools/Pickleball/Setup Chibi Sandbox")]
    public static void Run()
    {
        // Tidy first so every path below resolves to its canonical location.
        Organize();

        var model = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (model == null) { Debug.LogError($"Not found: {ModelPath}"); return; }

        SetupMaterial(model);
        if (!SetupRig(model)) return;

        // Group by file: one importer pass can define several sub-clips.
        var clips = new Dictionary<string, AnimationClip>();
        foreach (var group in ClipDefs.GroupBy(d => d.file))
        {
            foreach (var kv in SetupClips(group.Key, group.ToArray()))
                clips[kv.Key] = kv.Value;
        }
        if (!clips.ContainsKey("walk") || !clips.ContainsKey("idle"))
        {
            Debug.LogError("Armed idle/walk produced no clips â€” cannot build locomotion. " +
                           $"Is the sword-and-shield pack at {PackDir}?");
            return;
        }
        Debug.Log($"Clips wired: {string.Join(", ", clips.Keys.OrderBy(k => k))}");

        var avatar = LoadAsset<Avatar>(ModelPath);
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError($"Avatar invalid (valid={avatar?.isValid}, human={avatar?.isHuman}). " +
                           "Check the FBX â–¸ Rig â–¸ Configure window.");
            return;
        }

        var controller = BuildController(clips);

        // BuildController CREATES the controller from scratch, which drops the
        // combo layer WeaponSetup adds. Rebuilding the weapon here is therefore
        // not a convenience — it is the only ordering that leaves a working
        // attack chain, and it means one entry point rebuilds the whole sandbox
        // instead of two that have to be run in the right order by hand.
        //
        // Assets first, scene second: every FBX reimport is finished before a
        // single scene object exists, and BuildScene then attaches the weapon
        // before saving the prefab, so the combo is part of Chibi.prefab rather
        // than a scene-only override that the next rebuild throws away.
        var weaponClips = WeaponSetup.PrepareAssets();
        BuildScene(avatar, controller, weaponClips);

        AssetDatabase.SaveAssets();
        Debug.Log($"Chibi sandbox ready â€” {ScenePath}. Press Play. " +
                  "WASD move, Shift sprint, Space jump, Ctrl crouch, Esc frees the cursor." +
                  (clips.ContainsKey("crouchIdle") ? "" :
                   "  NOTE: no 'Crouching Idle.fbx' â€” crouch holds the last frame of " +
                   "Standing To Crouched. Add Crouching Idle + Crouched Walking for proper crouch."));
    }

    // --------------------------------------------------------------- organize

    /// <summary>
    /// Puts every asset in its canonical folder and clears out the strays.
    ///
    /// Uses AssetDatabase.MoveAsset throughout â€” that preserves each asset's
    /// GUID, so scenes, prefabs and the Animator controller keep pointing at
    /// the same files. Moving the files on disk instead would be the way to
    /// break every reference in the project at once.
    /// </summary>
    [MenuItem("Tools/Pickleball/Organize Project")]
    public static void Organize()
    {
        foreach (var dir in new[] { CharacterDir, PropsDir, WorldDir, ClipDir, AnimDir })
            EnsureFolder(dir);

        int moved = 0;

        // The sword-and-shield pack, moved as one folder with Mixamo's names
        // kept (same reasoning as WeaponSetup: renaming the 40+ unwired clips
        // would make them harder to find later). Locomotion clips resolve
        // against PackDir, so this must happen before SetupClips runs.
        if (AssetDatabase.IsValidFolder(PackSourceDir) &&
            !AssetDatabase.IsValidFolder(PackDir))
        {
            string packErr = AssetDatabase.MoveAsset(PackSourceDir, PackDir);
            if (string.IsNullOrEmpty(packErr)) { moved++; Debug.Log($"moved {PackSourceDir} -> {PackDir}"); }
            else Debug.LogWarning($"could not move pack: {packErr}");
        }
        foreach (var (file, dir) in AssetHomes)
        {
            string target = $"{dir}/{file}";
            if (AssetDatabase.LoadAssetAtPath<Object>(target) != null) continue;

            string current = FindLoose(file);
            if (current == null) continue;

            string err = AssetDatabase.MoveAsset(current, target);
            if (string.IsNullOrEmpty(err)) { moved++; Debug.Log($"moved {current} -> {target}"); }
            else Debug.LogWarning($"could not move {current}: {err}");
        }

        // Tripo drops a model plus its .fbm texture sidecar folder; move the
        // whole folder so the two stay together.
        //
        // Match on a normalised name: Tripo names folders both "skateboard 3d
        // model" and "feather+3d+model", and matching the spaced form alone
        // silently skipped the feather â€” which is why it fell back to a cube.
        foreach (var stray in AssetDatabase.GetSubFolders("Assets"))
        {
            string name = Path.GetFileName(stray);
            string norm = name.Replace('+', ' ').Replace('_', ' ').ToLowerInvariant();
            if (!norm.Contains("3d model") || name.StartsWith("_")) continue;
            if (stray == StrayWorkDir) continue;
            string target = $"{PropsDir}/{name}";
            if (AssetDatabase.LoadAssetAtPath<Object>(target) != null) continue;
            string err = AssetDatabase.MoveAsset(stray, target);
            if (string.IsNullOrEmpty(err)) { moved++; Debug.Log($"moved {stray} -> {target}"); }
            else Debug.LogWarning($"could not move {stray}: {err}");
        }

        // The dragged-in scratch directory: a duplicate character, the 68-part
        // source with all its textures, plus non-Unity files.
        if (AssetDatabase.LoadAssetAtPath<Object>(StrayWorkDir) != null)
        {
            if (AssetDatabase.DeleteAsset(StrayWorkDir))
                Debug.Log($"removed stray working directory: {StrayWorkDir}");
            else
                Debug.LogWarning($"could not remove {StrayWorkDir} â€” delete it by hand.");
        }

        // My hand-copied texture duplicates the one extracted from the FBX.
        const string dupTexture = CharacterDir + "/chibi_basecolor.jpg";
        if (AssetDatabase.LoadAssetAtPath<Object>(dupTexture) != null &&
            AssetDatabase.LoadAssetAtPath<Object>(
                CharacterDir + "/chibi+character+3d+model_basecolor.jpg") != null)
        {
            AssetDatabase.DeleteAsset(dupTexture);
            Debug.Log("removed duplicate chibi texture (kept the FBX-extracted one)");
        }

        // Any model still loose at the root is a prop: Tripo's exports are named
        // every which way ("ProtractorModel.fbx", "feather+3d+model"), so match
        // on location rather than on naming and give each its own folder.
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetDirectoryName(path)?.Replace('\\', '/') != "Assets") continue;

            string bare = Path.GetFileNameWithoutExtension(path);
            string dir = $"{PropsDir}/{bare}";
            EnsureFolder(dir);
            string err = AssetDatabase.MoveAsset(path, $"{dir}/{Path.GetFileName(path)}");
            if (string.IsNullOrEmpty(err))
            {
                moved++;
                Debug.Log($"moved {path} -> {dir}");
                // Bring the FBX's texture sidecar along with it.
                string fbm = $"Assets/{bare}.fbm";
                if (AssetDatabase.IsValidFolder(fbm))
                    AssetDatabase.MoveAsset(fbm, $"{dir}/{bare}.fbm");
            }
            else Debug.LogWarning($"could not move {path}: {err}");
        }

        // Re-importing a Tripo model can leave several copies around: a loose
        // FBX at the root, its .fbm sidecar, and the organised folder. Keep the
        // organised one and drop root-level duplicates of the same filename.
        int dupes = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith("Assets/_Project/")) continue;
            string file = Path.GetFileName(path);
            bool organisedCopyExists = AssetDatabase
                .FindAssets(Path.GetFileNameWithoutExtension(file), new[] { "Assets/_Project" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Any(q => Path.GetFileName(q) == file);
            if (!organisedCopyExists) continue;
            if (AssetDatabase.DeleteAsset(path)) { dupes++; Debug.Log($"removed duplicate {path}"); }
        }
        // Sweep up now-empty stray folders and orphaned .fbm sidecars.
        foreach (var dir in AssetDatabase.GetSubFolders("Assets"))
        {
            string name = Path.GetFileName(dir);
            string norm = name.Replace('+', ' ').Replace('_', ' ').ToLowerInvariant();
            bool tripoish = norm.Contains("3d model") || name.EndsWith(".fbm");
            if (!tripoish) continue;
            bool empty = AssetDatabase.FindAssets("", new[] { dir }).Length == 0;
            if (empty && AssetDatabase.DeleteAsset(dir))
                Debug.Log($"removed empty stray folder {dir}");
        }

        AssetDatabase.Refresh();
        Debug.Log($"Organize: {moved} asset(s) relocated, {dupes} duplicate(s) removed.");
    }

    /// <summary>Find an asset by filename wherever it currently sits.</summary>
    static string FindLoose(string file)
    {
        string atRoot = $"Assets/{file}";
        if (AssetDatabase.LoadAssetAtPath<Object>(atRoot) != null) return atRoot;

        string bare = Path.GetFileNameWithoutExtension(file);
        foreach (var guid in AssetDatabase.FindAssets($"\"{bare}\""))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileName(path) == file && !path.StartsWith(StrayWorkDir))
                return path;
        }
        return null;
    }

    // ---------------------------------------------------------------- material

    static void SetupMaterial(ModelImporter importer) =>
        SetupMaterial(importer, ModelPath, ArtDir, "Chibi");

    /// <summary>
    /// Extract the FBX's embedded texture, build a URP Lit material from it and
    /// remap every material slot the model declares onto it.
    ///
    /// Parameterised so a second character does not need a second copy: the
    /// chibi and Newton differ only in paths.
    /// </summary>
    static void SetupMaterial(ModelImporter importer, string modelPath,
                              string artDir, string materialName)
    {
        // A copy of the source atlas is checked into artDir, so the material
        // has a texture even if extraction from the FBX yields nothing.
        // ExtractTextures returns false when there is nothing left to pull out,
        // which is not an error worth aborting on.
        if (importer.ExtractTextures(artDir))
        {
            AssetDatabase.Refresh();
            Debug.Log($"Extracted embedded texture(s) to {artDir}");
        }

        var textures = AssetDatabase.FindAssets("t:Texture2D", new[] { artDir })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
            .Where(t => t != null)
            .ToList();
        var tex = textures.FirstOrDefault(t => t.name.ToLower().Contains("basecolor"))
                  ?? textures.FirstOrDefault();
        if (tex == null)
        {
            Debug.LogWarning($"No texture found in {artDir}; leaving materials as imported.");
            return;
        }
        // Unity's importer default caps textures at 2048, which silently halves
        // a 4k source and just reads as a soft-looking character.
        var texPath = AssetDatabase.GetAssetPath(tex);
        if (AssetImporter.GetAtPath(texPath) is TextureImporter ti && ti.maxTextureSize < 4096)
        {
            ti.maxTextureSize = 4096;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            Debug.Log($"Raised '{tex.name}' to maxTextureSize 4096 (was capped at 2048).");
        }
        Debug.Log($"Using texture '{tex.name}' ({tex.width}x{tex.height})");

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) { Debug.LogError("URP Lit shader not found."); return; }

        string matPath = $"{artDir}/{materialName}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.shader = shader;
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Smoothness", 0.1f);   // cloth/skin, not plastic
        EditorUtility.SetDirty(mat);

        // Remap every material slot the FBX declares onto ours.
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.SaveAndReimport();

        var embedded = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>().ToArray();
        foreach (var em in embedded)
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), em.name), mat);

        if (embedded.Length == 0)
            Debug.LogWarning("FBX declared no material slots to remap.");
        else
            Debug.Log($"Remapped {embedded.Length} material slot(s) -> {matPath}");

        importer.SaveAndReimport();
    }

    // --------------------------------------------------------------------- rig

    static bool SetupRig(ModelImporter importer) => SetupRig(importer, TargetHeight);

    /// <summary>
    /// Import as Humanoid with an explicitly mapped avatar.
    ///
    /// The bone map is shared across every Tripo character: their auto-rigger
    /// emits the same 41-bone skeleton every time (Hip/Waist/Spine01/
    /// NeckTwist01/L_Thigh...), which is why Newton needed no new mapping work.
    /// </summary>
    static bool SetupRig(ModelImporter importer, float targetHeight)
    {
        importer.globalScale = targetHeight;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.SaveAndReimport();

        var desc = importer.humanDescription;
        var present = new HashSet<string>(desc.skeleton?.Select(s => s.name)
                                          ?? Enumerable.Empty<string>());
        bool verify = present.Count > 0;

        var bones = new List<HumanBone>();
        foreach (var kv in BoneMap)
        {
            if (verify && !present.Contains(kv.Value))
            {
                Debug.LogWarning($"Bone '{kv.Value}' missing â€” {kv.Key} unmapped.");
                continue;
            }
            bones.Add(new HumanBone
            {
                humanName = kv.Key,
                boneName = kv.Value,
                limit = new HumanLimit { useDefaultValues = true }
            });
        }

        if (bones.Count < 15)
        {
            Debug.LogError($"Only {bones.Count} bones mapped; Unity needs 15 required bones.");
            return false;
        }

        desc.human = bones.ToArray();
        importer.humanDescription = desc;
        importer.SaveAndReimport();
        Debug.Log($"Rig: Humanoid, {bones.Count} bones mapped, scale {targetHeight}.");
        return true;
    }

    static Dictionary<string, AnimationClip> SetupClips(
        string path, (string role, string file, bool loop, int first, int last)[] defs)
    {
        var result = new Dictionary<string, AnimationClip>();
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null)
        {
            // Optional clips (crouch idle/walk) simply may not exist yet.
            if (defs.Any(d => d.role.StartsWith("crouch") && d.role != "crouch"))
                Debug.Log($"Optional clip absent: {Path.GetFileName(path)}");
            else
                Debug.LogWarning($"Missing animation source: {path}");
            return result;
        }

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;

        var source = importer.defaultClipAnimations;
        if (source == null || source.Length == 0)
        {
            Debug.LogWarning($"No take found in {path}");
            return result;
        }
        var take = source[0];

        var built = new List<ModelImporterClipAnimation>();
        foreach (var d in defs)
        {
            built.Add(new ModelImporterClipAnimation
            {
                takeName = take.takeName,
                name = d.role,
                firstFrame = d.first < 0 ? take.firstFrame : d.first,
                lastFrame = d.last < 0 ? take.lastFrame : d.last,
                loopTime = d.loop,
                // Root motion is disabled on the Animator, so anything NOT
                // baked into pose is discarded -- which is exactly what we want
                // here, because the CharacterController owns all travel:
                //   XZ not baked -> cycles play in place instead of drifting
                //                   forward and snapping back each loop.
                //   Y  not baked -> the jump arc comes from the controller only,
                //                   rather than adding to the clip's 0.24 rise.
                //   Rotation not baked -> Sprint Turn cannot spin the character.
                lockRootRotation = false,
                lockRootHeightY = false,
                lockRootPositionXZ = false,
                // Based Upon: feet on the ground, body centred over the capsule.
                keepOriginalOrientation = true,
                keepOriginalPositionY = false,
                keepOriginalPositionXZ = false,
            });
        }
        importer.clipAnimations = built.ToArray();
        importer.SaveAndReimport();

        var produced = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).ToList();
        foreach (var d in defs)
        {
            var clip = produced.FirstOrDefault(c => c.name == d.role);
            if (clip == null)
            {
                Debug.LogWarning($"Sub-clip '{d.role}' not produced from {Path.GetFileName(path)}");
                continue;
            }
            result[d.role] = clip;
            Debug.Log($"  {d.role,-10} <- {Path.GetFileName(path),-30} " +
                      $"{clip.length:0.00}s loop={d.loop}");
        }
        return result;
    }

    // -------------------------------------------------------------- controller

    static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("SpeedMultiplier", AnimatorControllerParameterType.Float);
        controller.AddParameter("VerticalSpeed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Stop", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Flip", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Skating", AnimatorControllerParameterType.Bool);
        controller.AddParameter("FlipSpeed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Dash", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Tethered", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Fire", AnimatorControllerParameterType.Trigger);

        var ps = controller.parameters;
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].name == "SpeedMultiplier") ps[i].defaultFloat = 1f;
            if (ps[i].name == "FlipSpeed") ps[i].defaultFloat = 1f;
            if (ps[i].name == "Grounded") ps[i].defaultBool = true;
        }
        controller.parameters = ps;

        var sm = controller.layers[0].stateMachine;

        // ---- ground locomotion ------------------------------------------
        var loco = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(clips["idle"], 0f);
        tree.AddChild(clips["walk"], WalkSpeed);
        if (clips.TryGetValue("run", out var run)) tree.AddChild(run, RunSpeed);
        loco.speedParameterActive = true;
        loco.speedParameter = "SpeedMultiplier";
        sm.defaultState = loco;

        // ---- crouch -------------------------------------------------------
        // With a real Crouching Idle (+ Crouched Walking) this becomes a proper
        // blend tree. Without them, Standing To Crouched is played once and
        // holds its final crouched pose, which reads acceptably as an idle.
        AnimatorState crouch;
        if (clips.TryGetValue("crouchIdle", out var cIdle))
        {
            crouch = controller.CreateBlendTreeInController("Crouch", out BlendTree cTree, 0);
            cTree.blendType = BlendTreeType.Simple1D;
            cTree.blendParameter = "Speed";
            cTree.useAutomaticThresholds = false;
            cTree.AddChild(cIdle, 0f);
            if (clips.TryGetValue("crouchWalk", out var cWalk))
                cTree.AddChild(cWalk, CrouchWalkSpeed);
        }
        else
        {
            crouch = sm.AddState("Crouch");
            crouch.motion = clips["crouch"];
        }

        // ---- air ----------------------------------------------------------
        // Standing and running jumps are different shapes, so the jump state
        // blends between them on the same Speed parameter. Speed persists
        // through the airborne phase, so whichever was true at takeoff holds.
        AnimatorState jump;
        clips.TryGetValue("jump", out var jClip);
        if (clips.TryGetValue("runJump", out var rjClip) && jClip != null)
        {
            jump = controller.CreateBlendTreeInController("Jump", out BlendTree jTree, 0);
            jTree.blendType = BlendTreeType.Simple1D;
            jTree.blendParameter = "Speed";
            jTree.useAutomaticThresholds = false;
            jTree.AddChild(jClip, 0f);
            jTree.AddChild(rjClip, RunSpeed);
        }
        else
        {
            jump = sm.AddState("Jump");
            jump.motion = jClip ?? clips["idle"];
        }

        var fall = sm.AddState("Fall");
        fall.motion = clips.TryGetValue("fall", out var fClip) ? fClip : jump.motion;

        var land = sm.AddState("Land");
        land.motion = clips.TryGetValue("land", out var lClip) ? lClip : clips["idle"];

        // ---- transitions ---------------------------------------------------
        // Jump is a trigger, so it must not wait for exit time.
        var t = loco.AddTransition(jump);
        t.hasExitTime = false; t.duration = 0.05f;
        t.AddCondition(AnimatorConditionMode.If, 0, "Jump");

        // Walked off a ledge (falling, not jumping).
        t = loco.AddTransition(fall);
        t.hasExitTime = false; t.duration = 0.15f;
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
        t.AddCondition(AnimatorConditionMode.Less, -0.1f, "VerticalSpeed");

        // Past the apex -> fall.
        t = jump.AddTransition(fall);
        t.hasExitTime = false; t.duration = 0.12f;
        t.AddCondition(AnimatorConditionMode.Less, -0.1f, "VerticalSpeed");
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");

        // Short hop that lands before the apex condition fires.
        t = jump.AddTransition(land);
        t.hasExitTime = false; t.duration = 0.08f;
        t.AddCondition(AnimatorConditionMode.If, 0, "Grounded");

        t = fall.AddTransition(land);
        t.hasExitTime = false; t.duration = 0.06f;
        t.AddCondition(AnimatorConditionMode.If, 0, "Grounded");

        // Landing recovery is interruptible so movement stays responsive.
        t = land.AddTransition(loco);
        t.hasExitTime = true; t.exitTime = 0.55f; t.duration = 0.18f;

        t = land.AddTransition(jump);
        t.hasExitTime = false; t.duration = 0.05f;
        t.AddCondition(AnimatorConditionMode.If, 0, "Jump");

        // ---- run-to-stop ---------------------------------------------------
        if (clips.TryGetValue("stop", out var stopClip))
        {
            var stop = sm.AddState("Stop");
            stop.motion = stopClip;

            t = loco.AddTransition(stop);
            t.hasExitTime = false; t.duration = 0.12f;
            t.AddCondition(AnimatorConditionMode.If, 0, "Stop");

            // Moving again cancels the stop immediately â€” responsiveness beats
            // playing the plant out.
            t = stop.AddTransition(loco);
            t.hasExitTime = false; t.duration = 0.15f;
            t.AddCondition(AnimatorConditionMode.Greater, 0.6f, "Speed");

            // The character is at rest 54% in; the tail is just settling.
            t = stop.AddTransition(loco);
            t.hasExitTime = true; t.exitTime = 0.62f; t.duration = 0.25f;

            // Still jumpable and still falls off ledges mid-stop.
            t = stop.AddTransition(jump);
            t.hasExitTime = false; t.duration = 0.05f;
            t.AddCondition(AnimatorConditionMode.If, 0, "Jump");

            t = stop.AddTransition(fall);
            t.hasExitTime = false; t.duration = 0.15f;
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
        }

        // ---- front flip: ground trick AND the feather's double jump ---------
        AnimatorState flip = null;
        if (clips.TryGetValue("flip", out var flipClip))
        {
            flip = sm.AddState("FrontFlip");
            flip.motion = flipClip;
            // No foot IK mid-flip; his feet are over his head for most of it.
            flip.iKOnFeet = false;

            // Ground trick (F): the full clip, wind-up and landing included.
            t = loco.AddTransition(flip);
            t.hasExitTime = false; t.duration = 0.08f;
            t.AddCondition(AnimatorConditionMode.If, 0, "Flip");

            t = flip.AddTransition(loco);
            t.hasExitTime = true; t.exitTime = 0.88f; t.duration = 0.18f;
        }

        // Air variant for the double jump: only the airborne portion, and time
        // scaled by the script so it finishes exactly as he lands. Matching it
        // by jump height instead would need a 3.24m leap.
        if (clips.TryGetValue("flipAir", out var flipAirClip))
        {
            var flipAir = sm.AddState("FlipAir");
            flipAir.motion = flipAirClip;
            flipAir.iKOnFeet = false;
            flipAir.speedParameterActive = true;
            flipAir.speedParameter = "FlipSpeed";

            foreach (var from in new[] { jump, fall })
            {
                t = from.AddTransition(flipAir);
                t.hasExitTime = false; t.duration = 0.06f;
                t.AddCondition(AnimatorConditionMode.If, 0, "Flip");
            }

            t = flipAir.AddTransition(land);
            t.hasExitTime = false; t.duration = 0.08f;
            t.AddCondition(AnimatorConditionMode.If, 0, "Grounded");

            t = flipAir.AddTransition(fall);
            t.hasExitTime = true; t.exitTime = 0.97f; t.duration = 0.12f;
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
        }

        // ---- dash -------------------------------------------------------------
        if (clips.TryGetValue("dashPose", out var dashClip))
        {
            var dash = sm.AddState("Dash");
            dash.motion = dashClip;
            dash.iKOnFeet = false;      // airborne, and the pose is the point

            // Enterable from anywhere on the ground or in the air.
            foreach (var from in new[] { loco, jump, fall, land })
            {
                t = from.AddTransition(dash);
                t.hasExitTime = false; t.duration = 0.04f;   // snap in, no ramp
                t.AddCondition(AnimatorConditionMode.If, 0, "Dash");
            }

            t = dash.AddTransition(fall);
            t.hasExitTime = true; t.exitTime = 0.9f; t.duration = 0.12f;
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");

            t = dash.AddTransition(land);
            t.hasExitTime = false; t.duration = 0.08f;
            t.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
        }

        // ---- tether ----------------------------------------------------------
        if (clips.TryGetValue("hang", out var hangClip))
        {
            var hang = sm.AddState("Tethered");
            hang.motion = hangClip;
            hang.iKOnFeet = false;      // he is hanging; there is no ground

            // Any state can be interrupted by latching on.
            t = sm.AddAnyStateTransition(hang);
            t.hasExitTime = false; t.duration = 0.18f;
            t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0, "Tethered");

            t = hang.AddTransition(fall);
            t.hasExitTime = false; t.duration = 0.15f;
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "Tethered");
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");

            t = hang.AddTransition(loco);
            t.hasExitTime = false; t.duration = 0.15f;
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "Tethered");
            t.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
        }

        // ---- skateboard ------------------------------------------------------
        if (clips.TryGetValue("skate", out var skateClip))
        {
            AnimatorState skate;
            if (clips.TryGetValue("skatePush", out var pushClip))
            {
                skate = controller.CreateBlendTreeInController("Skate", out BlendTree sTree, 0);
                sTree.blendType = BlendTreeType.Simple1D;
                sTree.blendParameter = "Speed";
                sTree.useAutomaticThresholds = false;
                sTree.AddChild(skateClip, 0f);          // coasting
                sTree.AddChild(pushClip, SkateSpeed);   // pushing along
            }
            else
            {
                skate = sm.AddState("Skate");
                skate.motion = skateClip;
            }
            // Feet are on the board, not the ground â€” IK would fight the deck.
            skate.iKOnFeet = false;

            t = loco.AddTransition(skate);
            t.hasExitTime = false; t.duration = 0.25f;
            t.AddCondition(AnimatorConditionMode.If, 0, "Skating");

            t = skate.AddTransition(loco);
            t.hasExitTime = false; t.duration = 0.25f;
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "Skating");
        }

        t = loco.AddTransition(crouch);
        t.hasExitTime = false; t.duration = 0.15f;
        t.AddCondition(AnimatorConditionMode.If, 0, "Crouch");

        t = crouch.AddTransition(loco);
        t.hasExitTime = false; t.duration = 0.2f;
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "Crouch");

        // Foot IK on every grounded state. The chibi's legs are far shorter
        // relative to its body than the Mixamo source rig's, so humanoid
        // retargeting places the hips too low and the feet penetrate the floor.
        // Foot IK re-plants them on the surface after retargeting, which is the
        // actual fix for that clipping â€” no amount of collider tuning helps,
        // because the capsule is already sitting correctly on the ground.
        // Deliberately NOT enabled on Jump/Fall: planting feet mid-air is wrong.
        foreach (var s in sm.states)
        {
            bool airborne = s.state == jump || s.state == fall;
            s.state.iKOnFeet = !airborne;
        }

        BuildGestureLayer(controller, clips);

        // OnAnimatorIK is only called when the LAYER has an IK pass. This is a
        // different setting from the per-state "Foot IK" flag above, and
        // without it ChibiFootIK silently never runs.
        var layers = controller.layers;
        layers[0].iKPass = true;
        controller.layers = layers;

        EditorUtility.SetDirty(controller);
        Debug.Log($"Controller: {ControllerPath} â€” locomotion({tree.children.Length}), " +
                  $"jump, fall, land, crouch{(clips.ContainsKey("crouchIdle") ? " (blend)" : " (held pose)")}" +
                  ", foot IK on grounded states");
        return controller;
    }

    /// <summary>
    /// The arms/spine/head mask shared by every additive-over-locomotion layer:
    /// the hook throw here, and the sword combo in WeaponSetup.
    ///
    /// Root is deliberately NOT included, so the hips stay wherever locomotion
    /// put them and only the upper body is overridden.
    /// </summary>
    public static AvatarMask EnsureUpperBodyMask()
    {
        string maskPath = $"{AnimDir}/UpperBody.mask";
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
        if (mask != null) return mask;

        mask = new AvatarMask();
        foreach (AvatarMaskBodyPart part in System.Enum.GetValues(typeof(AvatarMaskBodyPart)))
        {
            if (part == AvatarMaskBodyPart.LastBodyPart) continue;
            bool upper = part is AvatarMaskBodyPart.RightArm
                      or AvatarMaskBodyPart.LeftArm
                      or AvatarMaskBodyPart.RightFingers
                      or AvatarMaskBodyPart.LeftFingers
                      or AvatarMaskBodyPart.Head
                      or AvatarMaskBodyPart.Body;
            mask.SetHumanoidBodyPartActive(part, upper);
        }
        AssetDatabase.CreateAsset(mask, maskPath);
        return mask;
    }

    /// <summary>
    /// Adds a masked upper-body layer for the hook throw.
    ///
    /// A gesture layer is the right shape for this: firing the hook can happen
    /// while running, falling or already swinging, and a full-body state would
    /// stamp on all of them. Masking to the arms and spine lets the throw play
    /// over whatever the legs are doing.
    ///
    /// The layer sits at zero weight by default and the script fades it in for
    /// the gesture â€” an Override layer at full weight with an empty default
    /// state would push the arms into the bind pose permanently.
    /// </summary>
    static void BuildGestureLayer(AnimatorController controller,
                                  Dictionary<string, AnimationClip> clips)
    {
        if (!clips.TryGetValue("throw", out var throwClip))
        {
            Debug.Log("No Throw.fbx â€” hook-firing gesture skipped. " +
                      $"Drop a one-armed throw in {ClipDir}/Throw.fbx and re-run.");
            return;
        }

        var mask = EnsureUpperBodyMask();

        var gestureSm = new AnimatorStateMachine
        {
            name = "Gesture",
            hideFlags = HideFlags.HideInHierarchy,
        };
        AssetDatabase.AddObjectToAsset(gestureSm, controller);

        var empty = gestureSm.AddState("None");      // no motion: the rest pose
        var fire = gestureSm.AddState("Fire");
        fire.motion = throwClip;
        gestureSm.defaultState = empty;

        var t = empty.AddTransition(fire);
        t.hasExitTime = false; t.duration = 0.05f;
        t.AddCondition(AnimatorConditionMode.If, 0, "Fire");

        t = fire.AddTransition(empty);
        t.hasExitTime = true; t.exitTime = 0.85f; t.duration = 0.15f;

        controller.AddLayer(new AnimatorControllerLayer
        {
            name = "Gesture",
            stateMachine = gestureSm,
            avatarMask = mask,
            blendingMode = AnimatorLayerBlendingMode.Override,
            defaultWeight = 0f,       // faded in by ChibiLocomotion
        });

        Debug.Log($"Gesture layer added (upper-body mask) using '{throwClip.name}'.");
    }

    // ------------------------------------------------------------------- scene

    static void BuildScene(Avatar avatar, AnimatorController controller,
                           Dictionary<string, AnimationClip> weaponClips)
    {
        // Never silently discard unsaved work in whatever scene is open.
        EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // Safe to remove the old preview assets now that they are not open.
        // OldWorldFbx was my Blender GLB->FBX conversion, made before glTFast
        // was installed; the GLB now imports natively so it is dead weight.
        foreach (var stale in new[] { OldScenePath, OldControllerPath, OldWorldFbx })
            if (AssetDatabase.LoadAssetAtPath<Object>(stale) != null && AssetDatabase.DeleteAsset(stale))
                Debug.Log($"Removed superseded asset: {stale}");

        Vector3 spawn = BuildWorld();

        // The model goes UNDER the controller rather than sharing its object.
        // That way the visual can be nudged vertically against the capsule
        // without moving the collider â€” the practical lever for residual foot
        // clipping after retargeting, which no collider tuning can reach.
        var go = new GameObject("Chibi");
        go.transform.position = spawn;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        model.name = "ChibiModel";
        model.transform.SetParent(go.transform, false);
        model.transform.localPosition = Vector3.zero;

        var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;   // the CharacterController owns travel
        if (model.GetComponent<ChibiFootIK>() == null)
            model.AddComponent<ChibiFootIK>();

        // Size the capsule from the actual renderer bounds rather than guessing.
        var bounds = new Bounds(go.transform.position, Vector3.zero);
        bool any = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        float height = any ? bounds.size.y : TargetHeight;
        float radius = any ? Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.75f : 0.3f;
        radius = Mathf.Clamp(radius, 0.05f, height * 0.4f);

        var cc = go.AddComponent<CharacterController>();
        cc.height = height;
        cc.radius = radius;
        cc.center = new Vector3(0f, height * 0.5f, 0f);
        cc.slopeLimit = 50f;
        cc.stepOffset = Mathf.Min(0.3f, height * 0.25f);

        var loco = go.AddComponent<ChibiLocomotion>();
        var so = new SerializedObject(loco);
        so.FindProperty("animator").objectReferenceValue = animator;
        so.FindProperty("modelRoot").objectReferenceValue = model.transform;
        so.FindProperty("modelYOffset").floatValue = ModelYOffset;
        so.FindProperty("moveSpeed").floatValue = MoveSpeed;
        so.FindProperty("sprintSpeed").floatValue = RunSpeed;
        so.FindProperty("crouchSpeed").floatValue = CrouchWalkSpeed;

        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = spawn + new Vector3(0f, height * 0.8f, 4f);
            cam.transform.rotation = Quaternion.Euler(12f, 180f, 0f);
            var orbit = cam.gameObject.AddComponent<SandboxOrbitCamera>();
            var cso = new SerializedObject(orbit);
            cso.FindProperty("target").objectReferenceValue = go.transform;
            cso.FindProperty("targetOffset").vector3Value = new Vector3(0f, height * 0.8f, 0f);
            cso.ApplyModifiedPropertiesWithoutUndo();

            so.FindProperty("cameraTransform").objectReferenceValue = cam.transform;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        // ---- abilities, board and VFX ---------------------------------------
        go.AddComponent<PlayerAbilities>();

        EnsureFolder(VfxDir);
        EnsureFolder(PickupDir);
        var puffPrefab = SandboxVfxBuilder.BuildCloudPuff(VfxDir, $"{PickupDir}/CloudPuff.prefab");
        if (puffPrefab != null)
        {
            var puff = (GameObject)PrefabUtility.InstantiatePrefab(puffPrefab.gameObject);
            puff.name = "DoubleJumpPuff";
            puff.transform.SetParent(go.transform, false);
            so.FindProperty("doubleJumpEffect").objectReferenceValue =
                puff.GetComponent<ParticleSystem>();
        }

        var board = BuildBoard(go, height);
        so.FindProperty("skateRideHeight").floatValue = RideHeight;

        // ---- dash VFX --------------------------------------------------------
        var effects = go.AddComponent<DashEffects>();
        var eso = new SerializedObject(effects);

        var ghostMat = SandboxVfxBuilder.BuildGlowMaterial(
            VfxDir, "DashGhost", new Color(0.55f, 0.85f, 1f, 0.55f));
        eso.FindProperty("ghostMaterial").objectReferenceValue = ghostMat;

        var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>();
        var srcProp = eso.FindProperty("sources");
        srcProp.arraySize = smrs.Length;
        for (int i = 0; i < smrs.Length; i++)
            srcProp.GetArrayElementAtIndex(i).objectReferenceValue = smrs[i];

        var arcGo = new GameObject("DashArc");
        arcGo.transform.SetParent(go.transform, false);
        var arcMesh = SandboxVfxBuilder.BuildProtractorArc(VfxDir);
        var arcMat = SandboxVfxBuilder.BuildGlowMaterial(
            VfxDir, "ProtractorArc", new Color(0.6f, 0.9f, 1f, 0.9f));
        arcGo.AddComponent<MeshFilter>().sharedMesh = arcMesh;
        var arcMr = arcGo.AddComponent<MeshRenderer>();
        arcMr.sharedMaterial = arcMat;
        arcMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var arc = arcGo.AddComponent<DashArc>();
        var aso = new SerializedObject(arc);
        aso.FindProperty("arcRenderer").objectReferenceValue = arcMr;
        aso.ApplyModifiedPropertiesWithoutUndo();
        eso.FindProperty("arc").objectReferenceValue = arc;

        if (cam != null)
            eso.FindProperty("cameraRig").objectReferenceValue =
                cam.GetComponent<SandboxOrbitCamera>();
        eso.ApplyModifiedPropertiesWithoutUndo();

        so.FindProperty("dashEffects").objectReferenceValue = effects;

        // ---- tether ----------------------------------------------------------
        var tetherSys = go.AddComponent<TetherSystem>();
        var tso = new SerializedObject(tetherSys);

        // Rope leaves from about hand height rather than the feet.
        var ropeOrigin = new GameObject("RopeOrigin");
        ropeOrigin.transform.SetParent(go.transform, false);
        ropeOrigin.transform.localPosition = new Vector3(0f, height * 0.8f, 0.1f);

        var ropeGo = new GameObject("Rope");
        ropeGo.transform.SetParent(go.transform, false);
        var line = ropeGo.AddComponent<LineRenderer>();
        var ropeMat = SandboxVfxBuilder.BuildGlowMaterial(
            VfxDir, "TetherRope", new Color(0.85f, 0.95f, 1f, 1f));
        line.sharedMaterial = ropeMat;
        line.widthMultiplier = 0.035f;
        line.numCapVertices = 4;
        line.textureMode = LineTextureMode.Tile;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.useWorldSpace = true;
        line.enabled = false;

        tso.FindProperty("rope").objectReferenceValue = line;
        tso.FindProperty("ropeOrigin").objectReferenceValue = ropeOrigin.transform;
        if (cam != null) tso.FindProperty("aimSource").objectReferenceValue = cam.transform;
        tso.ApplyModifiedPropertiesWithoutUndo();

        so.FindProperty("tether").objectReferenceValue = tetherSys;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Weapon and combo go on BEFORE the prefab is written, so they are part
        // of the prefab rather than a scene-only override.
        WeaponSetup.AttachToPlayer(go, weaponClips);

        // One line that says whether the character will actually animate. A null
        // controller here is the difference between a working sandbox and a
        // T-posing one, and it is not otherwise obvious until Play.
        Debug.Log($"Animator wired: controller='{controller.name}' " +
                  $"({controller.layers.Length} layers), avatar='{avatar.name}', " +
                  $"assigned={(animator.runtimeAnimatorController != null)}");

        // Save the assembled player as a prefab and reconnect the scene copy,
        // so future tweaks propagate instead of living only in this scene.
        EnsureFolder(PrefabDir);
        PrefabUtility.SaveAsPrefabAssetAndConnect(
            go, $"{PrefabDir}/Chibi.prefab", InteractionMode.AutomatedAction);

        PlacePickup(AbilityKind.DoubleJump, spawn + new Vector3(2.5f, 0f, 1.5f));
        PlacePickup(AbilityKind.Skateboard, spawn + new Vector3(-2.5f, 0f, 1.5f));
        PlacePickup(AbilityKind.Dash, spawn + new Vector3(0f, 0f, 3.5f));
        PlacePickup(AbilityKind.Tether, spawn + new Vector3(0f, 0f, -3f));
        PlaceAnchors(spawn);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = go;
        Debug.Log($"CharacterController: height {height:0.00}, radius {radius:0.00}");
        Debug.Log($"Pickups placed. Board: {(board ? "ready" : "none")}. " +
                  "F flip Â· double-tap Space after the feather Â· R to ride after the board.");
    }

    /// <summary>
    /// Drops the World Labs environment in and returns a spawn position.
    ///
    /// The .glb cannot be used directly â€” this project has no glTF importer
    /// package â€” so it was converted to FBX with Blender. It is the *collider*
    /// export: 199k tris, one material, no UVs and no textures, so it renders
    /// flat. Convert the textured visual mesh the same way when you have it.
    /// </summary>
    static Vector3 BuildWorld()
    {
        var worldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WorldPath);
        if (worldPrefab == null)
        {
            Debug.LogWarning($"{WorldPath} not found â€” falling back to a ground plane.");
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Ground (placeholder)";
            plane.transform.localScale = Vector3.one * 10f;
            return new Vector3(0f, 0.1f, 0f);
        }

        var world = (GameObject)PrefabUtility.InstantiatePrefab(worldPrefab);
        world.name = "AcademyWorld";
        world.transform.position = Vector3.zero;
        foreach (var tr in world.GetComponentsInChildren<Transform>())
            tr.gameObject.isStatic = true;

        // glTFast does not create colliders, so add them. Non-convex MeshCollider
        // on a static object is fine at this triangle count, and using the
        // visual mesh means collision matches exactly what is rendered.
        int added = 0;
        foreach (var mf in world.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.GetComponent<Collider>() != null) continue;
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            added++;
        }

        int tris = world.GetComponentsInChildren<MeshFilter>()
            .Where(m => m.sharedMesh != null).Sum(m => m.sharedMesh.triangles.Length / 3);
        Debug.Log($"World placed: {added} MeshCollider(s), {tris:n0} tris (visual + collision).");

        // Physics needs the new colliders registered before we can raycast them.
        Physics.SyncTransforms();
        return FindSpawn(world);
    }

    /// <summary>
    /// Raycast the world to find flat, low ground. Deriving the spawn from the
    /// actual collider is far safer than converting a probe coordinate through
    /// glTF -> Blender -> Unity by hand.
    /// </summary>
    static Vector3 FindSpawn(GameObject world)
    {
        var candidates = new List<Vector3>();
        for (int x = -30; x <= 30; x += 5)
            for (int z = -30; z <= 30; z += 5)
                candidates.Add(new Vector3(x, 0f, z));

        var flat = new List<Vector3>();
        foreach (var c in candidates)
        {
            var ray = new Ray(new Vector3(c.x, 200f, c.z), Vector3.down);
            if (!Physics.Raycast(ray, out var hit, 400f, ~0, QueryTriggerInteraction.Ignore))
                continue;
            if (!hit.transform.IsChildOf(world.transform) && hit.transform != world.transform)
                continue;
            if (Vector3.Angle(hit.normal, Vector3.up) > 12f) continue;   // must be flat
            flat.Add(hit.point);
        }

        if (flat.Count == 0)
        {
            Debug.LogWarning($"No flat ground found by raycast; using fallback {FallbackSpawn}.");
            return FallbackSpawn;
        }

        // Pick ground at the MEDIAN height, not the lowest. Taking the minimum
        // finds whatever pit or sunken floor is deepest in the scene, which is
        // how the last build spawned at y = -1.70; the median is the level the
        // world actually sits on. Among those, prefer the point nearest origin.
        var sorted = flat.OrderBy(p => p.y).ToList();
        float median = sorted[sorted.Count / 2].y;
        var onLevel = flat.Where(p => Mathf.Abs(p.y - median) < 0.75f).ToList();
        Vector3 best = onLevel
            .OrderBy(p => new Vector2(p.x, p.z).sqrMagnitude)
            .First();

        var spawn = best + Vector3.up * 0.1f;
        Debug.Log($"Spawn {spawn} â€” median ground {median:0.00} " +
                  $"(from {flat.Count} flat probes spanning {sorted[0].y:0.00}..{sorted[^1].y:0.00})");
        return spawn;
    }

    /// <summary>Pendulum is authored 0.912m tall, which puts its hook below the
    /// character's head. Anchors are scaled so the hook lands at a height worth
    /// swinging from.</summary>
    const float AnchorScale = 4.5f;
    /// <summary>Hook height in the model's own units (parts 20/21 sit at 0.907;
    /// the crossbeam it hangs from is 0.880).</summary>
    const float AnchorHookLocalY = 0.88f;

    /// <summary>Latch range, applied to every anchor the setup places so they
    /// cannot drift apart as the value is tuned.</summary>
    const float AnchorRange = 9f;

    /// <summary>
    /// Scatters tether anchors around the spawn. Uses the Foucault pendulum if
    /// it has been imported â€” the hook is a natural attach point and a pendulum
    /// is the right object for a swing mechanic in a maths-themed game â€” and
    /// falls back to a glowing ring so the mechanic is testable regardless.
    /// </summary>
    static void PlaceAnchors(Vector3 spawn)
    {
        string modelPath = FindPropModel("pendulum") ?? FindPropModel("foucault");

        // Ring of anchors at varied heights and distances so there is something
        // to chain between rather than a single test post.
        // Kept inside AnchorRange of each other and of the spawn, so there is
        // always something to reach for and something to chain to. Y is only
        // used by the fallback ring; the pendulum's hook height comes from the
        // model.
        var offsets = new[]
        {
            new Vector3( 5.0f, 4.5f,  4.0f),
            new Vector3(-6.0f, 5.5f,  3.0f),
            new Vector3( 1.5f, 6.0f,  8.0f),
            new Vector3(-3.0f, 4.0f, -5.0f),
        };

        int placed = 0;
        foreach (var off in offsets)
        {
            var root = new GameObject($"TetherAnchor_{placed}");
            Vector3 basePos = spawn + new Vector3(off.x, 0f, off.z);
            if (Physics.Raycast(basePos + Vector3.up * 40f, Vector3.down, out var hit, 90f,
                                ~0, QueryTriggerInteraction.Ignore))
                basePos = hit.point;
            root.transform.position = basePos;

            GameObject visual;
            float hookY;
            if (modelPath != null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                visual.transform.SetParent(root.transform, false);
                visual.transform.localScale = Vector3.one * AnchorScale;
                hookY = AnchorHookLocalY * AnchorScale;
            }
            else
            {
                visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = Vector3.up * off.y;
                visual.transform.localScale = Vector3.one * 0.5f;
                hookY = off.y;
            }
            visual.name = "Visual";
            foreach (var c in visual.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(c);

            var attach = new GameObject("Hook");
            attach.transform.SetParent(root.transform, false);
            attach.transform.localPosition = new Vector3(0f, hookY, 0f);

            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(attach.transform, false);
            var glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.range = 7f;
            glow.color = new Color(0.45f, 0.85f, 1f);
            glow.intensity = 1.2f;

            var anchor = root.AddComponent<TetherAnchor>();
            var aso = new SerializedObject(anchor);
            aso.FindProperty("attachPoint").objectReferenceValue = attach.transform;
            aso.FindProperty("glow").objectReferenceValue = glow;
            aso.FindProperty("maxRange").floatValue = AnchorRange;
            var rend = visual.GetComponentInChildren<Renderer>();
            if (rend != null) aso.FindProperty("highlightRenderer").objectReferenceValue = rend;
            aso.ApplyModifiedPropertiesWithoutUndo();

            EnsureFolder(PrefabDir);
            if (placed == 0)
                PrefabUtility.SaveAsPrefabAssetAndConnect(
                    root, $"{PrefabDir}/TetherAnchor.prefab", InteractionMode.AutomatedAction);
            placed++;
        }
        Debug.Log($"Placed {placed} tether anchor(s)" +
                  (modelPath != null ? $" using {modelPath} at {AnchorScale}x" : " (placeholder rings)"));
    }

    // ------------------------------------------------------- pickups & board

    /// <summary>
    /// Drops an ability pickup on the ground: mesh on a child (so the trigger
    /// stays put while the visual bobs), a tinted point light for the glow, and
    /// a sphere trigger sized to be easy to walk into.
    /// </summary>
    static void PlacePickup(AbilityKind kind, Vector3 near)
    {
        var root = new GameObject($"Pickup_{kind}");

        // Settle it on whatever is underneath rather than trusting the offset.
        Vector3 pos = near + Vector3.up * 0.9f;
        if (Physics.Raycast(near + Vector3.up * 20f, Vector3.down, out var hit, 60f,
                            ~0, QueryTriggerInteraction.Ignore))
            pos = hit.point + Vector3.up * 0.9f;
        root.transform.position = pos;

        var trigger = root.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 0.8f;

        GameObject visual = null;
        string modelPath = kind switch
        {
            AbilityKind.DoubleJump => FindPropModel("feather"),
            AbilityKind.Skateboard => FindPropModel("skateboard"),
            AbilityKind.Dash => FindPropModel("protractor") ?? FindPropModel("compass"),
            AbilityKind.Tether => FindPropModel("pendulum") ?? FindPropModel("foucault"),
            _ => null,
        };
        if (modelPath != null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            // Normalise to a consistent pickup size regardless of authored scale.
            visual.transform.localScale = Vector3.one;
            float span = MeasureBounds(visual).size.magnitude;
            if (span > 0.01f) visual.transform.localScale = Vector3.one * (0.8f / span);
            Debug.Log($"{kind} pickup uses {modelPath}");
        }
        else if (kind == AbilityKind.Skateboard)
        {
            visual = BuildBoardMesh("BoardVisual");
        }

        if (visual == null)     // fall back to something visible
        {
            visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.transform.localScale = Vector3.one * 0.35f;
            Object.DestroyImmediate(visual.GetComponent<Collider>());
        }
        visual.name = "Visual";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = Vector3.zero;
        foreach (var c in visual.GetComponentsInChildren<Collider>())
            Object.DestroyImmediate(c);

        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(root.transform, false);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 4.5f;
        light.color = kind switch
        {
            AbilityKind.DoubleJump => new Color(0.75f, 0.9f, 1f),   // cool, airy
            AbilityKind.Skateboard => new Color(1f, 0.8f, 0.45f),   // warm
            AbilityKind.Dash => new Color(0.55f, 1f, 0.8f),         // sharp mint
            AbilityKind.Tether => new Color(0.5f, 0.7f, 1f),        // deep blue
            _ => Color.white,
        };
        light.intensity = 2f;

        var pickup = root.AddComponent<AbilityPickup>();
        var pso = new SerializedObject(pickup);
        pso.FindProperty("ability").enumValueIndex = (int)kind;
        pso.FindProperty("visual").objectReferenceValue = visual.transform;
        pso.FindProperty("glowLight").objectReferenceValue = light;
        pso.ApplyModifiedPropertiesWithoutUndo();

        EnsureFolder(PickupDir);
        PrefabUtility.SaveAsPrefabAssetAndConnect(
            root, $"{PickupDir}/Pickup_{kind}.prefab", InteractionMode.AutomatedAction);

        Debug.Log($"Pickup_{kind} at {root.transform.position} (prefabbed)");
    }

    /// <summary>Attaches the ride-able board under the player, hidden until
    /// mounted. Uses a real skateboard model if one has been imported, and a
    /// primitive stand-in otherwise so the mechanic is testable meanwhile.</summary>
    static bool BuildBoard(GameObject player, float height)
    {
        var holder = new GameObject("Skateboard");
        holder.transform.SetParent(player.transform, false);
        holder.transform.localPosition = Vector3.zero;

        var mount = holder.AddComponent<SkateboardMount>();

        string modelPath = FindPropModel("skateboard");
        GameObject visual;
        var wheels = new List<Transform>();
        float wheelRadius = 0.035f;

        if (modelPath != null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            visual.name = "BoardVisual";
            visual.transform.SetParent(holder.transform, false);
            visual.transform.localPosition = Vector3.zero;
            wheelRadius = RigWheels(visual, wheels);
            Debug.Log($"Skateboard model: {modelPath} â€” {wheels.Count} wheel(s) rigged");
        }
        else
        {
            visual = BuildBoardMesh("BoardVisual", wheels);
            visual.transform.SetParent(holder.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.06f, 0.05f);
            Debug.Log("No skateboard model found â€” using a primitive stand-in.");
        }

        foreach (var c in visual.GetComponentsInChildren<Collider>())
            Object.DestroyImmediate(c);

        // Where the feet actually land: the top of the deck. Measured rather
        // than guessed, so a different board just works.
        var bounds = MeasureBounds(visual);
        float deckTop = bounds.max.y - holder.transform.position.y;
        RideHeight = Mathf.Clamp(deckTop, 0.02f, 0.5f);

        var mso = new SerializedObject(mount);
        mso.FindProperty("boardVisual").objectReferenceValue = visual;
        mso.FindProperty("wheelRadius").floatValue = wheelRadius;
        var wheelsProp = mso.FindProperty("wheels");
        wheelsProp.arraySize = wheels.Count;
        for (int i = 0; i < wheels.Count; i++)
            wheelsProp.GetArrayElementAtIndex(i).objectReferenceValue = wheels[i];
        mso.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log($"Board deck top {RideHeight:0.000} â€” player raised by that much while riding.");
        return true;
    }

    /// <summary>Height of the deck's standing surface, filled in by BuildBoard.</summary>
    static float RideHeight = 0.2f;

    /// <summary>
    /// Give each wheel its own pivot so it spins in place.
    ///
    /// Tripo puts every segmented part's origin at the MODEL origin, so
    /// rotating a wheel object swings it around the deck like a wrecking ball.
    /// The fix is an empty placed at the wheel's own centre, with the wheel
    /// re-parented under it; rotating the empty then spins the wheel about its
    /// axle. Returns the measured wheel radius.
    /// </summary>
    static float RigWheels(GameObject board, List<Transform> wheelsOut)
    {
        var parts = board.GetComponentsInChildren<MeshRenderer>();
        if (parts.Length < 2) return 0.035f;

        var whole = MeasureBounds(board);
        float halfWidth = Mathf.Max(whole.extents.x, 0.0001f);
        float bottom = whole.min.y;
        float height = Mathf.Max(whole.size.y, 0.0001f);

        // Wheels are small, sit low, and â€” the discriminator that matters â€”
        // are offset laterally. Trucks and hardware sit on the centreline.
        var candidates = parts
            .Select(r => (r, b: r.bounds))
            .Where(t => t.b.center.y < bottom + height * 0.55f)
            .Where(t => t.b.size.magnitude < whole.size.magnitude * 0.3f)
            .Where(t => Mathf.Abs(t.b.center.x - whole.center.x) > halfWidth * 0.25f)
            .OrderBy(t => t.b.size.magnitude)
            .Take(4)
            .ToList();

        if (candidates.Count == 0)
        {
            Debug.LogWarning("No wheels identified on the board; it will not spin.");
            return 0.035f;
        }

        float radius = candidates.Average(t => Mathf.Max(t.b.extents.y, t.b.extents.z));
        foreach (var (renderer, b) in candidates)
        {
            var pivot = new GameObject($"WheelPivot_{renderer.name}");
            pivot.transform.SetParent(board.transform, false);
            pivot.transform.position = b.center;
            pivot.transform.rotation = board.transform.rotation;
            renderer.transform.SetParent(pivot.transform, true);   // keep world pose
            wheelsOut.Add(pivot.transform);
        }
        Debug.Log($"Wheel radius measured at {radius:0.000}");
        return Mathf.Max(radius, 0.005f);
    }

    /// <summary>Primitive skateboard: a deck and four wheels, each wheel its own
    /// object so it is pivoted at its axle and spins in place.</summary>
    static GameObject BuildBoardMesh(string name, List<Transform> wheelsOut = null)
    {
        var root = new GameObject(name);

        var deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
        deck.name = "Deck";
        deck.transform.SetParent(root.transform, false);
        deck.transform.localScale = new Vector3(0.24f, 0.03f, 0.85f);
        Object.DestroyImmediate(deck.GetComponent<Collider>());

        foreach (var (x, z) in new[] { (0.1f, 0.28f), (-0.1f, 0.28f), (0.1f, -0.28f), (-0.1f, -0.28f) })
        {
            var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wheel.name = "Wheel";
            wheel.transform.SetParent(root.transform, false);
            // Cylinder's axis is Y, so lay it on its side to roll about X.
            wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            wheel.transform.localScale = new Vector3(0.07f, 0.02f, 0.07f);
            wheel.transform.localPosition = new Vector3(x, -0.05f, z);
            Object.DestroyImmediate(wheel.GetComponent<Collider>());
            wheelsOut?.Add(wheel.transform);
        }
        return root;
    }

    /// <summary>Find a prop model by keyword anywhere under Props, rather than
    /// by an exact folder name. Tripo's naming is inconsistent enough
    /// ("feather+3d+model" vs "skateboard 3d model") that pinning an exact
    /// path is how the feather silently became a cube.</summary>
    static string FindPropModel(string keyword)
    {
        if (!AssetDatabase.IsValidFolder(PropsDir)) return null;
        return AssetDatabase.FindAssets("t:Model", new[] { PropsDir })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.ToLowerInvariant().Contains(keyword.ToLowerInvariant()))
            .OrderBy(p => p.Length)      // prefer the model over deeper variants
            .FirstOrDefault();
    }

    static Bounds MeasureBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    // ------------------------------------------------------------------ helpers

    // Reusable entry points for other character setups (NewtonBossSetup).
    // Exposed rather than duplicated: the Tripo bone map, rig configuration and
    // texture extraction are identical for every character their rigger emits.
    public static void EnsureFolderPublic(string dir) => EnsureFolder(dir);
    public static void SetupMaterialPublic(ModelImporter importer, string modelPath,
                                           string artDir, string materialName) =>
        SetupMaterial(importer, modelPath, artDir, materialName);
    public static bool SetupRigPublic(ModelImporter importer, float targetHeight) =>
        SetupRig(importer, targetHeight);

    static void EnsureFolder(string dir)
    {
        if (AssetDatabase.IsValidFolder(dir)) return;
        string parent = Path.GetDirectoryName(dir)!.Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(dir));
    }

    static T LoadAsset<T>(string path) where T : Object =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<T>().FirstOrDefault();
}
