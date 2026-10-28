using System.Collections.Generic;
using System.IO;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Combat;
using Pickleball.Sandbox.Dialogue;
using Pickleball.Sandbox.Movement;
using Pickleball.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tools ▸ Pickleball ▸ Setup Dialogue. Wires Breath-of-the-Wild-style NPC
/// talk into the open scene (ChibiSandbox):
///
/// <list type="bullet">
/// <item>the player gets a <see cref="PlayerInputGate"/> and a
/// <see cref="PlayerInteractor"/> (wired to lock-on, locomotion, melee and
/// Spacetime for its eligibility rules), and the gate is filled into
/// <see cref="PlayerMovementInput"/>, <see cref="SwordInput"/>,
/// <see cref="LockOnTargeting"/> and, if it has the field,
/// <see cref="SpacetimeView"/>;</item>
/// <item>a "Dialogue" object gets the <see cref="DialogueDirector"/>, and the
/// main camera a <see cref="DialogueCamera"/> that takes over from the orbit
/// camera during a talk;</item>
/// <item>the dialogue box and the talk prompt are built inside the FightHUD
/// canvas (or a DialogueHUD canvas when the fight has not been set up), on a
/// nested canvas sorted above the rest of the HUD that ignores parent
/// CanvasGroups;</item>
/// <item>every other screen-space root canvas (FightHUD, SwordHUD, …) gets a
/// CanvasGroup that the box fades out while it is up;</item>
/// <item>two small sprites are generated into <c>Art/UI</c> (rewritten only if
/// their bytes change), and <c>ScriptableObjects/Dialogue/DialogueConfig.asset</c>
/// and <c>TalkableSet.asset</c> are created once and never overwritten.</item>
/// </list>
///
/// Idempotent: every object is found by name or type and re-configured, never
/// destroyed and rebuilt, so a second run changes nothing (scene file IDs
/// included). It marks the scene dirty and never saves it.
///
/// Run it after Setup Lock-On (so the lock-on gate and the lock-on talk path
/// are wired) and after Setup Newton Fight (so the box lands in FightHUD).
/// Re-running either of those keeps what this added; re-running Setup Chibi
/// Sandbox wipes it, like every other Setup menu.
///
/// NPC setups call <see cref="MakeTalkable"/>.
/// </summary>
public static class DialogueSetup
{
    /// <summary>Folder for the dialogue assets. Shared with the NPC content (Birdy_Intro).</summary>
    public const string AssetDir = "Assets/_Project/ScriptableObjects/Dialogue";
    /// <summary>The one config asset every dialogue component reads.</summary>
    public const string ConfigPath = AssetDir + "/DialogueConfig.asset";
    /// <summary>The runtime set Talkables register in and the interactor scans.</summary>
    public const string TalkableSetPath = AssetDir + "/TalkableSet.asset";

    // The folder's GUID is fixed: the NPC branch commits the same folder, and two
    // branches adding it with different GUIDs would conflict on merge.
    const string AssetDirGuid = "182ae63664be4615bb6e2329a9d64209";

    const string UiArtDir = "Assets/_Project/Art/UI";
    const string RoundSpritePath = UiArtDir + "/DialogueRound.png";
    const string ArrowSpritePath = UiArtDir + "/DialogueArrow.png";

    const string DirectorObjectName = "Dialogue";
    const string FightHudName = "FightHUD";
    const string OwnHudName = "DialogueHUD";
    const string UiRootName = "DialogueUI";
    const string BodyFontName = "Roboto-Bold SDF";

    // Rounded sprite geometry, px. Corner radius on screen = RoundRadius / pixelsPerUnitMultiplier.
    const int RoundSize = 128;
    const float RoundRadius = 48f;
    const int RoundBorder = 56;

    // Sorted above the fight HUD's bars and damage numbers.
    const int UiSortingOrder = 50;

    [MenuItem("Tools/Pickleball/Setup Dialogue")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Setup Dialogue: leave Play mode first. Setup menus write the scene.");
            return;
        }

        var player = Object.FindFirstObjectByType<ChibiLocomotion>();
        if (player == null)
        {
            Debug.LogError("Setup Dialogue: no ChibiLocomotion in the scene. Open ChibiSandbox " +
                           "or run the chibi setup first.");
            return;
        }
        var cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("Setup Dialogue: no camera tagged MainCamera in the scene.");
            return;
        }

        var config = EnsureConfig();
        var talkables = EnsureTalkableSet();

        var gate = GetOrAdd<PlayerInputGate>(player.gameObject);
        var director = EnsureDirector(config, gate);

        var lockOn = player.GetComponent<LockOnTargeting>();
        var melee = player.GetComponentInChildren<MeleeCombat>(true);
        var spacetime = Object.FindFirstObjectByType<SpacetimeView>(FindObjectsInactive.Include);
        var interactor = GetOrAdd<PlayerInteractor>(player.gameObject);
        SetRefs(interactor,
                ("config", config), ("talkables", talkables), ("director", director),
                ("inputGate", gate), ("lockOn", lockOn), ("locomotion", player),
                ("melee", melee), ("spacetime", spacetime));

        WireGate(player.gameObject, gate, spacetime);

        var orbit = cam.GetComponent<SandboxOrbitCamera>();
        if (orbit == null)
            Debug.LogWarning("Setup Dialogue: the main camera has no SandboxOrbitCamera. The " +
                             "dialogue camera will still frame talks, but nothing is disabled for them.");
        var dialogueCamera = GetOrAdd<DialogueCamera>(cam.gameObject);
        SetRefs(dialogueCamera,
                ("director", director), ("config", config), ("targetCamera", cam), ("orbitCamera", orbit));

        var hud = BuildHud(director, interactor, config, cam);

        EditorUtility.SetDirty(player.gameObject);
        EditorUtility.SetDirty(cam.gameObject);
        EditorUtility.SetDirty(director.gameObject);
        EditorUtility.SetDirty(hud);
        // No AssetDatabase.SaveAssets(): it would also write every other dirty
        // asset, such as the TMP example fonts that a scene load upgrades in
        // memory. This menu's own assets are saved where they are created.
        // Marked, never saved: writing the user's scene is not this menu's call.
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);

        Debug.Log($"Dialogue set up on {player.name}: walk up to (or lock on to) an NPC with a " +
                  "Talkable and press E / RB. NPCs become talkable through DialogueSetup.MakeTalkable. " +
                  "SAVE THE SCENE (Ctrl+S) to keep the wiring.");
    }

    /// <summary>
    /// Makes <paramref name="npc"/> talkable: adds a <see cref="Talkable"/> if
    /// missing and sets its dialogue, its focus point (null falls back to a
    /// height above the root) and the shared <see cref="TalkableSet"/>, so the
    /// player's interactor finds it by walking up. Safe to call repeatedly;
    /// works on scene objects and on prefab contents. Does not save anything.
    /// </summary>
    public static Talkable MakeTalkable(GameObject npc, DialogueDefinition definition, Transform focus)
    {
        if (npc == null)
        {
            Debug.LogError("DialogueSetup.MakeTalkable: npc is null.");
            return null;
        }

        var talkable = GetOrAdd<Talkable>(npc);
        SetRefs(talkable,
                ("dialogue", definition), ("focusPoint", focus), ("registry", EnsureTalkableSet()));
        EditorUtility.SetDirty(talkable);
        if (!EditorUtility.IsPersistent(npc) && npc.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(npc.scene);
        return talkable;
    }

    // ------------------------------------------------------------------ assets

    /// <summary>Seeded once; hand tuning on the asset survives every re-run.</summary>
    public static DialogueConfig EnsureConfig() => LoadOrCreate<DialogueConfig>(ConfigPath);

    /// <summary>The shared runtime set; created once.</summary>
    public static TalkableSet EnsureTalkableSet() => LoadOrCreate<TalkableSet>(TalkableSetPath);

    static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        EnsureAssetDir();
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssetIfDirty(asset);
        return asset;
    }

    /// <summary>Creates the dialogue asset folder with its agreed GUID, so this
    /// menu and the NPC branch always agree on it, whichever runs first.</summary>
    static void EnsureAssetDir()
    {
        if (AssetDatabase.IsValidFolder(AssetDir)) return;
        ChibiSandboxSetup.EnsureFolderPublic(Path.GetDirectoryName(AssetDir)!.Replace('\\', '/'));
        string full = ProjectPath(AssetDir);
        Directory.CreateDirectory(full);
        string meta = full + ".meta";
        if (!File.Exists(meta))
        {
            File.WriteAllText(meta,
                "fileFormatVersion: 2\nguid: " + AssetDirGuid + "\nfolderAsset: yes\nDefaultImporter:\n" +
                "  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
        }
        AssetDatabase.ImportAsset(AssetDir, ImportAssetOptions.ForceSynchronousImport);
    }

    static string ProjectPath(string assetPath)
        => Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, assetPath);

    // ------------------------------------------------------------------- scene

    static DialogueDirector EnsureDirector(DialogueConfig config, PlayerInputGate gate)
    {
        var director = Object.FindFirstObjectByType<DialogueDirector>();
        if (director == null)
        {
            var go = GameObject.Find(DirectorObjectName);
            if (go == null) go = new GameObject(DirectorObjectName);
            director = go.AddComponent<DialogueDirector>();
        }
        SetRefs(director, ("config", config), ("inputGate", gate));
        return director;
    }

    /// <summary>Fills the optional gate field on the input readers. Each one
    /// keeps today's behaviour when its field is empty.</summary>
    static void WireGate(GameObject player, PlayerInputGate gate, SpacetimeView spacetime)
    {
        // Soft: SpacetimeView's gate field is a separate, droppable commit.
        if (spacetime != null)
        {
            var so = new SerializedObject(spacetime);
            var property = so.FindProperty("inputGate");
            if (property != null)
            {
                property.objectReferenceValue = gate;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else Debug.LogWarning("Setup Dialogue: SpacetimeView has no inputGate field; T / d-pad " +
                                  "left still toggles 4D during a conversation.");
        }

        var movement = player.GetComponent<PlayerMovementInput>();
        if (movement != null) SetRefs(movement, ("inputGate", gate));
        else Debug.LogWarning("Setup Dialogue: no PlayerMovementInput on the player; movement and dash are not gated.");

        var sword = player.GetComponentInChildren<SwordInput>(true);
        if (sword != null) SetRefs(sword, ("inputGate", gate));
        else Debug.LogWarning("Setup Dialogue: no SwordInput on the player; attacks are not gated.");

        var lockOn = player.GetComponent<LockOnTargeting>();
        if (lockOn != null) SetRefs(lockOn, ("inputGate", gate));
        else Debug.LogWarning("Setup Dialogue: no LockOnTargeting on the player. Run Setup Lock-On, " +
                              "then this again, to gate the lock toggle and to talk to locked NPCs.");
    }

    // --------------------------------------------------------------------- HUD

    /// <summary>
    /// Builds, or re-configures, the box and the prompt:
    ///
    ///   FightHUD (or DialogueHUD)
    ///     DialogueUI           full-screen, nested canvas sorted on top
    ///       TalkPrompt         pill: KeyCap/Key + Label   (InteractPromptView)
    ///       DialogueBox        20–80% width, bottom      (DialogueView)
    ///         Box, NameTab/Name, Body, Continue
    /// </summary>
    static GameObject BuildHud(DialogueDirector director, PlayerInteractor interactor,
                               DialogueConfig config, Camera cam)
    {
        var round = RoundSprite();
        var arrow = ArrowSprite();
        var font = NewtonFightArt.Font(BodyFontName);

        var canvas = FindHudCanvas();
        var root = FindOrMoveUiRoot(canvas);
        Stretch(root, Vector2.zero, Vector2.zero);
        var nested = GetOrAdd<Canvas>(root.gameObject);
        nested.overrideSorting = true;
        nested.sortingOrder = UiSortingOrder;
        // The rest of the HUD is faded out during a talk through CanvasGroups on
        // the root canvases, which may include the one this sits in.
        var ownGroup = GetOrAdd<CanvasGroup>(root.gameObject);
        ownGroup.ignoreParentGroups = true;
        ownGroup.alpha = 1f;
        ownGroup.blocksRaycasts = false;
        ownGroup.interactable = false;
        root.SetAsLastSibling();

        BuildPrompt(root, interactor, config, cam, round, font);
        var view = BuildBox(root, director, config, round, arrow, font);
        SetHudToHide(view, root);
        return canvas.gameObject;
    }

    /// <summary>The DialogueUI root, moved under <paramref name="canvas"/> if an
    /// earlier run built it elsewhere (FightHUD appearing after a first run that
    /// made DialogueHUD). Never two.</summary>
    static RectTransform FindOrMoveUiRoot(Canvas canvas)
    {
        var view = Object.FindFirstObjectByType<DialogueView>(FindObjectsInactive.Include);
        var existing = view != null ? view.transform.parent as RectTransform : null;
        if (existing == null || existing.name != UiRootName) return Child(canvas.transform, UiRootName);
        if (existing.parent == canvas.transform) return existing;

        var oldParent = existing.parent;
        existing.SetParent(canvas.transform, false);
        if (oldParent != null && oldParent.name == OwnHudName && oldParent.childCount == 0)
            Object.DestroyImmediate(oldParent.gameObject);
        return existing;
    }

    /// <summary>Every other screen-space root canvas gets a CanvasGroup the box
    /// fades out while it is up. Sorted by hierarchy path, so the array, and the
    /// scene, are the same on every run.</summary>
    static void SetHudToHide(DialogueView view, RectTransform uiRoot)
    {
        var groups = new List<(string path, CanvasGroup group)>();
        foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var parent = canvas.transform.parent;
            if (parent != null && parent.GetComponentInParent<Canvas>(true) != null) continue;   // nested
            if (canvas.renderMode == RenderMode.WorldSpace) continue;
            // The DialogueHUD canvas holds nothing but the box and the prompt.
            if (canvas.name == OwnHudName && uiRoot.IsChildOf(canvas.transform)) continue;
            groups.Add((PathOf(canvas.transform), GetOrAdd<CanvasGroup>(canvas.gameObject)));
        }
        groups.Sort((a, b) => string.CompareOrdinal(a.path, b.path));

        var so = new SerializedObject(view);
        var array = so.FindProperty("hideWhileTalking");
        array.arraySize = groups.Count;
        for (int i = 0; i < groups.Count; i++)
        {
            array.GetArrayElementAtIndex(i).objectReferenceValue = groups[i].group;
            groups[i].group.alpha = 1f;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static string PathOf(Transform t)
    {
        string path = t.name;
        for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    static Canvas FindHudCanvas()
    {
        var fight = GameObject.Find(FightHudName);
        if (fight != null && fight.TryGetComponent(out Canvas fightCanvas)) return fightCanvas;

        var go = GameObject.Find(OwnHudName);
        if (go == null) go = new GameObject(OwnHudName, typeof(RectTransform));
        var canvas = GetOrAdd<Canvas>(go);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = GetOrAdd<CanvasScaler>(go);
        // The same reference as FightHUD, so the box looks the same either way.
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        return canvas;
    }

    static void BuildPrompt(RectTransform root, PlayerInteractor interactor, DialogueConfig config,
                            Camera cam, Sprite round, TMP_FontAsset font)
    {
        var pill = Child(root, "TalkPrompt");
        pill.anchorMin = pill.anchorMax = new Vector2(0.5f, 0.5f);
        pill.pivot = new Vector2(0.5f, 0f);          // sits on the point, above it
        pill.anchoredPosition = Vector2.zero;
        pill.sizeDelta = new Vector2(176f, 52f);
        pill.SetAsFirstSibling();                     // under the box
        var group = GetOrAdd<CanvasGroup>(pill.gameObject);
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
        // 1: the 52-unit pill is shorter than the slice borders, which Unity
        // shrinks to fit, so the ends come out fully round.
        Rounded(GetOrAdd<Image>(pill.gameObject), round, new Color(0.04f, 0.045f, 0.06f, 0.8f), 1f);

        var cap = Child(pill, "KeyCap");
        cap.anchorMin = cap.anchorMax = new Vector2(0f, 0.5f);
        cap.pivot = new Vector2(0f, 0.5f);
        cap.anchoredPosition = new Vector2(8f, 0f);
        cap.sizeDelta = new Vector2(48f, 38f);
        Rounded(GetOrAdd<Image>(cap.gameObject), round, new Color(0.96f, 0.95f, 0.9f, 1f), 4f);

        var key = Text(Child(cap, "Key"), font, 24f, TextAlignmentOptions.Center,
                       new Color(0.08f, 0.08f, 0.1f, 1f), "E");
        Stretch(key.rectTransform, Vector2.zero, Vector2.zero);

        var label = Text(Child(pill, "Label"), font, 27f, TextAlignmentOptions.Left, Color.white, "Talk");
        var labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(68f, 0f);
        labelRect.offsetMax = new Vector2(-12f, 0f);

        var view = GetOrAdd<InteractPromptView>(pill.gameObject);
        SetRefs(view,
                ("interactor", interactor), ("config", config), ("worldCamera", cam),
                ("group", group), ("pill", pill), ("keyText", key), ("labelText", label));
    }

    static DialogueView BuildBox(RectTransform root, DialogueDirector director, DialogueConfig config,
                                 Sprite round, Sprite arrow, TMP_FontAsset font)
    {
        // 60% of the width, a 1920-wide reference: the same wrap on every aspect.
        const float height = 232f;
        const float bottomMargin = 52f;

        var panel = Child(root, "DialogueBox");
        panel.anchorMin = new Vector2(0.2f, 0f);
        panel.anchorMax = new Vector2(0.8f, 0f);
        panel.pivot = new Vector2(0.5f, 0.5f);         // scales in about its centre
        panel.sizeDelta = new Vector2(0f, height);
        panel.anchoredPosition = new Vector2(0f, bottomMargin + height * 0.5f);
        panel.SetAsLastSibling();
        var group = GetOrAdd<CanvasGroup>(panel.gameObject);
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        var boxRect = Child(panel, "Box");
        Stretch(boxRect, Vector2.zero, Vector2.zero);
        var box = GetOrAdd<Image>(boxRect.gameObject);
        Rounded(box, round, config.BoxColour, 2f);     // 24-unit corners
        boxRect.SetSiblingIndex(0);

        // Name tab: above the top-left corner, overlapping the edge a little so
        // it reads as part of the box. Sized to the name by a layout group.
        var tabRect = Child(panel, "NameTab");
        tabRect.anchorMin = tabRect.anchorMax = new Vector2(0f, 1f);
        tabRect.pivot = new Vector2(0f, 0f);
        tabRect.anchoredPosition = new Vector2(40f, -8f);
        // Width is the fitter's; only the height is ours, so a re-run does not fight it.
        tabRect.sizeDelta = new Vector2(tabRect.sizeDelta.x, 50f);
        tabRect.SetSiblingIndex(1);
        var tab = GetOrAdd<Image>(tabRect.gameObject);
        Rounded(tab, round, config.NameTabColour, 3.4f);
        var layout = GetOrAdd<HorizontalLayoutGroup>(tabRect.gameObject);
        layout.padding = new RectOffset(24, 24, 4, 4);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        var fitter = GetOrAdd<ContentSizeFitter>(tabRect.gameObject);
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        var nameText = Text(Child(tabRect, "Name"), font, 30f, TextAlignmentOptions.Center,
                            config.NameColour, "");
        nameText.characterSpacing = 2f;

        var body = Text(Child(panel, "Body"), font, 36f, TextAlignmentOptions.TopLeft,
                        config.TextColour, "");
        body.fontStyle = FontStyles.Normal;
        body.lineSpacing = 6f;
        body.textWrappingMode = TextWrappingModes.Normal;
        body.overflowMode = TextOverflowModes.Overflow;
        Stretch(body.rectTransform, new Vector2(52f, 40f), new Vector2(-52f, -44f));
        body.rectTransform.SetSiblingIndex(2);

        var markRect = Child(panel, "Continue");
        markRect.anchorMin = markRect.anchorMax = new Vector2(1f, 0f);
        markRect.pivot = new Vector2(1f, 0f);
        markRect.anchoredPosition = new Vector2(-34f, 22f);
        markRect.sizeDelta = new Vector2(28f, 18f);
        markRect.SetSiblingIndex(3);
        var mark = GetOrAdd<Image>(markRect.gameObject);
        mark.sprite = arrow;
        mark.type = Image.Type.Simple;
        mark.preserveAspect = true;
        mark.color = config.ContinueColour;
        mark.raycastTarget = false;
        mark.enabled = false;

        var view = GetOrAdd<DialogueView>(panel.gameObject);
        SetRefs(view,
                ("director", director), ("config", config), ("group", group), ("panel", panel),
                ("box", box), ("nameTab", tab), ("nameText", nameText), ("bodyText", body),
                ("continueMark", mark));
        return view;
    }

    // ---------------------------------------------------------------- sprites

    /// <summary>A white rounded square, 9-sliced. Tinted it is the box, the
    /// tab, the pill and the key cap; the corner size comes from each Image's
    /// pixels-per-unit multiplier.</summary>
    static Sprite RoundSprite() => WriteSprite(RoundSpritePath, RoundSize, RoundSize,
        new Vector4(RoundBorder, RoundBorder, RoundBorder, RoundBorder),
        (x, y) => Mathf.Clamp01(0.5f - RoundBox(x, y, RoundSize, RoundSize, RoundRadius)));

    /// <summary>The ▼: a soft-edged downward triangle. A sprite rather than a
    /// glyph, so it does not depend on the font having U+25BC.</summary>
    static Sprite ArrowSprite() => WriteSprite(ArrowSpritePath, 64, 40, Vector4.zero, (x, y) =>
    {
        // Triangle (6,34)-(58,34)-(32,6) in texture pixels, y up: flat top, apex
        // at the bottom. Coverage is the worst edge's signed distance (positive
        // outside), antialiased over one pixel.
        float top = y - 34f;
        float left = Edge(x, y, new Vector2(6f, 34f), new Vector2(32f, 6f));
        float right = Edge(x, y, new Vector2(32f, 6f), new Vector2(58f, 34f));
        return Mathf.Clamp01(0.5f - Mathf.Max(top, Mathf.Max(left, right)));
    });

    /// <summary>Signed distance from (x, y) to the line a→b; positive on the
    /// right of the direction a→b (y up), which is outside when the edges run
    /// clockwise round the shape.</summary>
    static float Edge(float x, float y, Vector2 a, Vector2 b)
    {
        Vector2 d = (b - a).normalized;
        Vector2 n = new(d.y, -d.x);
        return Vector2.Dot(new Vector2(x, y) - a, n);
    }

    static float RoundBox(float x, float y, float w, float h, float r)
    {
        float qx = Mathf.Abs(x - w * 0.5f) - (w * 0.5f - r);
        float qy = Mathf.Abs(y - h * 0.5f) - (h * 0.5f - r);
        float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
        return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    /// <summary>
    /// Writes a white sprite with the given alpha to <paramref name="path"/>,
    /// but only if the bytes differ, and fixes its import settings only if they
    /// differ, so a re-run touches nothing on disk.
    /// </summary>
    static Sprite WriteSprite(string path, int w, int h, Vector4 border, System.Func<float, float, float> alpha)
    {
        ChibiSandboxSetup.EnsureFolderPublic(UiArtDir);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            pixels[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha(x + 0.5f, y + 0.5f) * 255f));
        tex.SetPixels32(pixels);
        byte[] png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);

        string full = ProjectPath(path);
        bool write = !File.Exists(full) || !BytesEqual(File.ReadAllBytes(full), png);
        if (write)
        {
            File.WriteAllBytes(full, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        bool configured = importer.textureType == TextureImporterType.Sprite
                          && importer.spriteImportMode == SpriteImportMode.Single
                          && importer.spriteBorder == border
                          && !importer.mipmapEnabled
                          && importer.alphaIsTransparency
                          && importer.wrapMode == TextureWrapMode.Clamp
                          && importer.textureCompression == TextureImporterCompression.Uncompressed
                          && settings.spriteMeshType == SpriteMeshType.FullRect;
        if (!configured)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.ReadTextureSettings(settings);
            // Full rect: a tight mesh breaks 9-slicing.
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    // ---------------------------------------------------------------- helpers

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        // Explicit null check: ?? bypasses Unity's fake-null on missing components.
        var c = go.GetComponent<T>();
        if (c == null) c = go.AddComponent<T>();
        return c;
    }

    /// <summary>The child called <paramref name="name"/>, created with a
    /// RectTransform if missing. Found, never rebuilt, so IDs stay stable.</summary>
    static RectTransform Child(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null)
        {
            if (existing is RectTransform rect) return rect;
            Debug.LogWarning($"Setup Dialogue: '{name}' under '{parent.name}' has no RectTransform; replacing it.");
            Object.DestroyImmediate(existing.gameObject);
        }
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    static void Rounded(Image image, Sprite sprite, Color colour, float pixelsPerUnitMultiplier)
    {
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.fillCenter = true;
        image.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier;
        image.color = colour;
        image.raycastTarget = false;
    }

    static TextMeshProUGUI Text(RectTransform rect, TMP_FontAsset font, float size,
                                TextAlignmentOptions alignment, Color colour, string initial)
    {
        var text = GetOrAdd<TextMeshProUGUI>(rect.gameObject);
        if (font != null)
        {
            text.font = font;
            text.fontSharedMaterial = font.material;
        }
        text.fontSize = size;
        text.alignment = alignment;
        text.color = colour;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        text.richText = true;
        text.text = initial;
        return text;
    }

    /// <summary>Writes object references through SerializedObject, as the
    /// Inspector would, and fails loudly on a renamed field.</summary>
    static void SetRefs(Object target, params (string field, Object value)[] refs)
    {
        var so = new SerializedObject(target);
        foreach (var (field, value) in refs)
        {
            var property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"Setup Dialogue: {target.GetType().Name} has no serialized field " +
                               $"'{field}'. It was probably renamed; update DialogueSetup.");
                continue;
            }
            property.objectReferenceValue = value;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
