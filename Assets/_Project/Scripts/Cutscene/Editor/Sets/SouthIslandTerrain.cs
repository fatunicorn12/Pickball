using UnityEngine;

namespace Pickleball.Cutscene.Editor.Sets
{
    /// <summary>
    /// The south island the three walkable sets stand on: its outline, its ground height and its
    /// painted ground colour, as pure functions of (x, z). The set builder meshes it in three
    /// bands (OuterShrine z &lt; -86, OlivePath -86..-57, SouthCrest ≥ -57) from these same
    /// functions, so the bands meet without seams; markers are dropped onto <see cref="Height"/>.
    ///
    /// <b>Shape:</b> a flat meadow round the shrine (y 0), an olive hill rising north along the
    /// path to a level crest band on the plateau's south rim (y 11), and a ridge falling east
    /// along the rim to the parapet's height (0.9 m at x 26), which is the way down into the
    /// Gymnasium. The hill's flanks drop up to 35% away from the path so it reads as a rise,
    /// not a ramp.
    /// </summary>
    public static class SouthIslandTerrain
    {
        /// <summary>Band edges (z) between the three set prefabs.</summary>
        public const float ShrineBandMaxZ = -86f, PathBandMaxZ = -57f;
        /// <summary>The island's north edge: 5 cm south of the plateau's south parapet.</summary>
        public const float NorthEdgeZ = -50.45f;
        /// <summary>The bottom of the island's rock skirt, metres.</summary>
        public const float SkirtBottom = -2.5f;

        private const float CrossDrop = 0.35f, CrossDropStart = 6f, CrossDropEnd = 16f;
        private const float HillEastFadeStart = 5f, HillEastFadeEnd = 10f;
        private const float RidgeTopX = 2f, RidgeFootX = 26f, RidgeMaskSouth = -58.5f, RidgeMaskNorth = -55.5f;

        // The hill's rise along z, meadow (y 0) to crest (y 11); monotone, so no overshoot. The
        // walked stretch (Path_Track_A at z -84 to B at -60) climbs evenly at about 18-20°; it
        // steepens only past B, where scene 4 has cut away.
        private static readonly HeightProfile Rise = new(
            new[] { -85f, -82f, -72f, -62f, -57f, -54.5f, -53f, NorthEdgeZ },
            new[] { 0f, 0.4f, 3.6f, 6.9f, 9.3f, 10.6f, SetLayout.CrestHeight, SetLayout.CrestHeight });

        /// <summary>Ground height at (x, z), metres. Defined everywhere; only meaningful inside the outline.</summary>
        public static float Height(float x, float z)
        {
            float off = Mathf.Abs(x - PathX(z));
            // The hill fades out east of the slope, so the ridge alone shapes the way down.
            float rise = Rise.Evaluate(z);
            float hill = rise * (1f - CrossDrop * Smooth(CrossDropStart, CrossDropEnd, off))
                         * (1f - Smooth(HillEastFadeStart, HillEastFadeEnd, x));
            // Along the rim the crest keeps its full height west of the path and falls east to
            // the parapet; it never rises above the hill's own profile, so the path stays smooth.
            float ridge = Mathf.Lerp(rise, SetLayout.RidgeFootHeight, Smooth(RidgeTopX, RidgeFootX, x))
                          * Smooth(RidgeMaskSouth, RidgeMaskNorth, z);
            return Mathf.Max(hill, ridge);
        }

        /// <summary>The surface normal at (x, z), from the height function (central differences).</summary>
        public static Vector3 Normal(float x, float z)
        {
            const float e = 0.05f;
            float dx = (Height(x + e, z) - Height(x - e, z)) / (2f * e);
            float dz = (Height(x, z + e) - Height(x, z - e)) / (2f * e);
            return new Vector3(-dx, 1f, -dz).normalized;
        }

        /// <summary>Slope at (x, z), degrees.</summary>
        public static float SlopeDegrees(float x, float z) => Vector3.Angle(Normal(x, z), Vector3.up);

        /// <summary>The path's centreline x at z (piecewise linear through SetLayout.PathPoints).</summary>
        public static float PathX(float z)
        {
            Vector2[] p = SetLayout.PathPoints;
            if (z <= p[0].y) return p[0].x;
            for (int i = 0; i < p.Length - 1; i++)
            {
                if (z <= p[i + 1].y) return Mathf.Lerp(p[i].x, p[i + 1].x, Mathf.InverseLerp(p[i].y, p[i + 1].y, z));
            }
            return p[p.Length - 1].x;
        }

        /// <summary>
        /// Signed distance to the island's outline, metres (negative inside): the slope (with the
        /// propylon and steps on its south-west), the round shrine lobe between Delta islets 2
        /// and 3, and the crest band along the rim with its ridge east to x 26.5.
        /// </summary>
        public static float SignedDistance(float x, float z)
        {
            float slope = RoundedBox(x, z, -17.5f, 7.5f, -90f, NorthEdgeZ, 2.5f);
            float lobe = new Vector2(x - SetLayout.ShrineCentre.x, z - SetLayout.ShrineCentre.z).magnitude - 7.5f;
            float crest = RoundedBox(x, z, -17.5f, 26.5f, -56.5f, NorthEdgeZ, 1.5f);
            return Mathf.Min(slope, Mathf.Min(lobe, crest));
        }

        /// <summary>True on the north edge, where the island meets the plateau (no fence, no rim wall).</summary>
        public static bool IsNorthEdge(float z) => z > NorthEdgeZ - 0.2f;

        /// <summary>
        /// The ground's painted colour: saturated meadow green round the shrine, warmer dry grass
        /// up the olive slope, fresh green on the crest, with large soft patches (no fine noise).
        /// </summary>
        public static Color GroundColour(float x, float z)
        {
            float h = Height(x, z);
            var meadow = new Color(0.42f, 0.69f, 0.27f);
            var slope = new Color(0.64f, 0.67f, 0.33f);
            var crest = new Color(0.46f, 0.71f, 0.29f);
            float up = Mathf.Clamp01(h / 6f);
            Color c = Color.Lerp(meadow, slope, up);
            c = Color.Lerp(c, crest, Smooth(9.5f, 11f, h));
            float patch = Noise(x * 0.11f, z * 0.11f) - 0.5f;
            float warm = Noise(x * 0.05f + 17.3f, z * 0.05f - 4.1f) - 0.5f;
            c = new Color(c.r * (1f + patch * 0.18f) + warm * 0.08f, c.g * (1f + patch * 0.14f), c.b * (1f + patch * 0.1f));
            return c;
        }

        /// <summary>Smoothstep from <paramref name="a"/> to <paramref name="b"/> (either order).</summary>
        public static float Smooth(float a, float b, float v)
        {
            float t = Mathf.Clamp01((v - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Smooth value noise, 0..1, deterministic (no Random state).</summary>
        public static float Noise(float x, float z)
        {
            int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
            float fx = x - ix, fz = z - iz;
            fx = fx * fx * (3f - 2f * fx);
            fz = fz * fz * (3f - 2f * fz);
            float a = Hash(ix, iz), b = Hash(ix + 1, iz), c = Hash(ix, iz + 1), d = Hash(ix + 1, iz + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
        }

        /// <summary>Integer hash to 0..1.</summary>
        public static float Hash(int x, int z)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)z * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        private static float RoundedBox(float x, float z, float x0, float x1, float z0, float z1, float round)
        {
            float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
            float hx = (x1 - x0) * 0.5f - round, hz = (z1 - z0) * 0.5f - round;
            float qx = Mathf.Abs(x - cx) - hx, qz = Mathf.Abs(z - cz) - hz;
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qz, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qz), 0f) - round;
        }
    }
}
