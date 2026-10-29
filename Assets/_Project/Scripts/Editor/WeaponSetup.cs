using System.Collections.Generic;
using System.Linq;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Hands;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Attaches the drafting-compass weapon to the player and wires the combo chain.
///
/// Tools ▸ Pickleball ▸ Setup Weapon (everything), or Tools ▸ Pickleball ▸
/// Setup Weapon Grip (only re-fits the grip on the existing weapon).
///
/// The weapon hangs off HumanBodyBones.RightHand rather than a bone found by
/// name: the avatar already knows which transform that is, so this works for any
/// Tripo rig without hard-coding "R_Hand".
///
/// The WeaponSocket is the hand's grab point (a <see cref="HandGrip"/>): it is
/// placed by measuring the hand (<see cref="HandGripSetup"/>), and the compass
/// carries a grip anchor at the middle of its handle that the grab point seats
/// on (<see cref="HeldItem"/>). The socket keeps its old name because Setup
/// Weapon Clearance and the Tripo contact sheet find it by that name.
///
/// Runs with a primitive stand-in until the real compass is imported, so the
/// combo timing and hit sweep can be tuned before the model exists — the same
/// approach that let the skateboard mechanic be built ahead of its model.
/// </summary>
public static class WeaponSetup
{
    const string PropsDir = "Assets/_Project/Art/Props";
    const string ClipDir = "Assets/_Project/Animation/Chibi";
    const string VfxDir = "Assets/_Project/Art/VFX";
    const string ControllerPath = "Assets/_Project/Animation/Controllers/ChibiLocomotion.controller";

    const string PackDir = ClipDir + "/SwordShield";
    const string PackSource = "Assets/Sword and Shield Pack";

    /// <summary>
    /// The combo chain, chosen by measuring the pack rather than by filename.
    ///
    /// Ordered so the swing direction ALTERNATES every hit — L>R, R>L, L>R, R>L.
    /// Two consecutive swings the same way read as a stutter rather than a
    /// combo, which is the most common way a chain looks wrong.
    ///
    /// It escalates: quick opener, widest sweep, heavy downward, then the
    /// upward slash as the finisher. Launching LAST is deliberate — the combo
    /// ends by popping the target up, which is the setup for juggling it with
    /// the air attack.
    ///
    /// `speed` is the Animator state's playback rate. Mixamo authors these
    /// swings at 1.37-1.73s, which is far too ponderous for an arcade combo, so
    /// each is played back at roughly 2.2-2.5x. Measured peak hand speed lands
    /// at 40-53% of every clip.
    ///
    /// Only the clip and the speed live here. The hit windows (active,
    /// comboOpen, comboClose) used to sit in this table too and were written
    /// into MeleeCombat; they now live on the step assets
    /// (ScriptableObjects/Combat/Steps/Step_Slash1-4, Step_AirSlash), which are
    /// the only source of truth for timing. The columns were removed rather
    /// than left to drift — AirAttack's had already drifted from its asset.
    /// </summary>
    static readonly (string role, string file, string trigger, float speed)[] AttackClips =
    {
        // 1.37s -> 0.55s. L>R, flat, wide — fastest of the set, so it opens.
        ("attack1", "sword and shield slash (5).fbx", "Attack1", 2.5f),
        // 1.73s -> 0.69s. R>L, widest lateral sweep (0.603) and longest reach.
        ("attack2", "sword and shield attack (3).fbx", "Attack2", 2.5f),
        // 1.50s -> 0.63s. L>R, downward, big vertical — the heavy third hit.
        ("attack3", "sword and shield slash.fbx", "Attack3", 2.4f),
        // 1.57s -> 0.71s. R>L, UPWARD — the launcher that ends the ground chain.
        // Kept the slowest of the four so the finisher still lands with weight.
        ("attack4", "sword and shield slash (3).fbx", "Attack4", 2.2f),
        // 1.00s -> 0.45s. In place, compact — suits being airborne.
        ("airAttack", "sword and shield attack (4).fbx", "AirAttack", 2.2f),
    };

    /// <summary>
    /// Which end of the weapon is the handle. The compass model runs 1.0
    /// units along its own +Y with the origin at the low end, so the old code —
    /// which just parented it at the socket origin — always gripped that low
    /// end. True flips it, so the far end (the compass's knob and hinge) is
    /// held and the model points the other way out of the hand.
    /// </summary>
    const bool GripFarEnd = true;

    /// <summary>Weapon length after normalising, whatever the authored scale.</summary>
    const float WeaponLength = 0.85f;

    /// <summary>The socket is the hand's grab point; Setup Weapon Clearance and
    /// the Tripo contact sheet find it, and the visual under it, by these names.</summary>
    const string SocketName = "WeaponSocket";
    const string VisualName = "WeaponVisual";
    const string GripAnchorName = "GripAnchor";

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

    /// <summary>The old hand-tuned socket pose, kept only as the fallback for
    /// a rig whose hand has no skinned mesh to measure.</summary>
    static readonly Vector3 FallbackSocketPosition = new(0f, 0.02f, 0.04f);
    static readonly Vector3 FallbackSocketEuler = new(0f, 90f, 100f);

    /// <summary>
    /// Name of the masked layer the combo lives on. SwordAnimatorDriver and
    /// SwingTwist look the layer up by this name at runtime, so they must agree.
    ///
    /// The attacks used to be full-body states on the base layer, which meant a
    /// swing replaced the run — the legs froze mid-stride while the character
    /// kept travelling. Masked to the arms, spine and head instead, the swing
    /// composes over whatever the legs are already doing.
    /// </summary>
    public const string AttackLayer = "Attack";

    [MenuItem("Tools/Pickleball/Setup Weapon")]
    public static void Run()
    {
        var clips = PrepareAssets();

        var player = GameObject.Find("Chibi");
        if (player == null)
        {
            Debug.LogError("No 'Chibi' in the scene — run Setup Chibi Sandbox first.");
            return;
        }
        if (!AttachToPlayer(player, clips)) return;

        AssetDatabase.SaveAssets();
        Debug.Log("Weapon setup complete. Left click to attack; click again mid-swing to chain.");
    }

    /// <summary>
    /// Re-fits only the grip on the weapon that is already in the hand:
    /// re-measures the hand and the compass's handle, moves the grab point,
    /// re-seats the compass and moves BladeBase / BladeTip to match.
    ///
    /// Deliberately much less than Setup Weapon: it doesn't rebuild the socket,
    /// so the WeaponClearance on it and the MeleeCombat and BladeTrail
    /// references to the blade markers stay as they are, and it doesn't call
    /// SwordAbilitySetup, so the tuned combo (states, step assets, effects)
    /// is left alone. Run it after changing the HandGripProfile's grab point
    /// fields, or in a scene saved before the grab point existed.
    /// </summary>
    [MenuItem("Tools/Pickleball/Setup Weapon Grip")]
    public static void RunGripOnly()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Setup Weapon Grip: leave Play mode first; changes made in Play mode are lost.");
            return;
        }
        var player = GameObject.Find("Chibi");
        if (player == null)
        {
            Debug.LogError("No 'Chibi' in the scene — run Setup Chibi Sandbox first.");
            return;
        }
        var animator = player.GetComponentInChildren<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
        {
            Debug.LogError("Player has no Humanoid avatar; cannot find the hand bone.");
            return;
        }
        var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        var socket = hand != null ? hand.Find(SocketName) : null;
        var visual = socket != null ? socket.Find(VisualName) : null;
        if (visual == null)
        {
            Debug.LogError($"No {SocketName}/{VisualName} on the right hand — run Tools ▸ Pickleball ▸ " +
                           "Setup Weapon first.");
            return;
        }

        // The compass is a model prefab instance; the stand-in is a bare
        // primitive, which has no far end to prefer.
        bool isModel = PrefabUtility.IsPartOfPrefabInstance(visual.gameObject);
        FitGrip(animator, socket, visual, isModel && GripFarEnd);

        EditorUtility.SetDirty(player);
        EditorSceneManager.MarkSceneDirty(player.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Weapon grip re-fitted. The combo, weapon clearance and blade trail were not rebuilt; " +
                  "save the scene to keep it.");
    }

    /// <summary>
    /// Phase 1, assets only: file the pack, import the attack clips, and add the
    /// masked Attack layer to the controller. Touches no scene objects.
    ///
    /// Split from the scene work so ChibiSandboxSetup can run it BEFORE building
    /// the scene. ImportAttackClips calls SaveAndReimport on five FBXs, and
    /// kicking off asset imports halfway through scene construction is asking
    /// for a half-built scene. Getting the Attack layer onto the controller
    /// first also means the Animator is wired to a finished controller rather
    /// than one that grows a layer afterwards.
    /// </summary>
    public static Dictionary<string, AnimationClip> PrepareAssets()
    {
        OrganizeWeaponAssets();
        var clips = ImportAttackClips();
        WireController(clips);
        return clips;
    }

    /// <summary>
    /// Phase 2, scene only: hang the weapon off the right hand and wire up
    /// MeleeCombat, then hand over to SwordAbilitySetup for the combo itself.
    /// Returns false if the player is not in a fit state for it.
    /// </summary>
    /// <param name="clips">Kept for ChibiSandboxSetup's call; no longer read here —
    /// swing timing comes from the step assets' own clips.</param>
    public static bool AttachToPlayer(GameObject player,
                                      Dictionary<string, AnimationClip> clips)
    {
        var animator = player.GetComponentInChildren<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
        {
            Debug.LogError("Player has no Humanoid avatar; cannot find the hand bone.");
            return false;
        }

        var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (hand == null)
        {
            Debug.LogError("Avatar has no RightHand bone mapped.");
            return false;
        }

        AttachWeapon(animator, hand, out Transform bladeBase, out Transform bladeTip);
        WireCombat(player, bladeBase, bladeTip);
        // The Attack layer's mask drops the swings' hip turn; put it back.
        SwingTwistSetup.Bake(player);
        // The socket was just rebuilt, so its clearance pass has to be re-added.
        WeaponClearanceSetup.Attach(player);
        // Last: the combo itself — moveset, abilities, levelling, animation and
        // effects listeners, plus the Attack-layer states for every ability
        // step (and their swing-twist bake). Without this a rebuilt player has
        // a sword but no idea what to do with it.
        if (!SwordAbilitySetup.AttachToPlayer(player)) return false;
        EditorUtility.SetDirty(player);
        return true;
    }

    // --------------------------------------------------------------- organize

    /// <summary>
    /// Files the whole Mixamo pack under Animation and the compass under Props.
    ///
    /// The pack moves as one folder and keeps Mixamo's filenames: only 5 of its
    /// 49 clips are wired right now, and renaming would make the other 44 harder
    /// to find later. ChibiSandboxSetup's organizer deliberately does not touch
    /// it — its loose-model sweep would file these animations as props.
    /// </summary>
    static void OrganizeWeaponAssets()
    {
        if (AssetDatabase.IsValidFolder(PackSource))
        {
            ChibiSandboxSetup.EnsureFolderPublic(ClipDir);
            string err = AssetDatabase.MoveAsset(PackSource, PackDir);
            if (string.IsNullOrEmpty(err)) Debug.Log($"moved {PackSource} -> {PackDir}");
            else Debug.LogWarning($"could not move pack: {err}");
        }

        // Compass: same treatment as any other Tripo prop, sidecar included.
        const string compassSrc = "Assets/compass-weapon";
        string compassDst = $"{PropsDir}/compass-weapon";
        if (AssetDatabase.IsValidFolder(compassSrc) &&
            !AssetDatabase.IsValidFolder(compassDst))
        {
            ChibiSandboxSetup.EnsureFolderPublic(PropsDir);
            string err = AssetDatabase.MoveAsset(compassSrc, compassDst);
            if (string.IsNullOrEmpty(err)) Debug.Log($"moved {compassSrc} -> {compassDst}");
            else Debug.LogWarning($"could not move compass: {err}");
        }
        AssetDatabase.Refresh();
    }

    // ------------------------------------------------------------------ clips

    static Dictionary<string, AnimationClip> ImportAttackClips()
    {
        var result = new Dictionary<string, AnimationClip>();
        foreach (var (role, file, _, _) in AttackClips)
        {
            string path = $"{PackDir}/{file}";
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) continue;

            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importAnimation = true;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;

            var defs = imp.defaultClipAnimations;
            if (defs is { Length: > 0 })
            {
                foreach (var d in defs)
                {
                    d.loopTime = false;                 // attacks are one-shots
                    // Bake the body's turn into the pose, from its original
                    // orientation. These swings are authored side-on, hips
                    // turned up to ~110°; left as root motion (and root motion
                    // is off) that turn was simply discarded. SwingTwist bakes
                    // it from the clip, so it has to be in the pose to be seen.
                    d.lockRootRotation = true;
                    d.keepOriginalOrientation = true;
                    d.lockRootHeightY = false;
                    d.lockRootPositionXZ = false;       // lunge is scripted
                    d.keepOriginalPositionY = false;
                }
                imp.clipAnimations = defs;
            }
            imp.SaveAndReimport();

            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) continue;
            result[role] = clip;
            Debug.Log($"  {role,-10} <- {file,-14} {clip.length:0.00}s");
        }
        if (result.Count == 0)
            Debug.LogWarning($"No attack clips found in {PackDir}. Expected the " +
                             "Mixamo sword-and-shield pack. The weapon and combo " +
                             "system are still wired; add clips and re-run.");
        return result;
    }

    // ----------------------------------------------------------------- weapon

    /// <summary>
    /// Builds the WeaponSocket from scratch: the compass (or a stand-in), the
    /// blade markers, and then the grip (<see cref="FitGrip"/>).
    /// </summary>
    static GameObject AttachWeapon(Animator animator, Transform hand,
                                   out Transform bladeBase, out Transform bladeTip)
    {
        // The socket is the hand's grab point. Everything held hangs off it, so
        // the grip can be re-fitted without touching the bone or the model.
        var socketGo = hand.Find(SocketName)?.gameObject;
        if (socketGo != null) Object.DestroyImmediate(socketGo);
        socketGo = new GameObject(SocketName);
        socketGo.transform.SetParent(hand, false);

        string modelPath = FindModel("compass") ?? FindModel("protractor");
        GameObject visual;
        if (modelPath != null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            visual.transform.SetParent(socketGo.transform, false);
            Debug.Log($"Weapon model: {modelPath} (scaled to {WeaponLength:0.00}m, " +
                      $"handle at its {(GripFarEnd ? "far" : "near")} end)");
        }
        else
        {
            // An empty root around a thin cylinder, so the uniform rescale in
            // PrepareHeldItem keeps it thin (the cylinder mesh is 2 x 1 units).
            visual = new GameObject(VisualName);
            visual.transform.SetParent(socketGo.transform, false);
            var standIn = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            standIn.name = "StandIn";
            standIn.transform.SetParent(visual.transform, false);
            standIn.transform.localScale = new Vector3(0.04f, 0.5f, 0.04f);
            Debug.Log("No compass model found — using a primitive stand-in. " +
                      $"Drop one under {PropsDir} and re-run.");
        }
        visual.name = VisualName;
        foreach (var c in visual.GetComponentsInChildren<Collider>())
            Object.DestroyImmediate(c);

        var baseGo = new GameObject("BladeBase");
        baseGo.transform.SetParent(socketGo.transform, false);
        var tipGo = new GameObject("BladeTip");
        tipGo.transform.SetParent(socketGo.transform, false);

        FitGrip(animator, socketGo.transform, visual.transform, modelPath != null && GripFarEnd);

        bladeBase = baseGo.transform;
        bladeTip = tipGo.transform;
        return socketGo;
    }

    /// <summary>
    /// Fits the grip on an existing socket, in place: measures the weapon's
    /// handle and gives it a grip anchor, places the socket at the hand's grab
    /// point, seats the weapon on it, and moves BladeBase to the centre of the
    /// fist and BladeTip to the weapon's far end. Re-running gives the same
    /// result.
    ///
    /// This replaced a hand-typed "first guess" socket offset that sat at the
    /// wrist (0.02 m along the hand) and held the compass by its very end, so
    /// the blade came out of the wrist rather than the fist. Now the hand side
    /// is measured from the rig and the weapon side from the mesh.
    /// </summary>
    static void FitGrip(Animator animator, Transform socket, Transform visual, bool gripFarEnd)
    {
        var profile = HandGripSetup.LoadOrCreateProfile();
        var item = PrepareHeldItem(visual.gameObject, WeaponLength, gripFarEnd, out Vector3 tipInModel);

        if (HandGripSetup.TrySolve(animator, rightHand: true, profile, out GripFrame frame, out string how))
        {
            socket.SetLocalPositionAndRotation(frame.Position, frame.Rotation);
            Debug.Log($"Grab point: {how}.");
        }
        else
        {
            socket.SetLocalPositionAndRotation(FallbackSocketPosition, Quaternion.Euler(FallbackSocketEuler));
            Debug.LogWarning($"Could not measure the hand ({how}); using the old hand-tuned socket pose.");
        }

        var grip = HandGripSetup.Attach(animator, rightHand: true, socket, profile);
        grip.Hold(item);

        // The blade runs from the centre of the fist to the weapon's far end:
        // WeaponClearance turns it about that grip point, MeleeCombat sweeps it
        // and BladeTrail draws it.
        var bladeBase = socket.Find("BladeBase");
        var bladeTip = socket.Find("BladeTip");
        if (bladeBase != null) bladeBase.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        if (bladeTip != null)
            bladeTip.SetLocalPositionAndRotation(socket.InverseTransformPoint(visual.TransformPoint(tipInModel)),
                                                 Quaternion.identity);

        RecordInstanceChanges(socket, visual, grip, item, bladeBase, bladeTip);
    }

    /// <summary>
    /// Marks objects changed through their C# API as dirty and, when they
    /// belong to a prefab instance (the Chibi in every saved scene), records
    /// the change as an instance override. Without the record, an edit to a
    /// prefab-owned object made outside a SerializedObject can be dropped on
    /// the next save or reload. Harmless on objects outside a prefab.
    /// </summary>
    static void RecordInstanceChanges(params Object[] objects)
    {
        foreach (var o in objects)
        {
            if (o == null) continue;
            PrefabUtility.RecordPrefabInstancePropertyModifications(o);
            EditorUtility.SetDirty(o);
        }
    }

    /// <summary>
    /// Readies a weapon model to be held: scales it to
    /// <paramref name="wanted"/> metres, measures where its handle is, and
    /// gives it a <see cref="HeldItem"/> with a grip anchor at the middle of
    /// that handle (+Y toward the tip, +Z along the weapon's wider side, so the
    /// flat faces the palm and the edge leads with the knuckles).
    ///
    /// The old code assumed the model's own origin WAS the handle and just
    /// parented it at zero. That happens to be half-right for the compass
    /// (measured: it runs 0.998 along its +Y with the origin exactly at the low
    /// end), but it gave no way to hold the other end, and it would be wrong for
    /// any model whose origin sits at its centre. Then the grip went to the very
    /// end of the model, so the compass's 8 cm knob stuck out of the hand
    /// instead of sitting in it. Measuring the handle puts the fist round the
    /// knob and neck, up to the hinge disc.
    /// </summary>
    /// <param name="tipInModel">The far end of the weapon, in the model's own
    /// (unscaled) space, for the BladeTip marker.</param>
    static HeldItem PrepareHeldItem(GameObject visual, float wanted, bool gripFarEnd, out Vector3 tipInModel)
    {
        var tr = visual.transform;

        // Measured in place, in the model's own space. The visual is never
        // moved out to the world origin to read Renderer.bounds (the old trick):
        // in every saved scene it sits inside the Chibi prefab instance, where
        // it can't be reparented, and Setup Weapon Grip threw on exactly that.
        var local = MeasureModelBounds(visual);

        // Longest dimension is the weapon's length; the other two are its girth.
        int axis = 0;
        if (local.size.y > local.size[axis]) axis = 1;
        if (local.size.z > local.size[axis]) axis = 2;
        float span = local.size[axis];
        float scale = span > 0.0001f ? wanted / span : 1f;

        // The held end, the far end, and the direction from one to the other.
        var gripEnd = local.center;
        gripEnd[axis] = gripFarEnd ? local.max[axis] : local.min[axis];
        tipInModel = local.center;
        tipInModel[axis] = gripFarEnd ? local.min[axis] : local.max[axis];
        var along = Vector3.zero;
        along[axis] = gripFarEnd ? -1f : 1f;

        // The wider girth axis is the edge: it leads with the knuckles. On the
        // compass that is the plane its legs splay in, the same roll it was
        // held at before.
        int g1 = (axis + 1) % 3, g2 = (axis + 2) % 3;
        var edge = Vector3.zero;
        edge[local.size[g1] >= local.size[g2] ? g1 : g2] = 1f;

        float handle = MeasureHandle(visual, local, axis, gripEnd, along, span);
        Vector3 anchorInModel = gripEnd + along * (handle * 0.5f);

        tr.localScale = Vector3.one * scale;

        // A new child and a new component are allowed on a prefab instance
        // (they become added-object overrides); only reparenting or destroying
        // what the prefab owns is not.
        var anchor = tr.Find(GripAnchorName);
        if (anchor == null)
        {
            anchor = new GameObject(GripAnchorName).transform;
            anchor.SetParent(tr, false);
        }
        anchor.SetLocalPositionAndRotation(anchorInModel, Quaternion.LookRotation(edge, along));
        anchor.localScale = Vector3.one;

        if (!visual.TryGetComponent(out HeldItem item))
            item = visual.AddComponent<HeldItem>();
        item.SetGripAnchor(anchor);
        RecordInstanceChanges(tr, anchor, item);

        Debug.Log(handle > 0f
            ? $"Weapon handle: {handle * scale:0.000} m from the held end; the fist closes on its middle."
            : "Weapon has no distinct handle (it never widens near the held end); the fist closes on the end.");
        return item;
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

    /// <summary>
    /// Length of the weapon's handle from its held end, in model units: slices
    /// the mesh along its length and finds where it first widens sharply (see
    /// <see cref="GripSeat.HandleLength"/>). Vertices only, which is enough:
    /// a knob, guard or disc is where a mesh puts its vertices. Like
    /// <see cref="MeasureModelBounds"/>, it reads the model in place.
    /// </summary>
    static float MeasureHandle(GameObject visual, Bounds local, int axis, Vector3 gripEnd, Vector3 along, float span)
    {
        float slice = span * HandleSliceFraction;
        if (slice <= 0f) return 0f;
        int count = Mathf.CeilToInt(HandleSearchFraction / HandleSliceFraction);
        var radii = new float[count];
        for (int i = 0; i < count; i++) radii[i] = -1f;

        int g1 = (axis + 1) % 3, g2 = (axis + 2) % 3;
        var toModel = visual.transform.worldToLocalMatrix;
        foreach (var mf in visual.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var meshToModel = toModel * mf.transform.localToWorldMatrix;
            foreach (var v in mf.sharedMesh.vertices)
            {
                Vector3 p = meshToModel.MultiplyPoint3x4(v);
                int bin = Mathf.FloorToInt(Vector3.Dot(p - gripEnd, along) / slice);
                if (bin < 0 || bin >= count) continue;
                float du = p[g1] - local.center[g1];
                float dv = p[g2] - local.center[g2];
                float r = Mathf.Sqrt(du * du + dv * dv);
                if (r > radii[bin]) radii[bin] = r;
            }
        }
        return GripSeat.HandleLength(radii, slice, HandleWidenRatio);
    }

    // ------------------------------------------------------------- controller

    /// <summary>
    /// Rebuilds the Attack layer from the attack clips exactly as they are
    /// imported now, WITHOUT the SaveAndReimport that PrepareAssets does — so
    /// it cannot overwrite import settings tuned on those FBXs since. Used by
    /// ChibiSandboxSetup.RebuildAnimator.
    /// </summary>
    public static void RewireControllerFromImportedClips()
    {
        var clips = new Dictionary<string, AnimationClip>();
        foreach (var (role, file, _, _) in AttackClips)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath($"{PackDir}/{file}").OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip != null) clips[role] = clip;
        }
        WireController(clips);
        // WireController rebuilt the Attack layer from AttackClips alone; put
        // the sword-ability states back so a rebuilt animator keeps the combo.
        SwordAbilitySetup.WireAttackStatesFromAssets();
    }

    static void WireController(Dictionary<string, AnimationClip> clips)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"Controller not found: {ControllerPath}");
            return;
        }

        var existing = new HashSet<string>(controller.parameters.Select(p => p.name));
        foreach (var (_, _, trigger, _) in AttackClips)
            if (!existing.Contains(trigger))
                controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
        if (!existing.Contains("Combo"))
            controller.AddParameter("Combo", AnimatorControllerParameterType.Int);

        var baseSm = controller.layers[0].stateMachine;
        if (baseSm.states.All(s => s.state.name != "Locomotion"))
        {
            Debug.LogError("No 'Locomotion' state — run Setup Chibi Sandbox first.");
            return;
        }

        // Older versions put the attacks on the base layer as full-body states.
        // Clear those out, or a rebuilt controller would keep stopping the legs.
        foreach (var s in baseSm.states.Where(s => s.state.name.StartsWith("Attack")).ToList())
            baseSm.RemoveState(s.state);

        // Re-running rebuilds the layer from scratch rather than appending.
        // RemoveLayer rather than reassigning controller.layers: it also destroys
        // the layer's state machine, which is a sub-asset of the .controller and
        // would otherwise be orphaned inside the file on every re-run.
        for (int i = controller.layers.Length - 1; i >= 0; i--)
            if (controller.layers[i].name == AttackLayer)
                controller.RemoveLayer(i);

        var sm = new AnimatorStateMachine
        {
            name = AttackLayer,
            hideFlags = HideFlags.HideInHierarchy,
        };
        AssetDatabase.AddObjectToAsset(sm, controller);

        // Empty default state: the layer contributes nothing until a swing
        // starts, and SwordAnimatorDriver fades its weight in for the duration.
        var idle = sm.AddState("None");
        sm.defaultState = idle;

        // One state per swing, named after its trigger so SwordAnimatorDriver
        // can crossfade to it by name. There are deliberately NO transitions on
        // this layer:
        //  - MeleeCombat's timer already owns every window (when the blade is
        //    live, when the chain may extend), and SwordAnimatorDriver crossfades
        //    straight to each step as MeleeCombat starts it. Trigger transitions
        //    duplicated that timing, and
        //    a click that landed while the layer was mid-blend left its trigger
        //    latched to fire a swing later on.
        //  - No exit back to None. An Override layer blending toward an empty
        //    state at full weight drags the arms toward the bind pose; the
        //    swing instead holds its last frame while SwordAnimatorDriver fades
        //    the layer weight to zero underneath it.
        int chain = 0;
        for (int i = 0; i < 4; i++)
        {
            string role = $"attack{i + 1}";
            if (!clips.TryGetValue(role, out var clip)) break;
            var st = sm.AddState(AttackClips[i].trigger);
            st.motion = clip;
            // Must match the base step asset's playbackSpeed (Step_Slash1-4):
            // the step's duration is its clip length / that speed, and
            // SwordAbilitySetup re-applies the asset's value after this runs.
            st.speed = AttackClips[i].speed;
            chain++;
        }

        if (clips.TryGetValue("airAttack", out var airClip))
        {
            var air = sm.AddState(AttackClips[^1].trigger);   // "AirAttack"
            air.motion = airClip;
            air.speed = AttackClips[^1].speed;
        }

        // Upper-body layer: Foot IK has nothing to do here.
        foreach (var s in sm.states) s.state.iKOnFeet = false;

        controller.AddLayer(new AnimatorControllerLayer
        {
            name = AttackLayer,
            stateMachine = sm,
            avatarMask = ChibiSandboxSetup.EnsureUpperBodyMask(),
            blendingMode = AnimatorLayerBlendingMode.Override,
            defaultWeight = 0f,       // faded in by SwordAnimatorDriver
        });

        EditorUtility.SetDirty(controller);
        Debug.Log($"Combo chain wired on masked '{AttackLayer}' layer: " +
                  $"{chain} ground attack(s)" +
                  (clips.ContainsKey("airAttack") ? " + air attack" : "") +
                  " — legs stay under locomotion, so you can swing while moving.");
    }

    // ------------------------------------------------------------------ combat

    /// <summary>
    /// The weapon's half of the combat wiring: the MeleeCombat component, its
    /// blade ends, and a fresh BladeTrail. Everything else — moveset, swing
    /// timing, animation and effects listeners — belongs to
    /// SwordAbilitySetup.AttachToPlayer, which reads the step assets. Timing used
    /// to be written in here from the clip lengths; each step asset now derives
    /// its own duration from its clip, so there is nothing to keep in step.
    /// </summary>
    static void WireCombat(GameObject player, Transform bladeBase, Transform bladeTip)
    {
        // Explicit check, not GetComponent ?? AddComponent: in the Editor a
        // missing component comes back as a fake-null object that ?? treats as
        // real, so the component was never added on a fresh player.
        if (!player.TryGetComponent(out MeleeCombat combat))
            combat = player.AddComponent<MeleeCombat>();

        // Every previous run created one of these at the SCENE ROOT, unparented
        // and never cleaned up, so re-running the setup piled up a new BladeTrail
        // each time. Sweep them all, then build one parented to the player.
        foreach (var stale in Object.FindObjectsByType<BladeTrail>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(stale.gameObject);

        var trailGo = new GameObject("BladeTrail");
        trailGo.transform.SetParent(player.transform, false);
        trailGo.AddComponent<MeshFilter>();
        var mr = trailGo.AddComponent<MeshRenderer>();
        // Placeholder until SwordAbilitySetup swaps in its vertex-colour
        // material, which is what carries the per-step tint and the fade.
        mr.sharedMaterial = SandboxVfxBuilder.BuildGlowMaterial(
            VfxDir, "BladeTrail", new Color(0.8f, 0.95f, 1f, 0.9f));
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var trail = trailGo.AddComponent<BladeTrail>();
        var tso = new SerializedObject(trail);
        tso.FindProperty("bladeBase").objectReferenceValue = bladeBase;
        tso.FindProperty("bladeTip").objectReferenceValue = bladeTip;
        tso.ApplyModifiedPropertiesWithoutUndo();

        var so = new SerializedObject(combat);
        so.FindProperty("bladeBase").objectReferenceValue = bladeBase;
        so.FindProperty("bladeTip").objectReferenceValue = bladeTip;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ----------------------------------------------------------------- helpers

    static string FindModel(string keyword)
    {
        if (!AssetDatabase.IsValidFolder(PropsDir)) return null;
        return AssetDatabase.FindAssets("t:Model", new[] { PropsDir })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.ToLowerInvariant().Contains(keyword))
            .OrderBy(p => p.Length)
            .FirstOrDefault();
    }
}
