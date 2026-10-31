using System.Collections.Generic;
using System.IO;
using Pickleball.Cutscene.Subtitles;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Pickleball.Cutscene.Editor.Subtitles
{
    /// <summary>
    /// Step 5 of the opening-cutscene setup (<see cref="CutsceneMenus.BuildSubtitleHud"/>): builds
    /// or rebuilds the subtitle HUD in the open scene and wires it to the scene's
    /// <see cref="SubtitleRelay"/> and to whichever component implements
    /// <see cref="ICutscenePlayback"/>, both found by type so this compiles without the pipeline.
    ///
    /// <code>
    /// CutsceneHUD            Screen Space Overlay canvas, sorting order 60, 1920×1080 by width
    ///   SafeArea             SafeAreaPanel: stretched over Screen.safeArea
    ///     Subtitle           SubtitleView + CanvasGroup, stretched over the safe area
    ///       Card             soft backing (9-sliced), bottom centre
    ///         Body           TMP text, wrapping off
    ///         Tab            speaker tab above the card
    ///           Name         TMP, small caps
    ///     SkipIndicator      SkipIndicatorView + CanvasGroup, bottom right
    ///       Track, Fill      ring Images (Fill: Radial 360)
    ///       Label            "hold to skip"
    /// </code>
    ///
    /// Idempotent: every object is found by name before it is created, every property is set to
    /// the same value, and the style asset and sprites are only created when missing (a re-run
    /// keeps tuned values and only adds missing speakers). Never touches the dialogue HUD.
    /// Marks the scene dirty but doesn't save it, like the other setup menus.
    /// </summary>
    public static class SubtitleHudBuilder
    {
        /// <summary>Folder of the style asset and the two generated sprites.</summary>
        public const string StyleDir = "Assets/_Project/ScriptableObjects/Cutscene/UI";

        /// <summary>The style asset the HUD reads.</summary>
        public const string StylePath = StyleDir + "/SubtitleStyle.asset";

        /// <summary>The soft-edged rounded backing, white, 9-sliced.</summary>
        public const string BackingSpritePath = StyleDir + "/SubtitleBacking.png";

        /// <summary>The skip ring, white.</summary>
        public const string RingSpritePath = StyleDir + "/SkipRing.png";

        /// <summary>Root object name; the pipeline's HUD fade must leave this canvas alone.</summary>
        public const string HudName = "CutsceneHUD";

        /// <summary>
        /// Sorting order of the HUD canvas. The fight HUD sorts at 0 and the dialogue box's nested
        /// canvas at 50; the cutscene fades those, but subtitles must stay on top of both even
        /// mid-fade, and on top of a dialogue box a gameplay hook might open during a cutscene.
        /// Nothing that matters sorts above 60, so a later overlay (a pause menu, a fade-to-black)
        /// can still go on top with a higher number.
        /// </summary>
        public const int SortingOrder = 60;

        const string FontPath = "Assets/TextMesh Pro/Examples & Extras/Resources/Fonts & Materials/Roboto-Bold SDF.asset";
        const string ParentDir = "Assets/_Project/ScriptableObjects/Cutscene";

        // A typical full 42-character line from the script, measured after the build so a font or
        // size change that no longer fits is reported, not discovered on a phone.
        const string WidthSample = "You'll figure it out as you go. Just know ";
        // Share of the 1920 reference width a full card may take: leaves room for a notch inset.
        const float MaxCardWidthShare = 0.85f;

        // Backing sprite geometry, px: a rounded square whose alpha feathers to 0 at the edge.
        const int BackingSize = 128;
        const float BackingRadius = 48f;
        const float BackingFeather = 18f;
        const int BackingBorder = 56;

        // Ring sprite geometry, px.
        const int RingSize = 128;
        const float RingRadius = 56f;
        const float RingHalfThickness = 6f;

        /// <summary>The menu: build or rebuild the HUD in the open scene.</summary>
        [MenuItem(CutsceneMenus.BuildSubtitleHud)]
        public static void BuildSubtitleHud()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Build Subtitle HUD: leave Play mode first. Setup menus write the scene.");
                return;
            }

            var style = LoadOrCreateStyle();
            var backing = BackingSprite();
            var ring = RingSprite();
            var font = LoadFont();

            var relay = FindRelay();
            var playback = FindPlayback();

            var hud = FindOrCreateHud();
            var (view, _) = BuildInto(hud, style, font, backing, ring, relay, playback);

            ReportWidth(view, style);
            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            Debug.Log($"Build Subtitle HUD: done in '{hud.gameObject.scene.name}'. Relay: " +
                      $"{(relay != null ? relay.name : "none")}, playback: " +
                      $"{(playback != null ? playback.name : "none")}. Save the scene to keep it.", hud);
        }

        /// <summary>
        /// Builds, or re-configures, the HUD under <paramref name="hud"/> (which must have a
        /// RectTransform; it gets the Canvas and CanvasScaler) and wires it. Public so a test can
        /// build the real hierarchy on a temporary object; sprites and font may be null there.
        /// </summary>
        public static (SubtitleView view, SkipIndicatorView skip) BuildInto(
            RectTransform hud, SubtitleStyle style, TMP_FontAsset font, Sprite backingSprite, Sprite ringSprite,
            MonoBehaviour source, MonoBehaviour playback)
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) hud.gameObject.layer = uiLayer;

            var canvas = GetOrAdd<Canvas>(hud.gameObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            canvas.pixelPerfect = false;
            var scaler = GetOrAdd<CanvasScaler>(hud.gameObject);
            // As DialogueView's canvas: 1920×1080 reference matched by width, so a 42-character line
            // takes the same share of the width on a 16:9 monitor and a 20:9 phone.
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;

            var safe = Child(hud, "SafeArea");
            Stretch(safe);
            GetOrAdd<SafeAreaPanel>(safe.gameObject);

            // ---- subtitle card
            var subtitle = Child(safe, "Subtitle");
            Stretch(subtitle);
            var group = GetOrAdd<CanvasGroup>(subtitle.gameObject);
            group.alpha = 0f;
            var card = Child(subtitle, "Card");
            var backing = GetOrAdd<Image>(card.gameObject);
            Sliced(backing, backingSprite);
            var body = Text(Child(card, "Body"), font);
            var tabRect = Child(card, "Tab");
            var tab = GetOrAdd<Image>(tabRect.gameObject);
            Sliced(tab, backingSprite);
            var nameText = Text(Child(tabRect, "Name"), font);

            var view = GetOrAdd<SubtitleView>(subtitle.gameObject);
            SetRefs(view,
                ("source", source), ("playback", playback), ("style", style),
                ("group", group), ("card", card), ("backing", backing), ("tab", tab),
                ("nameText", nameText), ("bodyText", body));
            view.ApplyStyle();

            // ---- skip ring
            var skipRect = Child(safe, "SkipIndicator");
            var skipGroup = GetOrAdd<CanvasGroup>(skipRect.gameObject);
            skipGroup.alpha = 0f;
            var skipBackingRect = Child(skipRect, "Backing");
            skipBackingRect.SetAsFirstSibling();   // drawn under the ring and the hint
            var skipBacking = GetOrAdd<Image>(skipBackingRect.gameObject);
            Sliced(skipBacking, backingSprite);
            var trackRect = Child(skipRect, "Track");
            Stretch(trackRect);
            var track = GetOrAdd<Image>(trackRect.gameObject);
            track.sprite = ringSprite;
            track.preserveAspect = true;
            var fillRect = Child(skipRect, "Fill");
            Stretch(fillRect);
            var fill = GetOrAdd<Image>(fillRect.gameObject);
            fill.sprite = ringSprite;
            fill.preserveAspect = true;
            fill.fillAmount = 0f;
            var label = Text(Child(skipRect, "Label"), font);

            var skip = GetOrAdd<SkipIndicatorView>(skipRect.gameObject);
            SetRefs(skip, ("playback", playback), ("style", style), ("group", skipGroup), ("backing", skipBacking),
                    ("track", track), ("fill", fill), ("label", label));
            skip.ApplyStyle();
            label.alpha = 0f;

            foreach (var component in new Object[] { canvas, scaler, group, backing, tab, body, nameText, view,
                                                     skipGroup, skipBacking, track, fill, label, skip })
                EditorUtility.SetDirty(component);
            return (view, skip);
        }

        // ------------------------------------------------------------ scene lookups

        static RectTransform FindOrCreateHud()
        {
            var scene = SceneManager.GetActiveScene();
            GameObject found = null;
            int count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != HudName) continue;
                count++;
                if (found == null) found = root;
            }
            if (count > 1)
                Debug.LogWarning($"Build Subtitle HUD: {count} root objects called {HudName}; rebuilding the " +
                                 "first. Delete the others.", found);
            if (found == null)
            {
                found = new GameObject(HudName, typeof(RectTransform));
                SceneManager.MoveGameObjectToScene(found, scene);
            }
            if (found.transform is RectTransform rect) return rect;

            Debug.LogWarning($"Build Subtitle HUD: {HudName} had no RectTransform; replacing it.", found);
            Object.DestroyImmediate(found);
            found = new GameObject(HudName, typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(found, scene);
            return (RectTransform)found.transform;
        }

        static SubtitleRelay FindRelay()
        {
            var relays = new List<SubtitleRelay>(
                Object.FindObjectsByType<SubtitleRelay>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            if (relays.Count == 0)
            {
                Debug.LogWarning("Build Subtitle HUD: no SubtitleRelay in the open scene, so the subtitles are " +
                                 "unwired. Run 4 Build Opening Scene first, then this again.");
                return null;
            }
            relays.Sort((a, b) => string.CompareOrdinal(PathOf(a.transform), PathOf(b.transform)));
            if (relays.Count > 1)
                Debug.LogWarning($"Build Subtitle HUD: {relays.Count} SubtitleRelays; wiring " +
                                 $"{PathOf(relays[0].transform)}.", relays[0]);
            return relays[0];
        }

        static MonoBehaviour FindPlayback()
        {
            var found = new List<MonoBehaviour>();
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,
                                                                              FindObjectsSortMode.None))
                if (behaviour is ICutscenePlayback) found.Add(behaviour);
            if (found.Count == 0)
            {
                Debug.LogWarning("Build Subtitle HUD: nothing in the open scene implements ICutscenePlayback " +
                                 "(the pipeline's cutscene player), so the skip ring is unwired. Run this " +
                                 "again once it exists.");
                return null;
            }
            found.Sort((a, b) => string.CompareOrdinal(PathOf(a.transform) + a.GetType().Name,
                                                       PathOf(b.transform) + b.GetType().Name));
            if (found.Count > 1)
                Debug.LogWarning($"Build Subtitle HUD: {found.Count} ICutscenePlayback components; wiring " +
                                 $"{found[0].GetType().Name} on {PathOf(found[0].transform)}.", found[0]);
            return found[0];
        }

        static void ReportWidth(SubtitleView view, SubtitleStyle style)
        {
            var body = view != null ? view.GetComponentInChildren<TMP_Text>(true) : null;
            if (body == null || style == null) return;
            // Body is the first TMP_Text under the view (Card/Body precedes Card/Tab/Name).
            float width = body.GetPreferredValues(WidthSample).x + 2f * style.BackingPadding.x;
            float limit = 1920f * MaxCardWidthShare;
            if (width > limit)
                Debug.LogWarning($"Build Subtitle HUD: a full {style.MaxCharsPerLine}-character line is " +
                                 $"{width:0} px wide with padding, over {limit:0} of the 1920 reference. " +
                                 "Lower SubtitleStyle.fontSize.", style);
            else
                Debug.Log($"Build Subtitle HUD: a full {style.MaxCharsPerLine}-character line is {width:0} px " +
                          "wide with padding (1920 reference).");
        }

        // ------------------------------------------------------------ assets

        /// <summary>
        /// Loads the style asset, creating it with the house defaults if missing. On a re-run it
        /// keeps every tuned value and only adds the default speakers that have no entry.
        /// </summary>
        public static SubtitleStyle LoadOrCreateStyle()
        {
            EnsureFolder();
            var style = AssetDatabase.LoadAssetAtPath<SubtitleStyle>(StylePath);
            if (style == null)
            {
                style = ScriptableObject.CreateInstance<SubtitleStyle>();
                AssetDatabase.CreateAsset(style, StylePath);
                return style;
            }
            bool added = false;
            foreach (var speaker in SubtitleStyle.DefaultSpeakers())
                added |= style.EnsureSpeaker(speaker.SpeakerId, speaker.DisplayName, speaker.Colour);
            if (added)
            {
                EditorUtility.SetDirty(style);
                AssetDatabase.SaveAssetIfDirty(style);
            }
            return style;
        }

        static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(StyleDir)) return;
            if (!AssetDatabase.IsValidFolder(ParentDir))
            {
                Debug.LogError($"Build Subtitle HUD: {ParentDir} is missing; it ships with the contracts commit.");
                return;
            }
            AssetDatabase.CreateFolder(ParentDir, "UI");
        }

        static TMP_FontAsset LoadFont()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font != null) return font;
            Debug.LogWarning($"Build Subtitle HUD: {FontPath} not found; using TMP's default font.");
            return TMP_Settings.defaultFontAsset;
        }

        /// <summary>A white rounded square whose alpha feathers out over the last pixels, so the
        /// tinted backing reads as a soft panel rather than a hard box.</summary>
        static Sprite BackingSprite() => WriteSprite(BackingSpritePath, BackingSize, BackingSize,
            new Vector4(BackingBorder, BackingBorder, BackingBorder, BackingBorder), (x, y) =>
            {
                float inside = -RoundBox(x, y, BackingSize, BackingSize, BackingRadius);
                float t = Mathf.Clamp01(inside / BackingFeather);
                return t * t * (3f - 2f * t);
            });

        /// <summary>A white antialiased ring; the fill Image's Radial 360 mode turns it into an arc.</summary>
        static Sprite RingSprite() => WriteSprite(RingSpritePath, RingSize, RingSize, Vector4.zero, (x, y) =>
        {
            float d = new Vector2(x - RingSize * 0.5f, y - RingSize * 0.5f).magnitude;
            return Mathf.Clamp01(0.5f - (Mathf.Abs(d - RingRadius) - RingHalfThickness));
        });

        static float RoundBox(float x, float y, float w, float h, float r)
        {
            float qx = Mathf.Abs(x - w * 0.5f) - (w * 0.5f - r);
            float qy = Mathf.Abs(y - h * 0.5f) - (h * 0.5f - r);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        /// <summary>
        /// Writes a white sprite with the given alpha only if no file is there (a committed or
        /// hand-edited sprite is never overwritten) and fixes its import settings only if they
        /// differ, so a re-run touches nothing on disk.
        /// </summary>
        static Sprite WriteSprite(string path, int w, int h, Vector4 border, System.Func<float, float, float> alpha)
        {
            EnsureFolder();
            string full = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", path);
            if (!File.Exists(full))
            {
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                var pixels = new Color32[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = new Color32(255, 255, 255,
                                                    (byte)Mathf.RoundToInt(alpha(x + 0.5f, y + 0.5f) * 255f));
                tex.SetPixels32(pixels);
                File.WriteAllBytes(full, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) return null;
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
                settings.spriteMeshType = SpriteMeshType.FullRect;   // a tight mesh breaks 9-slicing
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ------------------------------------------------------------ helpers

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            // Explicit null check: ?? bypasses Unity's fake-null on missing components.
            var c = go.GetComponent<T>();
            if (c == null) c = go.AddComponent<T>();
            return c;
        }

        /// <summary>The child called <paramref name="name"/>, created with a RectTransform if
        /// missing. Found, never rebuilt, so file IDs stay stable across runs.</summary>
        static RectTransform Child(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null)
            {
                if (existing is RectTransform rect) return rect;
                Debug.LogWarning($"Build Subtitle HUD: '{name}' under '{parent.name}' has no RectTransform; replacing it.");
                Object.DestroyImmediate(existing.gameObject);
            }
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

        static void Sliced(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.fillCenter = true;
            image.raycastTarget = false;
        }

        static TextMeshProUGUI Text(RectTransform rect, TMP_FontAsset font)
        {
            var text = GetOrAdd<TextMeshProUGUI>(rect.gameObject);
            if (font != null)
            {
                text.font = font;
                text.fontSharedMaterial = font.material;
            }
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.text = string.Empty;
            return text;
        }

        /// <summary>Writes object references through SerializedObject, as the Inspector would, and
        /// fails loudly on a renamed field.</summary>
        static void SetRefs(Object target, params (string field, Object value)[] refs)
        {
            var so = new SerializedObject(target);
            foreach (var (field, value) in refs)
            {
                var property = so.FindProperty(field);
                if (property == null)
                {
                    Debug.LogError($"Build Subtitle HUD: {target.GetType().Name} has no field '{field}'.");
                    continue;
                }
                property.objectReferenceValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static string PathOf(Transform t)
        {
            string path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
