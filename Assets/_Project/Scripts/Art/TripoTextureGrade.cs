using System.Globalization;
using UnityEngine;

namespace Pickleball.Art
{
    /// <summary>
    /// A colour correction baked into a Tripo texture at import: desaturate, then
    /// scale brightness. It exists because Tripo paints what it paints (the
    /// "white marble" bust came back beige twice), and a grade at import is free
    /// where a regeneration is not. The source JPG is never changed; the grade
    /// lives in the texture importer's userData, so it reapplies on every import
    /// and is undone by setting it back to neutral.
    /// </summary>
    public readonly struct TripoTextureGrade
    {
        const string Prefix = "tripo-grade:";

        /// <summary>0 = greyscale, 1 = the painted colours unchanged.</summary>
        public readonly float Saturation;

        /// <summary>Brightness multiplier after desaturating; 1 = unchanged.</summary>
        public readonly float Gain;

        /// <summary>Creates a grade.</summary>
        public TripoTextureGrade(float saturation, float gain)
        {
            Saturation = saturation;
            Gain = gain;
        }

        /// <summary>Whether this grade changes anything.</summary>
        public bool IsNeutral => Mathf.Approximately(Saturation, 1f) && Mathf.Approximately(Gain, 1f);

        /// <summary>The graded colour (sRGB values in, sRGB values out; alpha kept).</summary>
        public Color Apply(Color c)
        {
            float luma = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            return new Color(Mathf.Clamp01(Mathf.LerpUnclamped(luma, c.r, Saturation) * Gain),
                             Mathf.Clamp01(Mathf.LerpUnclamped(luma, c.g, Saturation) * Gain),
                             Mathf.Clamp01(Mathf.LerpUnclamped(luma, c.b, Saturation) * Gain),
                             c.a);
        }

        /// <summary>The importer userData string; empty for a neutral grade.</summary>
        public string Encode() => IsNeutral
            ? ""
            : Prefix + Saturation.ToString("R", CultureInfo.InvariantCulture) + ";" +
              Gain.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>Reads a grade written by <see cref="Encode"/>; anything else is neutral.</summary>
        public static bool TryDecode(string userData, out TripoTextureGrade grade)
        {
            grade = new TripoTextureGrade(1f, 1f);
            if (string.IsNullOrEmpty(userData) || !userData.StartsWith(Prefix)) return false;
            var parts = userData.Substring(Prefix.Length).Split(';');
            if (parts.Length != 2 ||
                !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float s) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float g))
                return false;
            grade = new TripoTextureGrade(s, g);
            return !grade.IsNeutral;
        }
    }
}
