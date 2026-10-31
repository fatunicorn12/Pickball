using System;
using System.Collections.Generic;

namespace Pickleball.Cutscene.Editor.Actors
{
    /// <summary>
    /// A hand-keyed cutscene clip: which verbs it plays, the base clip it layers on, its keys,
    /// and how it loops. <see cref="PoseClipBaker"/> turns one into a Humanoid <c>.anim</c>.
    /// </summary>
    /// <remarks>
    /// Hand-keying happens here, in muscle space, instead of in Blender: a muscle clip is
    /// Humanoid by construction, so it plays on MC and Birdy (and on any swapped-in humanoid)
    /// without a retarget step, it is regenerated identically by the setup menu, and the poses
    /// stay readable as code ("right arm up, fist closed"). See Docs/Art/CutsceneCharacters.md.
    /// </remarks>
    public sealed class PoseClipSpec
    {
        /// <summary>Asset file name without extension ("MC_SitUp").</summary>
        public string Name;

        /// <summary>The model the clip is grounded and sampled on (MC's or Birdy's FBX).</summary>
        public string ModelPath;

        /// <summary>Output folder.</summary>
        public string Folder;

        /// <summary>The verbs (shot list §6 spelling, lower case) this clip plays.</summary>
        public string[] Verbs = new string[0];

        /// <summary>Base clip asset path (an FBX's first take, or an .anim).</summary>
        public string BaseClip;

        /// <summary>Sample the base frozen at this time (s), or play it when negative.</summary>
        public float BaseTime = -1f;

        /// <summary>Playback rate of an animated base.</summary>
        public float BaseSpeed = 1f;

        /// <summary>Length, seconds.</summary>
        public float Duration = 1f;

        /// <summary>Loop (Loop Time and Loop Pose on). Non-looping clips hold their last frame.</summary>
        public bool Loop;

        /// <summary>Blend-in the library suggests, seconds.</summary>
        public float BlendIn = 0.25f;

        /// <summary>
        /// A looping walk: the right leg plays the left leg's muscles half a cycle later, so both
        /// legs swing the same. The Mixamo walk on the chibi kicks the right heel up behind to
        /// above the knee (0.31 m against a 0.21 m knee) while the left stays low; in the cutscene
        /// close-ups that read as a boot frozen behind him (review round 2, N1).
        /// </summary>
        public bool SymmetricLegs;

        /// <summary>Default grounding weight (keys may override).</summary>
        public float Ground = 1f;

        /// <summary>The keys, in time order.</summary>
        public readonly List<PoseKey> Keys = new List<PoseKey>();

        /// <summary>Optional per-frame procedural layer (bobs, flaps, wobbles), after the keys.</summary>
        public Action<float, PoseFrame> Procedural;

        /// <summary>One line for the docs: what the clip shows and how it was made.</summary>
        public string Note;

        /// <summary>Add keys; returns this for chaining.</summary>
        public PoseClipSpec K(params PoseKey[] keys)
        {
            Keys.AddRange(keys);
            return this;
        }
    }
}
