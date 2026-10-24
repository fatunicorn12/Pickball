using System;
using System.Collections.Generic;
using Pickleball.Sandbox;
using TMPro;
using UnityEngine;

namespace Pickleball.UI
{
    /// <summary>
    /// Floating damage numbers over whoever got hurt, drawn on the HUD canvas.
    ///
    /// The numbers are there to teach the fight, not just decorate it, so the
    /// styling carries the rules:
    ///
    /// - Hits on Newton from inside his weak-point range come up gold, large and
    ///   with a "!" — and resisted long-range pokes come up small and grey. The
    ///   inverse-square weak point is otherwise invisible; this makes it legible
    ///   from the first swing. Read off <see cref="Health.LastHitScale"/>, so
    ///   this view never needs to know what the boss's filter is.
    /// - The Fluxion Beam's permanent damage is drawn in the beam's own blue,
    ///   apart from the ordinary red, because it is the one kind of damage the
    ///   regain mechanic cannot give back.
    ///
    /// <b>Continuous damage is merged.</b> The beam and the well core deal damage
    /// every frame; one popup per frame would be an unreadable fountain of 0s.
    /// A hit landing within <see cref="Source.mergeWindow"/> of the previous one
    /// of the same style is added to the live number instead, which re-punches
    /// and keeps it hovering. A held beam is one number counting up.
    ///
    /// Pooled: the texts are created once and recycled.
    /// </summary>
    public class DamageNumbers : MonoBehaviour
    {
        [Serializable]
        public class Source
        {
            public Health health;
            public Color colour = Color.white;

            [Tooltip("Colour for damage that skips the regain pool.")]
            public Color permanentColour = new(0.45f, 0.8f, 1f);

            [Tooltip("Style weak-point and resisted hits (Health.LastHitScale).")]
            public bool showRangeScaling;

            [Tooltip("Numbers never spawn lower than this above the target's root, " +
                     "so damage reported at the feet still reads at body height.")]
            public float minHeight = 1.4f;

            [Tooltip("Seconds after a hit during which the next one is added to it.")]
            public float mergeWindow = 0.12f;
        }

        enum Style { Normal, Permanent, Weak, Resisted }

        class Popup
        {
            public TMP_Text Text;
            public RectTransform Rect;
            public Source Source;
            public Style Style;
            public Vector3 World;
            public Vector2 Drift;
            public float Amount;
            public int Shown;
            public float LastAdd;
            public bool Live;
        }

        [SerializeField] private RectTransform canvas;
        [SerializeField] private TMP_Text template;
        [SerializeField] private List<Source> sources = new();

        [Header("Motion")]
        [SerializeField] private float lifetime = 0.9f;
        [SerializeField] private float riseHeight = 70f;
        [SerializeField] private float popScale = 1.7f;
        [SerializeField] private float popSeconds = 0.12f;
        [SerializeField] private int poolSize = 24;

        [Tooltip("Pixels each overlapping number is lifted above the last.")]
        [SerializeField] private float stackStep = 46f;

        [Tooltip("Seconds within which a new number counts as overlapping.")]
        [SerializeField] private float stackWindow = 0.35f;

        [Header("Styles")]
        [Tooltip("LastHitScale at or above this counts as a weak-point hit.")]
        [SerializeField] private float weakAt = 1.2f;
        [Tooltip("LastHitScale at or below this counts as resisted.")]
        [SerializeField] private float resistedAt = 0.5f;
        [SerializeField] private Color weakColour = new(1f, 0.82f, 0.2f);
        [SerializeField] private Color resistedColour = new(0.7f, 0.7f, 0.72f);
        [SerializeField] private float baseFontSize = 46f;

        readonly List<Popup> _pool = new();
        readonly Dictionary<Source, Popup> _open = new();
        readonly Dictionary<Source, (float time, int depth)> _stack = new();
        readonly List<(Source source, Action<float, Vector3> handler)> _handlers = new();
        Camera _camera;

        void Awake()
        {
            if (canvas == null) canvas = transform as RectTransform;
            if (template != null)
            {
                template.gameObject.SetActive(false);
                for (int i = 0; i < poolSize; i++) _pool.Add(MakePopup());
            }
        }

        void OnEnable()
        {
            foreach (var source in sources)
            {
                if (source?.health == null) continue;
                var captured = source;
                Action<float, Vector3> handler = (amount, at) => OnHurt(captured, amount, at);
                source.health.Hurt += handler;
                _handlers.Add((source, handler));
            }
        }

        void OnDisable()
        {
            foreach (var (source, handler) in _handlers)
                if (source.health != null) source.health.Hurt -= handler;
            _handlers.Clear();
        }

        Popup MakePopup()
        {
            var text = Instantiate(template, template.transform.parent);
            text.gameObject.SetActive(false);
            return new Popup { Text = text, Rect = text.rectTransform };
        }

        void OnHurt(Source source, float amount, Vector3 at)
        {
            Style style = Classify(source);

            float floor = source.health.transform.position.y + source.minHeight;
            if (at.y < floor) at.y = floor;

            if (_open.TryGetValue(source, out var open) && open.Live && open.Style == style
                && Time.time - open.LastAdd <= source.mergeWindow)
            {
                open.Amount += amount;
                open.World = at;
                open.LastAdd = Time.time;
                return;
            }

            var popup = Acquire();
            if (popup == null) return;
            // Recycling a live one: retire it properly first, or its old source
            // would keep merging into a number now showing someone else's damage.
            if (popup.Live) Release(popup);
            popup.Source = source;
            popup.Style = style;
            popup.World = at;
            popup.Amount = amount;
            popup.Shown = int.MinValue;
            popup.LastAdd = Time.time;
            popup.Drift = new Vector2(UnityEngine.Random.Range(-40f, 40f),
                                      UnityEngine.Random.Range(-10f, 20f) + stackStep * NextStackDepth(source));
            popup.Live = true;
            popup.Text.color = ColourFor(source, style);
            popup.Text.gameObject.SetActive(true);
            popup.Text.transform.SetAsLastSibling();
            _open[source] = popup;
        }

        /// <summary>Numbers spawned close together on one target climb a rung
        /// each, so a regular hit and a beam tick landing on the same frame read
        /// as two numbers rather than one illegible overprint.</summary>
        int NextStackDepth(Source source)
        {
            int depth = _stack.TryGetValue(source, out var last) && Time.time - last.time < stackWindow
                ? last.depth + 1
                : 0;
            _stack[source] = (Time.time, depth);
            return depth % 4;
        }

        Style Classify(Source source)
        {
            var health = source.health;
            if (!health.LastHitRecoverable) return Style.Permanent;
            if (!source.showRangeScaling) return Style.Normal;
            if (health.LastHitScale >= weakAt) return Style.Weak;
            if (health.LastHitScale <= resistedAt) return Style.Resisted;
            return Style.Normal;
        }

        Color ColourFor(Source source, Style style) => style switch
        {
            Style.Permanent => source.permanentColour,
            Style.Weak => weakColour,
            Style.Resisted => resistedColour,
            _ => source.colour,
        };

        /// <summary>A free popup, or the oldest live one when the pool is spent —
        /// in a frantic exchange the newest number matters more than the oldest.</summary>
        Popup Acquire()
        {
            Popup oldest = null;
            foreach (var p in _pool)
            {
                if (!p.Live) return p;
                if (oldest == null || p.LastAdd < oldest.LastAdd) oldest = p;
            }
            return oldest;
        }

        void LateUpdate()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null || canvas == null) return;

            foreach (var p in _pool)
            {
                if (!p.Live) continue;

                float age = Time.time - p.LastAdd;
                if (age >= lifetime)
                {
                    Release(p);
                    continue;
                }

                // Wait for a whole point before showing anything, so the first
                // frames of a trickle are not a flash of "0".
                int value = Mathf.RoundToInt(p.Amount);
                if (value < 1)
                {
                    p.Text.alpha = 0f;
                    continue;
                }
                if (value != p.Shown)
                {
                    p.Shown = value;
                    if (p.Style == Style.Weak) p.Text.SetText("{0}!", value);
                    else p.Text.SetText("{0}", value);
                }

                Vector3 screen = _camera.WorldToScreenPoint(p.World);
                if (screen.z <= 0f)
                {
                    p.Text.alpha = 0f;
                    continue;
                }
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out Vector2 local);

                float life = age / lifetime;
                float rise = 1f - (1f - life) * (1f - life);
                p.Rect.anchoredPosition = local + p.Drift + Vector2.up * (riseHeight * rise);

                float pop = popSeconds > 0f ? Mathf.Clamp01(age / popSeconds) : 1f;
                float scale = Mathf.Lerp(popScale, 1f, 1f - (1f - pop) * (1f - pop)) * SizeFor(p);
                p.Rect.localScale = Vector3.one * scale;

                p.Text.alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, life));
            }
        }

        /// <summary>Bigger hits, bigger numbers — but on a log curve, so a 60-point
        /// weak-point slash is impressive rather than screen-filling.</summary>
        float SizeFor(Popup p)
        {
            float bySize = Mathf.Clamp(0.75f + Mathf.Log10(1f + p.Amount) * 0.35f, 0.75f, 1.6f);
            float byStyle = p.Style switch
            {
                Style.Weak => 1.3f,
                Style.Resisted => 0.8f,
                _ => 1f,
            };
            return bySize * byStyle * (baseFontSize / Mathf.Max(p.Text.fontSize, 1f));
        }

        void Release(Popup p)
        {
            p.Live = false;
            p.Text.gameObject.SetActive(false);
            if (p.Source != null && _open.TryGetValue(p.Source, out var open) && open == p)
                _open.Remove(p.Source);
        }
    }
}
