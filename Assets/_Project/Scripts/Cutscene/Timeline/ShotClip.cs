using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Pickleball.Cutscene.Timeline
{
    /// <summary>
    /// One shot from the shot list on the timeline: its id, the framing note, the lens and the
    /// subject. The clip's start and duration are the shot's cut points. This is the contract the
    /// cameras workstream builds from: it reads every <see cref="ShotClip"/> on the
    /// <see cref="ShotTrack"/> and places a camera per shot over the same time range. Until then the
    /// track can drive a plain Camera to a placeholder framing of <see cref="Subject"/>.
    /// </summary>
    [Serializable]
    public sealed class ShotClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("Shot id, e.g. S2_SH05.")]
        [SerializeField] private string shotId = string.Empty;

        [Tooltip("The shot list's framing, condensed.")]
        [TextArea(2, 4)]
        [SerializeField] private string framing = string.Empty;

        [Tooltip("Vertical FOV at the start of the shot, in degrees.")]
        [Range(5f, 120f)]
        [SerializeField] private float lensStart = 40f;

        [Tooltip("Vertical FOV at the end of the shot, in degrees (equal to the start unless it zooms).")]
        [Range(5f, 120f)]
        [SerializeField] private float lensEnd = 40f;

        [Tooltip("The subject anchor the shot frames (MC.Head, Birdy.Head, MC.Blade, the propeller...).")]
        [SerializeField] private ExposedReference<Transform> subject;

        [Tooltip("The subject's actor root, whose facing orients the placeholder camera. Optional.")]
        [SerializeField] private ExposedReference<Transform> subjectRoot;

        [Tooltip("The subject reference as the plan writes it (@MC.Head, @BIRDY.Head, ...), for readers " +
                 "that want the anchor name rather than the transform.")]
        [SerializeField] private string subjectName = string.Empty;

        [Tooltip("Placeholder camera distance from the subject, in metres. Ignored once real cameras exist.")]
        [Min(0.05f)]
        [SerializeField] private float placeholderDistance = 3f;

        /// <summary>Shot id.</summary>
        public string ShotId => shotId;

        /// <summary>Framing note.</summary>
        public string Framing => framing;

        /// <summary>Vertical FOV at the start, degrees.</summary>
        public float LensStart => lensStart;

        /// <summary>Vertical FOV at the end, degrees.</summary>
        public float LensEnd => lensEnd;

        /// <summary>The subject transform as an exposed reference; resolve it through the PlayableDirector.</summary>
        public ExposedReference<Transform> Subject => subject;

        /// <summary>The subject's actor root as an exposed reference.</summary>
        public ExposedReference<Transform> SubjectRoot => subjectRoot;

        /// <summary>The plan's subject string (<c>@MC.Head</c>).</summary>
        public string SubjectName => subjectName;

        /// <summary>Placeholder distance, metres.</summary>
        public float PlaceholderDistance => placeholderDistance;

        /// <inheritdoc />
        public ClipCaps clipCaps => ClipCaps.None;

        /// <summary>Builder entry point.</summary>
        public void Configure(string id, string framingNote, float fovStart, float fovEnd,
            ExposedReference<Transform> subjectRef, ExposedReference<Transform> rootRef, string subjectRefName,
            float distance)
        {
            shotId = id;
            framing = framingNote;
            lensStart = fovStart;
            lensEnd = fovEnd;
            subject = subjectRef;
            subjectRoot = rootRef;
            subjectName = subjectRefName;
            placeholderDistance = Mathf.Max(0.05f, distance);
        }

        /// <inheritdoc />
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<ShotBehaviour>.Create(graph);
            var b = playable.GetBehaviour();
            var resolver = graph.GetResolver();
            b.Subject = subject.Resolve(resolver);
            b.SubjectRoot = subjectRoot.Resolve(resolver);
            b.Distance = placeholderDistance;
            b.LensStart = lensStart;
            b.LensEnd = lensEnd;
            return playable;
        }
    }
}
