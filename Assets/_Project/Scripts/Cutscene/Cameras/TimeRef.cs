using System;
using UnityEngine;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// A time inside a shot written the way the shot list writes it: "at the start of OC_S3_14's
    /// audio", "0.5 s into the hold", "the end of the shot". It names one of the shot's duration
    /// beats (the timing plan's wordless beats, holds and lines, in order) plus seconds, so when real
    /// audio re-times a line every camera move inside the shot moves with it.
    /// </summary>
    [Serializable]
    public struct TimeRef
    {
        /// <summary>Beat index meaning "the shot's start".</summary>
        public const int ShotStart = -1;

        /// <summary>Beat index meaning "the shot's end".</summary>
        public const int ShotEnd = 99;

        [Tooltip("Beat index in the shot's duration breakdown (0 = first beat); -1 = the shot's " +
                 "start, 99 = its end.")]
        public int beat;

        [Tooltip("Seconds after (or, negative, before) that beat's start.")]
        public float at;

        /// <summary>Seconds after the shot starts.</summary>
        public static TimeRef Start(float at = 0f) => new TimeRef { beat = ShotStart, at = at };

        /// <summary>Seconds relative to the shot's end (usually negative or zero).</summary>
        public static TimeRef End(float at = 0f) => new TimeRef { beat = ShotEnd, at = at };

        /// <summary>Seconds after a beat starts.</summary>
        public static TimeRef Beat(int index, float at = 0f) => new TimeRef { beat = index, at = at };

        /// <summary>
        /// Shot-local seconds, clamped to the shot. <paramref name="beatStarts"/> are the beats'
        /// shot-local starts.
        /// </summary>
        public float Resolve(float[] beatStarts, float duration)
        {
            float t;
            if (beat < 0) t = 0f;
            else if (beatStarts != null && beat < beatStarts.Length) t = beatStarts[beat];
            else t = duration;
            return Mathf.Clamp(t + at, 0f, Mathf.Max(0f, duration));
        }
    }
}
