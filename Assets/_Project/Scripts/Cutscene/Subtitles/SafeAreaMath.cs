using UnityEngine;

namespace Pickleball.Cutscene.Subtitles
{
    /// <summary>
    /// Converts <c>Screen.safeArea</c> (pixels) into RectTransform anchors (0..1), so a panel
    /// stretched between them stays clear of notches, rounded corners and the home indicator at any
    /// resolution, without knowing the canvas scale.
    /// </summary>
    public static class SafeAreaMath
    {
        /// <summary>
        /// Anchors for <paramref name="safeArea"/> on a <paramref name="screenWidth"/> ×
        /// <paramref name="screenHeight"/> pixel screen. A degenerate screen or safe area gives the
        /// full screen, so a bad report never hides the subtitles.
        /// </summary>
        public static void Anchors(Rect safeArea, int screenWidth, int screenHeight,
                                   out Vector2 anchorMin, out Vector2 anchorMax)
        {
            if (screenWidth <= 0 || screenHeight <= 0 || safeArea.width <= 0f || safeArea.height <= 0f)
            {
                anchorMin = Vector2.zero;
                anchorMax = Vector2.one;
                return;
            }
            anchorMin = new Vector2(Mathf.Clamp01(safeArea.xMin / screenWidth),
                                    Mathf.Clamp01(safeArea.yMin / screenHeight));
            anchorMax = new Vector2(Mathf.Clamp01(safeArea.xMax / screenWidth),
                                    Mathf.Clamp01(safeArea.yMax / screenHeight));
        }
    }
}
