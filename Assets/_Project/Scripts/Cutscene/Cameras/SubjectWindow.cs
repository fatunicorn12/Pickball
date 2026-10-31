using System;
using UnityEngine;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// A subject the framing check holds to the safe frame, and when: the shot list's "Subject:
    /// MC.Head for 0–1.6 s, then Birdy.Head". INSERT and NO-CHAR shots name an object anchor.
    /// </summary>
    [Serializable]
    public struct SubjectWindow
    {
        [Tooltip("The anchor that must stay inside the 90% safe frame.")]
        public RigBasis subject;

        [Tooltip("From when.")]
        public TimeRef from;

        [Tooltip("Until when.")]
        public TimeRef to;
    }
}
