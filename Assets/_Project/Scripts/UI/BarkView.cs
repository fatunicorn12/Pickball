using Pickleball.Sandbox;
using TMPro;
using UnityEngine;

namespace Pickleball.UI
{
    /// <summary>
    /// Subtitle line for a boss's barks: shows what <see cref="NewtonBarks"/> says,
    /// then fades it out. Sits under the boss health bar so the voice reads as
    /// coming from the name above it.
    /// </summary>
    public class BarkView : MonoBehaviour
    {
        [SerializeField] private NewtonBarks source;
        [SerializeField] private TMP_Text text;
        [SerializeField] private float fadeSeconds = 0.5f;

        float _hideAt;
        float _duration;

        void OnEnable()
        {
            if (text != null) text.alpha = 0f;
            if (source != null) source.Spoke += Show;
        }

        void OnDisable()
        {
            if (source != null) source.Spoke -= Show;
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
