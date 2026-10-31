using UnityEngine;

namespace Pickleball.Cutscene.Subtitles
{
    /// <summary>
    /// Keeps its RectTransform stretched over <c>Screen.safeArea</c>, so everything laid out
    /// inside it (the subtitle card, the skip ring) clears a notch, rounded corners and the home
    /// indicator. The safe area changes on rotation, on a split-screen resize and when the Device
    /// Simulator switches phone, so it is re-checked every frame; the check compares two rects and
    /// two ints, so it allocates nothing, and the anchors are only written when something changed.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaPanel : MonoBehaviour
    {
        RectTransform _rect;
        Rect _applied;
        int _appliedWidth = -1;
        int _appliedHeight = -1;

        /// <summary>The safe area last applied, in screen pixels.</summary>
        public Rect AppliedSafeArea => _applied;

        void OnEnable()
        {
            _appliedWidth = _appliedHeight = -1;
            Refresh();
        }

        void LateUpdate()
        {
            Refresh();
        }

        /// <summary>Applies the real safe area if it, or the screen size, changed.</summary>
        public void Refresh()
        {
            Rect safe = Screen.safeArea;
            int w = Screen.width, h = Screen.height;
            if (safe == _applied && w == _appliedWidth && h == _appliedHeight) return;
            Apply(safe, w, h);
        }

        /// <summary>
        /// Stretches the panel over <paramref name="safeArea"/> on a screen of the given size. Public
        /// so editor previews and screenshots can simulate a notched phone; at runtime the next
        /// <see cref="Refresh"/> puts the real safe area back if it differs.
        /// </summary>
        public void Apply(Rect safeArea, int screenWidth, int screenHeight)
        {
            if (_rect == null) _rect = (RectTransform)transform;
            SafeAreaMath.Anchors(safeArea, screenWidth, screenHeight, out var min, out var max);
            _rect.anchorMin = min;
            _rect.anchorMax = max;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
            _applied = safeArea;
            _appliedWidth = screenWidth;
            _appliedHeight = screenHeight;
        }
    }
}
