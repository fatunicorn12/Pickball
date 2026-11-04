using UnityEngine;

namespace Pickleball.Rendering
{
    /// <summary>
    /// The sums behind the screen outline, kept apart from the render pass so they can be tested
    /// without a GPU. Width is authored for a 1080p screen and scaled here, so a line takes up the
    /// same share of the screen on every device instead of looking thinner on a sharper phone.
    /// </summary>
    public static class OutlineMath
    {
        /// <summary>The screen height, in pixels, that authored line widths refer to.</summary>
        public const float ReferenceHeight = 1080f;

        /// <summary>Below this the sample ring collapses onto the pixel itself and draws nothing.</summary>
        public const float MinRadiusPixels = 0.5f;

        /// <summary>The smallest gap kept between fade start and end, so the shader never divides by zero.</summary>
        public const float MinFadeSpan = 0.01f;

        /// <summary>
        /// How far from a pixel, in pixels, the shader looks for an edge. The line straddles the edge,
        /// drawing on both sides of it, so the radius is half the visible width.
        /// </summary>
        /// <param name="widthAt1080p">The whole line's width on a 1080p screen, in pixels.</param>
        /// <param name="screenHeight">The height of the camera's target, in pixels, after render scale.</param>
        public static float RadiusPixels(float widthAt1080p, int screenHeight)
        {
            float radius = 0.5f * widthAt1080p * screenHeight / ReferenceHeight;
            return Mathf.Max(MinRadiusPixels, radius);
        }

        /// <summary>
        /// The distance range the line fades over, packed for the shader as x = start and y = end.
        /// Guarantees end is beyond start, even when the two are set the wrong way round.
        /// </summary>
        /// <param name="start">Where the line starts to fade, in metres from the camera.</param>
        /// <param name="end">Where the line has gone completely, in metres from the camera.</param>
        public static Vector2 FadeRange(float start, float end)
        {
            float safeStart = Mathf.Max(0f, start);
            return new Vector2(safeStart, Mathf.Max(end, safeStart + MinFadeSpan));
        }
    }
}
