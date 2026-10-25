using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// The recent past of one object: timestamped positions, oldest first.
    ///
    /// Plain C# rather than a component, so it can outlive the object that wrote
    /// it. An apple that splats has stopped existing, but the stretch of
    /// spacetime it passed through has not, and the 4D view should still show
    /// it. <see cref="WorldlineRecorder"/> hands its history over when it dies
    /// for exactly that reason.
    /// </summary>
    public class WorldlineHistory
    {
        struct Sample
        {
            public Vector3 Position;
            public float Time;
        }

        readonly List<Sample> _samples = new();

        /// <summary>How far back the history reaches, in seconds.</summary>
        public float Span { get; }

        /// <summary>Movement that forces a new sample, in metres.</summary>
        public float MinSpacing { get; }

        /// <summary>
        /// Time that forces a new sample even if the object has not moved.
        ///
        /// Spacing alone is not enough. A stationary object would write one
        /// sample and then nothing, so its history would age out and vanish. With
        /// a time axis in the projection, standing still is meant to draw a
        /// vertical pillar, and that pillar needs samples all the way up it.
        /// </summary>
        public float MaxInterval { get; }

        public int Count => _samples.Count;
        public float OldestTime => _samples.Count > 0 ? _samples[0].Time : float.NaN;
        public float NewestTime => _samples.Count > 0 ? _samples[^1].Time : float.NaN;
        public Vector3 NewestPosition => _samples.Count > 0 ? _samples[^1].Position : Vector3.zero;

        public WorldlineHistory(float span, float minSpacing, float maxInterval)
        {
            Span = Mathf.Max(span, 0.01f);
            MinSpacing = Mathf.Max(minSpacing, 0f);
            MaxInterval = Mathf.Max(maxInterval, 0.001f);
        }

        /// <summary>Adds a sample if the object has moved far enough, or enough
        /// time has passed, since the last one. Returns whether it was kept.</summary>
        public bool Record(Vector3 position, float time)
        {
            if (_samples.Count > 0)
            {
                Sample last = _samples[^1];
                bool moved = (position - last.Position).sqrMagnitude >= MinSpacing * MinSpacing;
                bool waited = time - last.Time >= MaxInterval;
                if (!moved && !waited) return false;
            }
            _samples.Add(new Sample { Position = position, Time = time });
            return true;
        }

        /// <summary>Forgets everything older than <see cref="Span"/>. An ended
        /// history keeps losing samples this way until it is empty, which is how
        /// a dead object's worldline expires.</summary>
        public void Prune(float now)
        {
            float cutoff = now - Span;
            int drop = 0;
            while (drop < _samples.Count && _samples[drop].Time < cutoff) drop++;
            if (drop > 0) _samples.RemoveRange(0, drop);
        }

        /// <summary>
        /// The history as a path through space, ready to be meshed.
        ///
        /// <paramref name="timeAxis"/> is the spacetime-diagram projection: each
        /// sample is moved by <c>timeAxis × (sampleTime − now)</c>, metres per
        /// second of age. The present stays where the object really is, and the
        /// past trails off against the axis. A zero axis gives the plain spatial
        /// trace. An upward axis turns a back-and-forth platform into a zigzag
        /// ramp, a circling one into a spiral staircase, and something standing
        /// still into a pillar. The <i>shape</i> of the motion becomes terrain.
        ///
        /// Long histories are thinned evenly to <paramref name="maxPoints"/>, and
        /// both ends are always kept so the line still reaches the present.
        /// </summary>
        public void Project(float now, Vector3 timeAxis, int maxPoints, List<Vector3> into)
        {
            into.Clear();
            int n = _samples.Count;
            if (n == 0) return;

            int m = maxPoints < 2 ? n : Mathf.Min(n, maxPoints);
            for (int k = 0; k < m; k++)
            {
                int i = m == 1 ? 0 : Mathf.RoundToInt(k * (n - 1) / (float)(m - 1));
                Sample s = _samples[i];
                into.Add(s.Position + timeAxis * (s.Time - now));
            }
        }
    }
}
