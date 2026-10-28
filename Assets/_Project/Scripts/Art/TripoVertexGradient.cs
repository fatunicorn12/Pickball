using System.Globalization;
using UnityEngine;

namespace Pickleball.Art
{
    /// <summary>
    /// A bottom-to-top colour gradient baked into a Tripo mesh's vertex colours at
    /// import, for shading a texture cannot give: the thundercloud came back a
    /// uniform slate and needs a light top over a dark underside. The material
    /// multiplies the vertex colour in (URP Particles/Lit does; URP Lit ignores
    /// vertex colour), so the gradient is material-side and the texture is left
    /// alone.
    ///
    /// Like <see cref="TripoTextureGrade"/>, it travels in the importer's userData,
    /// so changing it reimports the model and the FBX is never modified.
    /// </summary>
    public readonly struct TripoVertexGradient
    {
        const string Prefix = "tripo-gradient:";

        /// <summary>Multiplier at the mesh's lowest point.</summary>
        public readonly Color Bottom;

        /// <summary>Multiplier at the mesh's highest point.</summary>
        public readonly Color Top;

        /// <summary>Creates a gradient.</summary>
        public TripoVertexGradient(Color bottom, Color top)
        {
            Bottom = bottom;
            Top = top;
        }

        /// <summary>White at both ends: no gradient.</summary>
        public bool IsNeutral => IsWhite(Bottom) && IsWhite(Top);

        /// <summary>The multiplier at a normalised height (0 = lowest vertex, 1 =
        /// highest). Smoothstepped, so the change happens across the middle of the
        /// mesh and the extremes hold their colours.</summary>
        public Color Evaluate(float height01)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(height01));
            return Color.Lerp(Bottom, Top, t);
        }

        /// <summary>The importer userData string; empty when neutral.</summary>
        public string Encode() => IsNeutral ? "" : Prefix + Format(Bottom) + ";" + Format(Top);

        /// <summary>Reads a gradient written by <see cref="Encode"/>; anything else is neutral.</summary>
        public static bool TryDecode(string userData, out TripoVertexGradient gradient)
        {
            gradient = new TripoVertexGradient(Color.white, Color.white);
            if (string.IsNullOrEmpty(userData) || !userData.StartsWith(Prefix)) return false;
            var parts = userData.Substring(Prefix.Length).Split(';');
            if (parts.Length != 2 || !TryParse(parts[0], out var bottom) || !TryParse(parts[1], out var top))
                return false;
            gradient = new TripoVertexGradient(bottom, top);
            return !gradient.IsNeutral;
        }

        static bool IsWhite(Color c) =>
            Mathf.Approximately(c.r, 1f) && Mathf.Approximately(c.g, 1f) && Mathf.Approximately(c.b, 1f);

        static string Format(Color c) =>
            c.r.ToString("R", CultureInfo.InvariantCulture) + "," +
            c.g.ToString("R", CultureInfo.InvariantCulture) + "," +
            c.b.ToString("R", CultureInfo.InvariantCulture);

        static bool TryParse(string s, out Color c)
        {
            c = Color.white;
            var v = s.Split(',');
            if (v.Length != 3) return false;
            var f = new float[3];
            for (int i = 0; i < 3; i++)
                if (!float.TryParse(v[i], NumberStyles.Float, CultureInfo.InvariantCulture, out f[i])) return false;
            c = new Color(f[0], f[1], f[2], 1f);
            return true;
        }
    }
}
