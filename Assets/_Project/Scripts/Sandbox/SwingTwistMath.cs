using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// The maths behind <see cref="SwingTwist"/>, with no Transforms and no
    /// Animator, so EditMode tests can pin it down.
    ///
    /// SwingTwist puts back the hip turn that the upper-body mask drops from a
    /// swing. Each frame it samples the hip rotation baked from the clip, blends
    /// it across a chain crossfade, and turns the spine chain by
    /// (clip hips / locomotion hips).
    ///
    /// Every interpolation here is a <see cref="Quaternion.Slerp"/>. Unity's
    /// Slerp takes the shorter arc whichever sign a quaternion is stored with,
    /// so a baked sample stored as −q instead of q changes nothing. The tests
    /// check this, because a hemisphere flip in the bake was once a suspect for
    /// the third swing contorting.
    /// </summary>
    public static class SwingTwistMath
    {
        /// <summary>
        /// The baked hip rotation at <paramref name="normalizedTime"/>.
        /// <paramref name="samples"/> are spaced evenly from the first frame of
        /// the clip to the last. The time is clamped, not wrapped: swings are
        /// one-shots that hold their last frame while the layer fades out.
        /// </summary>
        /// <param name="samples">Hip rotation relative to the Animator's transform.</param>
        /// <param name="normalizedTime">The state's normalized time. 1 is the end of the clip.</param>
        public static Quaternion SampleTrack(Quaternion[] samples, float normalizedTime)
        {
            if (samples == null || samples.Length == 0) return Quaternion.identity;
            if (samples.Length == 1) return samples[0];

            float f = Mathf.Clamp01(normalizedTime) * (samples.Length - 1);
            int i = Mathf.Min((int)f, samples.Length - 2);
            return Quaternion.Slerp(samples[i], samples[i + 1], f - i);
        }

        /// <summary>
        /// The clip hip rotation partway through a crossfade from one swing into
        /// the next, so the correction moves with the swing rather than popping
        /// when the state changes.
        /// </summary>
        /// <param name="current">Hips of the state being faded out.</param>
        /// <param name="next">Hips of the state being faded in.</param>
        /// <param name="transition">The transition's normalized time, 0-1.</param>
        public static Quaternion BlendStates(Quaternion current, Quaternion next, float transition)
            => Quaternion.Slerp(current, next, transition);

        /// <summary>
        /// The world rotation to pre-multiply onto the spine base. At
        /// <paramref name="weight"/> 1 it carries the hips from where locomotion
        /// put them to where the clip wants them, so the chest, arms and blade
        /// end up where the clip authored them. Lower weights take that fraction
        /// of the shorter arc.
        /// </summary>
        /// <param name="animatorRotation">World rotation of the Animator's transform.</param>
        /// <param name="clipHips">The clip's hip rotation, relative to the Animator's transform.</param>
        /// <param name="currentHips">World rotation of the hips as the Animator left them.</param>
        /// <param name="weight">Attack-layer weight × strength, 0-1.</param>
        public static Quaternion Correction(Quaternion animatorRotation, Quaternion clipHips,
                                            Quaternion currentHips, float weight)
        {
            Quaternion want = animatorRotation * clipHips;
            Quaternion delta = want * Quaternion.Inverse(currentHips);
            return Quaternion.Slerp(Quaternion.identity, delta, weight);
        }
    }
}
