using System;
using UnityEngine;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>A span of a shot, for rules that apply only part of the time (the allowed Birdy TCUs).</summary>
    [Serializable]
    public struct TimeWindow
    {
        [Tooltip("From when.")]
        public TimeRef from;

        [Tooltip("Until when.")]
        public TimeRef to;

        /// <summary>A window over the whole shot.</summary>
        public static TimeWindow Whole => new TimeWindow { from = TimeRef.Start(), to = TimeRef.End() };
    }
}
