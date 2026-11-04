using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// A stylised moon as a function of direction from its centre: craters with
    /// raised rims, and broad dark seas. Setup Hypatia Fight bakes it into the
    /// texture the moon shader reads.
    ///
    /// It is defined on directions, not texture coordinates, so it has no seam and
    /// does not pinch at the poles however it is unwrapped. Deterministic per seed,
    /// so regenerating the texture changes nothing. Never called per frame.
    /// </summary>
    public sealed class MoonSurface
    {
        readonly struct Feature
        {
            public readonly Vector3 Centre;
            public readonly float Radius;   // radians
            public readonly float Strength;
            public readonly float CosReach; // cos of the furthest angle it affects

            public Feature(Vector3 centre, float radius, float strength, float reachFactor)
            {
                Centre = centre;
                Radius = radius;
                Strength = strength;
                CosReach = Mathf.Cos(Mathf.Min(Mathf.PI, radius * reachFactor));
            }
        }

        const float RimReach = 1.45f;
        const float SeaReach = 2.2f;

        readonly Feature[] _craters;
        readonly Feature[] _seas;

        /// <param name="seed">Same seed, same moon.</param>
        /// <param name="craterCount">Craters, mostly small with a few large.</param>
        /// <param name="seaCount">Broad dark patches.</param>
        public MoonSurface(int seed, int craterCount, int seaCount)
        {
            uint state = (uint)seed * 2654435761u + 1u;
            _craters = new Feature[Mathf.Max(0, craterCount)];
            for (int i = 0; i < _craters.Length; i++)
            {
                float size = Next(ref state);
                float radius = Mathf.Lerp(0.035f, 0.3f, size * size * size);
                _craters[i] = new Feature(NextDirection(ref state), radius, radius * 0.55f, RimReach);
            }
            _seas = new Feature[Mathf.Max(0, seaCount)];
            for (int i = 0; i < _seas.Length; i++)
            {
                float radius = Mathf.Lerp(0.35f, 0.8f, Next(ref state));
                _seas[i] = new Feature(NextDirection(ref state), radius, Mathf.Lerp(0.5f, 1f, Next(ref state)), SeaReach);
            }
        }

        /// <summary>The direction a longitude-latitude texture coordinate stands for:
        /// u wraps the equator, v runs south pole (0) to north pole (1). The moon
        /// shader inverts exactly this.</summary>
        public static Vector3 DirectionAt(float u, float v)
        {
            float longitude = (u - 0.5f) * 2f * Mathf.PI;
            float latitude = (v - 0.5f) * Mathf.PI;
            float c = Mathf.Cos(latitude);
            return new Vector3(c * Mathf.Cos(longitude), Mathf.Sin(latitude), c * Mathf.Sin(longitude));
        }

        /// <summary>Height at unit direction <paramref name="direction"/>: below 0 in
        /// a crater's bowl, above 0 on its rim, 0 on open ground.</summary>
        public float Height(Vector3 direction)
        {
            float height = 0f;
            foreach (Feature crater in _craters)
            {
                float cos = Vector3.Dot(direction, crater.Centre);
                if (cos < crater.CosReach) continue;
                float t = Mathf.Acos(Mathf.Min(1f, cos)) / crater.Radius;
                float rim = (t - 1f) / 0.22f;
                float shape = 0.28f * Mathf.Exp(-rim * rim);
                if (t < 1f) shape -= 1f - t * t;
                height += crater.Strength * shape;
            }
            return height;
        }

        /// <summary>Surface brightness, 0..1: dark in the seas and crater floors,
        /// brighter on crater rims.</summary>
        public float Albedo(Vector3 direction)
        {
            float sea = 0f;
            foreach (Feature patch in _seas)
            {
                float cos = Vector3.Dot(direction, patch.Centre);
                if (cos < patch.CosReach) continue;
                float t = Mathf.Acos(Mathf.Min(1f, cos)) / (patch.Radius * SeaReach);
                float u = 1f - t * t;
                sea = Mathf.Max(sea, patch.Strength * u * u);
            }

            float relief = 0f;
            foreach (Feature crater in _craters)
            {
                float cos = Vector3.Dot(direction, crater.Centre);
                if (cos < crater.CosReach) continue;
                float t = Mathf.Acos(Mathf.Min(1f, cos)) / crater.Radius;
                float rim = (t - 1f) / 0.22f;
                relief += 0.22f * Mathf.Exp(-rim * rim);
                if (t < 1f) relief -= 0.16f;
            }
            return Mathf.Clamp01(0.82f - 0.42f * sea + relief);
        }

        /// <summary>The bumped surface normal at <paramref name="direction"/>, unit
        /// length, tilted by the height's slope. Higher
        /// <paramref name="strength"/> = deeper-looking craters.</summary>
        public Vector3 Normal(Vector3 direction, float strength)
        {
            const float step = 0.012f;
            Vector3 helper = Mathf.Abs(direction.y) < 0.9f ? Vector3.up : Vector3.right;
            Vector3 east = Vector3.Cross(helper, direction).normalized;
            Vector3 north = Vector3.Cross(direction, east);

            float h = Height(direction);
            float slopeEast = (Height((direction + east * step).normalized) - h) / step;
            float slopeNorth = (Height((direction + north * step).normalized) - h) / step;
            return (direction - strength * (east * slopeEast + north * slopeNorth)).normalized;
        }

        static float Next(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0xFFFFFF) / (float)0xFFFFFF;
        }

        static Vector3 NextDirection(ref uint state)
        {
            float y = Next(ref state) * 2f - 1f;
            float angle = Next(ref state) * 2f * Mathf.PI;
            float c = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            return new Vector3(c * Mathf.Cos(angle), y, c * Mathf.Sin(angle));
        }
    }
}
