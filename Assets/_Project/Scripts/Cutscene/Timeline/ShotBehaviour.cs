using UnityEngine;
using UnityEngine.Playables;

namespace Pickleball.Cutscene.Timeline
{
    /// <summary>Runtime half of a <see cref="ShotClip"/>: the resolved subject and the lens, cached at graph build.</summary>
    public sealed class ShotBehaviour : PlayableBehaviour
    {
        /// <summary>Subject transform (an anchor, a prop, a marker), or null.</summary>
        public Transform Subject;

        /// <summary>The actor root whose yaw orients the placeholder (falls back to the subject).</summary>
        public Transform SubjectRoot;

        /// <summary>Placeholder distance, metres.</summary>
        public float Distance;

        /// <summary>Vertical FOV at the shot's start, degrees.</summary>
        public float LensStart;

        /// <summary>Vertical FOV at the shot's end, degrees.</summary>
        public float LensEnd;
    }
}
