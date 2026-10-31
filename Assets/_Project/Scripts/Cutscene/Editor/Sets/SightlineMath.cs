using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Cutscene.Editor.Sets
{
    /// <summary>
    /// Segment-versus-box visibility, for checking camera sightlines against the greybox without
    /// loading a scene (slab method).
    /// </summary>
    public static class SightlineMath
    {
        /// <summary>True when the segment from <paramref name="a"/> to <paramref name="b"/> passes
        /// through the box (touching counts).</summary>
        public static bool SegmentHitsBox(Vector3 a, Vector3 b, Vector3 min, Vector3 max)
        {
            Vector3 d = b - a;
            float tMin = 0f, tMax = 1f;
            for (int axis = 0; axis < 3; axis++)
            {
                float origin = a[axis], dir = d[axis];
                if (Mathf.Abs(dir) < 1e-7f)
                {
                    if (origin < min[axis] || origin > max[axis]) return false;
                    continue;
                }
                float t0 = (min[axis] - origin) / dir, t1 = (max[axis] - origin) / dir;
                if (t0 > t1) (t0, t1) = (t1, t0);
                tMin = Mathf.Max(tMin, t0);
                tMax = Mathf.Min(tMax, t1);
                if (tMin > tMax) return false;
            }
            return true;
        }

        /// <summary>The name of the first occluder the segment hits, or null if the view is clear.</summary>
        public static string FirstBlocker(Vector3 from, Vector3 to, IReadOnlyList<(Vector3 min, Vector3 max, string name)> occluders)
        {
            for (int i = 0; i < occluders.Count; i++)
            {
                if (SegmentHitsBox(from, to, occluders[i].min, occluders[i].max)) return occluders[i].name;
            }
            return null;
        }
    }
}
