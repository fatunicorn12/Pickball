using System.Collections.Generic;
using System.Linq;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Combat;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools ▸ Pickleball ▸ Setup Ruler Moveset. Builds the ruler sword's own combo —
/// big, slower swings that hit harder — idempotently:
///
///  1. <b>Clips</b>: imports the four sword-pack FBXs it uses as Humanoid, with
///     WeaponSetup.ImportAttackClips' settings, and cuts named <c>Ruler_*</c> takes out
///     of them (clip start and end) where a wind-up, a second strike or a recovery
///     was too long. Reimports only when a setting differs.
///  2. <b>Data</b>: seeds <c>Moveset_Ruler.asset</c> and <c>Steps/Ruler/Step_Ruler*.asset</c>.
///     As with Setup Sword Abilities, a seed is written only when its asset is missing;
///     after that the asset is the source of truth and re-running never retunes it.
///  3. <b>Animator</b>: <see cref="SwordAbilitySetup.WireAttackStatesFromAssets"/> adds one
///     Attack-layer state per step (it finds every step under ScriptableObjects/Combat,
///     the ruler's included) and the AttackBody states for the full-body ones, and the
///     player's swing twist is re-baked if a player is in the open scene.
///
/// Setup Sword Abilities calls <see cref="EnsureAssets"/> too, so every rebuild of the
/// compass data and the Attack layer keeps the ruler's.
///
/// <para><b>Why only these FBXs:</b> the five FBXs the compass uses are re-imported by
/// WeaponSetup.ImportAttackClips with their default take only, which would delete any
/// extra take cut from them. The ruler therefore cuts its takes only from FBXs nothing
/// else imports: <c>attack</c>, <c>attack (2)</c>, <c>slash (2)</c> and <c>slash (4)</c>.
/// Their default "mixamo.com" take is dropped; nothing referenced it (they were Generic).</para>
///
/// The clip choices and every number below come from measuring the clips on the chibi
/// rig (RulerMovesetAudit) — see Docs/Systems/SwordAbilities.md, "Ruler".
/// </summary>
public static class RulerMovesetSetup
{
    const string PackDir = "Assets/_Project/Animation/Chibi/SwordShield";
    const string DataDir = "Assets/_Project/ScriptableObjects/Combat";
    const string StepDir = DataDir + "/Steps/Ruler";
    const string AbilityDir = DataDir + "/Abilities";
    public const string MovesetPath = DataDir + "/Moveset_Ruler.asset";

    // ------------------------------------------------------------------ takes

    /// <summary>A named take cut out of a pack FBX, as fractions (0-1) of the FBX's
    /// default take, so the cut survives a re-export at another frame rate.</summary>
    sealed class Take
    {
        public string File, Name;
        public float From, To;
    }

    /// <summary>Measured on the chibi rig with a 0.9 m blade (fractions of the source):
    /// <list type="bullet">
    /// <item><c>attack (2)</c>, 1.30 s: a 270° flat cleave from his left round to his
    /// right, tip 1.3 m out, peak 36 m/s at 0.46, after a 0.38 wind-up that draws the
    /// blade back round his right side. The tail after 0.87 is a still hold.</item>
    /// <item><c>slash (2)</c>, 3.53 s: a whirl. Strike 1 (0.165-0.265) is a spinning
    /// diagonal cleave from his right down to his left; strike 2 (0.33-0.47) sweeps back
    /// left to right; then a hop and a down-chop (0.67-0.72). The back cleave is strike 1
    /// alone; the air spin is both strikes.</item>
    /// <item><c>attack</c>, 2.33 s: crouch, a leap (hips +0.6 m) with the blade raised
    /// behind the head, and a down-chop that lands with the feet at 0.53, then a crouch
    /// hold. 0.12 skips a small anticipation hop; 0.90 drops the stand-up.</item>
    /// <item><c>slash (4)</c>, 2.30 s: a hop with the blade raised overhead (0.40) and a
    /// straight down-chop peaking at 0.60.</item>
    /// </list></summary>
    static readonly Take[] Takes =
    {
        new() { File = "sword and shield attack (2).fbx", Name = "Ruler_Cleave", From = 0.05f, To = 0.87f },
        new() { File = "sword and shield slash (2).fbx", Name = "Ruler_BackCleave", From = 0.06f, To = 0.31f },
        new() { File = "sword and shield slash (2).fbx", Name = "Ruler_Spin", From = 0.06f, To = 0.50f },
        new() { File = "sword and shield attack.fbx", Name = "Ruler_Slam", From = 0.12f, To = 0.90f },
        new() { File = "sword and shield slash (4).fbx", Name = "Ruler_Chop", From = 0.42f, To = 0.80f },
    };

    // ------------------------------------------------------------------ seeds

    sealed class StepSeed
    {
        public string Asset, Display, State, Take;
        public float Speed, BlendIn = 0.12f, Lunge;
        public bool FullBody, Hang = true;
        public SwingWindows Windows;
        public HitSettings Hit;
        public AreaBurst Burst;
        public Color Trail = new(1f, 0.86f, 0.35f, 0.95f);
        public float Shake, ShakeSeconds, Fov;
        public string HitFx, BurstFx;
    }

    static readonly Color RulerTrail = new(1f, 0.86f, 0.35f, 0.95f);   // the blade's yellow

    /// <summary>
    /// The ruler's steps. Window fractions are of the trimmed take, converted from the
    /// measured source fractions above. Feel targets against the compass (see the doc's
    /// table): each hit 2-2.5x the damage of the compass hit in the same slot, the mashed
    /// chain about 1.5x as long with a wind-up about twice as long before each blade goes
    /// live, hitstop 1.6-1.8x, more shake and FOV punch. Single-target damage per second
    /// ends up about 1.05x the compass's on the blade (1.2x with the slam's ring): the
    /// ruler trades commitment for weight, not for raw DPS.
    /// </summary>
    static readonly StepSeed[] Steps =
    {
        // Hit 1: the wide flat cleave, his left to his right. comboOpen waits for the
        // follow-through to settle (source 0.62), so a mash can't clip the arc. Full body
        // when standing: the clip turns the hips 180°, which masked would all go into the waist.
        // Lunge 2.5: the arc crosses his front low, 1.3 m out, and at 1 m/s it fell short of
        // the training dummy from where the compass's opener reaches it (play test r1).
        new() { Asset = "Step_RulerCleave", Display = "Straightedge", State = "Ruler_Cleave", Take = "Ruler_Cleave",
                Speed = 1.15f, BlendIn = 0.08f, Lunge = 2.5f, FullBody = true,
                Windows = new SwingWindows(0.41f, 0.61f, 0.70f, 1f), Hit = new HitSettings(24f, 1.5f, 0f, 0.09f),
                Trail = RulerTrail, Shake = 0.22f, ShakeSeconds = 0.22f, Fov = 2f },
        // Hit 2: the spinning back cleave, his right down to his left. Full body when
        // standing: the spin turns the hips, which the masked layer would wrench into the waist.
        new() { Asset = "Step_RulerBackCleave", Display = "Reflex Angle", State = "Ruler_BackCleave", Take = "Ruler_BackCleave",
                Speed = 1.05f, Lunge = 1.5f, FullBody = true,
                Windows = new SwingWindows(0.42f, 0.81f, 0.84f, 1f), Hit = new HitSettings(27f, 2f, 0f, 0.09f),
                Trail = RulerTrail, Shake = 0.26f, ShakeSeconds = 0.24f, Fov = 2.5f },
        // Finisher: the leap slam, landing a ground shock ring where the blade strikes.
        new() { Asset = "Step_RulerSlam", Display = "Plumb Line", State = "Ruler_Slam", Take = "Ruler_Slam",
                Speed = 1.1f, BlendIn = 0.14f, Lunge = 5f, FullBody = true,
                Windows = new SwingWindows(0.43f, 0.57f, 0.74f, 0.87f), Hit = new HitSettings(40f, 9f, 0f, 0.16f),
                Burst = new AreaBurst(0.53f, 2.6f, 0.3f, new HitSettings(12f, 6f, 3f, 0.1f), 1.0f, 0.6f),
                Trail = RulerTrail, Shake = 0.45f, ShakeSeconds = 0.35f, Fov = 6f, BurstFx = "ShockRing" },
        // Air: one committed spin, both strikes of the whirl under one window.
        new() { Asset = "Step_RulerSpin", Display = "Full Rotation", State = "Ruler_Spin", Take = "Ruler_Spin",
                Speed = 1.9f, BlendIn = 0.08f,
                Windows = new SwingWindows(0.24f, 0.93f, 0.93f, 1f), Hit = new HitSettings(24f, 3f, 0f, 0.09f),
                Trail = RulerTrail, Shake = 0.25f, ShakeSeconds = 0.22f, Fov = 3f },

        // The ruler's own versions of learned abilities (see the moveset's ability rows).
        // + C: a third cleave at ruler pace (his left to right again, between the back
        // cleave and the slam), gold, with the "+" pop.
        new() { Asset = "Step_RulerPlusC", Display = "+ C", State = "Ruler_PlusC", Take = "Ruler_Cleave",
                Speed = 1.3f, Lunge = 2f, FullBody = true,
                Windows = new SwingWindows(0.41f, 0.61f, 0.70f, 1f), Hit = new HitSettings(22f, 1.2f, 0f, 0.08f),
                Trail = new Color(1f, 0.85f, 0.35f, 0.95f), Shake = 0.2f, ShakeSeconds = 0.2f, Fov = 1.5f, HitFx = "PlusPop" },
        // Phase Shift: a second air hit, the overhead down-chop.
        new() { Asset = "Step_RulerPhaseShift", Display = "Phase Shift", State = "Ruler_PhaseShift", Take = "Ruler_Chop",
                Speed = 1.3f,
                Windows = new SwingWindows(0.36f, 0.54f, 0.58f, 1f), Hit = new HitSettings(26f, 2.5f, 0f, 0.09f),
                Trail = new Color(0.75f, 0.5f, 1f, 0.95f), Shake = 0.25f, ShakeSeconds = 0.22f, Fov = 2f },
        // Area Under the Curve: the slam, slower, with the Riemann ring at 3.6 m.
        new() { Asset = "Step_RulerAreaUnderCurve", Display = "Area Under the Curve", State = "Ruler_AreaUnderCurve",
                Take = "Ruler_Slam", Speed = 1.0f, BlendIn = 0.14f, Lunge = 5.5f, FullBody = true,
                Windows = new SwingWindows(0.43f, 0.57f, 0.74f, 0.87f), Hit = new HitSettings(44f, 10f, 0f, 0.18f),
                Burst = new AreaBurst(0.53f, 3.6f, 0.4f, new HitSettings(16f, 7f, 3f, 0.1f), 1.0f, 0.6f),
                Trail = new Color(0.55f, 1f, 0.45f, 0.95f), Shake = 0.5f, ShakeSeconds = 0.38f, Fov = 7f,
                BurstFx = "RiemannRing" },
    };

    /// <summary>How each learned ability plays with the ruler. Unit Circle is declined:
    /// the ruler's air game is one committed spin (plus Phase Shift's chop), and a
    /// 360° ring on top of a 360° spin would be the same move twice.</summary>
    static readonly (string ability, string step)[] AbilityRows =
    {
        ("Ability_PlusC", "Step_RulerPlusC"),
        ("Ability_PhaseShift", "Step_RulerPhaseShift"),
        ("Ability_AreaUnderCurve", "Step_RulerAreaUnderCurve"),
        ("Ability_UnitCircle", null),
    };

    // ------------------------------------------------------------------- menu

    [MenuItem("Tools/Pickleball/Setup Ruler Moveset")]
    public static void Run() => Build(reseed: false);

    /// <summary>Command line: -executeMethod RulerMovesetSetup.Batch. Assets only.</summary>
    public static void Batch()
    {
        Build(reseed: false);
        AssetDatabase.SaveAssets();
    }

    /// <summary>Command line: rewrites every ruler step and the moveset from the seeds
    /// above (GUIDs kept). For retuning the seeds themselves; it discards hand tuning.</summary>
    public static void BatchReseed()
    {
        Build(reseed: true);
        AssetDatabase.SaveAssets();
    }

    static void Build(bool reseed)
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("Setup Ruler Moveset edits clip imports, assets and the controller — exit Play first.");
            return;
        }
        EnsureAssets(SwordAbilitySetup.BuildEffects(), reseed);
        SwordAbilitySetup.WireAttackStatesFromAssets();
        var player = Object.FindFirstObjectByType<ChibiLocomotion>();
        if (player != null) SwingTwistSetup.Bake(player.gameObject);
        AssetDatabase.SaveAssets();
        Debug.Log("Ruler moveset set up: " + MovesetPath + ". Equip it through Weapon_Ruler (inventory), or " +
                  "assign it to MeleeCombat.moveset to try it.");
    }

    // ------------------------------------------------------------------- data

    /// <summary>
    /// Imports the ruler's clips and seeds its steps and moveset if missing (or rewrites
    /// them when <paramref name="reseed"/>). <paramref name="fx"/> is Setup Sword
    /// Abilities' effect prefabs by name. Saves nothing; the caller does.
    /// </summary>
    public static SwordMoveset EnsureAssets(Dictionary<string, GameObject> fx, bool reseed)
    {
        var clips = ImportTakes();
        ChibiSandboxSetup.EnsureFolderPublic(StepDir);

        var steps = new Dictionary<string, ComboStepDefinition>();
        foreach (var seed in Steps)
            steps[seed.Asset] = EnsureStep(seed, clips, fx, reseed);

        var moveset = AssetDatabase.LoadAssetAtPath<SwordMoveset>(MovesetPath);
        bool created = moveset == null;
        if (created) moveset = ScriptableObject.CreateInstance<SwordMoveset>();
        if (created || reseed)
        {
            var so = new SerializedObject(moveset);
            SwordAbilitySetup.SetList(so.FindProperty("groundChain"), steps["Step_RulerCleave"], steps["Step_RulerBackCleave"]);
            so.FindProperty("groundFinisher").objectReferenceValue = steps["Step_RulerSlam"];
            SwordAbilitySetup.SetList(so.FindProperty("airChain"), steps["Step_RulerSpin"]);
            so.FindProperty("airFinisher").objectReferenceValue = null;
            so.FindProperty("acceptOtherAbilities").boolValue = false;
            var rows = so.FindProperty("abilitySteps");
            rows.arraySize = 0;
            foreach (var (abilityName, stepName) in AbilityRows)
            {
                var ability = AssetDatabase.LoadAssetAtPath<SwordAbilityDefinition>($"{AbilityDir}/{abilityName}.asset");
                if (ability == null)
                {
                    Debug.LogWarning($"Setup Ruler Moveset: {abilityName} is missing; run Setup Sword Abilities. Row skipped.");
                    continue;
                }
                rows.arraySize++;
                var row = rows.GetArrayElementAtIndex(rows.arraySize - 1);
                row.FindPropertyRelative("ability").objectReferenceValue = ability;
                row.FindPropertyRelative("step").objectReferenceValue = stepName != null ? steps[stepName] : null;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            if (created) AssetDatabase.CreateAsset(moveset, MovesetPath);
            else EditorUtility.SetDirty(moveset);
        }
        return moveset;
    }

    static ComboStepDefinition EnsureStep(StepSeed seed, Dictionary<string, AnimationClip> clips,
                                          Dictionary<string, GameObject> fx, bool reseed)
    {
        string path = $"{StepDir}/{seed.Asset}.asset";
        var step = AssetDatabase.LoadAssetAtPath<ComboStepDefinition>(path);
        bool created = step == null;
        if (created) step = ScriptableObject.CreateInstance<ComboStepDefinition>();

        var so = new SerializedObject(step);
        var clipProp = so.FindProperty("clip");
        clips.TryGetValue(seed.Take, out var clip);
        if (created || reseed)
        {
            so.FindProperty("displayName").stringValue = seed.Display;
            so.FindProperty("animatorState").stringValue = seed.State;
            so.FindProperty("playbackSpeed").floatValue = seed.Speed;
            so.FindProperty("blendIn").floatValue = seed.BlendIn;
            so.FindProperty("lungeSpeed").floatValue = seed.Lunge;
            so.FindProperty("fullBodyWhenStanding").boolValue = seed.FullBody;
            so.FindProperty("hangInAir").boolValue = seed.Hang;
            SwordAbilitySetup.WriteWindows(so.FindProperty("windows"), seed.Windows);
            SwordAbilitySetup.WriteHit(so.FindProperty("hit"), seed.Hit);
            SwordAbilitySetup.WriteBurst(so.FindProperty("burst"), seed.Burst);
            var look = so.FindProperty("presentation");
            look.FindPropertyRelative("trailColor").colorValue = seed.Trail;
            look.FindPropertyRelative("shakeAmount").floatValue = seed.Shake;
            look.FindPropertyRelative("shakeSeconds").floatValue = seed.ShakeSeconds;
            look.FindPropertyRelative("fovPunch").floatValue = seed.Fov;
            look.FindPropertyRelative("hitEffect").objectReferenceValue = null;
            look.FindPropertyRelative("burstEffect").objectReferenceValue = null;
            clipProp.objectReferenceValue = clip;
        }

        // Top-ups: only fill blanks, so they are safe on a tuned asset.
        if (clipProp.objectReferenceValue == null) clipProp.objectReferenceValue = clip;
        var hitFx = so.FindProperty("presentation.hitEffect");
        if (hitFx.objectReferenceValue == null && seed.HitFx != null && fx != null && fx.TryGetValue(seed.HitFx, out var h))
            hitFx.objectReferenceValue = h;
        var burstFx = so.FindProperty("presentation.burstEffect");
        if (burstFx.objectReferenceValue == null && seed.BurstFx != null && fx != null && fx.TryGetValue(seed.BurstFx, out var b))
            burstFx.objectReferenceValue = b;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (created) AssetDatabase.CreateAsset(step, path);
        else EditorUtility.SetDirty(step);
        return step;
    }

    // ------------------------------------------------------------------ clips

    /// <summary>
    /// Imports each FBX in <see cref="Takes"/> as Humanoid with exactly its Ruler takes,
    /// using WeaponSetup.ImportAttackClips' settings (loop off, root rotation and height
    /// baked into the pose from the original orientation, XZ left to the scripted lunge).
    /// Leaves an FBX alone when it already matches, so re-running reimports nothing.
    /// Returns the takes by name.
    /// </summary>
    static Dictionary<string, AnimationClip> ImportTakes()
    {
        var result = new Dictionary<string, AnimationClip>();
        foreach (var group in Takes.GroupBy(t => t.File))
        {
            string path = $"{PackDir}/{group.Key}";
            if (!(AssetImporter.GetAtPath(path) is ModelImporter imp))
            {
                Debug.LogWarning($"Setup Ruler Moveset: {path} not found; its steps get no clip.");
                continue;
            }

            var source = imp.defaultClipAnimations.FirstOrDefault();
            if (source == null)
            {
                Debug.LogWarning($"Setup Ruler Moveset: {path} has no animation take.");
                continue;
            }
            float first = source.firstFrame, span = source.lastFrame - source.firstFrame;

            // Each take starts as a fresh copy of the default one, so every setting not
            // named here (masks, height from feet, mirror) stays the importer's default.
            var wanted = group.Select(t =>
            {
                var c = imp.defaultClipAnimations[0];
                c.name = t.Name;
                c.firstFrame = Mathf.Round(first + t.From * span);
                c.lastFrame = Mathf.Round(first + t.To * span);
                c.loopTime = false;
                c.lockRootRotation = true;
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = true;
                c.keepOriginalPositionY = false;
                c.lockRootPositionXZ = false;
                return c;
            }).ToArray();

            bool matches = imp.animationType == ModelImporterAnimationType.Human
                && imp.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel
                && imp.importAnimation
                && imp.materialImportMode == ModelImporterMaterialImportMode.None
                && SameTakes(imp.clipAnimations, wanted);
            if (!matches)
            {
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.importAnimation = true;
                imp.materialImportMode = ModelImporterMaterialImportMode.None;
                imp.clipAnimations = wanted;
                imp.SaveAndReimport();
                Debug.Log($"Setup Ruler Moveset: imported {group.Key} as Humanoid with " +
                          string.Join(", ", wanted.Select(w => $"{w.name} [{w.firstFrame:0}-{w.lastFrame:0}]")));
            }

            var all = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().ToList();
            foreach (var t in group)
            {
                var clip = all.FirstOrDefault(c => c.name == t.Name);
                if (clip != null) result[t.Name] = clip;
                else Debug.LogWarning($"Setup Ruler Moveset: take {t.Name} missing from {path} after import.");
            }
        }
        return result;
    }

    static bool SameTakes(ModelImporterClipAnimation[] have, ModelImporterClipAnimation[] want)
    {
        if (have == null || have.Length != want.Length) return false;
        for (int i = 0; i < want.Length; i++)
        {
            var h = have[i];
            var w = want[i];
            if (h.name != w.name || !Mathf.Approximately(h.firstFrame, w.firstFrame)
                || !Mathf.Approximately(h.lastFrame, w.lastFrame) || h.loopTime
                || !h.lockRootRotation || !h.keepOriginalOrientation || !h.lockRootHeightY
                || h.keepOriginalPositionY || h.lockRootPositionXZ)
                return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ audit

    /// <summary>Audit helper: imports every attack-like clip in the pack that is still
    /// Generic as Humanoid, full length, so RulerMovesetAudit can measure it. Changes
    /// clip imports: revert the .meta files of clips you don't keep.</summary>
    public static void ImportCandidatesForAudit()
    {
        foreach (var file in System.IO.Directory.GetFiles(PackDir, "*.fbx").Select(System.IO.Path.GetFileName)
                     .Where(f => f.Contains("attack") || f.Contains("slash") || f.Contains("power up")
                                 || f.Contains("casting") || f.Contains("kick")))
        {
            string path = $"{PackDir}/{file}";
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null || imp.animationType == ModelImporterAnimationType.Human) continue;
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importAnimation = true;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            var defs = imp.defaultClipAnimations;
            foreach (var d in defs)
            {
                d.loopTime = false;
                d.lockRootRotation = true;
                d.keepOriginalOrientation = true;
                d.lockRootHeightY = true;
                d.lockRootPositionXZ = false;
                d.keepOriginalPositionY = false;
            }
            imp.clipAnimations = defs;
            imp.SaveAndReimport();
            Debug.Log($"Audit import: {file} -> Humanoid");
        }
    }

    /// <summary>Command line: audit every attack-like clip (importing the Generic ones),
    /// then render contact sheets. See RulerMovesetAudit / RulerMovesetPreview for args.</summary>
    public static void BatchAudit()
    {
        ImportCandidatesForAudit();
        RulerMovesetAudit.Batch();
        RulerMovesetPreview.Batch();
    }

    /// <summary>Command line: the timing table, the clip audit of -auditClips, and a
    /// contact sheet per step. Changes no asset.</summary>
    public static void BatchVerify()
    {
        RulerMovesetAudit.BatchTiming();
        RulerMovesetAudit.Batch();
        RulerMovesetPreview.Batch();
    }
}
