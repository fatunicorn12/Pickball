using Pickleball.Cutscene.Timeline;
using UnityEngine.Timeline;

namespace Pickleball.Cutscene.Cameras.Editor
{
    /// <summary>
    /// One shot as it sits on a generated timeline: its <see cref="ShotClip"/>, cut points and
    /// shot-local beat starts. Read from the pipeline's Shots track (the cut points) and its timing
    /// layout (the beats), so the cameras follow whatever the pipeline last built.
    /// </summary>
    public sealed class ShotSlot
    {
        /// <summary>Scene number.</summary>
        public int Scene;

        /// <summary>The scene's timeline.</summary>
        public TimelineAsset Timeline;

        /// <summary>Shot id.</summary>
        public string ShotId;

        /// <summary>The pipeline's clip.</summary>
        public ShotClip Clip;

        /// <summary>Start on the timeline, seconds.</summary>
        public double Start;

        /// <summary>Length, seconds.</summary>
        public double Duration;

        /// <summary>Shot-local beat starts, seconds.</summary>
        public float[] Beats;

        /// <summary>The shot list's lens at the start (from the ShotClip).</summary>
        public float LensStart;

        /// <summary>The shot list's lens at the end.</summary>
        public float LensEnd;
    }
}
