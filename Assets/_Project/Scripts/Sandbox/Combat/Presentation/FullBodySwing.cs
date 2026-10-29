using UnityEngine;

namespace Pickleball.Sandbox.Combat.Presentation
{
    /// <summary>
    /// Decides how much of a swing plays on the whole body, and fades it.
    ///
    /// The combo plays on a masked upper-body layer so the legs keep running. Some
    /// swings are authored as whole-body moves, though. The downstroke turns its hips
    /// about 60° and drops them 27 cm into a lunge. Over standing legs, the upper body
    /// alone cannot carry that pose: SwingTwist faces the chest the way the clip does,
    /// and the waist had to twist 126° and bend 41° sideways to get there. That is the
    /// "whole body contorts" bug.
    ///
    /// A step that asks for it (<see cref="ComboStepDefinition.FullBodyWhenStanding"/>)
    /// therefore also plays on a full-body layer under the masked one. That layer's
    /// weight comes from here:
    ///  - <b>Engages at swing start only:</b> the step asks, and he is grounded and
    ///    standing, i.e. his own speed is at or under the standing speed. A lunge's
    ///    shove is momentum, not his own speed, so it does not count.
    ///  - <b>Lets go for the rest of the swing</b> as soon as the swing ends, he leaves
    ///    the ground, or he starts moving. It never re-engages mid-swing, so a speed
    ///    hovering at the threshold cannot make the legs flicker.
    ///  - <b>Fades rather than switches:</b> in over the step's blend-in, and out over
    ///    the release time whatever ends it. The legs hand back to locomotion without
    ///    a pop.
    ///
    /// Plain C# with no Animator, so the rules are EditMode-tested.
    /// </summary>
    public sealed class FullBodySwing
    {
        float _rate;

        /// <summary>True while the current swing is meant to play full-body.</summary>
        public bool Engaged { get; private set; }

        /// <summary>Weight for the full-body layer this frame, 0-1.</summary>
        public float Weight { get; private set; }

        /// <summary>
        /// True while the layer is invisible, so its state can be switched outright.
        /// When the layer is visible, switch it with a crossfade instead. Crossfading
        /// out of the layer's empty state at a visible weight would drag the legs
        /// toward the bind pose.
        /// </summary>
        public bool IsHidden => Weight <= 0.001f;

        /// <summary>
        /// A swing has started. Engages if <paramref name="stepWantsFullBody"/> and he
        /// is grounded with <paramref name="speed"/> at or under
        /// <paramref name="standingSpeed"/>. The fade-in then takes
        /// <paramref name="blendSeconds"/>, the same crossfade the masked layer uses.
        /// Otherwise any full-body swing still showing fades out over
        /// <paramref name="releaseSeconds"/>. A chained masked swing would otherwise
        /// snap the legs out of a lunge in the 0.1 s upper-body blend: measured, the hips
        /// swung 15° a frame.
        /// </summary>
        /// <returns>Whether this swing plays full-body.</returns>
        public bool Begin(bool stepWantsFullBody, bool grounded, float speed,
                          float standingSpeed, float blendSeconds, float releaseSeconds)
        {
            Engaged = stepWantsFullBody && grounded && speed <= standingSpeed;
            _rate = RateFor(Engaged ? blendSeconds : releaseSeconds);
            return Engaged;
        }

        /// <summary>
        /// Once per frame. Lets go for good, for this swing, when it is no longer
        /// <paramref name="swinging"/>, he is not <paramref name="grounded"/>, or his
        /// <paramref name="speed"/> passes <paramref name="standingSpeed"/>. The fade-out
        /// then takes <paramref name="releaseSeconds"/>. Then moves
        /// <see cref="Weight"/> toward its target.
        /// </summary>
        public void Tick(float dt, bool swinging, bool grounded, float speed,
                         float standingSpeed, float releaseSeconds)
        {
            if (Engaged && (!swinging || !grounded || speed > standingSpeed))
            {
                Engaged = false;
                _rate = RateFor(releaseSeconds);
            }

            float target = Engaged ? 1f : 0f;
            float step = float.IsPositiveInfinity(_rate) ? 1f : _rate * Mathf.Max(0f, dt);
            Weight = Mathf.MoveTowards(Weight, target, step);
        }

        /// <summary>Drops everything at once: the component was disabled.</summary>
        public void Reset()
        {
            Engaged = false;
            Weight = 0f;
            _rate = 0f;
        }

        static float RateFor(float seconds) => seconds > 1e-4f ? 1f / seconds : float.PositiveInfinity;
    }
}
