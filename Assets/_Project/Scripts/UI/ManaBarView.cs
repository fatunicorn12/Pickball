using Pickleball.Sandbox.Magic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pickleball.UI
{
    /// <summary>
    /// The player's <see cref="Mana"/> as a blue bar sitting on the health bar,
    /// built as its sibling (same rounded well, bevel frame and font) so the two
    /// read as one stack. Painted back to front:
    ///
    ///   Halo → Well [ Trail → Current → Shimmer → FillGlow → CostPreview →
    ///                 DelayLine → Notches → Glint → Flash ] → Frame → Icon → Value
    ///
    /// Every layer answers a question the player has mid-fight, which is why
    /// there are so many:
    ///
    /// - <b>Notches</b> every spell cost: count the ones the fill has passed and
    ///   that is how many shots are banked. "73 / 100" makes you divide by 15;
    ///   four notches does not.
    /// - <b>Cost preview</b> while aiming: the next shot's worth of fill pulses,
    ///   or the whole price glows red when you are short — so a dry click is
    ///   predicted, not discovered.
    /// - <b>Regen delay</b>: the Terraria model holds regen off after a spend
    ///   (longer the emptier the bar), and a bar that is silently not filling
    ///   reads as a bug. While held off the fill dims and a fuse line along the
    ///   bottom burns down; when regen starts the fill brightens once.
    /// - <b>Shimmer</b> sweeps the filled part while regenerating, quicker as the
    ///   bar fills, because Terraria's regen accelerates — at empty it runs at a
    ///   fifth of its near-full rate.
    /// - <b>Trail</b>: the same chip as the health bar, showing what a cast cost.
    /// - <b>Full</b>: Terraria's sparkle-and-chime moment — a glint crosses the
    ///   bar, the frame glows and the star pops.
    /// - <b>Failed spend</b>: a damped shake and a red flash, the universal "no".
    ///
    /// Same conventions as <see cref="HealthBarView"/>: values are read per
    /// frame and eased (an event would only duplicate the frame tick), and the
    /// events (Spent, SpendFailed, Filled, CastFailed) only kick one-shot
    /// feedback. Animation runs on unscaled time, like <see cref="Pickleball.Sandbox.LockOnReticle"/>,
    /// so slow motion or a paused timescale never freezes a flash half-lit.
    /// </summary>
    public class ManaBarView : MonoBehaviour
    {
        const float Tau = Mathf.PI * 2f;

        /// <summary>Share of each shimmer cycle spent sweeping. The band rests
        /// past the end for the rest, so it reads as pulses, not a conveyor.</summary>
        const float SweepShare = 0.75f;

        /// <summary>Seconds of quiet needed before another failed spend shakes the
        /// bar again (see <see cref="KickFailure"/>). Shorter than any deliberate
        /// re-press, longer than a frame.</summary>
        const float FailDebounce = 0.1f;

        [Header("Sources")]
        [Tooltip("The pool drawn. Falls back to the caster's Mana, then to a Mana on a parent.")]
        [SerializeField] private Mana source;

        [Tooltip("Optional. Supplies the spell cost (notches, aim preview) and CastFailed. " +
                 "Without it the bar is a plain mana readout.")]
        [SerializeField] private SpellCaster caster;

        [Header("Layers")]
        [Tooltip("Parent of every visible layer, stretched over this rect with zero offsets. " +
                 "It is what shakes, so the bar's own placement is never written to.")]
        [SerializeField] private RectTransform body;

        [Tooltip("Soft glow behind the frame, lit when the bar fills. Optional.")]
        [SerializeField] private Image halo;

        [Tooltip("Filled image for mana actually held. Image Type must be Filled, with a sprite.")]
        [SerializeField] private Image currentFill;

        [Tooltip("Filled image behind the current fill showing mana just spent. Optional.")]
        [SerializeField] private Image trailFill;

        [Tooltip("RectMask2D whose right edge follows the fill, so the regen shimmer only " +
                 "crosses mana you hold. Optional.")]
        [SerializeField] private RectTransform shimmerClip;

        [Tooltip("The highlight band inside the shimmer clip, anchored to its left edge.")]
        [SerializeField] private Image shimmerBand;

        [Tooltip("Filled white overlay matching the current fill. Brightens it when regen " +
                 "kicks in and when the bar fills. Optional.")]
        [SerializeField] private Image fillGlow;

        [Tooltip("Simple image in the well whose horizontal anchors are moved over the next " +
                 "cast's worth of fill while aiming. Optional.")]
        [SerializeField] private Image costPreview;

        [Tooltip("Thin Filled line along the bottom of the well that burns down as the regen " +
                 "delay runs out. Optional.")]
        [SerializeField] private Image delayLine;

        [Tooltip("Holds one notch per cast's worth of mana. Its children ARE the notches: " +
                 "more are cloned from the template when the cost or max changes. Optional.")]
        [SerializeField] private RectTransform notchContainer;

        [Tooltip("A notch inside the container, cloned for the rest.")]
        [SerializeField] private Image notchTemplate;

        [Tooltip("Bright band swept across the bar when it fills. Optional.")]
        [SerializeField] private Image glint;

        [Tooltip("Overlay over the whole well that flashes on a failed spend. Optional.")]
        [SerializeField] private Image flash;

        [Tooltip("The bevel frame; glows when the bar fills. Optional.")]
        [SerializeField] private Image frame;

        [Tooltip("The mana star at the left end; pops when the bar fills and reddens on a " +
                 "failed spend. Optional.")]
        [SerializeField] private Image icon;

        [Tooltip("Optional numeric readout, e.g. 73 / 100.")]
        [SerializeField] private TMP_Text valueText;

        [Header("Easing")]
        [Tooltip("Seconds (time constant) the fill takes to catch up. Snappier than the " +
                 "health bar's 0.12: mana moves a point at a time, and a cast should read " +
                 "the frame it happens. Purely cosmetic.")]
        [Min(0f)] [SerializeField] private float smoothing = 0.07f;

        [Header("Spent trail")]
        [Tooltip("Seconds the pale chip holds after a spend before draining.")]
        [Min(0f)] [SerializeField] private float trailDelay = 0.4f;

        [Tooltip("Seconds (time constant) the chip takes to catch up once draining.")]
        [Min(0f)] [SerializeField] private float trailSmoothing = 0.18f;

        [Header("Regen delay")]
        [Tooltip("Multiplied into the fill colour while regen is held off, so a waiting bar " +
                 "reads cooler and dimmer than a charging one. Alpha is ignored.")]
        [SerializeField] private Color delayTint = new(0.6f, 0.66f, 0.8f, 1f);

        [Tooltip("Seconds to fade the dim and the delay line in or out.")]
        [Min(0.001f)] [SerializeField] private float delayFade = 0.15f;

        [Tooltip("Colour of the delay line; its alpha is the line's full opacity.")]
        [SerializeField] private Color delayLineColour = new(0.78f, 0.92f, 1f, 0.85f);

        [Tooltip("Opacity (0..1) of the white brighten over the fill when regen kicks in.")]
        [Range(0f, 1f)] [SerializeField] private float regenKickAlpha = 0.35f;

        [Tooltip("Seconds that brighten takes to fade.")]
        [Min(0.001f)] [SerializeField] private float regenKickSeconds = 0.3f;

        [Header("Regen shimmer")]
        [Tooltip("Colour of the band sweeping the fill while regenerating; alpha is its peak opacity.")]
        [SerializeField] private Color shimmerColour = new(0.85f, 0.97f, 1f, 0.35f);

        [Tooltip("Sweeps per second near empty, where regen is slowest.")]
        [Min(0f)] [SerializeField] private float shimmerHzEmpty = 0.45f;

        [Tooltip("Sweeps per second near full, where regen is fastest.")]
        [Min(0f)] [SerializeField] private float shimmerHzFull = 1.3f;

        [Tooltip("Multiplier on the sweep rate while standing still, which regenerates faster.")]
        [Min(0f)] [SerializeField] private float shimmerStillBoost = 1.5f;

        [Tooltip("Seconds to fade the shimmer in or out as regen starts and stops.")]
        [Min(0.001f)] [SerializeField] private float shimmerFade = 0.2f;

        [Header("Cost preview (while aiming)")]
        [Tooltip("Colour over the next cast's worth of fill; alpha is its peak opacity.")]
        [SerializeField] private Color previewColour = new(0.9f, 0.98f, 1f, 0.5f);

        [Tooltip("Colour over the whole price when the bar cannot afford it.")]
        [SerializeField] private Color previewShortColour = new(1f, 0.25f, 0.25f, 0.55f);

        [Tooltip("Pulses per second.")]
        [Min(0f)] [SerializeField] private float previewHz = 2.2f;

        [Tooltip("Seconds to fade the preview in when aim starts and out when it ends.")]
        [Min(0.001f)] [SerializeField] private float previewFade = 0.1f;

        [Header("Notches")]
        [Tooltip("Notch colour where the fill covers it: an engraved cut in the bar.")]
        [SerializeField] private Color notchFilledColour = new(0.03f, 0.06f, 0.18f, 0.6f);

        [Tooltip("Notch colour over the empty track: faint, so capacity reads without clutter.")]
        [SerializeField] private Color notchEmptyColour = new(0.7f, 0.86f, 1f, 0.16f);

        [Tooltip("Colour a notch flashes as the fill passes it: another shot banked.")]
        [SerializeField] private Color notchPingColour = new(1f, 1f, 1f, 0.95f);

        [Tooltip("Seconds that flash takes to fade.")]
        [Min(0.001f)] [SerializeField] private float notchPingSeconds = 0.35f;

        [Tooltip("With more notches than this they are hidden: a cost too small to count in " +
                 "shots is better read off the number.")]
        [Min(1)] [SerializeField] private int maxNotches = 24;

        [Header("Full")]
        [Tooltip("Colour of the halo behind the bar when it fills; alpha is its peak opacity.")]
        [SerializeField] private Color haloColour = new(0.42f, 0.78f, 1f, 0.85f);

        [Tooltip("Colour the frame flares to when the bar fills.")]
        [SerializeField] private Color frameGlowColour = new(0.86f, 0.97f, 1f, 1f);

        [Tooltip("Colour of the glint swept across the bar; alpha is its peak opacity.")]
        [SerializeField] private Color glintColour = new(1f, 1f, 1f, 0.85f);

        [Tooltip("Seconds the glint takes to cross the bar.")]
        [Min(0.001f)] [SerializeField] private float glintSeconds = 0.45f;

        [Tooltip("Seconds the halo, frame flare and star pop take to fade.")]
        [Min(0.001f)] [SerializeField] private float fullGlowSeconds = 0.6f;

        [Tooltip("Opacity (0..1) of the white brighten over the fill when the bar fills.")]
        [Range(0f, 1f)] [SerializeField] private float fullFillGlow = 0.3f;

        [Tooltip("Extra scale (fraction of its size) the star pops to when the bar fills.")]
        [Min(0f)] [SerializeField] private float iconPop = 0.3f;

        [Header("Failed spend")]
        [Tooltip("Colour of the flash, and of the star and number while it fades.")]
        [SerializeField] private Color failColour = new(1f, 0.22f, 0.2f, 1f);

        [Tooltip("Peak opacity (0..1) of the flash over the well.")]
        [Range(0f, 1f)] [SerializeField] private float failFlashAlpha = 0.55f;

        [Tooltip("Seconds the shake and the flash last.")]
        [Min(0.001f)] [SerializeField] private float failSeconds = 0.3f;

        [Tooltip("Peak horizontal shake in canvas pixels (1080p reference).")]
        [Min(0f)] [SerializeField] private float shakeAmplitude = 7f;

        [Tooltip("Shake oscillations per second.")]
        [Min(0f)] [SerializeField] private float shakeHz = 22f;

        float _shownCurrent, _shownTrail, _trailHoldUntil;
        float _dim, _regenKickLeft;
        bool _wasDelayed;
        float _shimmerVis, _shimmerPhase;
        float _previewVis;
        float _fullGlow, _glintT = -1f;
        float _failFlash, _shakeT = -1f, _lastFailAt = float.NegativeInfinity;

        // Notch layout: rebuilt lazily whenever cost or max differs from what
        // was laid out, which also covers a script reload resetting these.
        int _notchCost = -1, _notchMax = -1, _notchCount;
        Image[] _notches;
        int _bankedShown = -1, _pingIndex = -1;
        float _ping;

        Color _fillBase = Color.white, _frameBase = Color.white, _iconBase = Color.white, _textBase = Color.white;
        bool _hasBaseColours;
        int _shownValue = int.MinValue, _shownMax = int.MinValue;
        Mana _subscribedMana;
        SpellCaster _subscribedCaster;

        void Awake() => ResolveSources();

        void OnEnable()
        {
            // Captured here, not in Awake: Awake does not run again after a script
            // reload in Play mode. OnDisable puts the resting colours back first,
            // so a reload mid-flash never captures a flash as the base.
            if (!_hasBaseColours) CaptureBaseColours();
            Subscribe(source, caster);
            Snap();
        }

        void OnDisable()
        {
            Subscribe(null, null);
            RestoreRestingLook();
        }

        /// <summary>
        /// Points the bar at a pool, and optionally at the caster that spends it,
        /// at runtime. Either may be null: a missing pool is taken from the
        /// caster, and a missing caster only loses the notches, the aim preview
        /// and the CastFailed shake. Snaps to the current value so rebinding
        /// never sweeps the bar up from empty.
        /// </summary>
        public void Bind(Mana mana, SpellCaster spellCaster)
        {
            source = mana;
            caster = spellCaster;
            ResolveSources();
            if (isActiveAndEnabled) Subscribe(source, caster);
            Snap();
        }

        void ResolveSources()
        {
            if (source == null && caster != null) source = caster.Mana;
            if (source == null) source = GetComponentInParent<Mana>();
            if (caster == null && source != null) caster = source.GetComponent<SpellCaster>();
        }

        void Subscribe(Mana mana, SpellCaster spellCaster)
        {
            if (_subscribedMana != null)
            {
                _subscribedMana.Spent -= OnSpent;
                _subscribedMana.SpendFailed -= OnSpendFailed;
                _subscribedMana.Filled -= OnFilled;
            }
            _subscribedMana = mana;
            if (_subscribedMana != null)
            {
                _subscribedMana.Spent += OnSpent;
                _subscribedMana.SpendFailed += OnSpendFailed;
                _subscribedMana.Filled += OnFilled;
            }

            if (_subscribedCaster != null) _subscribedCaster.CastFailed -= OnCastFailed;
            _subscribedCaster = spellCaster;
            if (_subscribedCaster != null) _subscribedCaster.CastFailed += OnCastFailed;
        }

        /// <summary>Start already showing the truth, so the bar does not sweep up
        /// from empty or flash a stale regen kick on its first frame.</summary>
        void Snap()
        {
            if (source == null) return;
            _shownCurrent = source.Normalised;
            _shownTrail = _shownCurrent;
            _wasDelayed = source.RegenDelayRemaining > 0f;
            _dim = _wasDelayed ? 1f : 0f;
            _bankedShown = -1;
        }

        void CaptureBaseColours()
        {
            if (currentFill != null) _fillBase = currentFill.color;
            if (frame != null) _frameBase = frame.color;
            if (icon != null) _iconBase = icon.color;
            if (valueText != null) _textBase = valueText.color;
            _hasBaseColours = true;
        }

        /// <summary>Ends every one-shot effect and puts the resting look back,
        /// so a disabled bar is never frozen mid-shake or mid-flash.</summary>
        void RestoreRestingLook()
        {
            _fullGlow = _failFlash = _regenKickLeft = _previewVis = _shimmerVis = _ping = 0f;
            _glintT = _shakeT = -1f;

            if (body != null) body.anchoredPosition = Vector2.zero;
            if (icon != null) icon.rectTransform.localScale = Vector3.one;
            Tint(halo, haloColour, 0f);
            Tint(fillGlow, Color.white, 0f);
            Tint(costPreview, previewColour, 0f);
            Tint(delayLine, delayLineColour, 0f);
            Tint(shimmerBand, shimmerColour, 0f);
            Tint(glint, glintColour, 0f);
            Tint(flash, failColour, 0f);

            if (!_hasBaseColours) return;
            if (currentFill != null) currentFill.color = _fillBase;
            if (frame != null) frame.color = _frameBase;
            if (icon != null) icon.color = _iconBase;
            if (valueText != null) valueText.color = _textBase;
        }

        // ---- one-shot kicks --------------------------------------------------

        // Every spend restarts the hold, so a quick volley reads as one chunk
        // rather than draining in stutters between casts.
        void OnSpent(int cost) => _trailHoldUntil = Time.unscaledTime + trailDelay;

        void OnSpendFailed(int cost) => KickFailure();

        void OnCastFailed() => KickFailure();

        /// <summary>
        /// Debounced: a failure within <see cref="FailDebounce"/> of the previous
        /// one only pushes the window on. A dry press raises SpendFailed and then
        /// CastFailed in the same frame, and a held auto-repeat spell that has run
        /// dry calls TrySpend — so raises SpendFailed — every frame. Restarting on
        /// each would pin the flash at full red and freeze the shake at its first
        /// frame; this plays one shake per burst of failures instead.
        /// </summary>
        void KickFailure()
        {
            float now = Time.unscaledTime;
            bool fresh = now - _lastFailAt > FailDebounce;
            _lastFailAt = now;
            if (!fresh) return;
            _failFlash = 1f;
            _shakeT = 0f;
        }

        void OnFilled()
        {
            _fullGlow = 1f;
            _glintT = 0f;
        }

        // ---- per frame -------------------------------------------------------

        void Update()
        {
            if (source == null) return;

            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;
            int current = source.Current;
            int max = source.Max;
            float target = source.Normalised;
            int cost = caster != null ? caster.Cost : 0;

            _shownCurrent = smoothing > 0f
                ? Mathf.Lerp(_shownCurrent, target, 1f - Mathf.Exp(-dt / smoothing))
                : target;
            if (currentFill != null) currentFill.fillAmount = _shownCurrent;

            UpdateTrail(target, now, dt);
            UpdateDelay(dt);
            UpdateShimmer(target, dt);
            UpdatePreview(cost, max, now, dt);
            UpdateNotches(cost, max, dt);
            UpdateFull(dt);
            UpdateFailure(dt);
            UpdateAccents();
            UpdateValue(current, max);
        }

        void UpdateTrail(float target, float now, float dt)
        {
            if (trailFill == null) return;

            // Regen snaps the chip up — it marks mana spent, never mana gained.
            if (target >= _shownTrail) _shownTrail = target;
            else if (now >= _trailHoldUntil)
                _shownTrail = trailSmoothing > 0f
                    ? Mathf.Lerp(_shownTrail, target, 1f - Mathf.Exp(-dt / trailSmoothing))
                    : target;

            trailFill.fillAmount = Mathf.Max(_shownTrail, _shownCurrent);
        }

        void UpdateDelay(float dt)
        {
            bool delayed = source.RegenDelayRemaining > 0f;

            // The moment the wait ends and the bar starts charging gets a beat of
            // its own, so "regen started" is something you notice rather than a
            // slope you infer a second later.
            if (_wasDelayed && !delayed && !source.IsFull) _regenKickLeft = 1f;
            _wasDelayed = delayed;

            _dim = Mathf.MoveTowards(_dim, delayed ? 1f : 0f, dt / delayFade);
            _regenKickLeft = Mathf.MoveTowards(_regenKickLeft, 0f, dt / regenKickSeconds);

            if (currentFill != null)
            {
                var dimmed = new Color(_fillBase.r * delayTint.r, _fillBase.g * delayTint.g,
                                       _fillBase.b * delayTint.b, _fillBase.a);
                currentFill.color = Color.Lerp(_fillBase, dimmed, _dim);
            }

            if (delayLine != null)
            {
                // Remaining over total: full width right after a spend, burning
                // down to nothing as regen starts. A second spend refills it, which
                // is exactly the "timer reset" the player needs to see.
                delayLine.fillAmount = source.RegenDelayNormalised;
                Tint(delayLine, delayLineColour, _dim);
            }
        }

        void UpdateShimmer(float target, float dt)
        {
            if (shimmerClip == null || shimmerBand == null) return;

            _shimmerVis = Mathf.MoveTowards(_shimmerVis, source.IsRegenerating ? 1f : 0f, dt / shimmerFade);

            // The clip's right edge rides the fill, so the band only ever
            // crosses mana you actually hold.
            SetAnchorMaxX(shimmerClip, _shownCurrent);
            if (_shimmerVis <= 0f)
            {
                Tint(shimmerBand, shimmerColour, 0f);
                return;
            }

            // Linear in fill, like Terraria's regen multiplier (a fifth of the
            // rate at empty, full rate at max), plus the standing-still bonus.
            float hz = Mathf.Lerp(shimmerHzEmpty, shimmerHzFull, target);
            if (source.IsStandingStill) hz *= shimmerStillBoost;
            // Phase is accumulated, not derived from time × rate, so the band
            // never jumps when the rate changes under it.
            _shimmerPhase = Mathf.Repeat(_shimmerPhase + hz * dt, 1f);

            var band = shimmerBand.rectTransform;
            float half = band.rect.width * 0.5f;
            float filledWidth = ParentWidth(shimmerClip) * _shownCurrent;
            float sweep = Mathf.Clamp01(_shimmerPhase / SweepShare);
            band.anchoredPosition = new Vector2(Mathf.Lerp(-half, filledWidth + half, sweep), 0f);
            Tint(shimmerBand, shimmerColour, _shimmerVis);
        }

        void UpdatePreview(int cost, int max, float now, float dt)
        {
            if (costPreview == null) return;

            bool show = caster != null && caster.IsAiming && cost > 0 && max > 0;
            _previewVis = Mathf.MoveTowards(_previewVis, show ? 1f : 0f, dt / previewFade);
            if (_previewVis <= 0f)
            {
                Tint(costPreview, previewColour, 0f);
                return;
            }

            float costShare = Mathf.Clamp01((float)cost / Mathf.Max(1, max));
            bool affordable = source.CanAfford(cost);
            float from, to;
            if (affordable)
            {
                // The chunk the next shot will take off the end of the fill.
                to = _shownCurrent;
                from = Mathf.Max(0f, to - costShare);
            }
            else
            {
                // The whole price, so the shortfall shows as a red ghost past the
                // fill: "you need this much" rather than just "no".
                from = 0f;
                to = costShare;
            }
            SetHorizontalAnchors(costPreview.rectTransform, from, to);

            float pulse = 0.5f + 0.5f * Mathf.Sin(now * previewHz * Tau);
            Tint(costPreview, affordable ? previewColour : previewShortColour,
                 _previewVis * Mathf.Lerp(0.45f, 1f, pulse));
        }

        void UpdateNotches(int cost, int max, float dt)
        {
            if (notchContainer == null) return;
            if (cost != _notchCost || max != _notchMax) LayoutNotches(cost, max);
            if (_notchCount == 0) return;

            // Off the eased fill (with half a point of slack, since the easing
            // only approaches its target), so a notch lights as the bar visibly
            // crosses it rather than a few frames before.
            int banked = Mathf.Clamp(Mathf.FloorToInt((_shownCurrent * max + 0.5f) / cost), 0, _notchCount);
            if (_bankedShown >= 0 && banked > _bankedShown)
            {
                _pingIndex = banked - 1;
                _ping = 1f;
            }
            _bankedShown = banked;
            _ping = Mathf.MoveTowards(_ping, 0f, dt / notchPingSeconds);

            for (int i = 0; i < _notchCount; i++)
            {
                var notch = _notches[i];
                if (notch == null) continue;
                var colour = i < banked ? notchFilledColour : notchEmptyColour;
                if (i == _pingIndex && _ping > 0f) colour = Color.Lerp(colour, notchPingColour, _ping);
                notch.color = colour;
            }
        }

        /// <summary>
        /// One notch at every whole cast's worth short of the end — 15, 30 … 90
        /// for 15 of 100. Runs only when the cost or max changes. The container's
        /// children are the notches, so a script reload that forgets the array
        /// finds the same objects again instead of cloning a second set.
        /// </summary>
        void LayoutNotches(int cost, int max)
        {
            _notchCost = cost;
            _notchMax = max;
            _bankedShown = -1;
            _pingIndex = -1;
            _ping = 0f;

            int count = cost > 0 && max > 0 ? (max - 1) / cost : 0;
            if (count > maxNotches) count = 0;

            if (notchTemplate != null)
                while (notchContainer.childCount < count)
                    Instantiate(notchTemplate, notchContainer);

            int children = notchContainer.childCount;
            count = Mathf.Min(count, children);
            if (_notches == null || _notches.Length < children) _notches = new Image[children];

            for (int i = 0; i < children; i++)
            {
                var child = notchContainer.GetChild(i);
                _notches[i] = child.GetComponent<Image>();
                bool used = i < count;
                if (child.gameObject.activeSelf != used) child.gameObject.SetActive(used);
                if (!used || !(child is RectTransform rect)) continue;
                float x = (float)((i + 1) * cost) / max;
                rect.anchorMin = new Vector2(x, rect.anchorMin.y);
                rect.anchorMax = new Vector2(x, rect.anchorMax.y);
            }
            _notchCount = count;
        }

        void UpdateFull(float dt)
        {
            _fullGlow = Mathf.MoveTowards(_fullGlow, 0f, dt / fullGlowSeconds);
            // Squared: a bright pop that falls away fast, then a long soft tail.
            float glow = _fullGlow * _fullGlow;

            Tint(halo, haloColour, glow);
            if (frame != null) frame.color = Color.Lerp(_frameBase, frameGlowColour, glow);

            if (fillGlow != null)
            {
                fillGlow.fillAmount = _shownCurrent;
                Tint(fillGlow, Color.white, Mathf.Max(_regenKickLeft * regenKickAlpha, glow * fullFillGlow));
            }

            if (glint == null) return;
            if (_glintT >= 0f)
            {
                _glintT += dt / glintSeconds;
                if (_glintT >= 1f) _glintT = -1f;
            }
            if (_glintT < 0f)
            {
                Tint(glint, glintColour, 0f);
                return;
            }
            var rect = glint.rectTransform;
            float half = rect.rect.width * 0.5f;
            float t = Mathf.SmoothStep(0f, 1f, _glintT);
            rect.anchoredPosition = new Vector2(Mathf.Lerp(-half, ParentWidth(rect) + half, t), 0f);
            Tint(glint, glintColour, Mathf.Sin(_glintT * Mathf.PI));
        }

        void UpdateFailure(float dt)
        {
            _failFlash = Mathf.MoveTowards(_failFlash, 0f, dt / failSeconds);
            Tint(flash, failColour, _failFlash * failFlashAlpha);

            if (body == null || _shakeT < 0f) return;
            _shakeT += dt;
            float envelope = 1f - _shakeT / failSeconds;
            if (envelope <= 0f)
            {
                _shakeT = -1f;
                body.anchoredPosition = Vector2.zero;
                return;
            }
            // Damped (envelope squared) and starting from rest, so it reads as a
            // head-shake, not a jolt.
            float x = shakeAmplitude * envelope * envelope * Mathf.Sin(_shakeT * shakeHz * Tau);
            body.anchoredPosition = new Vector2(x, 0f);
        }

        void UpdateAccents()
        {
            float glow = _fullGlow * _fullGlow;
            if (icon != null)
            {
                var colour = Color.Lerp(_iconBase, Color.white, glow * 0.7f);
                icon.color = Color.Lerp(colour, failColour, _failFlash);
                float scale = 1f + iconPop * glow;
                var rect = icon.rectTransform;
                if (rect.localScale.x != scale) rect.localScale = new Vector3(scale, scale, 1f);
            }
            if (valueText != null) valueText.color = Color.Lerp(_textBase, failColour, _failFlash);
        }

        void UpdateValue(int current, int max)
        {
            if (valueText == null || (current == _shownValue && max == _shownMax)) return;
            _shownValue = current;
            _shownMax = max;
            valueText.SetText("{0} / {1}", current, max);
        }

        // ---- helpers ---------------------------------------------------------

        /// <summary>Sets a graphic to a colour with its alpha scaled. Graphic.color
        /// ignores a write of the same value, so calling this every frame on a
        /// hidden overlay costs no canvas rebuild.</summary>
        static void Tint(Graphic graphic, Color colour, float alphaScale)
        {
            if (graphic == null) return;
            colour.a *= alphaScale;
            graphic.color = colour;
        }

        static void SetAnchorMaxX(RectTransform rect, float x)
        {
            var anchor = rect.anchorMax;
            if (anchor.x == x) return;
            anchor.x = x;
            rect.anchorMax = anchor;
        }

        static void SetHorizontalAnchors(RectTransform rect, float from, float to)
        {
            var min = rect.anchorMin;
            var max = rect.anchorMax;
            if (min.x == from && max.x == to) return;
            min.x = from;
            max.x = to;
            rect.anchorMin = min;
            rect.anchorMax = max;
        }

        /// <summary>Width of the well a layer lives in. Read from the parent rather
        /// than the layer itself, whose anchors may have moved this frame.</summary>
        static float ParentWidth(RectTransform rect) =>
            rect.parent is RectTransform parent ? parent.rect.width : 0f;
    }
}
