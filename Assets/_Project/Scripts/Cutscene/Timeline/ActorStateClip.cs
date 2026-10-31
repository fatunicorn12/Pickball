using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Pickleball.Cutscene.Timeline
{
    /// <summary>
    /// A held state for one actor or prop, from the clip's start until the next clip: whether
    /// Birdy's skinned rotor is on his helmet and how fast it spins, and whether the loose
    /// propeller prop is shown and how fast it spins. A field left at -1 is "leave as it is", so
    /// one track carries both the rotor and the prop keys of a scene. Built by the timeline
    /// builder from the plan's state keys (<see cref="Plan.StateChannels"/>).
    /// </summary>
    [Serializable]
    public sealed class ActorStateClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("Birdy's skinned rotor: 1 on the helmet, 0 detached (the Rotor bone scales to 0), -1 leave.")]
        [SerializeField] private int rotorAttached = -1;

        [Tooltip("Birdy's rotor target spin, degrees per second (eased by PropellerConfig); -1 leaves it.")]
        [SerializeField] private float rotorSpin = -1f;

        [Tooltip("The loose propeller prop: 1 shown, 0 hidden, -1 leave.")]
        [SerializeField] private int propVisible = -1;

        [Tooltip("The loose prop's target spin, degrees per second; 0 winds it down; -1 leaves it.")]
        [SerializeField] private float propSpin = -1f;

        [Tooltip("EyeSwap variant on the actor (0 the original eyes, 1 its first painted variant: MC Closed, Birdy Drop); -1 leaves it.")]
        [SerializeField] private int eyes = -1;

        /// <summary>Rotor attached state (1, 0 or -1 = leave).</summary>
        public int RotorAttached => rotorAttached;

        /// <summary>Rotor spin (°/s, or -1 = leave).</summary>
        public float RotorSpin => rotorSpin;

        /// <summary>Prop visibility (1, 0 or -1 = leave).</summary>
        public int PropVisible => propVisible;

        /// <summary>Prop spin (°/s, or -1 = leave).</summary>
        public float PropSpin => propSpin;

        /// <summary>Eye variant (or -1 = leave).</summary>
        public int Eyes => eyes;

        /// <inheritdoc />
        public ClipCaps clipCaps => ClipCaps.None;

        /// <summary>Setup-menu entry point.</summary>
        public void Configure(int attached, float spin, int visible, float propellerSpin, int eyeVariant = -1)
        {
            eyes = eyeVariant;
            rotorAttached = attached;
            rotorSpin = spin;
            propVisible = visible;
            propSpin = propellerSpin;
        }

        /// <inheritdoc />
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<ActorStateBehaviour>.Create(graph);
            var b = playable.GetBehaviour();
            b.RotorAttached = rotorAttached;
            b.RotorSpin = rotorSpin;
            b.PropVisible = propVisible;
            b.PropSpin = propSpin;
            b.Eyes = eyes;
            return playable;
        }
    }
}
