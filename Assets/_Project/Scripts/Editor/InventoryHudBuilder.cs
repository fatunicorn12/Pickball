using Pickleball.Sandbox.Inventory;
using Pickleball.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Builds the "InventoryHUD" canvas: the inventory menu (hidden until opened)
/// and the equipped-weapon readout in the bottom-right corner. Called by
/// <see cref="InventorySetup"/>; not a menu item itself.
///
/// Its own Screen Space – Overlay canvas, drawn above FightHUD and SwordHUD
/// (sort order <see cref="SortOrder"/>), so the menu covers the fight HUD
/// and the HUD menus never need to agree on sibling order. Same reference
/// resolution as FightHUD (1920 × 1080).
///
/// Idempotent the way MagicHudBuilder is: the canvas's children are destroyed
/// and rebuilt from code every run. It generates no art: it reuses the
/// rounded plate and bevel frame from NewtonFightArt and the slot well,
/// frame and dot from the magic HUD (loaded by path), and the HUD's
/// outlined Oswald, so the menu matches the spell selector. A sprite that is
/// missing (Setup Magic never ran) degrades to a plain square, never an error.
/// </summary>
public static class InventoryHudBuilder
{
    const string CanvasName = "InventoryHUD";
    const string MenuName = "InventoryMenu";
    const string ReadoutName = "EquippedWeapon";
    const int SortOrder = 40;

    const string UiDir = "Assets/_Project/Art/UI";
    const string MagicUiDir = UiDir + "/Magic";

    // Layout, in canvas pixels at 1920 × 1080.
    static readonly Vector2 PanelSize = new(1120f, 480f);
    const float Pad = 40f;
    const float TabWidth = 210f, TabHeight = 52f, TabGap = 12f, TabTop = 30f;
    const float GridTop = 128f;
    const float SlotSize = 96f, SlotGap = 20f;
    const float DetailLeft = 680f, DetailIcon = 112f;
    const float HintBottom = 26f;

    // Strong, because UI blends in linear space here: 0.6 only dimmed the scene by a third.
    static readonly Color Backdrop = new(0.01f, 0.02f, 0.05f, 0.78f);
    static readonly Color PlateColour = new(0.03f, 0.04f, 0.1f, 0.93f);
    static readonly Color FrameColour = new(0.74f, 0.8f, 0.92f, 0.85f);
    static readonly Color WellColour = new(0.06f, 0.08f, 0.16f, 0.95f);
    static readonly Color RuleColour = new(0.62f, 0.68f, 0.8f, 0.3f);
    static readonly Color HintColour = new(0.62f, 0.68f, 0.8f, 0.9f);
    static readonly Color AccentColour = new(0.62f, 0.84f, 1f, 1f);

    struct Style
    {
        public Sprite Plate, Frame, Well, SlotFrame, Dot;
        public TMP_FontAsset Font;
        public Material FontMaterial;
    }

    /// <summary>
    /// Finds or creates the InventoryHUD canvas in <paramref name="scene"/> and
    /// rebuilds its contents, wired to <paramref name="menu"/> and
    /// <paramref name="equipper"/>. The grid gets one slot per slot of the
    /// largest tab in <paramref name="config"/>.
    /// </summary>
    public static void Build(Scene scene, InventoryMenu menu, WeaponEquipper equipper, InventoryConfig config)
    {
        var canvas = FindOrCreateCanvas(scene);
        for (int i = canvas.childCount - 1; i >= 0; i--) Object.DestroyImmediate(canvas.GetChild(i).gameObject);

        var style = LoadStyle();
        var capacity = config != null ? config.Capacity : InventoryCapacity.Default;
        int slots = Mathf.Max(capacity.Weapons, Mathf.Max(capacity.Magic, capacity.KeyItems));
        int columns = config != null ? config.Columns : 5;

        BuildReadout(canvas, equipper, style);
        BuildMenu(canvas, menu, equipper, style, slots, columns);
        EditorUtility.SetDirty(canvas.gameObject);
    }

    static RectTransform FindOrCreateCanvas(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == CanvasName && root.TryGetComponent(out Canvas _))
                return (RectTransform)root.transform;

        var go = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        SceneManager.MoveGameObjectToScene(go, scene);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortOrder;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        go.layer = LayerMask.NameToLayer("UI") >= 0 ? LayerMask.NameToLayer("UI") : 0;
        return (RectTransform)go.transform;
    }

    static Style LoadStyle()
    {
        var style = new Style
        {
            Plate = Load($"{UiDir}/BarMask.png") ?? NewtonFightArt.BarMask(),
            Frame = Load($"{UiDir}/BarFrame.png") ?? NewtonFightArt.BarFrame(),
            Well = Load($"{MagicUiDir}/SlotWell.png"),
            SlotFrame = Load($"{MagicUiDir}/SlotFrame.png"),
            Dot = Load($"{MagicUiDir}/Dot.png"),
            Font = NewtonFightArt.Font("Oswald Bold SDF"),
        };
        if (style.Font != null)
        {
            style.FontMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{UiDir}/HudLabel.mat");
            if (style.FontMaterial == null)
                style.FontMaterial = NewtonFightArt.OutlinedFontMaterial(style.Font, "HudLabel", 0.2f);
        }
        return style;
    }

    static Sprite Load(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        return sprite != null ? sprite : null;   // never a fake-null into ??
    }

    // ---- readout -----------------------------------------------------------

    /// <summary>
    ///   EquippedWeapon (CanvasGroup; bottom-right)
    ///     Body (scaled for the pop)
    ///       Plate → Frame → Caption ("WEAPON  ·  X") → Name
    /// </summary>
    static void BuildReadout(RectTransform canvas, WeaponEquipper equipper, Style s)
    {
        var root = NewRect(ReadoutName, canvas);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(1f, 0f);
        root.sizeDelta = new Vector2(280f, 84f);
        root.anchoredPosition = new Vector2(-40f, 40f);
        var group = root.gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        group.alpha = 0f;

        var body = NewRect("Body", root);
        Stretch(body);
        body.pivot = new Vector2(1f, 0f);
        AddImage(body, "Plate", PlateColour, s.Plate, Image.Type.Sliced);
        var frame = AddImage(body, "Frame", FrameColour, s.Frame, Image.Type.Sliced);
        Outset(frame.rectTransform, 5f);

        var caption = AddText(body, "Caption", s, 17f, TextAlignmentOptions.TopLeft);
        Stretch(caption.rectTransform);
        caption.rectTransform.offsetMin = new Vector2(16f, 0f);
        caption.rectTransform.offsetMax = new Vector2(-14f, -10f);
        caption.text = "WEAPON  ·  X / SELECT";
        caption.color = AccentColour;

        var name = AddText(body, "Name", s, 30f, TextAlignmentOptions.BottomLeft);
        Stretch(name.rectTransform);
        name.rectTransform.offsetMin = new Vector2(16f, 10f);
        name.rectTransform.offsetMax = new Vector2(-14f, 0f);
        name.fontStyle = FontStyles.Bold;
        name.color = Color.white;
        name.text = "";

        var view = root.gameObject.AddComponent<InventoryEquippedView>();
        var so = new SerializedObject(view);
        Set(so, "equipper", equipper);
        Set(so, "group", group);
        Set(so, "body", body);
        Set(so, "nameText", name);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---- menu --------------------------------------------------------------

    /// <summary>
    ///   InventoryMenu (CanvasGroup; hidden until opened)
    ///     Backdrop (dims the game)
    ///     Panel
    ///       Plate → Frame
    ///       Tab0..2 (Plate → Label)        Count ("2 / 8")
    ///       Rule
    ///       Grid: Slot0..n (Well → Icon → Initial → Frame → Marker)
    ///       Detail: Icon well (Well → Icon → Initial → Frame) → Name → Status → Description
    ///       Hint
    /// </summary>
    static void BuildMenu(RectTransform canvas, InventoryMenu menu, WeaponEquipper equipper, Style s,
                          int slotCount, int columns)
    {
        var root = NewRect(MenuName, canvas);
        Stretch(root);
        var group = root.gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        group.alpha = 0f;

        AddImage(root, "Backdrop", Backdrop, null, Image.Type.Simple);

        var panel = NewRect("Panel", root);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = PanelSize;
        panel.anchoredPosition = Vector2.zero;
        AddImage(panel, "Plate", PlateColour, s.Plate, Image.Type.Sliced);
        var frame = AddImage(panel, "Frame", FrameColour, s.Frame, Image.Type.Sliced);
        Outset(frame.rectTransform, 5f);

        // Tabs along the top.
        string[] names = { "WEAPONS", "MAGIC", "KEY ITEMS" };
        var tabPlates = new Image[names.Length];
        var tabLabels = new TMP_Text[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            var tab = NewRect($"Tab{i}", panel);
            TopLeft(tab, new Vector2(Pad + i * (TabWidth + TabGap), TabTop), new Vector2(TabWidth, TabHeight));
            tabPlates[i] = AddImage(tab, "Plate", new Color(0.12f, 0.15f, 0.26f, 0.9f), s.Plate, Image.Type.Sliced);
            // The view hit-tests the plate's rect; make it the tab's own rect.
            var label = AddText(tab, "Label", s, 26f, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            label.fontStyle = FontStyles.Bold;
            label.text = names[i];
            label.color = Color.white;
            tabLabels[i] = label;
        }

        var tabHint = AddText(panel, "TabHint", s, 18f, TextAlignmentOptions.Left);
        TopLeft(tabHint.rectTransform, new Vector2(Pad + names.Length * (TabWidth + TabGap) + 6f, TabTop),
                new Vector2(220f, TabHeight));
        tabHint.text = "Q / E  ·  LB / RB";
        tabHint.color = HintColour;

        var count = AddText(panel, "Count", s, 22f, TextAlignmentOptions.Right);
        TopLeft(count.rectTransform, new Vector2(PanelSize.x - Pad - 140f, TabTop), new Vector2(140f, TabHeight));
        count.color = HintColour;

        var rule = AddImage(panel, "Rule", RuleColour, null, Image.Type.Simple);
        TopLeft(rule.rectTransform, new Vector2(Pad, TabTop + TabHeight + 18f), new Vector2(PanelSize.x - 2f * Pad, 2f));

        // The slot grid.
        var slots = new RectTransform[slotCount];
        var frames = new Image[slotCount];
        var icons = new Image[slotCount];
        var initials = new TMP_Text[slotCount];
        var markers = new Image[slotCount];
        for (int i = 0; i < slotCount; i++)
        {
            int row = i / columns, col = i % columns;
            var slot = NewRect($"Slot{i + 1}", panel);
            slot.anchorMin = slot.anchorMax = new Vector2(0f, 1f);
            slot.pivot = new Vector2(0.5f, 0.5f);
            slot.sizeDelta = new Vector2(SlotSize, SlotSize);
            slot.anchoredPosition = new Vector2(Pad + SlotSize * 0.5f + col * (SlotSize + SlotGap),
                                                -(GridTop + SlotSize * 0.5f + row * (SlotSize + SlotGap)));
            BuildWell(slot, s, SlotSize, 44f, out icons[i], out initials[i], out frames[i]);

            var marker = AddImage(slot, "Marker", new Color(1f, 0.8f, 0.25f, 1f), s.Dot, Image.Type.Simple);
            Centre(marker.rectTransform, new Vector2(SlotSize * 0.5f - 10f, SlotSize * 0.5f - 10f), new Vector2(28f, 28f));
            marker.enabled = false;

            slots[i] = slot;
            markers[i] = marker;
        }

        // The highlighted item, right of the grid.
        var iconWell = NewRect("DetailIcon", panel);
        TopLeft(iconWell, new Vector2(DetailLeft, GridTop), new Vector2(DetailIcon, DetailIcon));
        BuildWell(iconWell, s, DetailIcon, 56f, out var detailIcon, out var detailInitial, out _);

        float textLeft = DetailLeft + DetailIcon + 22f;
        float textWidth = PanelSize.x - Pad - textLeft;
        var name = AddText(panel, "Name", s, 40f, TextAlignmentOptions.BottomLeft);
        TopLeft(name.rectTransform, new Vector2(textLeft, GridTop), new Vector2(textWidth, 60f));
        name.fontStyle = FontStyles.Bold;
        name.color = Color.white;

        var status = AddText(panel, "Status", s, 22f, TextAlignmentOptions.TopLeft);
        TopLeft(status.rectTransform, new Vector2(textLeft, GridTop + 66f), new Vector2(textWidth, 40f));
        status.color = AccentColour;

        float descriptionWidth = PanelSize.x - Pad - DetailLeft;
        var description = AddText(panel, "Description", s, 24f, TextAlignmentOptions.TopLeft);
        TopLeft(description.rectTransform, new Vector2(DetailLeft, GridTop + DetailIcon + 22f),
                new Vector2(descriptionWidth, 150f));
        description.textWrappingMode = TextWrappingModes.Normal;
        description.color = new Color(0.9f, 0.93f, 1f, 1f);

        var hint = AddText(panel, "Hint", s, 20f, TextAlignmentOptions.Bottom);
        hint.rectTransform.anchorMin = new Vector2(0f, 0f);
        hint.rectTransform.anchorMax = new Vector2(1f, 0f);
        hint.rectTransform.pivot = new Vector2(0.5f, 0f);
        hint.rectTransform.sizeDelta = new Vector2(-2f * Pad, 30f);
        hint.rectTransform.anchoredPosition = new Vector2(0f, HintBottom);
        hint.text = "ENTER / A  use      ARROWS / D-PAD  move      I / ESC / B  close      " +
                    "X / SELECT  next weapon (outside the menu)";
        hint.color = HintColour;

        var view = root.gameObject.AddComponent<InventoryMenuView>();
        var so = new SerializedObject(view);
        Set(so, "menu", menu);
        Set(so, "equipper", equipper);
        Set(so, "group", group);
        SetArray(so, "tabPlates", tabPlates);
        SetArray(so, "tabLabels", tabLabels);
        SetArray(so, "slots", slots);
        SetArray(so, "slotFrames", frames);
        SetArray(so, "slotIcons", icons);
        SetArray(so, "slotInitials", initials);
        SetArray(so, "slotMarkers", markers);
        Set(so, "nameText", name);
        Set(so, "statusText", status);
        Set(so, "descriptionText", description);
        Set(so, "detailWell", iconWell.gameObject);
        Set(so, "detailIcon", detailIcon);
        Set(so, "detailInitial", detailInitial);
        Set(so, "countText", count);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>A slot's layers: dark well, icon, initial (for items without an
    /// icon), frame. Same sprites as the spell selector's slots.</summary>
    static void BuildWell(RectTransform slot, Style s, float size, float initialSize,
                          out Image icon, out TMP_Text initial, out Image frame)
    {
        AddImage(slot, "Well", WellColour, s.Well, Image.Type.Simple);
        icon = AddImage(slot, "Icon", Color.white, null, Image.Type.Simple);
        Centre(icon.rectTransform, Vector2.zero, new Vector2(size - 18f, size - 18f));
        icon.preserveAspect = true;
        icon.enabled = false;
        initial = AddText(slot, "Initial", s, initialSize, TextAlignmentOptions.Center);
        Stretch(initial.rectTransform);
        initial.fontStyle = FontStyles.Bold;
        initial.color = Color.white;
        initial.enabled = false;
        frame = AddImage(slot, "Frame", FrameColour, s.SlotFrame, Image.Type.Simple);
    }

    // ---- plumbing ----------------------------------------------------------

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static void Outset(RectTransform rect, float by)
    {
        rect.offsetMin = new Vector2(-by, -by);
        rect.offsetMax = new Vector2(by, by);
    }

    static void Centre(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    /// <summary>Places <paramref name="rect"/> by its top-left corner, measured
    /// from the parent's top-left (x right, y down).</summary>
    static void TopLeft(RectTransform rect, Vector2 fromTopLeft, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(fromTopLeft.x, -fromTopLeft.y);
    }

    static Image AddImage(RectTransform parent, string name, Color colour, Sprite sprite, Image.Type type)
    {
        var rect = NewRect(name, parent);
        Stretch(rect);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = colour;
        image.raycastTarget = false;
        image.sprite = sprite;
        // Sliced without a sprite draws nothing useful; a plain quad is the honest fallback.
        image.type = sprite != null ? type : Image.Type.Simple;
        return image;
    }

    static TextMeshProUGUI AddText(RectTransform parent, string name, Style s, float size,
                                   TextAlignmentOptions alignment)
    {
        var rect = NewRect(name, parent);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (s.Font != null) text.font = s.Font;
        if (s.FontMaterial != null) text.fontSharedMaterial = s.FontMaterial;
        text.fontSize = size;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    static void SetArray<T>(SerializedObject so, string field, T[] values) where T : Object
    {
        var property = so.FindProperty(field);
        if (property == null)
        {
            Debug.LogError($"InventoryHudBuilder: {so.targetObject.GetType().Name} has no serialized field '{field}'.");
            return;
        }
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    static void Set(SerializedObject so, string field, Object value)
    {
        var property = so.FindProperty(field);
        if (property == null)
        {
            Debug.LogError($"InventoryHudBuilder: {so.targetObject.GetType().Name} has no serialized field '{field}'.");
            return;
        }
        property.objectReferenceValue = value;
    }
}
