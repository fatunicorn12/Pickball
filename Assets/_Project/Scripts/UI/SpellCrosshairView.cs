using Pickleball.Sandbox.Magic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pickleball.UI
{
    /// <summary>
    /// The spell's screen-centre crosshair: a dot, four ticks, a thin cooldown
    /// ring and the cast cost underneath. It exists only while aiming — the
    /// rest of the game has no crosshair, and one left on screen would read as
    /// "you can shoot now" when you cannot.
    ///
    /// It answers three questions without the player looking away from the
    /// target: will this hit (ticks tighten and turn gold on a damageable),
    /// can I fire (grey and a red cost when short of mana; the ring while on
    /// cooldown), and did it fire (the ticks bloom outward and settle).
    ///
    /// State is polled every frame from <see cref="SpellCaster"/>, so a missed
    /// event — or a script reload, which drops every subscription — can never
    /// leave it showing while not aiming. The events only kick one-shot
    /// feedback: AimStarted (the ticks converge as it appears), Cast (bloom)
    /// and CastFailed (a red flash and a shake of the cost). AimEnded needs no
    /// handler; the polled fade-out already covers it.
    ///
    /// Animation runs on unscaled time, like the mana bar beside it, so slow
    /// motion never leaves the reticle half-faded.
    /// </summary>
    public class SpellCrosshairView : MonoBehaviour
    {
        const float Tau = Mathf.PI * 2f;
        const int TickCount = 4;

        [Header("Source")]
        [Tooltip("The caster whose aim this draws. Hidden while missing.")]
        [SerializeField] private SpellCaster caster;

        [Header("Parts")]
        [Tooltip("Fades the whole crosshair. Should neither block raycasts nor be interactable. " +
                 "Falls back to a CanvasGroup on this object.")]
        [SerializeField] private CanvasGroup group;

        [Tooltip("Parent of the dot, ticks and ring. Scaled for the show / hide pop, so the " +
                 "cost label underneath never moves with it.")]
        [SerializeField] private RectTransform reticle;

        [Tooltip("The centre dot.")]
        [SerializeField] private Image dot;

        [Tooltip("Four ticks in the order up, right, down, left. Each is moved along its own " +
                 "axis; their rotation is the builder's.")]
        [SerializeField] private Image[] ticks = new Image[TickCount];

        [Tooltip("Filled (Radial 360) ring that drains over the cooldown. Optional.")]
        [SerializeField] private Image cooldownRing;

        [Tooltip("Parent of the cost icon and number; shaken on a failed cast. Optional.")]
        [SerializeField] private RectTransform costGroup;

        [Tooltip("The small mana star beside the cost. Optional.")]
        [SerializeField] private Image costIcon;

        [Tooltip("The cast cost, e.g. 15. Optional.")]
        [SerializeField] private TMP_Text costText;

        [Header("Show / hide")]
        [Tooltip("Seconds to fade and scale in when aim starts, and out when it ends.")]
        [Min(0f)] [SerializeField] private float fadeSeconds = 0.12f;

        [Tooltip("Scale of the reticle while hidden: it contracts onto the centre as it " +
                 "appears and swells away as it goes.")]
        [Min(0f)] [SerializeField] private float hiddenScale = 1.35f;

        [Header("Ticks")]
        [Tooltip("Canvas pixels (1080p reference) from the centre to each tick's centre at rest.")]
        [Min(0f)] [SerializeField] private float restGap = 15f;

        [Tooltip("Canvas pixels when on a target. Tighter, so the lock reads before the colour does.")]
        [Min(0f)] [SerializeField] private float onTargetGap = 11f;

        [Tooltip("Extra canvas pixels a cast throws the ticks out (bloom).")]
        [Min(0f)] [SerializeField] private float bloomSpread = 12f;

        [Tooltip("Seconds (time constant) the bloom takes to settle.")]
        [Min(0.001f)] [SerializeField] private float bloomSettle = 0.12f;

        [Tooltip("Bloom kicked on aim start, as a fraction (0..1) of a cast's, so the ticks " +
                 "converge as the reticle appears.")]
        [Range(0f, 1f)] [SerializeField] private float aimStartBloom = 0.8f;

        [Tooltip("How much the dot swells with the bloom, as a fraction of its size.")]
        [Min(0f)] [SerializeField] private float dotPunch = 0.6f;

        [Tooltip("Seconds (time constant) to tighten onto or relax off a target.")]
        [Min(0.001f)] [SerializeField] private float targetSmoothing = 0.05f;

        [Header("Colours")]
        [Tooltip("Reticle colour when a cast is possible and nothing is under it.")]
        [SerializeField] private Color readyColour = new(0.95f, 0.97f, 1f, 1f);

        [Tooltip("Reticle colour on a damageable target: warm gold, like the weak-point numbers.")]
        [SerializeField] private Color onTargetColour = new(1f, 0.8f, 0.32f, 1f);

        [Tooltip("Reticle colour when the bar cannot afford a cast: greyed and slightly faded.")]
        [SerializeField] private Color shortColour = new(0.55f, 0.57f, 0.62f, 0.7f);

        [Tooltip("Seconds (time constant) the reticle takes to change colour.")]
        [Min(0.001f)] [SerializeField] private float colourSmoothing = 0.06f;

        [Header("Cost label")]
        [Tooltip("Cost colour (number and star) when affordable: mana blue.")]
        [SerializeField] private Color costColour = new(0.62f, 0.84f, 1f, 1f);

        [Tooltip("Cost colour when the bar cannot afford it.")]
        [SerializeField] private Color costShortColour = new(1f, 0.34f, 0.3f, 1f);

        [Header("Failed cast")]
        [Tooltip("Colour the reticle and cost flash on a failed cast.")]
        [SerializeField] private Color failColour = new(1f, 0.25f, 0.22f, 1f);

        [Tooltip("Seconds the flash and the cost shake last.")]
        [Min(0.001f)] [SerializeField] private float failSeconds = 0.3f;

        [Tooltip("Peak horizontal shake of the cost label in canvas pixels (1080p reference).")]
        [Min(0f)] [SerializeField] private float failShake = 5f;

        [Tooltip("Shake oscillations per second.")]
        [Min(0f)] [SerializeField] private float failShakeHz = 24f;

        float _shown, _lastEase = -1f;
        float _bloom, _onTarget, _failFlash, _failT = -1f;
        Color _tint = Color.white;
        bool _snapTint = true;
        int _shownCost = int.MinValue;
        Vector2 _costRest;
        bool _hasCostRest;
        SpellCaster _subscribed;

        void Awake()
        {
            if (group == null) group = GetComponent<CanvasGroup>();
        }

        void OnEnable()
        {
            // Captured here rather than in Awake (which a script reload skips);
            // OnDisable puts it back first, so a reload mid-shake is harmless.
            if (!_hasCostRest && costGroup != null)
            {
                _costRest = costGroup.anchoredPosition;
                _hasCostRest = true;
            }
            Subscribe(caster);
            _snapTint = true;
            ResetVisibility();
        }

        void OnDisable()
        {
            Subscribe(null);
            _bloom = _failFlash = 0f;
            _failT = -1f;
            if (costGroup != null && _hasCostRest) costGroup.anchoredPosition = _costRest;
            // Hidden outright, not frozen half-faded.
            ResetVisibility();
        }

        /// <summary>
        /// Points the crosshair at a caster at runtime. Its mana, cost, aim and
        /// cooldown are all read through the caster, so this is the only wiring
        /// it needs. Null hides it.
        /// </summary>
        public void Bind(SpellCaster spellCaster)
        {
            caster = spellCaster;
            if (isActiveAndEnabled) Subscribe(caster);
            _snapTint = true;
        }

        void Subscribe(SpellCaster spellCaster)
        {
            if (_subscribed != null)
            {
                _subscribed.AimStarted -= OnAimStarted;
                _subscribed.Cast -= OnCast;
                _subscribed.CastFailed -= OnCastFailed;
            }
            _subscribed = spellCaster;
            if (_subscribed != null)
            {
                _subscribed.AimStarted += OnAimStarted;
                _subscribed.Cast += OnCast;
                _subscribed.CastFailed += OnCastFailed;
            }
        }

        void ResetVisibility()
        {
            _shown = 0f;
            _lastEase = -1f;
            ApplyVisibility();
        }

        // ---- one-shot kicks --------------------------------------------------

        void OnAimStarted()
        {
            _bloom = Mathf.Max(_bloom, aimStartBloom);
            _onTarget = 0f;
            _snapTint = true;
        }

        void OnCast(MagicOrb orb) => _bloom = 1f;

        void OnCastFailed()
        {
            _failFlash = 1f;
            _failT = 0f;
        }

        // ---- per frame -------------------------------------------------------

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            bool aiming = caster != null && caster.IsAiming;
            float goal = aiming ? 1f : 0f;
            _shown = fadeSeconds > 0f ? Mathf.MoveTowards(_shown, goal, dt / fadeSeconds) : goal;
            ApplyVisibility();
            if (_shown <= 0f || caster == null) return;

            var mana = caster.Mana;
            int cost = caster.Cost;
            bool affordable = cost <= 0 || (mana != null && mana.CanAfford(cost));
            bool onTarget = affordable && caster.AimOnTarget;

            _onTarget = Mathf.Lerp(_onTarget, onTarget ? 1f : 0f, 1f - Mathf.Exp(-dt / targetSmoothing));
            _bloom *= Mathf.Exp(-dt / bloomSettle);
            _failFlash = Mathf.MoveTowards(_failFlash, 0f, dt / failSeconds);

            var want = !affordable ? shortColour : onTarget ? onTargetColour : readyColour;
            if (_snapTint)
            {
                // A fresh aim starts in the right colour instead of fading out of
                // whatever the last aim ended on.
                _tint = want;
                _snapTint = false;
            }
            else
            {
                _tint = Color.Lerp(_tint, want, 1f - Mathf.Exp(-dt / colourSmoothing));
            }
            var tint = Color.Lerp(_tint, failColour, _failFlash);

            float gap = Mathf.Lerp(restGap, onTargetGap, _onTarget) + bloomSpread * _bloom;
            if (ticks != null)
            {
                for (int i = 0; i < ticks.Length && i < TickCount; i++)
                {
                    var tick = ticks[i];
                    if (tick == null) continue;
                    tick.rectTransform.anchoredPosition = TickDirection(i) * gap;
                    tick.color = tint;
                }
            }

            if (dot != null)
            {
                dot.color = tint;
                float scale = 1f + dotPunch * _bloom;
                dot.rectTransform.localScale = new Vector3(scale, scale, 1f);
            }

            if (cooldownRing != null) cooldownRing.fillAmount = caster.CooldownNormalised;

            UpdateCost(cost, affordable, dt);
        }

        void ApplyVisibility()
        {
            float ease = Mathf.SmoothStep(0f, 1f, _shown);
            if (ease == _lastEase) return;
            _lastEase = ease;
            if (group != null) group.alpha = ease;
            if (reticle != null)
            {
                float scale = Mathf.LerpUnclamped(hiddenScale, 1f, ease);
                reticle.localScale = new Vector3(scale, scale, 1f);
            }
        }

        void UpdateCost(int cost, bool affordable, float dt)
        {
            if (cost != _shownCost)
            {
                // Only on change: SetText with a format argument builds no string,
                // and skipping unchanged frames skips the mesh rebuild too.
                _shownCost = cost;
                if (costText != null) costText.SetText("{0}", cost);
                if (costGroup != null) costGroup.gameObject.SetActive(cost > 0);
            }

            var colour = Color.Lerp(affordable ? costColour : costShortColour, failColour, _failFlash);
            if (costText != null) costText.color = colour;
            if (costIcon != null) costIcon.color = colour;

            if (costGroup == null || !_hasCostRest) return;
            var position = _costRest;
            if (_failT >= 0f)
            {
                _failT += dt;
                float envelope = 1f - _failT / failSeconds;
                if (envelope <= 0f) _failT = -1f;
                else position.x += failShake * envelope * envelope * Mathf.Sin(_failT * failShakeHz * Tau);
            }
            if (costGroup.anchoredPosition != position) costGroup.anchoredPosition = position;
        }

        static Vector2 TickDirection(int index) => index switch
        {
            0 => Vector2.up,
            1 => Vector2.right,
            2 => Vector2.down,
            _ => Vector2.left,
        };
    }
}
