using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Pickleball.Cutscene.Timeline
{
    /// <summary>
    /// Blocking for one character: moves and turns the bound <see cref="CutsceneActor"/>'s root
    /// between marker-relative poses (MC_Start, MC_Handover, the Path_Track walk, Crest_View, ...).
    /// The gameplay Player can be bound: its CharacterController is handled by the mixer.
    /// </summary>
    [TrackColor(0.35f, 0.75f, 0.45f)]
    [TrackClipType(typeof(ActorMarkClip))]
    [TrackBindingType(typeof(CutsceneActor))]
    public sealed class ActorMarkTrack : TrackAsset
    {
        [Header("Ground")]
        [Tooltip("Drop the actor onto the ground under each mark (a downward ray, ignoring the actor's own " +
                 "colliders and triggers). On for grounded walkers: the path between two marks is a straight " +
                 "line but the hill is curved, so without it MC floated up to 0.5 m on the olive path.")]
        [SerializeField] private bool snapToGround;

        [Tooltip("Metres above the ground hit to put the root: the CharacterController's resting skin, so the " +
                 "Player doesn't pop when gameplay resumes.")]
        [SerializeField] private float groundLift;

        /// <summary>True when marks are dropped onto the ground.</summary>
        public bool SnapToGround => snapToGround;

        /// <summary>Setup-menu entry point.</summary>
        public void ConfigureGround(bool snap, float lift)
        {
            snapToGround = snap;
            groundLift = lift;
        }

        /// <inheritdoc />
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            var mixer = ScriptPlayable<ActorMarkMixerBehaviour>.Create(graph, inputCount);
            mixer.GetBehaviour().ConfigureGround(snapToGround, groundLift);
            return mixer;
        }
    }
}
