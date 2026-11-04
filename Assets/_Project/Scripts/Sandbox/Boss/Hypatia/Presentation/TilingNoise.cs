namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// Fractal value noise that wraps seamlessly in both directions, for the
    /// textures the fight's shaders scroll and erode with. It has to tile: a
    /// shader wraps it round a circle, and a seam would show as a hard spoke.
    /// Deterministic per seed, so regenerating a texture from it changes nothing.
    ///
    /// Plain C#, with no Unity types, so it can be tested without a texture. Called
    /// from Setup Hypatia Fight, never per frame.
    /// </summary>
    public static class TilingNoise
    {
        /// <summary>
        /// A <paramref name="size"/> × <paramref name="size"/> field of noise in
        /// [0, 1], row by row, stretched to fill that range. <paramref name="cells"/>
        /// is the coarsest feature count across; each of the
        /// <paramref name="octaves"/> doubles it at half the strength.
        /// </summary>
        public static float[] Generate(int size, int seed, int cells, int octaves)
        {
            var field = new float[size * size];
            float strength = 1f;
            for (int octave = 0; octave < octaves; octave++)
            {
                int count = cells << octave;
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    field[y * size + x] += strength * Sample((float)x / size * count, (float)y / size * count,
                                                             count, seed + octave * 101);
                strength *= 0.5f;
            }

            float min = float.MaxValue, max = float.MinValue;
            foreach (float v in field)
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }
            float range = max - min;
            if (range <= 0f) return field;
            for (int i = 0; i < field.Length; i++) field[i] = (field[i] - min) / range;
            return field;
        }

        static float Sample(float x, float y, int period, int seed)
        {
            int x0 = (int)x, y0 = (int)y;
            float tx = Fade(x - x0), ty = Fade(y - y0);
            int x1 = (x0 + 1) % period, y1 = (y0 + 1) % period;
            x0 %= period;
            y0 %= period;
            float top = Lerp(Lattice(x0, y0, seed), Lattice(x1, y0, seed), tx);
            float bottom = Lerp(Lattice(x0, y1, seed), Lattice(x1, y1, seed), tx);
            return Lerp(top, bottom, ty);
        }

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>A repeatable pseudo-random value in [0, 1] for one lattice point.</summary>
        static float Lattice(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2147483647u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }
    }
}
