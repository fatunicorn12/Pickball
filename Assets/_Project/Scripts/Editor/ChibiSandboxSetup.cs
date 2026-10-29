using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pickleball.Sandbox;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;

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

    // FALLBACK blend thresholds only. BuildController measures each clip's
    // stride on the chibi himself (MeasureStrideSpeed) and uses that; these
    // source-rig numbers apply only if the measurement fails. They are NOT
    // right for the chibi: retargeting scales travel by his humanScale (0.72),
    // and measured on him the armed walk strides 0.98 m/s and the armed run
    // 2.57 m/s — so a 4.84 run threshold left the feet skating at ~1.9x.
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
    // Travel speeds and the model's Y offset (0.64, the centre-of-mass lift)
    // used to be constants here, written onto ChibiLocomotion on every run. They
    // are tunables on the MovementConfig / MovementAnimationConfig assets now,
    // which a re-run never overwrites.

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
        // Run To Dive (Mixamo X Bot, 37 frames): 15-20 is the Sunshine-style
        // dive -- body flat, both hands straight ahead and together, legs
        // scissored -- held on its last frame. Past ~22 it pitches into a roll,
        // so it is never played further. Its .meta carries the canonical X Bot
        // reference skeleton from the clip audit; without it the knees and feet
        // play 30-40 deg off.
        ("dive",       ClipDir + "/Run To Dive.fbx",         false, 15, 20),
        // Belly Slide Pose Wide: an authored two-frame pose on the same X Bot
        // skeleton (arms ahead in a shallow V, 17 deg out and 6 deg up; torso
        // flat, head up, legs together, knees bent), held for the whole slide.
        // The V puts the hands beside the big head instead of under it, so the
        // arms read from the side and front. Its .meta carries the same
        // canonical reference skeleton as Run To Dive.
        ("bellySlide", ClipDir + "/Belly Slide Pose Wide.fbx", false, 0,  1),
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

    /// <summary>Roles whose root height is baked into the pose (grounded
    /// cycles). Measured with these settings, feet sit within 7 cm of the
    /// ground: idle -0.3 cm, run -7 cm, stop -6 cm.</summary>
    static readonly HashSet<string> BakedHeightRoles = new() { "idle", "walk", "run", "stop" };

    /// <summary>
    /// Roles whose horizontal root motion is baked into the pose. The idle only:
    /// its hips sway is otherwise stripped out as root motion (which is then
    /// discarded), so the body is held still and the planted feet swim around
    /// the floor by up to 4 cm over the loop. Baking it keeps the sway in the
    /// hips and the feet where they were put. Walk and run must stay unbaked —
    /// their forward travel would then drift the cycle off and snap it back.
    /// Matches the idle clip audit's meta fix, so a rebuild keeps it.
    /// </summary>
    static readonly HashSet<string> BakedXZRoles = new() { "idle" };

    /// <summary>Per-role root height offset (Animation ▸ Root Transform Position
    /// (Y) ▸ Offset), from the idle clip audit: +2 cm on the idle settles both
    /// soles at 0–1.8 cm with ModelYOffset left at 0.64.</summary>
    static readonly Dictionary<string, float> HeightOffsets = new() { { "idle", 0.02f } };

    /// <summary>Roles imported with Loop Pose — the genuine cycles.</summary>
    static readonly HashSet<string> LoopPoseRoles = new() { "idle", "walk", "crouchWalk" };

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
        ("Skateboarding (1).fbx", ClipDir), ("Run To Dive.fbx", ClipDir),
        ("Belly Slide Pose Wide.fbx", ClipDir),
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
    /// <summary>An older 6-state copy of the locomotion controller from before
    /// controllers moved under Animation/Controllers. Nothing references it
    /// (Chibi.prefab uses Controllers/ChibiLocomotion.controller); it only
    /// invited editing the wrong file.</summary>
    const string StaleRootControllerPath = "Assets/_Project/Animation/ChibiLocomotion.controller";
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
                // Matches the per-clip import pass done on the metas by hand;
                // re-running the setup must not undo it.
                //   loopPose on the true cycles only, so their seams blend.
                loopPose = LoopPoseRoles.Contains(d.role),
                // Root motion is disabled on the Animator, so anything NOT
                // baked into pose is discarded, and the CharacterController
                // owns all travel:
                //   XZ not baked -> cycles play in place instead of drifting
                //                   forward and snapping back each loop —
                //                   except the idle (BakedXZRoles), whose
                //                   only XZ motion is the hips sway.
                //   Rotation BAKED (all roles) -> the clip's own heading drift
                //                   stays in the pose instead of being thrown
                //                   away and snapping at every loop seam.
                //   Y baked on the grounded cycles only (BakedHeightRoles), so
                //                   their bob stays in the pose; NOT baked on
                //                   jumps/landings/crouches, so a jump's arc
                //                   comes from the controller alone.
                lockRootRotation = true,
                lockRootHeightY = BakedHeightRoles.Contains(d.role),
                lockRootPositionXZ = BakedXZRoles.Contains(d.role),
                heightOffset = HeightOffsets.TryGetValue(d.role, out float offset) ? offset : 0f,
                // Based Upon: Y is the CENTER OF MASS (keepOriginalPositionY
                // false, heightFromFeet false) — the root is pinned at the
                // body's COM, so the model sinks by about its hip height.
                // That, not the chibi's short legs, is what ModelYOffset
                // (0.64) compensates for.
                keepOriginalOrientation = true,
                keepOriginalPositionY = false,
                heightFromFeet = false,
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

    /// <summary>Blend thresholds actually used by the last BuildController run
    /// — measured on the chibi where possible — so BuildScene can hand the same
    /// numbers to ChibiLocomotion.</summary>
    static float _walkStride = WalkSpeed, _runStride = RunSpeed, _crouchStride = CrouchWalkSpeed;

    /// <summary>
    /// Stride speed of a locomotion clip on the chibi himself, falling back to
    /// the source-rig constant if it cannot be measured. See MeasureStrideSpeed
    /// for why the source-rig numbers are not enough on chibi proportions.
    /// </summary>
    static float StrideOnChibi(AnimationClip clip, float fallback)
    {
        var avatar = LoadAsset<Avatar>(ModelPath);
        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (clip == null || avatar == null || modelAsset == null) return fallback;
        var go = Object.Instantiate(modelAsset);
        go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var anim = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            anim.avatar = avatar;
            anim.Rebind();
            float v = MeasureStrideSpeed(anim, clip, out _);
            return v > 0.05f ? v : fallback;
        }
        finally { Object.DestroyImmediate(go); }
    }

    static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
    {
        // In place, so the GUID Chibi.prefab references survives the rebuild.
        var controller = ResetControllerInPlace(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("SpeedMultiplier", AnimatorControllerParameterType.Float);
        controller.AddParameter("VerticalSpeed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Stop", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Flip", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Skating", AnimatorControllerParameterType.Bool);
        controller.AddParameter("FlipSpeed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Dash", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Tethered", AnimatorControllerParameterType.Bool);
        // Kept for compatibility; the gesture is now crossfaded by name.
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
        // Thresholds are the strides measured ON THE CHIBI. The source-rig
        // speeds (WalkSpeed/RunSpeed) are kept only as the fallback: humanoid
        // retargeting scales stride to the target's legs, and the chibi's are
        // far shorter than Mixamo's, so a threshold taken off the source rig
        // puts the blend at a clip that strides slower than he travels.
        clips.TryGetValue("run", out var run);
        clips.TryGetValue("crouchWalk", out var cWalkClip);
        _walkStride = StrideOnChibi(clips["walk"], WalkSpeed);
        _runStride = run != null ? StrideOnChibi(run, RunSpeed) : RunSpeed;
        _crouchStride = cWalkClip != null ? StrideOnChibi(cWalkClip, CrouchWalkSpeed) : CrouchWalkSpeed;
        if (_runStride <= _walkStride) _runStride = RunSpeed;   // measurement nonsense guard

        var loco = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(clips["idle"], 0f);
        tree.AddChild(clips["walk"], _walkStride);
        if (run != null) tree.AddChild(run, _runStride);
        // ChibiLocomotion raises SpeedMultiplier above 1 once travel outruns
        // the top threshold, so sprinting past the run clip's stride speeds the
        // cycle up instead of skating.
        loco.speedParameterActive = true;
        loco.speedParameter = "SpeedMultiplier";
        sm.defaultState = loco;
        Debug.Log($"Locomotion thresholds (measured on chibi): walk {_walkStride:0.00}, " +
                  $"run {_runStride:0.00}, crouch walk {_crouchStride:0.00} m/s");

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
            if (cWalkClip != null) cTree.AddChild(cWalkClip, _crouchStride);
            crouch.speedParameterActive = true;
            crouch.speedParameter = "SpeedMultiplier";
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
            jTree.AddChild(rjClip, _runStride);
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

        // Transitions out of the grounded states may be cut short by a
        // higher-priority transition of the same state (ordered interruption):
        // a jump pressed during the 0.12s blend into Run To Stop should not
        // have to wait for that blend to finish.
        AnimatorStateTransition Go(AnimatorState from, AnimatorState to, float duration)
        {
            var tr = from.AddTransition(to);
            tr.hasExitTime = false;
            tr.duration = duration;
            tr.interruptionSource = TransitionInterruptionSource.Source;
            tr.orderedInterruption = true;
            return tr;
        }

        // ---- transitions ---------------------------------------------------
        // Jump is a trigger, so it must not wait for exit time.
        var t = Go(loco, jump, 0.05f);
        t.AddCondition(AnimatorConditionMode.If, 0, "Jump");

        // Walked off a ledge (falling, not jumping).
        t = Go(loco, fall, 0.15f);
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
        t.AddCondition(AnimatorConditionMode.Less, -0.1f, "VerticalSpeed");

        // Past the apex -> fall.
        t = Go(jump, fall, 0.12f);
        t.AddCondition(AnimatorConditionMode.Less, -0.1f, "VerticalSpeed");
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");

        // Short hop that lands before the apex condition fires.
        t = Go(jump, land, 0.08f);
        t.AddCondition(AnimatorConditionMode.If, 0, "Grounded");

        // Coyote jump: walked off the ledge, pressed Jump inside the grace
        // window. The physics jump happens; without this the trigger had no
        // consumer in Fall and fired a jump animation after landing instead.
        t = Go(fall, jump, 0.05f);
        t.AddCondition(AnimatorConditionMode.If, 0, "Jump");

        t = Go(fall, land, 0.06f);
        t.AddCondition(AnimatorConditionMode.If, 0, "Grounded");

        // Landing recovery: re-jumpable at once, cut short by fresh input (a
        // running landing should not plant the legs for half a second while
        // the body slides on at speed), otherwise played out.
        t = Go(land, jump, 0.05f);
        t.AddCondition(AnimatorConditionMode.If, 0, "Jump");

        t = Go(land, loco, 0.15f);
        t.AddCondition(AnimatorConditionMode.If, 0, "Moving");

        t = land.AddTransition(loco);
        t.hasExitTime = true; t.exitTime = 0.55f; t.duration = 0.18f;

        // ---- run-to-stop ---------------------------------------------------
        if (clips.TryGetValue("stop", out var stopClip))
        {
            var stop = sm.AddState("Stop");
            stop.motion = stopClip;

            t = Go(loco, stop, 0.12f);
            t.AddCondition(AnimatorConditionMode.If, 0, "Stop");

            t = Go(stop, jump, 0.05f);
            t.AddCondition(AnimatorConditionMode.If, 0, "Jump");

            t = Go(stop, fall, 0.15f);
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");

            // Moving again cancels the stop immediately — responsiveness beats
            // playing the plant out. Keyed on INPUT (Moving), not on Speed:
            // the stop is triggered above 3 m/s and decelerates through every
            // speed threshold on its own, so the old "Speed > 0.6" exit was
            // already true the moment the state was entered and bounced him
            // straight back into the run — the plant never actually played.
            t = Go(stop, loco, 0.15f);
            t.AddCondition(AnimatorConditionMode.If, 0, "Moving");

            // The character is at rest 54% in; the tail is just settling.
            t = stop.AddTransition(loco);
            t.hasExitTime = true; t.exitTime = 0.62f; t.duration = 0.25f;
        }
        var stopState = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Stop");

        // ---- front flip: ground trick AND the feather's double jump ---------
        AnimatorState flip = null;
        if (clips.TryGetValue("flip", out var flipClip))
        {
            flip = sm.AddState("FrontFlip");
            flip.motion = flipClip;

            // Ground trick (F): the full clip, wind-up and landing included.
            // Reachable from every grounded standing state, so a press during a
            // landing or a stop is not left latched to fire later.
            foreach (var from in new[] { loco, land, stopState })
            {
                if (from == null) continue;
                t = Go(from, flip, 0.08f);
                t.AddCondition(AnimatorConditionMode.If, 0, "Flip");
            }

            // A real jump cancels the trick, and so does walking off a ledge.
            t = Go(flip, jump, 0.06f);
            t.AddCondition(AnimatorConditionMode.If, 0, "Jump");
            t = Go(flip, fall, 0.15f);
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
            t.AddCondition(AnimatorConditionMode.Less, -0.1f, "VerticalSpeed");

            t = flip.AddTransition(loco);
            t.hasExitTime = true; t.exitTime = 0.88f; t.duration = 0.18f;
        }

        // Air variant for the double jump: only the airborne portion, and time
        // scaled by the script so it finishes exactly as he lands. Matching it
        // by jump height instead would need a 3.24m leap.
        AnimatorState flipAir = null;
        if (clips.TryGetValue("flipAir", out var flipAirClip))
        {
            flipAir = sm.AddState("FlipAir");
            flipAir.motion = flipAirClip;
            flipAir.speedParameterActive = true;
            flipAir.speedParameter = "FlipSpeed";

            foreach (var from in new[] { jump, fall })
            {
                t = Go(from, flipAir, 0.06f);
                t.AddCondition(AnimatorConditionMode.If, 0, "Flip");
            }

            t = Go(flipAir, land, 0.08f);
            t.AddCondition(AnimatorConditionMode.If, 0, "Grounded");

            t = flipAir.AddTransition(fall);
            t.hasExitTime = true; t.exitTime = 0.97f; t.duration = 0.12f;
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
        }

        // ---- dash -------------------------------------------------------------
        AnimatorState dash = null;
        if (clips.TryGetValue("dashPose", out var dashClip))
        {
            dash = sm.AddState("Dash");
            dash.motion = dashClip;

            // From ANY state. The dash can start mid-stop, mid-flip, mid-landing
            // and mid-air-flip as well as from locomotion and the air states;
            // per-state transitions missed most of those, and the unconsumed
            // trigger then played a dash pose out of nowhere on the next return
            // to Locomotion. (Tether and crouch never reach here: the code
            // refuses to dash in either.)
            t = sm.AddAnyStateTransition(dash);
            t.hasExitTime = false; t.duration = 0.04f;   // snap in, no ramp
            t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0, "Dash");

            t = Go(dash, land, 0.08f);
            t.AddCondition(AnimatorConditionMode.If, 0, "Grounded");

            // Double jump straight out of an air dash.
            if (flipAir != null)
            {
                t = Go(dash, flipAir, 0.06f);
                t.AddCondition(AnimatorConditionMode.If, 0, "Flip");
            }

            t = dash.AddTransition(fall);
            t.hasExitTime = true; t.exitTime = 0.9f; t.duration = 0.12f;
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
        }

        // ---- tether ----------------------------------------------------------
        AnimatorState hang = null;
        if (clips.TryGetValue("hang", out var hangClip))
        {
            hang = sm.AddState("Tethered");
            hang.motion = hangClip;

            // Any state can be interrupted by latching on.
            t = sm.AddAnyStateTransition(hang);
            t.hasExitTime = false; t.duration = 0.18f;
            t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0, "Tethered");

            t = Go(hang, fall, 0.15f);
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "Tethered");
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");

            t = Go(hang, loco, 0.15f);
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "Tethered");
            t.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
        }

        // ---- skateboard ------------------------------------------------------
        AnimatorState skate = null;
        if (clips.TryGetValue("skate", out var skateClip))
        {
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

            t = Go(loco, skate, 0.25f);
            t.AddCondition(AnimatorConditionMode.If, 0, "Skating");

            t = Go(skate, loco, 0.25f);
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "Skating");
        }

        t = Go(loco, crouch, 0.15f);
        t.AddCondition(AnimatorConditionMode.If, 0, "Crouch");

        t = Go(crouch, loco, 0.2f);
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "Crouch");

        // Foot IK on the planted, upright states only. The chibi's legs are far
        // shorter relative to its body than the Mixamo source rig's, so humanoid
        // retargeting places the hips too low and the feet penetrate the floor;
        // Foot IK re-plants them after retargeting. Everywhere the feet are
        // off the ground — airborne, mid-flip (feet over his head), dashing,
        // hanging off the rope, stood on a deck — pinning them to their
        // ground-relative goals drags the legs, so it stays off there.
        //
        // This loop used to run last and set IK on for every state that was
        // not Jump or Fall, silently overwriting the per-state "off" each of
        // those states had just been given.
        //
        // Crouch is off too: measured, its Foot IK goals float the crouched
        // soles ~28 cm (+0.277 m with IK against +0.063 m without).
        var noFootIK = new HashSet<AnimatorState> { jump, fall, flip, flipAir, dash, hang, skate, crouch };

        // ---- Sunshine moves (Sandbox/Movement) --------------------------------
        // Skid, jump-chain variants, spin, dive and belly slide, driven by the
        // int Move / JumpKind parameters. All airborne or prone (the skid spins
        // the body through 180° over planted feet), so foot IK is off for each.
        noFootIK.UnionWith(MovementSetup.AddMovementStates(controller, clips, loco, fall, land, flipAir));

        foreach (var s in sm.states)
            s.state.iKOnFeet = !noFootIK.Contains(s.state);

        BuildGestureLayer(controller, clips);

        // OnAnimatorIK is only called when the LAYER has an IK pass. This is a
        // different setting from the per-state "Foot IK" flag above, and
        // without it ChibiFootIK silently never runs.
        var layers = controller.layers;
        layers[0].iKPass = true;
        controller.layers = layers;

        EditorUtility.SetDirty(controller);
        Debug.Log($"Controller: {ControllerPath} — locomotion({tree.children.Length}), " +
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
        empty.iKOnFeet = fire.iKOnFeet = false;
        gestureSm.defaultState = empty;

        // No transitions. ChibiLocomotion crossfades into Fire by name and
        // then fades the layer weight out while Fire holds its last frame.
        // The old Fire -> None exit blended the arm toward the empty state's
        // bind pose at FULL layer weight for its last 0.15s plus the fade —
        // a T-pose flick on the arm at the end of every hook throw.

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
        foreach (var stale in new[] { OldScenePath, OldControllerPath, OldWorldFbx, StaleRootControllerPath })
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
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (model.GetComponent<ChibiFootIK>() == null)
            model.AddComponent<ChibiFootIK>();
        // Spring-cloth chains on the coat/cloak; needs the avatar assigned
        // above, and must land before the prefab is saved below.
        ClothSetup.Apply(model);

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

        // Movement components (motor, input, animator driver, effects) are added
        // and wired by MovementSetup.WirePlayer once the effects they reference
        // exist, below. Tunables live on the MovementConfig assets now, not here:
        // speeds on MovementConfig, model offset and strides on
        // MovementAnimationConfig (the measured strides are written to it).
        go.AddComponent<ChibiLocomotion>();

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
        }

        // ---- abilities, board and VFX ---------------------------------------
        go.AddComponent<PlayerAbilities>();

        EnsureFolder(VfxDir);
        EnsureFolder(PickupDir);
        var puffPrefab = SandboxVfxBuilder.BuildCloudPuff(VfxDir, $"{PickupDir}/CloudPuff.prefab");
        if (puffPrefab != null)
        {
            var puff = (GameObject)PrefabUtility.InstantiatePrefab(puffPrefab.gameObject);
            // Found by this name by MovementSetup.WirePlayer.
            puff.name = "DoubleJumpPuff";
            puff.transform.SetParent(go.transform, false);
        }

        var board = BuildBoard(go, height);

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

        // ---- movement ----------------------------------------------------------
        // Motor, input, animator driver and effects, wired to everything above.
        MovementSetup.WirePlayer(go, cam != null ? cam.transform : null);
        MovementSetup.EnsureConfigs(out _, out var animConfig);
        MovementSetup.WriteMeasuredValues(animConfig, _runStride, _crouchStride, RideHeight);

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

    // ------------------------------------------------------- animator rebuild

    /// <summary>
    /// Empties the controller at <paramref name="path"/> IN PLACE and hands it
    /// back with a single blank "Base Layer", creating it only if missing.
    ///
    /// CreateAnimatorControllerAtPath replaces the asset, and with it the only
    /// guarantee that Chibi.prefab and NewtonBoss.prefab (which reference the
    /// controller by GUID) still point at it afterwards. Clearing the existing
    /// asset keeps the GUID by construction, and destroying every sub-asset
    /// first means no orphaned state machines or blend trees pile up inside the
    /// .controller across re-runs.
    /// </summary>
    public static AnimatorController ResetControllerInPlace(string path)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (controller == null)
            return AnimatorController.CreateAnimatorControllerAtPath(path);

        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(path))
            if (sub != null && sub != controller && !(sub is AnimatorController))
                Object.DestroyImmediate(sub, true);

        controller.layers = new AnimatorControllerLayer[0];
        controller.parameters = new AnimatorControllerParameter[0];
        controller.AddLayer("Base Layer");
        EditorUtility.SetDirty(controller);
        return controller;
    }

    /// <summary>
    /// Rebuilds ONLY the chibi's Animator controller — base locomotion, the
    /// gesture layer and the sword combo layer — from the clips as they are
    /// currently imported. Touches no import settings and no scene, so it is
    /// the safe way to pick up a controller change without re-running the
    /// full sandbox setup (which reimports every clip and rebuilds the scene).
    /// </summary>
    [MenuItem("Tools/Pickleball/Rebuild Chibi Animator")]
    public static void RebuildAnimator()
    {
        var clips = new Dictionary<string, AnimationClip>();
        foreach (var (role, file, _, _, _) in ClipDefs)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(file).OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == role);
            if (clip != null) clips[role] = clip;
            else Debug.LogWarning($"Rebuild Chibi Animator: no '{role}' clip in {file}");
        }
        if (!clips.ContainsKey("idle") || !clips.ContainsKey("walk"))
        {
            Debug.LogError("Rebuild Chibi Animator: idle/walk missing — run Setup Chibi Sandbox.");
            return;
        }
        BuildController(clips);
        WeaponSetup.RewireControllerFromImportedClips();
        AssetDatabase.SaveAssets();
        Debug.Log("Chibi animator rebuilt in place (GUID preserved).");
    }

    /// <summary>Run stride the last BuildController measured on the chibi, m/s.</summary>
    public static float MeasuredRunStride => _runStride;
    /// <summary>Crouch-walk stride the last BuildController measured, m/s.</summary>
    public static float MeasuredCrouchStride => _crouchStride;

    /// <summary>
    /// Re-imports every clip in the role table (import settings are idempotent)
    /// and rebuilds the chibi controller in place, weapon layer included —
    /// without touching the scene. The movement setup uses this to pick up new
    /// clip roles and states. Returns false if the base clips are missing.
    /// </summary>
    public static bool ImportClipsAndRebuildAnimator()
    {
        var clips = new Dictionary<string, AnimationClip>();
        foreach (var group in ClipDefs.GroupBy(d => d.file))
            foreach (var kv in SetupClips(group.Key, group.ToArray()))
                clips[kv.Key] = kv.Value;
        if (!clips.ContainsKey("walk") || !clips.ContainsKey("idle"))
        {
            Debug.LogError("Armed idle/walk produced no clips — cannot build locomotion. " +
                           $"Is the sword-and-shield pack at {PackDir}?");
            return false;
        }
        BuildController(clips);
        WeaponSetup.RewireControllerFromImportedClips();
        AssetDatabase.SaveAssets();
        return true;
    }

    /// <summary>Command-line entry point (-executeMethod): rebuilds both
    /// characters' controllers in place and logs the measured strides.</summary>
    public static void BatchRebuildAnimators()
    {
        NewtonBossSetup.RebuildAnimator();
        RebuildAnimator();
        ReportStrideSpeeds();
    }

    // ------------------------------------------------------------ diagnostics

    /// <summary>
    /// Ground speed a locomotion cycle travels at ON A GIVEN CHARACTER, in m/s
    /// — the number a blend-tree threshold has to equal for the feet not to
    /// slide.
    ///
    /// Measured on the character rather than read off the source FBX.
    /// Humanoid retargeting rescales travel to the target's proportions (by
    /// Animator.humanScale: 0.72 for the chibi, against ~1 for a Mixamo rig),
    /// so a speed measured on the Mixamo skeleton — which is what the old
    /// WalkSpeed/RunSpeed constants were — is only right for a character with
    /// Mixamo proportions. The clip is sampled onto the character with
    /// AnimationClip.SampleAnimation, which applies the clip's root motion to
    /// the body: the hips' horizontal travel over one cycle is exactly the
    /// distance the planted feet stay still over. (A PlayableGraph evaluated
    /// manually in edit mode does not write the pose at all, so it cannot be
    /// used for this.) Also returns when either hand is furthest from the
    /// hips, which is how a cast's peak is found.
    /// </summary>
    public static float MeasureStrideSpeed(Animator animator, AnimationClip clip,
                                           out float handPeakTime)
    {
        handPeakTime = 0f;
        if (animator == null || clip == null || !animator.isHuman || clip.length <= 0f) return 0f;

        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var lh = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        var rh = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (hips == null) return 0f;

        var root = animator.transform;
        Vector3 rootPos = root.localPosition;
        Quaternion rootRot = root.localRotation;
        try
        {
            const int samples = 120;
            float bestReach = -1f;
            Vector3 start = Vector3.zero, end = Vector3.zero;
            for (int i = 0; i <= samples; i++)
            {
                float time = clip.length * i / samples * 0.999f;
                root.localPosition = rootPos;
                root.localRotation = rootRot;
                clip.SampleAnimation(animator.gameObject, time);
                if (i == 0) start = hips.position;
                if (i == samples) end = hips.position;
                if (lh != null && rh != null)
                {
                    float reach = Mathf.Max(Vector3.Distance(lh.position, hips.position),
                                            Vector3.Distance(rh.position, hips.position));
                    if (reach > bestReach) { bestReach = reach; handPeakTime = time; }
                }
            }
            var travel = end - start;
            travel.y = 0f;
            return travel.magnitude / (clip.length * 0.999f);
        }
        finally
        {
            root.localPosition = rootPos;
            root.localRotation = rootRot;
        }
    }

    /// <summary>Logs every locomotion clip's measured stride speed on the
    /// chibi and on Newton, next to the thresholds currently in use.</summary>
    [MenuItem("Tools/Pickleball/Diagnostics/Measure Clip Stride Speeds")]
    public static void ReportStrideSpeeds()
    {
        void Report(string prefabPath, IEnumerable<(string label, string file, string clipName)> clips)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) { Debug.LogWarning($"Stride probe: no {prefabPath}"); return; }
            var go = (GameObject)Object.Instantiate(prefab);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            try
            {
                var anim = go.GetComponentInChildren<Animator>();
                anim.Rebind();
                var lines = new List<string>();
                foreach (var (label, file, clipName) in clips)
                {
                    var clip = AssetDatabase.LoadAllAssetsAtPath(file).OfType<AnimationClip>()
                        .FirstOrDefault(c => clipName == null ? !c.name.StartsWith("__preview__")
                                                              : c.name == clipName);
                    if (clip == null) { lines.Add($"  {label,-12} (missing)"); continue; }
                    float v = MeasureStrideSpeed(anim, clip, out float peak);
                    lines.Add($"  {label,-14} len {clip.length:0.00}s  stride {v:0.00} m/s  " +
                              $"(averageSpeed x humanScale {clip.averageSpeed.magnitude * anim.humanScale:0.00})  " +
                              $"hand-reach peak {peak:0.00}s");
                }
                Debug.Log($"[StrideProbe] {prefabPath} humanScale={anim.humanScale:0.000} " +
                          $"lossyScale={anim.transform.lossyScale.y:0.000}\n" + string.Join("\n", lines));
            }
            finally { Object.DestroyImmediate(go); }
        }

        Report($"{PrefabDir}/Chibi.prefab", new (string, string, string)[]
        {
            ("walk", PackDir + "/sword and shield walk.fbx", "walk"),
            ("run", PackDir + "/sword and shield run.fbx", "run"),
            ("crouchWalk", ClipDir + "/Crouched Walking.fbx", "crouchWalk"),
            ("skatePush", ClipDir + "/Skateboarding (1).fbx", "skatePush"),
            ("walk(unarmed)", ClipDir + "/Walking.fbx", "walk"),
            ("run(unarmed)", ClipDir + "/Running.fbx", "run"),
        });
        Report($"{PrefabDir}/NewtonBoss.prefab", new (string, string, string)[]
        {
            ("walk", "Assets/_Project/Animation/Newton/Walking.fbx", null),
            ("walk(chibi)", ClipDir + "/Walking.fbx", "walk"),
            ("throw", "Assets/_Project/Animation/Newton/Throw.fbx", null),
            ("cast", "Assets/_Project/Animation/Newton/Cast.fbx", null),
            ("cast2", "Assets/_Project/Animation/Newton/Cast2.fbx", null),
            ("powerUp", "Assets/_Project/Animation/Newton/PowerUp.fbx", null),
            ("hit", "Assets/_Project/Animation/Newton/Hit.fbx", null),
            ("death", "Assets/_Project/Animation/Newton/Death.fbx", null),
        });
    }
}
