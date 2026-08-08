using Pickleball.Sandbox;
using UnityEngine;
using UnityEngine.UI;

namespace Pickleball.UI
{
    /// <summary>
    /// Draws a <see cref="Health"/> as three stacked regions:
    ///
    ///   [ ==== current ==== ][ ~~ recoverable ~~ ][ ---- gone ---- ]
    ///
    /// The grey span is the whole point of the readout. It has to be legible at a
    /// glance mid-fight, because it is the difference between "back off" and
    /// "press now" — and the fight is tuned expecting the player to press.
    ///
    /// Read per frame rather than driven off Health.Changed: the bar eases toward
    /// its target, so it needs a frame tick regardless and an event would only
    /// duplicate what Update already does.
    /// </summary>
    public class HealthBarView : MonoBehaviour
    {
        [SerializeField] private Health source;

        [Tooltip("Filled image for health actually held. Image Type must be Filled.")]
        [SerializeField] private Image currentFill;

        [Tooltip("Filled image drawn behind the current fill, out to current + " +
                 "recoverable. Sits between the solid bar and the empty track.")]
        [SerializeField] private Image recoverableFill;

        [Tooltip("Seconds the bar takes to catch up. Purely cosmetic easing — the " +
                 "underlying values are already correct the frame they change.")]
        [SerializeField] private float smoothing = 0.12f;

        float _shownCurrent, _shownRecoverable;

        void Awake()
        {
            if (source == null) source = GetComponentInParent<Health>();
        }

        void OnEnable()
        {
            if (source == null) return;
            // Start already showing the truth, so the bar does not sweep up from
            // empty on the first frame of the fight.
            _shownCurrent = source.CurrentNormalised;
            _shownRecoverable = source.RecoverableNormalised;
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
            if (recoverableFill != null)
                recoverableFill.fillAmount = Mathf.Max(_shownRecoverable, _shownCurrent);
        }

        public void Bind(Health health)
        {
            source = health;
            if (source == null) return;
            _shownCurrent = source.CurrentNormalised;
            _shownRecoverable = source.RecoverableNormalised;
        }
    }
}
