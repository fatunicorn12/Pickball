using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Pickleball.Cutscene.Timeline
{
    /// <summary>
    /// The shot list on the timeline: one <see cref="ShotClip"/> per shot, in order, covering the
    /// scene end to end. The cameras workstream reads it to place a camera per shot. Bound to the
    /// main Camera so that, before real cameras exist, it can drive a placeholder framing; the
    /// camera builder sets <see cref="DrivePlaceholderCamera"/> to false when it takes over.
    /// </summary>
    [TrackColor(0.35f, 0.6f, 0.95f)]
    [TrackClipType(typeof(ShotClip))]
    [TrackBindingType(typeof(Camera))]
    public sealed class ShotTrack : TrackAsset
    {
        [Tooltip("Move the bound Camera to a placeholder framing of each shot's subject. Turn off " +
                 "once a camera system (Cinemachine) owns the camera.")]
        [SerializeField] private bool drivePlaceholderCamera = true;

        /// <summary>Whether the track drives the bound camera to placeholder framings.</summary>
        public bool DrivePlaceholderCamera
        {
            get => drivePlaceholderCamera;
            set => drivePlaceholderCamera = value;
        }

        /// <inheritdoc />
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            var mixer = ScriptPlayable<ShotMixerBehaviour>.Create(graph, inputCount);
            mixer.GetBehaviour().Drive = drivePlaceholderCamera;
            return mixer;
        }
    }
}
