using System;
using System.IO;
using Pickleball.Sandbox.Magic;
using Pickleball.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Builds the magic HUD under a given canvas — the mana bar on top of the
/// player's health bar, the spell selector beside it, the spell crosshair at
/// screen centre and the channel bar under it — plus the spells' generated
/// icons. Called by <see cref="MagicSetup"/>; not a menu item itself.
///
/// Idempotent the way NewtonFightSetup's HUD is: both roots are destroyed and
/// rebuilt from code on every run, so the HUD in the scene is always exactly
/// what this file describes, and the generated sprites are rewritten in place
/// (their .meta, and so every reference, is kept).
///
/// The mana bar is deliberately a sibling of the health bar rather than a new
/// design: the same rounded well, bevel frame and fill shading from
/// <see cref="NewtonFightArt"/>, the same outlined font material, so the two
/// read as one stack. Only the colour, the size and the mana-specific layers
/// (notches, cost preview, regen readouts) differ.
///
/// It is placed from the health bar's own RectTransform, so moving or
/// re-anchoring the health bar and re-running Setup Magic carries the mana bar
/// with it; without a health bar it takes the spot NewtonFightSetup would have
/// given one.
/// </summary>
public static class MagicHudBuilder
{
    const string ArtDir = "Assets/_Project/Art/UI/Magic";

    const string ManaBarName = "PlayerManaBar";
    const string CrosshairName = "SpellCrosshair";
    const string SelectorName = "SpellSelector";
    const string ChannelBarName = "SpellChannelBar";

    /// <summary>The shapes <see cref="SpellIcon"/> can draw, one per spell.</summary>
    public enum IconShape { Orb, Flame, Bolt, Snowflake, Cross }

    // Selector slot size and spacing, in canvas pixels, and its gap from the
    // mana bar's right end.
    const float SlotSize = 40f;
    const float SlotGap = 10f;
    const float SelectorGap = 26f;

    // Channel bar size and its drop below screen centre (below the crosshair's
    // cost label), in canvas pixels.
    static readonly Vector2 ChannelBarSize = new(280f, 14f);
    const float ChannelBarDrop = 112f;
    const string HealthBarName = "PlayerHealthBar";
    const string DamageNumbersName = "DamageNumbers";

    // NewtonFightSetup.BuildHud's player bar (anchor and pivot bottom-left),
    // used when the canvas has none to sit on.
    const float DefaultHealthX = 48f;
    const float DefaultHealthY = 48f;
    const float DefaultHealthHeight = 30f;

    /// <summary>BarFrame is laid this far outside the well on every side, on
    /// both bars (NewtonFightSetup.BuildBar).</summary>
    const float FrameOutset = 5f;

    /// <summary>Canvas pixels between the health bar's frame and the mana bar's:
    /// close enough to read as one stack, far enough that the two dark
    /// outlines never merge into a single thick line.</summary>
    const float GapAboveHealth = 10f;

    /// <summary>Shorter and thinner than the health bar's 520 × 30: mana is the
    /// secondary resource and should look it. 18 px is also exactly the well
    /// sprite's two 9 px slice borders, so its rounded ends stay round.</summary>
    static readonly Vector2 BarSize = new(420f, 18f);

    /// <summary>The halo sprite's soft edge reaches this far past the well.</summary>
    const float HaloReach = 16f;

    static readonly Color WellColour = new(0.04f, 0.05f, 0.11f, 0.92f);
    static readonly Color TrailColour = new(0.82f, 0.93f, 1f);
    static readonly Color FrameColour = new(0.74f, 0.8f, 0.92f);
    static readonly Color IconColour = new(0.55f, 0.8f, 1f);
    static readonly Color NotchColour = new(0.03f, 0.06f, 0.18f, 0.6f);
    static readonly Color RingColour = new(0.56f, 0.84f, 1f, 0.95f);
    static readonly Color RingTrackColour = new(1f, 1f, 1f, 0.2f);
    static readonly Color CostColour = new(0.62f, 0.84f, 1f);

    // Fill gradient, left to right: the bar grows more cyan as it fills.
    static readonly Color FillDeep = new(0.16f, 0.36f, 1f);
    static readonly Color FillMid = new(0.16f, 0.56f, 1f);
    static readonly Color FillBright = new(0.32f, 0.9f, 1f);

    struct HudStyle
    {
        public Sprite Mask, Frame, Fill, ManaFill, Band, Glow, Star, Ring, Dot, Tick, SlotWell, SlotFrame;
        public TMP_FontAsset Font;
        public Material FontMaterial;
    }

    /// <summary>
    /// Destroys and rebuilds "PlayerManaBar", "SpellSelector", "SpellCrosshair"
    /// and "SpellChannelBar" under <paramref name="canvas"/>, wired to
    /// <paramref name="mana"/>, <paramref name="caster"/> and
    /// <paramref name="selector"/>. Any of those may be null — the views hide
    /// what they cannot drive — but the canvas may not. The selector gets one
    /// slot per slot of <paramref name="selector"/>.
    /// </summary>
    public static void Build(Transform canvas, Mana mana, SpellCaster caster, SpellSelector selector = null)
    {
        if (canvas == null)
        {
            Debug.LogError("MagicHudBuilder: no canvas to build the mana bar and crosshair under.");
            return;
        }
        if (mana == null && caster == null)
            Debug.LogWarning("MagicHudBuilder: neither a Mana nor a SpellCaster was given; the mana " +
                             "bar and crosshair are built unbound and will stay empty.");

        var style = LoadStyle();
        var bar = BuildManaBar(canvas, mana, caster, style);
        var crosshair = BuildCrosshair(canvas, caster, style);
        SetDrawOrder(canvas, bar, crosshair);
        var picker = BuildSelector(canvas, selector, (RectTransform)bar, style);
        var channel = BuildChannelBar(canvas, caster, style);
        // The selector sits beside the mana bar and never overlaps it; the
        // channel bar draws with the crosshair, over the damage numbers.
        picker.SetSiblingIndex(bar.GetSiblingIndex());
        channel.SetSiblingIndex(crosshair.GetSiblingIndex() + 1);
        EditorUtility.SetDirty(canvas.gameObject);
    }

    /// <summary>
    /// The icon for one spell: a white shape with a dark rim, tinted by the
    /// spell's theme colour in the HUD. Written to Art/UI/Magic/SpellIcon*.png
    /// and reimported in place, so a SpellDefinition's reference survives a
    /// re-run. Setup Magic assigns it only to a spell whose icon is blank.
    /// </summary>
    public static Sprite SpellIcon(IconShape shape)
    {
        ChibiSandboxSetup.EnsureFolderPublic(ArtDir);
        Func<Vector2, bool> inside = shape switch
        {
            IconShape.Flame => InFlame,
            IconShape.Bolt => InBolt,
            IconShape.Snowflake => InSnowflake,
            IconShape.Cross => InCross,
            _ => InOrb,
        };
        return WriteSprite($"{ArtDir}/SpellIcon{shape}.png", 64, 64, Vector4.zero, true, (x, y) =>
        {
            // 4x4 supersampled shape, and a rim from the shape grown by ~4 px.
            const int n = 4;
            const float rim = 0.11f;
            float core = 0f, outline = 0f;
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                // Texture rows run bottom-up, so +y is up in the icon too.
                var q = new Vector2((x - 0.5f + (i + 0.5f) / n) / 32f - 1f, (y - 0.5f + (j + 0.5f) / n) / 32f - 1f);
                bool hit = inside(q);
                if (hit) core += 1f;
                bool near = hit;
                for (int k = 0; k < 8 && !near; k++)
                {
                    float a = k * Mathf.PI / 4f;
                    near = inside(q + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rim);
                }
                if (near) outline += 1f;
            }
            return Outlined(core / (n * n), outline / (n * n));
        });
    }

    static bool InOrb(Vector2 p)
    {
        float r = p.magnitude;
        return r < 0.42f || Mathf.Abs(r - 0.68f) < 0.07f;
    }

    static bool InFlame(Vector2 p)
    {
        const float baseY = -0.32f, radius = 0.44f, height = 1.12f;
        if ((p - new Vector2(0f, baseY)).magnitude < radius) return true;
        float t = (p.y - baseY) / height;
        if (t < 0f || t > 1f) return false;
        float sway = 0.16f * Mathf.Sin(t * 3.2f) * t;
        return Mathf.Abs(p.x - sway) < radius * Mathf.Pow(1f - t, 0.9f);
    }

    static readonly Vector2[] BoltShape =
    {
        new(0.32f, 0.9f), new(-0.4f, 0.02f), new(-0.03f, 0.02f), new(-0.3f, -0.9f),
        new(0.42f, 0.12f), new(0.05f, 0.12f),
    };

    static bool InBolt(Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = BoltShape.Length - 1; i < BoltShape.Length; j = i++)
        {
            Vector2 a = BoltShape[i], b = BoltShape[j];
            if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }
        return inside;
    }

    static bool InSnowflake(Vector2 p)
    {
        if (p.magnitude < 0.16f) return true;
        for (int k = 0; k < 6; k++)
        {
            float a = Mathf.PI * 0.5f + k * Mathf.PI / 3f;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            if (SegmentDistance(p, Vector2.zero, dir * 0.82f) < 0.065f) return true;
            for (int s = -1; s <= 1; s += 2)
            {
                float b = a + s * 0.85f;
                var twig = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                if (SegmentDistance(p, dir * 0.45f, dir * 0.45f + twig * 0.26f) < 0.05f) return true;
                if (SegmentDistance(p, dir * 0.66f, dir * 0.66f + twig * 0.16f) < 0.045f) return true;
            }
        }
        return false;
    }

    static bool InCross(Vector2 p)
    {
        bool plus = (Mathf.Abs(p.x) < 0.17f && Mathf.Abs(p.y) < 0.55f) || (Mathf.Abs(p.y) < 0.17f && Mathf.Abs(p.x) < 0.55f);
        return plus || Mathf.Abs(p.magnitude - 0.8f) < 0.06f;
    }

    static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return (p - (a + ab * t)).magnitude;
    }

    static HudStyle LoadStyle()
    {
        ChibiSandboxSetup.EnsureFolderPublic(ArtDir);
        var style = new HudStyle
        {
            Mask = NewtonFightArt.BarMask(),
            Frame = NewtonFightArt.BarFrame(),
            Fill = NewtonFightArt.BarFill(),
            ManaFill = ManaFillSprite(),
            Band = SoftBandSprite(),
            Glow = BarGlowSprite(),
            Star = ManaStarSprite(),
            Ring = RingSprite(),
            Dot = DotSprite(),
            Tick = TickSprite(),
            SlotWell = SlotWellSprite(),
            SlotFrame = SlotFrameSprite(),
            Font = NewtonFightArt.Font("Oswald Bold SDF"),
        };
        // The health bar's own label material (same name, same settings), so
        // the two numbers are one material and batch together.
        if (style.Font != null)
            style.FontMaterial = NewtonFightArt.OutlinedFontMaterial(style.Font, "HudLabel", 0.2f);
        return style;
    }

    /// <summary>
    /// Canvas children draw in sibling order, so it is set on purpose:
    ///
    ///   DamageNumbers → SpellCrosshair → … → PlayerManaBar → PlayerHealthBar → …
    ///
    /// The crosshair draws over the damage numbers because it is the aim: a
    /// number drifting through screen centre must never hide the point the orb
    /// will fly to. It draws under every bar (and the bark line) because the
    /// HUD's fixed furniture is never covered by playfield overlays.
    /// NewtonFightSetup always makes DamageNumbers the first child and rebuilds
    /// its bars at the end, so this order survives either menu being re-run.
    ///
    /// The mana bar goes immediately before the health bar so that, where the
    /// mana bar's full-glow halo reaches down over the health frame, the health
    /// bar draws on top rather than being washed out by it.
    /// </summary>
    static void SetDrawOrder(Transform canvas, Transform bar, Transform crosshair)
    {
        var health = canvas.Find(HealthBarName);
        if (health != null) bar.SetSiblingIndex(health.GetSiblingIndex());
        else bar.SetAsLastSibling();

        var numbers = canvas.Find(DamageNumbersName);
        crosshair.SetSiblingIndex(numbers != null ? numbers.GetSiblingIndex() + 1 : 0);
        // Whatever order the canvas was built in, never above a bar.
        if (crosshair.GetSiblingIndex() > bar.GetSiblingIndex())
            crosshair.SetSiblingIndex(bar.GetSiblingIndex());
    }

    // ---- mana bar --------------------------------------------------------

    /// <summary>
    /// One bar, painted back to front (see <see cref="ManaBarView"/> for what
    /// each layer is for):
    ///
    ///   Body (what shakes)
    ///     Halo
    ///     Well (rounded track, and the Mask that rounds everything inside it)
    ///       Trail → Current → Shimmer[Band] → FillGlow → CostPreview →
    ///       DelayLine → Notches[Notch…] → Glint → Flash
    ///     Frame (hollow bevel, 5 px proud of the well)
    ///     Icon (mana star over the left end cap)
    ///     Value ("73 / 100")
    /// </summary>
    static Transform BuildManaBar(Transform canvas, Mana mana, SpellCaster caster, HudStyle s)
    {
        DestroyChildren(canvas, ManaBarName);
        var root = NewRect(ManaBarName, canvas);
        PlaceAboveHealthBar(root, canvas.Find(HealthBarName) as RectTransform);

        var body = NewRect("Body", root);
        Stretch(body);

        var halo = AddImage(body, "Halo", Color.clear, s.Glow, Image.Type.Sliced);
        Outset(halo.rectTransform, HaloReach);

        var well = AddImage(body, "Well", WellColour, s.Mask, Image.Type.Sliced);
        well.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var w = well.rectTransform;

        var trail = AddFill(w, "Trail", TrailColour, s.Fill);
        // White: the gradient is baked into the sprite, and the view dims it
        // by multiplying a tint in during the regen delay.
        var current = AddFill(w, "Current", Color.white, s.ManaFill);

        // RectMask2D rather than a second stencil Mask: its rect is moved every
        // frame to follow the fill, which a clip rect handles for free.
        var shimmerClip = NewRect("Shimmer", w);
        Stretch(shimmerClip);
        shimmerClip.gameObject.AddComponent<RectMask2D>();
        var band = AddImage(shimmerClip, "Band", Color.clear, s.Band, Image.Type.Simple);
        VerticalStrip(band.rectTransform, 0f, 56f);
        band.rectTransform.anchoredPosition = new Vector2(-28f, 0f);

        var fillGlow = AddFill(w, "FillGlow", Color.clear, s.Fill);

        var preview = AddImage(w, "CostPreview", Color.clear, s.Fill, Image.Type.Simple);
        preview.rectTransform.anchorMax = new Vector2(0f, 1f);

        // Along the bottom lip, inset from the rounded ends so the mask does not
        // nibble it into a dash.
        var delayLine = AddFill(w, "DelayLine", Color.clear, s.Fill);
        var dl = delayLine.rectTransform;
        dl.anchorMin = new Vector2(0f, 0f);
        dl.anchorMax = new Vector2(1f, 0f);
        dl.pivot = new Vector2(0.5f, 0f);
        dl.offsetMin = new Vector2(6f, 2f);
        dl.offsetMax = new Vector2(-6f, 5f);

        var notches = NewRect("Notches", w);
        Stretch(notches);
        var notchTemplate = BuildNotches(notches, mana, caster);

        var glint = AddImage(w, "Glint", Color.clear, s.Band, Image.Type.Simple);
        VerticalStrip(glint.rectTransform, 0f, 90f);
        glint.rectTransform.anchoredPosition = new Vector2(-45f, 0f);

        var flash = AddImage(w, "Flash", Color.clear, s.Fill, Image.Type.Simple);

        var frame = AddImage(body, "Frame", FrameColour, s.Frame, Image.Type.Sliced);
        Outset(frame.rectTransform, FrameOutset);

        // Over the left end cap, the star's left edge flush with the frame's,
        // so the mana bar stays left-aligned with the health bar below.
        var icon = AddImage(body, "Icon", IconColour, s.Star, Image.Type.Simple);
        var ir = icon.rectTransform;
        ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f);
        ir.pivot = new Vector2(0.5f, 0.5f);
        ir.sizeDelta = new Vector2(30f, 30f);
        ir.anchoredPosition = new Vector2(15f - FrameOutset, 0f);

        var value = AddText(body, "Value", s, 15f, TextAlignmentOptions.Right);
        Stretch(value.rectTransform);
        value.rectTransform.offsetMax = new Vector2(-10f, 0f);
        value.color = new Color(1f, 1f, 1f, 0.95f);
        // Edit-mode preview only; the view rewrites it on its first frame.
        value.text = mana != null ? $"{mana.Current} / {mana.Max}" : "";

        var view = root.gameObject.AddComponent<ManaBarView>();
        var so = new SerializedObject(view);
        Set(so, "source", mana);
        Set(so, "caster", caster);
        Set(so, "body", body);
        Set(so, "halo", halo);
        Set(so, "currentFill", current);
        Set(so, "trailFill", trail);
        Set(so, "shimmerClip", shimmerClip);
        Set(so, "shimmerBand", band);
        Set(so, "fillGlow", fillGlow);
        Set(so, "costPreview", preview);
        Set(so, "delayLine", delayLine);
        Set(so, "notchContainer", notches);
        Set(so, "notchTemplate", notchTemplate);
        Set(so, "glint", glint);
        Set(so, "flash", flash);
        Set(so, "frame", frame);
        Set(so, "icon", icon);
        Set(so, "valueText", value);
        so.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    /// <summary>
    /// The template notch plus, for the edit-mode picture, one per cast's worth
    /// short of the end. The view re-lays them out from the live cost and max on
    /// its first frame (reusing these children), so a retuned spell never
    /// leaves them stale.
    /// </summary>
    static Image BuildNotches(RectTransform container, Mana mana, SpellCaster caster)
    {
        int cost = caster != null ? caster.Cost : 0;
        int max = mana != null ? mana.Max : 0;
        int count = cost > 0 && max > 0 ? (max - 1) / cost : 0;

        // No sprite: a notch is a flat 2 px cut, and Simple images draw a plain
        // quad without one.
        Image template = null;
        for (int i = 0; i < Mathf.Max(count, 1); i++)
        {
            var notch = AddImage(container, "Notch", NotchColour, null, Image.Type.Simple);
            float x = count > 0 ? (float)((i + 1) * cost) / max : 0f;
            VerticalStrip(notch.rectTransform, x, 2f);
            notch.rectTransform.sizeDelta = new Vector2(2f, -4f);   // 2 px shy of top and bottom
            if (template == null) template = notch;
        }
        if (count == 0) template.gameObject.SetActive(false);
        return template;
    }

    /// <summary>
    /// Bottom-left corner directly above the health bar's frame. The health
    /// bar's top-left corner is expressed as its left anchor and top anchor
    /// plus offsets, which is exact for any anchoring, stretched or not.
    /// </summary>
    static void PlaceAboveHealthBar(RectTransform bar, RectTransform health)
    {
        Vector2 anchor, topLeft;
        if (health != null)
        {
            anchor = new Vector2(health.anchorMin.x, health.anchorMax.y);
            topLeft = new Vector2(health.offsetMin.x, health.offsetMax.y);
        }
        else
        {
            anchor = Vector2.zero;
            topLeft = new Vector2(DefaultHealthX, DefaultHealthY + DefaultHealthHeight);
        }

        bar.anchorMin = bar.anchorMax = anchor;
        bar.pivot = Vector2.zero;
        bar.sizeDelta = BarSize;
        bar.anchoredPosition = topLeft + new Vector2(0f, FrameOutset + GapAboveHealth + FrameOutset);
    }

    // ---- crosshair -------------------------------------------------------

    /// <summary>
    ///   SpellCrosshair (CanvasGroup; hidden until aiming)
    ///     Reticle (scaled for the show / hide pop)
    ///       CooldownTrack → Cooldown (Radial 360) → TickUp/Right/Down/Left → Dot
    ///     Cost (star + number, under the reticle)
    /// </summary>
    static Transform BuildCrosshair(Transform canvas, SpellCaster caster, HudStyle s)
    {
        DestroyChildren(canvas, CrosshairName);
        var root = NewRect(CrosshairName, canvas);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(160f, 160f);
        root.anchoredPosition = Vector2.zero;

        var group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;               // hidden until the view sees aim
        group.interactable = false;
        group.blocksRaycasts = false;   // never eat a click meant for the game

        var reticle = NewRect("Reticle", root);
        Stretch(reticle);

        var track = AddImage(reticle, "CooldownTrack", RingTrackColour, s.Ring, Image.Type.Simple);
        Centre(track.rectTransform, Vector2.zero, new Vector2(54f, 54f));

        var ring = AddImage(reticle, "Cooldown", RingColour, s.Ring, Image.Type.Filled);
        Centre(ring.rectTransform, Vector2.zero, new Vector2(54f, 54f));
        ring.fillMethod = Image.FillMethod.Radial360;
        ring.fillOrigin = (int)Image.Origin360.Top;
        ring.fillClockwise = true;
        ring.fillAmount = 0f;

        // Up, right, down, left — the order SpellCrosshairView moves them in.
        // The sprite is vertical; the side ticks are turned to lie along their axis.
        string[] tickNames = { "TickUp", "TickRight", "TickDown", "TickLeft" };
        Vector2[] tickDirections = { Vector2.up, Vector2.right, Vector2.down, Vector2.left };
        var ticks = new Image[tickNames.Length];
        for (int i = 0; i < ticks.Length; i++)
        {
            var tick = AddImage(reticle, tickNames[i], Color.white, s.Tick, Image.Type.Simple);
            Centre(tick.rectTransform, tickDirections[i] * 15f, new Vector2(8f, 20f));
            tick.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i % 2 == 1 ? 90f : 0f);
            ticks[i] = tick;
        }

        var dot = AddImage(reticle, "Dot", Color.white, s.Dot, Image.Type.Simple);
        Centre(dot.rectTransform, Vector2.zero, new Vector2(10f, 10f));

        var cost = NewRect("Cost", root);
        Centre(cost, new Vector2(0f, -48f), new Vector2(70f, 26f));

        var costIcon = AddImage(cost, "Icon", CostColour, s.Star, Image.Type.Simple);
        Centre(costIcon.rectTransform, new Vector2(-12f, 0f), new Vector2(18f, 18f));

        var costText = AddText(cost, "Text", s, 20f, TextAlignmentOptions.Left);
        var ct = costText.rectTransform;
        ct.anchorMin = ct.anchorMax = new Vector2(0.5f, 0.5f);
        ct.pivot = new Vector2(0f, 0.5f);
        ct.anchoredPosition = new Vector2(-1f, 0f);
        ct.sizeDelta = new Vector2(44f, 26f);
        costText.color = CostColour;
        int price = caster != null ? caster.Cost : 0;
        costText.text = price > 0 ? price.ToString() : "";

        var view = root.gameObject.AddComponent<SpellCrosshairView>();
        var so = new SerializedObject(view);
        Set(so, "caster", caster);
        Set(so, "group", group);
        Set(so, "reticle", reticle);
        Set(so, "dot", dot);
        Set(so, "cooldownRing", ring);
        Set(so, "costGroup", cost);
        Set(so, "costIcon", costIcon);
        Set(so, "costText", costText);
        var tickArray = so.FindProperty("ticks");
        if (tickArray == null)
        {
            Debug.LogError("MagicHudBuilder: SpellCrosshairView has no serialized field 'ticks'.");
        }
        else
        {
            tickArray.arraySize = ticks.Length;
            for (int i = 0; i < ticks.Length; i++)
                tickArray.GetArrayElementAtIndex(i).objectReferenceValue = ticks[i];
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    // ---- spell selector ----------------------------------------------------

    /// <summary>
    ///   SpellSelector (CanvasGroup) — beside the mana bar's right end
    ///     Slot0..n (scaled for selection)
    ///       Well → Icon → Cooldown (Radial 360 sweep) → Frame → Key
    ///     Name (selected spell) · Cost
    /// </summary>
    static Transform BuildSelector(Transform canvas, SpellSelector selector, RectTransform bar, HudStyle s)
    {
        DestroyChildren(canvas, SelectorName);
        var root = NewRect(SelectorName, canvas);
        int count = selector != null ? selector.Count : 0;

        root.anchorMin = root.anchorMax = bar.anchorMin;
        root.pivot = new Vector2(0f, 0.5f);
        root.sizeDelta = new Vector2(Mathf.Max(1, count) * (SlotSize + SlotGap), SlotSize);
        root.anchoredPosition = bar.anchoredPosition + new Vector2(BarSize.x + FrameOutset + SelectorGap, BarSize.y * 0.5f);

        var group = root.gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        var slots = new RectTransform[count];
        var frames = new Image[count];
        var icons = new Image[count];
        var sweeps = new Image[count];
        var keys = new TMP_Text[count];
        for (int i = 0; i < count; i++)
        {
            var spell = selector.SpellAt(i);
            var slot = NewRect($"Slot{i + 1}", root);
            slot.anchorMin = slot.anchorMax = new Vector2(0f, 0.5f);
            slot.pivot = new Vector2(0.5f, 0.5f);
            slot.sizeDelta = new Vector2(SlotSize, SlotSize);
            slot.anchoredPosition = new Vector2(SlotSize * 0.5f + i * (SlotSize + SlotGap), 0f);

            AddImage(slot, "Well", WellColour, s.SlotWell, Image.Type.Simple);
            var icon = AddImage(slot, "Icon", spell != null ? spell.ThemeColour : Color.white,
                                spell != null ? spell.Icon : null, Image.Type.Simple);
            Centre(icon.rectTransform, Vector2.zero, new Vector2(SlotSize - 10f, SlotSize - 10f));
            icon.enabled = icon.sprite != null;
            var sweep = AddImage(slot, "Cooldown", new Color(0.02f, 0.03f, 0.08f, 0.72f), s.SlotWell, Image.Type.Filled);
            sweep.fillMethod = Image.FillMethod.Radial360;
            sweep.fillOrigin = (int)Image.Origin360.Top;
            sweep.fillClockwise = false;
            sweep.fillAmount = 0f;
            var frame = AddImage(slot, "Frame", FrameColour, s.SlotFrame, Image.Type.Simple);

            var key = AddText(slot, "Key", s, 13f, TextAlignmentOptions.BottomRight);
            Stretch(key.rectTransform);
            key.rectTransform.offsetMin = new Vector2(0f, -2f);
            key.rectTransform.offsetMax = new Vector2(-3f, 0f);
            key.text = (i + 1).ToString();
            key.color = Color.white;

            slots[i] = slot;
            frames[i] = frame;
            icons[i] = icon;
            sweeps[i] = sweep;
            keys[i] = key;
        }

        var name = AddText(root, "Name", s, 18f, TextAlignmentOptions.BottomLeft);
        var nr = name.rectTransform;
        nr.anchorMin = new Vector2(0f, 1f);
        nr.anchorMax = new Vector2(1f, 1f);
        nr.pivot = new Vector2(0f, 0f);
        nr.sizeDelta = new Vector2(0f, 24f);
        nr.anchoredPosition = new Vector2(0f, 6f);
        var selected = selector != null ? selector.Selected : null;
        name.text = selected != null ? selected.DisplayName : "";

        var cost = AddText(root, "Cost", s, 15f, TextAlignmentOptions.BottomRight);
        var cr = cost.rectTransform;
        cr.anchorMin = new Vector2(0f, 1f);
        cr.anchorMax = new Vector2(1f, 1f);
        cr.pivot = new Vector2(1f, 0f);
        cr.sizeDelta = new Vector2(0f, 22f);
        cr.anchoredPosition = new Vector2(0f, 7f);
        cost.color = CostColour;
        cost.text = selected != null && selected.ManaCost > 0 ? selected.ManaCost.ToString() : "";

        var view = root.gameObject.AddComponent<SpellSelectorView>();
        var so = new SerializedObject(view);
        Set(so, "selector", selector);
        Set(so, "group", group);
        SetArray(so, "slots", slots);
        SetArray(so, "frames", frames);
        SetArray(so, "icons", icons);
        SetArray(so, "cooldowns", sweeps);
        SetArray(so, "keys", keys);
        Set(so, "nameText", name);
        Set(so, "costText", cost);
        so.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    // ---- channel bar -------------------------------------------------------

    /// <summary>
    ///   SpellChannelBar (CanvasGroup; hidden until a channel spell is aimed)
    ///     Body (shaken on a cancel)
    ///       Track → Fill (Horizontal) → Frame
    ///       Label (spell name / result) above, Hint below
    /// </summary>
    static Transform BuildChannelBar(Transform canvas, SpellCaster caster, HudStyle s)
    {
        DestroyChildren(canvas, ChannelBarName);
        var root = NewRect(ChannelBarName, canvas);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = ChannelBarSize;
        root.anchoredPosition = new Vector2(0f, -ChannelBarDrop);

        var group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        var body = NewRect("Body", root);
        Stretch(body);

        var track = AddImage(body, "Track", WellColour, s.Mask, Image.Type.Sliced);
        track.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var fill = AddFill(track.rectTransform, "Fill", new Color(0.45f, 1f, 0.55f), s.Fill);
        fill.fillAmount = 0f;
        var frame = AddImage(body, "Frame", FrameColour, s.Frame, Image.Type.Sliced);
        Outset(frame.rectTransform, FrameOutset);

        var label = AddText(body, "Label", s, 22f, TextAlignmentOptions.Bottom);
        var lr = label.rectTransform;
        lr.anchorMin = new Vector2(0f, 1f);
        lr.anchorMax = new Vector2(1f, 1f);
        lr.pivot = new Vector2(0.5f, 0f);
        lr.sizeDelta = new Vector2(80f, 30f);
        lr.anchoredPosition = new Vector2(0f, 8f);

        var hint = AddText(body, "Hint", s, 15f, TextAlignmentOptions.Top);
        var hr = hint.rectTransform;
        hr.anchorMin = new Vector2(0f, 0f);
        hr.anchorMax = new Vector2(1f, 0f);
        hr.pivot = new Vector2(0.5f, 1f);
        hr.sizeDelta = new Vector2(120f, 22f);
        hr.anchoredPosition = new Vector2(0f, -8f);
        hint.color = new Color(1f, 1f, 1f, 0.85f);

        var view = root.gameObject.AddComponent<SpellChannelView>();
        var so = new SerializedObject(view);
        Set(so, "caster", caster);
        Set(so, "group", group);
        Set(so, "body", body);
        Set(so, "fill", fill);
        Set(so, "label", label);
        Set(so, "hint", hint);
        so.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    // ---- UI plumbing -----------------------------------------------------

    /// <summary>Every child with the name, not just the first: a hand-duplicated
    /// copy would otherwise survive the rebuild and draw twice.</summary>
    static void DestroyChildren(Transform parent, string name)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            if (child.name == name) Object.DestroyImmediate(child.gameObject);
        }
    }

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

    /// <summary>Stretched over the parent and grown by <paramref name="by"/>
    /// pixels on every side.</summary>
    static void Outset(RectTransform rect, float by)
    {
        rect.offsetMin = new Vector2(-by, -by);
        rect.offsetMax = new Vector2(by, by);
    }

    /// <summary>A full-height strip of fixed width centred on a horizontal
    /// anchor (0 = the parent's left edge, 1 = its right).</summary>
    static void VerticalStrip(RectTransform rect, float x, float width)
    {
        rect.anchorMin = new Vector2(x, 0f);
        rect.anchorMax = new Vector2(x, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, 0f);
        rect.anchoredPosition = Vector2.zero;
    }

    static void Centre(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    static Image AddImage(RectTransform parent, string name, Color colour, Sprite sprite, Image.Type type)
    {
        var rect = NewRect(name, parent);
        Stretch(rect);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = colour;
        image.type = type;
        image.raycastTarget = false;

        // Same trap NewtonFightSetup.AddImage guards: without a sprite,
        // Image.OnPopulateMesh draws a plain quad and never reaches the switch
        // on Image.Type, so Filled (and every fillAmount write) and Sliced are
        // silently ignored. Simple images may go without.
        image.sprite = sprite;
        if (image.sprite == null && type != Image.Type.Simple)
            Debug.LogError($"MagicHudBuilder: sprite for '{name}' failed to generate. Image ignores " +
                           $"Type.{type} entirely without a sprite, so this layer will not animate.");
        return image;
    }

    static Image AddFill(RectTransform parent, string name, Color colour, Sprite sprite)
    {
        var image = AddImage(parent, name, colour, sprite, Image.Type.Filled);
        image.fillMethod = Image.FillMethod.Horizontal;
        image.fillOrigin = (int)Image.OriginHorizontal.Left;
        image.fillAmount = 1f;
        return image;
    }

    static TextMeshProUGUI AddText(RectTransform parent, string name, HudStyle s, float size,
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
            Debug.LogError($"MagicHudBuilder: {so.targetObject.GetType().Name} has no serialized field '{field}'.");
            return;
        }
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    /// <summary>Writes one serialized reference, failing loudly if the field was
    /// renamed — a silent miss would leave a layer unwired and look like a view
    /// bug.</summary>
    static void Set(SerializedObject so, string field, Object value)
    {
        var property = so.FindProperty(field);
        if (property == null)
        {
            Debug.LogError($"MagicHudBuilder: {so.targetObject.GetType().Name} has no serialized field '{field}'.");
            return;
        }
        property.objectReferenceValue = value;
    }

    // ---- generated sprites -----------------------------------------------
    //
    // Generated rather than painted for the reason NewtonFightArt gives: no
    // external dependency, identical on every machine, rebuilt every run.
    // NewtonFightArt's writer is private, so this file carries its own copy
    // with the same import settings.

    /// <summary>The mana fill: deep blue on the left to cyan on the right, with
    /// BarFill's exact vertical profile (body ramp, gloss band, bright top lip,
    /// shaded bottom) so it is lit like the health fill below it. Coloured, not
    /// greyscale like BarFill, because one tint cannot run a hue gradient; a
    /// Filled image crops its UVs, so the gradient stays fixed to the bar and
    /// the fill turns cyan as it grows.</summary>
    static Sprite ManaFillSprite() => WriteSprite($"{ArtDir}/ManaFill.png", 128, 64, Vector4.zero, false,
        (x, y) =>
        {
            float u = x / 128f, v = y / 64f;
            Color hue = u < 0.5f ? Color.Lerp(FillDeep, FillMid, u * 2f)
                                 : Color.Lerp(FillMid, FillBright, (u - 0.5f) * 2f);
            float shade = Mathf.Lerp(0.6f, 0.92f, v);
            float gloss = 0.2f * Mathf.Exp(-Mathf.Pow((v - 0.74f) / 0.08f, 2f));
            var c = new Color(hue.r * shade, hue.g * shade, hue.b * shade, 1f);
            c = Color.Lerp(c, Color.white, gloss * 1.4f);
            if (v > 0.92f) c = Color.Lerp(hue, Color.white, 0.45f);
            if (v < 0.1f) c = new Color(c.r * 0.72f, c.g * 0.72f, c.b * 0.72f, 1f);
            c.a = 1f;
            return c;
        });

    /// <summary>A soft diagonal highlight with a bright core line, leaning like
    /// a "/" — the regen shimmer and the full glint. Fades to nothing well
    /// before the texture's edges, so a clamped sprite never shows a seam.</summary>
    static Sprite SoftBandSprite() => WriteSprite($"{ArtDir}/SoftBand.png", 64, 32, Vector4.zero, false,
        (x, y) =>
        {
            float u = x / 64f - 0.5f, v = y / 32f - 0.5f;
            float s = u - 0.3f * v;
            float body = Mathf.Exp(-Mathf.Pow(s / 0.15f, 2f));
            float core = Mathf.Exp(-Mathf.Pow(s / 0.035f, 2f));
            float edge = Mathf.Clamp01((0.5f - Mathf.Abs(u)) / 0.08f);
            return White((body * 0.75f + core * 0.5f) * edge);
        });

    /// <summary>A soft glow around a rounded box, 9-sliced so it fits any bar:
    /// solid inside, Gaussian falloff over the outer 16 px. Laid 16 px outside
    /// the well, its solid edge sits on the well's.</summary>
    static Sprite BarGlowSprite() => WriteSprite($"{ArtDir}/BarGlow.png", 64, 64, new Vector4(24, 24, 24, 24), false,
        (x, y) =>
        {
            float d = RoundBox(x, y, 32f, 32f, 16f, 16f, 8f);
            return White(d <= 0f ? 1f : Mathf.Exp(-Mathf.Pow(d / 6f, 2f)));
        });

    /// <summary>
    /// A faceted four-point star with a dark outline: the mana icon, on the
    /// bar's end cap and beside the crosshair's cost. Greyscale so it takes the
    /// tint (blue, white on a fill, red on a failed spend) while the outline
    /// stays dark over any background. Each point is split along its axis into
    /// a lit and a shaded half, lit from the top left, with a hot centre.
    /// Supersampled, because its concave points alias badly at icon size.
    /// </summary>
    static Sprite ManaStarSprite() => WriteSprite($"{ArtDir}/ManaStar.png", 64, 64, Vector4.zero, true,
        (x, y) =>
        {
            const int n = 4;
            const float outer = 0.96f, inner = 0.8f, sector = Mathf.PI / 4f;
            const float lightX = -0.6f, lightY = 0.8f;
            float covered = 0f, shadeSum = 0f;
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float u = (x - 0.5f + (i + 0.5f) / n) / 32f - 1f;
                float v = (y - 0.5f + (j + 0.5f) / n) / 32f - 1f;
                float norm = StarNorm(u, v);
                if (norm > outer) continue;
                covered += 1f;
                if (norm > inner)
                {
                    shadeSum += 0.08f;
                    continue;
                }
                float centre = (Mathf.Floor(Mathf.Atan2(v, u) / sector) + 0.5f) * sector;
                float lit = 0.5f + 0.5f * (Mathf.Cos(centre) * lightX + Mathf.Sin(centre) * lightY);
                float shade = Mathf.Lerp(0.5f, 1f, lit) + 0.6f * Mathf.Exp(-(u * u + v * v) / 0.012f);
                shadeSum += Mathf.Clamp01(shade);
            }
            if (covered <= 0f) return White(0f);
            float g = shadeSum / covered;
            return new Color(g, g, g, covered / (n * n));
        });

    /// <summary>The cooldown ring and its track: a thin white stroke with a dark
    /// edge either side, so it reads over sky and shadow alike.</summary>
    static Sprite RingSprite() => WriteSprite($"{ArtDir}/Ring.png", 128, 128, Vector4.zero, true,
        (x, y) =>
        {
            float dx = x - 64f, dy = y - 64f;
            float band = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - 55f);
            return Outlined(Coverage(band - 3f), Coverage(band - 5f));
        });

    /// <summary>The centre dot: white with a dark rim.</summary>
    static Sprite DotSprite() => WriteSprite($"{ArtDir}/Dot.png", 32, 32, Vector4.zero, true,
        (x, y) =>
        {
            float dx = x - 16f, dy = y - 16f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            return Outlined(Coverage(r - 9f), Coverage(r - 13f));
        });

    /// <summary>One crosshair tick: a vertical capsule, white with a dark rim.
    /// The side ticks are the same sprite turned 90°.</summary>
    static Sprite TickSprite() => WriteSprite($"{ArtDir}/Tick.png", 16, 40, Vector4.zero, true,
        (x, y) =>
        {
            float dx = x - 8f, dy = y - Mathf.Clamp(y, 11f, 29f);
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            return Outlined(Coverage(d - 3f), Coverage(d - 6f));
        });

    /// <summary>A selector slot's background: a white rounded square (tinted dark
    /// by the builder), also the cooldown sweep's shape.</summary>
    static Sprite SlotWellSprite() => WriteSprite($"{ArtDir}/SlotWell.png", 64, 64, Vector4.zero, true,
        (x, y) => White(Coverage(RoundBox(x, y, 32f, 32f, 30f, 30f, 12f))));

    /// <summary>A selector slot's frame: a white rounded outline with a dark
    /// edge either side, like the crosshair's ring.</summary>
    static Sprite SlotFrameSprite() => WriteSprite($"{ArtDir}/SlotFrame.png", 64, 64, Vector4.zero, true,
        (x, y) =>
        {
            float band = Mathf.Abs(RoundBox(x, y, 32f, 32f, 29f, 29f, 12f) + 2.5f);
            return Outlined(Coverage(band - 1.6f), Coverage(band - 3.4f));
        });

    /// <summary>White core inside a dark rim, from the two coverages.</summary>
    static Color Outlined(float core, float outline)
    {
        float shade = Mathf.Lerp(0.1f, 1f, core);
        return new Color(shade, shade, shade, outline);
    }

    /// <summary>A superellipse norm with exponent below 1: its unit "circle" is
    /// a four-point star with concave sides.</summary>
    static float StarNorm(float u, float v)
    {
        const float p = 0.72f;
        return Mathf.Pow(Mathf.Pow(Mathf.Abs(u), p) + Mathf.Pow(Mathf.Abs(v), p), 1f / p);
    }

    static Color White(float alpha) => new(1f, 1f, 1f, Mathf.Clamp01(alpha));

    /// <summary>One-pixel antialiased coverage from a pixel-space distance.</summary>
    static float Coverage(float d) => Mathf.Clamp01(0.5f - d);

    /// <summary>Signed pixel distance to a rounded box centred on (cx, cy) with
    /// half-extents (hx, hy) and corner radius r; negative inside.</summary>
    static float RoundBox(float x, float y, float cx, float cy, float hx, float hy, float r)
    {
        float qx = Mathf.Abs(x - cx) - (hx - r);
        float qy = Mathf.Abs(y - cy) - (hy - r);
        float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
        return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    /// <summary>
    /// Renders a pixel function to a PNG and imports it as a single sprite with
    /// NewtonFightArt's settings: uncompressed (block compression bands a
    /// gradient), clamped, and a full-rect mesh (a tight mesh breaks both
    /// slicing and fill). Mipmaps only for sprites drawn well below their
    /// texture size — the icon, ring, dot and ticks — so they minify smoothly.
    /// </summary>
    static Sprite WriteSprite(string path, int w, int h, Vector4 border, bool mipmaps,
                              Func<float, float, Color> pixel)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            pixels[y * w + x] = pixel(x + 0.5f, y + 0.5f);
        tex.SetPixels(pixels);
        byte[] png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string full = Path.Combine(projectRoot, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllBytes(full, png);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"MagicHudBuilder: {path} did not import as a texture.");
            return null;
        }
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spriteBorder = border;
        importer.spritePixelsPerUnit = 100f;
        importer.mipmapEnabled = mipmaps;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
