using System;
using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>One set of interchangeable voice clips and how loud they play. Add or
    /// remove clips freely: any clip in a group can follow any other.</summary>
    [Serializable]
    public class CombatVoiceGroup
    {
        [Tooltip("The clips to draw from. Each plays once before any repeats. Filled from " +
                 "this group's folder by Setup Combat Voice only while empty; after that, " +
                 "drag out any you don't like.")]
        [SerializeField] private AudioClip[] clips = Array.Empty<AudioClip>();

        [Tooltip("Playback volume, 0-1, on top of the level the clips were installed at.")]
        [Range(0f, 1f)] [SerializeField] private float volume = 1f;

        public AudioClip[] Clips => clips;
        public float Volume => volume;

        /// <summary>Build a group in code, for setup scripts and tests.</summary>
        public CombatVoiceGroup(AudioClip[] clips, float volume = 1f)
        {
            this.clips = clips ?? Array.Empty<AudioClip>();
            this.volume = volume;
        }
    }
}
