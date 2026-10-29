using Pickleball.Sandbox;
using TMPro;
using UnityEngine;

namespace Pickleball.UI
{
    /// <summary>
    /// Subtitle line for a boss's barks: shows what an <see cref="IBarkSource"/>
    /// (<see cref="NewtonBarks"/>, Hypatia's barks) says, then fades it out. Sits
    /// under the boss health bar so the voice reads as coming from the name above it.
    /// </summary>
    public class BarkView : MonoBehaviour
    {
        [Tooltip("A component implementing IBarkSource (NewtonBarks, HypatiaBarks).")]
        [SerializeField] private MonoBehaviour source;
        [SerializeField] private TMP_Text text;
        [SerializeField] private float fadeSeconds = 0.5f;

        float _hideAt;
        float _duration;
        IBarkSource _subscribed;

        void OnEnable()
        {
            if (text != null) text.alpha = 0f;
            if (source == null) return;
            _subscribed = source as IBarkSource;
            if (_subscribed != null) _subscribed.Spoke += Show;
            else Debug.LogWarning($"[BarkView] {name}: source {source.GetType().Name} is not an IBarkSource; " +
                                  "no lines will show.", this);
        }

        void OnDisable()
        {
            if (_subscribed != null) _subscribed.Spoke -= Show;
            _subscribed = null;
        }

        void Show(string line, float duration)
        {
            if (text == null) return;
            text.text = line;
            _duration = duration;
            _hideAt = Time.time + duration;
        }

        void Update()
        {
            if (text == null || _duration <= 0f) return;
            float left = _hideAt - Time.time;
            text.alpha = Mathf.Clamp01(left / Mathf.Max(fadeSeconds, 0.01f));
            if (left <= 0f) _duration = 0f;
        }
    }
}
