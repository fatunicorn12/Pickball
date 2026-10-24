using Pickleball.Sandbox;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pickleball.UI
{
    /// <summary>
    /// Draws a <see cref="Health"/> as four stacked regions:
    ///
    ///   [ ==== current ==== ][ ~~ recoverable ~~ ][ !! trail !! ][ ---- gone ---- ]
    ///
    /// The grey span is the whole point of the readout. It has to be legible at a
    /// glance mid-fight, because it is the difference between "back off" and
    /// "press now" — and the fight is tuned expecting the player to press.
    ///
    /// The trail is the fighting-game chip: a bright sliver showing the hit you
    /// just took, held for a beat and then drained. Without it a big hit and a
    /// small one look identical a frame later, and on the boss bar — which has no
    /// grey region — it is the only way to see how much a combo actually did.
    ///
    /// Read per frame rather than driven off Health.Changed: the bar eases toward
    /// its target, so it needs a frame tick regardless and an event would only
    /// duplicate what Update already does. <see cref="Health.Hurt"/> is still
    /// listened to, but only to kick the flash.
    /// </summary>
    public class HealthBarView : MonoBehaviour
    {
        [SerializeField] private Health source;

        [Tooltip("Filled image for health actually held. Image Type must be Filled.")]
        [SerializeField] private Image currentFill;

        [Tooltip("Filled image drawn behind the current fill, out to current + " +
                 "recoverable. Sits between the solid bar and the empty track.")]
        [SerializeField] private Image recoverableFill;

        [Tooltip("Filled image behind everything else showing damage just taken. " +
                 "Optional.")]
        [SerializeField] private Image trailFill;

        [Tooltip("Overlay whose alpha spikes on each hit. Optional.")]
        [SerializeField] private Image flash;

        [Tooltip("Optional numeric readout, e.g. 73 / 100.")]
        [SerializeField] private TMP_Text valueText;

        [Tooltip("Seconds the bar takes to catch up. Purely cosmetic easing — the " +
                 "underlying values are already correct the frame they change.")]
        [SerializeField] private float smoothing = 0.12f;

        [Header("Trail")]
        [Tooltip("Seconds the trail holds after the last drop before draining.")]
        [SerializeField] private float trailDelay = 0.55f;

        [Tooltip("Seconds the trail takes to catch up once it starts draining.")]
        [SerializeField] private float trailSmoothing = 0.18f;

        [Header("Feedback")]
        [Tooltip("Below this fraction the current fill pulses toward lowColour. " +
                 "Zero disables it.")]
        [Range(0f, 1f)]
        [SerializeField] private float lowThreshold = 0.25f;

        [SerializeField] private Color lowColour = new(1f, 0.25f, 0.2f);

        [Tooltip("Seconds a hit flash takes to fade.")]
        [SerializeField] private float flashFade = 0.25f;

        float _shownCurrent, _shownRecoverable, _shownTrail;
        float _trailHoldUntil;
        float _flash;
        Color _baseColour = Color.white;
        bool _hasBaseColour;
        int _shownValue = int.MinValue;
        Health _subscribed;

        void Awake()
        {
            if (source == null) source = GetComponentInParent<Health>();
        }

        void OnEnable()
        {
            // Captured here, not in Awake: Awake does not run again after a script
            // reload in Play mode, and the bar would then reset itself to white.
            if (!_hasBaseColour && currentFill != null)
            {
                _baseColour = currentFill.color;
                _hasBaseColour = true;
            }
            Subscribe(source);
            Snap();
        }

        void OnDisable() => Subscribe(null);

        void Subscribe(Health health)
        {
            if (_subscribed != null) _subscribed.Hurt -= OnHurt;
            _subscribed = health;
            if (_subscribed != null) _subscribed.Hurt += OnHurt;
        }

        /// <summary>Start already showing the truth, so the bar does not sweep up
        /// from empty on the first frame of the fight.</summary>
        void Snap()
        {
            if (source == null) return;
            _shownCurrent = source.CurrentNormalised;
            _shownRecoverable = source.RecoverableNormalised;
            _shownTrail = _shownRecoverable;
        }

        void OnHurt(float amount, Vector3 at)
        {
            // Scaled by the hit's share of the bar, with a floor, so a sword hit
            // pops and the beam's per-frame trickle glows steadily rather than
            // strobing at full brightness.
            float size = source != null && source.Max > 0f ? amount / source.Max : 0f;
            _flash = Mathf.Max(_flash, Mathf.Clamp01(0.35f + size * 8f));

            // Every hit restarts the hold, so a combo — or a beam held on you —
            // reads as one chunk rather than draining in stutters between hits.
            // Keyed off Hurt rather than off the bar dropping, because the grey
            // pool decaying is also a drop and must not freeze the trail.
            _trailHoldUntil = Time.time + trailDelay;
        }

        void Update()
        {
            if (source == null) return;

            float targetCurrent = source.CurrentNormalised;
            float targetRecoverable = source.RecoverableNormalised;

            if (smoothing <= 0f)
            {
                _shownCurrent = targetCurrent;
                _shownRecoverable = targetRecoverable;
            }
            else
            {
                float t = 1f - Mathf.Exp(-Time.deltaTime / smoothing);
                _shownCurrent = Mathf.Lerp(_shownCurrent, targetCurrent, t);
                _shownRecoverable = Mathf.Lerp(_shownRecoverable, targetRecoverable, t);
            }

            if (currentFill != null) currentFill.fillAmount = _shownCurrent;
            // Never let the grey edge fall behind the solid edge, which the two
            // different easing rates can otherwise do for a frame on a big heal.
            float farEdge = Mathf.Max(_shownRecoverable, _shownCurrent);
            if (recoverableFill != null) recoverableFill.fillAmount = farEdge;

            UpdateTrail(Mathf.Max(targetRecoverable, targetCurrent), farEdge);
            UpdateFeedback(targetCurrent);
            UpdateValue();
        }

        void UpdateTrail(float target, float farEdge)
        {
            if (trailFill == null) return;

            // Healing snaps the trail up — it marks loss, never gain.
            if (target >= _shownTrail) _shownTrail = target;

            if (Time.time >= _trailHoldUntil && _shownTrail > target)
            {
                float t = trailSmoothing > 0f ? 1f - Mathf.Exp(-Time.deltaTime / trailSmoothing) : 1f;
                _shownTrail = Mathf.Lerp(_shownTrail, target, t);
            }

            trailFill.fillAmount = Mathf.Max(_shownTrail, farEdge);
        }

        void UpdateFeedback(float current)
        {
            if (flash != null)
            {
                _flash = flashFade > 0f ? Mathf.MoveTowards(_flash, 0f, Time.deltaTime / flashFade) : 0f;
                var c = flash.color;
                c.a = _flash * 0.5f;
                flash.color = c;
            }

            if (currentFill == null) return;
            if (lowThreshold > 0f && current > 0f && current <= lowThreshold)
            {
                // Faster the closer to empty: a resting heartbeat at the threshold,
                // racing near zero.
                float urgency = 1f - current / lowThreshold;
                float hz = Mathf.Lerp(1.2f, 3f, urgency);
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * hz * Mathf.PI * 2f);
                currentFill.color = Color.Lerp(_baseColour, lowColour, 0.35f + 0.65f * pulse);
            }
            else
            {
                currentFill.color = _baseColour;
            }
        }

        void UpdateValue()
        {
            if (valueText == null) return;
            int value = Mathf.CeilToInt(source.Current);
            if (value == _shownValue) return;
            _shownValue = value;
            valueText.SetText("{0} / {1}", value, Mathf.CeilToInt(source.Max));
        }

        public void Bind(Health health)
        {
            source = health;
            if (isActiveAndEnabled) Subscribe(source);
            Snap();
        }
    }
}
