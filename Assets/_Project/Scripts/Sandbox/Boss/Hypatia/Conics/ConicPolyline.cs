using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Conics
{
    /// <summary>
    /// A path precomputed once into fixed buffers and walked by arc length, so the
    /// flyby shot moves at a constant speed along its hyperbola with no per-frame
    /// maths beyond a binary search and a lerp, and nothing allocated after the
    /// buffer exists. Read-only to everyone but its filler, so presentation can draw
    /// the same polyline the shot follows.
    /// </summary>
    public sealed class ConicPolyline
    {
        /// <summary>Points the flyby path is sampled into: enough that a 16 m arm
        /// at e near 1 reads as a curve, few enough to fill in a frame.</summary>
        public const int DefaultCapacity = 96;

        readonly Vector3[] _points;
        readonly float[] _cumulative;

        public ConicPolyline(int capacity = DefaultCapacity)
        {
            capacity = Mathf.Max(2, capacity);
            _points = new Vector3[capacity];
            _cumulative = new float[capacity];
        }

        /// <summary>Most points this buffer holds.</summary>
        public int Capacity => _points.Length;

        /// <summary>Points now in the path.</summary>
        public int Count { get; private set; }

        /// <summary>Total arc length, m.</summary>
        public float Length => Count > 0 ? _cumulative[Count - 1] : 0f;

        /// <summary>The i-th point, 0 ≤ i &lt; <see cref="Count"/>.</summary>
        public Vector3 this[int index] => _points[index];

        /// <summary>Empty the path (keeps the buffers).</summary>
        public void Clear() => Count = 0;

        /// <summary>Append a point. False (and ignored) once full.</summary>
        public bool Add(Vector3 point)
        {
            if (Count >= _points.Length) return false;
            _cumulative[Count] = Count == 0 ? 0f : _cumulative[Count - 1] + Vector3.Distance(_points[Count - 1], point);
            _points[Count] = point;
            Count++;
            return true;
        }

        /// <summary>The point <paramref name="distance"/> metres along the path,
        /// clamped to its ends.</summary>
        public Vector3 PointAt(float distance)
        {
            if (Count == 0) return Vector3.zero;
            if (Count == 1 || distance <= 0f) return _points[0];
            if (distance >= Length) return _points[Count - 1];

            int i = SegmentAt(distance);
            float span = _cumulative[i + 1] - _cumulative[i];
            float t = span > 1e-6f ? (distance - _cumulative[i]) / span : 0f;
            return Vector3.LerpUnclamped(_points[i], _points[i + 1], t);
        }

        /// <summary>Unit travel direction at <paramref name="distance"/> metres.</summary>
        public Vector3 DirectionAt(float distance)
        {
            if (Count < 2) return Vector3.forward;
            int i = distance <= 0f ? 0 : distance >= Length ? Count - 2 : SegmentAt(distance);
            Vector3 d = _points[i + 1] - _points[i];
            return d.sqrMagnitude > 1e-12f ? d.normalized : Vector3.forward;
        }

        /// <summary>Copy the points into <paramref name="into"/> (cleared first), for
        /// presentation that wants its own list for a LineRenderer.</summary>
        public void CopyTo(List<Vector3> into)
        {
            into.Clear();
            for (int i = 0; i < Count; i++) into.Add(_points[i]);
        }

        // Index i of the segment [i, i+1] containing the distance (0 < d < Length).
        int SegmentAt(float distance)
        {
            int lo = 0, hi = Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (_cumulative[mid] <= distance) lo = mid; else hi = mid;
            }
            return lo;
        }
    }
}
