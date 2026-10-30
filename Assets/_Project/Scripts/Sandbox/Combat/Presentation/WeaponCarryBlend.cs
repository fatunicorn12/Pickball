using UnityEngine;

namespace Pickleball.Sandbox.Combat.Presentation
{
    /// <summary>
    /// Decides how much of a weapon's carry pose shows, and fades it.
    ///
    /// The locomotion clips hold the sword for the 0.85 m compass. A big blade needs its
    /// own rest pose for the weapon arm (the ruler's low trail), played on a masked Carry
    /// layer under the Attack layers. That layer's weight comes from here:
    ///  - <b>Equip:</b> a weapon with a carry pose fades it in to the weapon's weight over
    ///    its blend time; a weapon without one fades it out over the same time, so the
    ///    compass gets its clips' own arm back.
    ///  - <b>Swings never move it.</b> While a swing or its hitstop runs the weight heads
    ///    for the weapon's weight whatever else is going on. The Attack layer above covers
    ///    the whole arm at full weight, so the carry under it is invisible, but it is what
    ///    the swing blends in from and what the Attack layer's fade at the end of the chain
    ///    reveals. Dropping it under a swing would show the clips' own arm in both blends:
    ///    with the ruler, the blade through the head.
    ///  - <b>Moves that use the arm</b> (the caller says which: the pendulum's hang, the
    ///    skateboard, the dive) fade it out over the faster <c>suppressSeconds</c>, so the
    ///    arm is free before the move's own pose arrives. It comes back over the weapon's
    ///    blend time.
    ///
    /// Linear fades scaled by dt, so the same seconds hold at any frame rate. Plain C#
    /// with no Animator, so the rules are EditMode-tested; the component that owns it
    /// rebuilds it on enable and seeds it from the layer's current weight, so a script
    /// reload never pops the arm.
    /// </summary>
    public sealed class WeaponCarryBlend
    {
        float _weaponWeight, _blendSeconds;

        /// <summary>True while the equipped weapon has a carry pose.</summary>
        public bool HasCarry { get; private set; }

        /// <summary>Weight for the Carry layer this frame, 0-1.</summary>
        public float Weight { get; private set; }

        /// <summary>Where <see cref="Weight"/> is heading after the last tick.</summary>
        public float Target { get; private set; }

        /// <summary>
        /// True while the layer is invisible, so its state can be switched outright. When
        /// it is visible, switch with a crossfade: jumping a visible Override layer to
        /// another state pops the arm.
        /// </summary>
        public bool IsHidden => Weight <= 0.001f;

        /// <summary>
        /// A weapon went into the hand. <paramref name="hasCarry"/> false (or a weight of
        /// 0) fades the pose out; otherwise it fades to <paramref name="weight"/>. Both
        /// fades take <paramref name="blendSeconds"/>.
        /// </summary>
        public void SetWeapon(bool hasCarry, float weight, float blendSeconds)
        {
            HasCarry = hasCarry && weight > 0f;
            _weaponWeight = HasCarry ? Mathf.Clamp01(weight) : 0f;
            _blendSeconds = Mathf.Max(0f, blendSeconds);
        }

        /// <summary>
        /// Starts from <paramref name="weight"/> without a fade: the layer already shows
        /// this much (after a script reload, the component reads it back off the Animator).
        /// </summary>
        public void SyncTo(float weight)
        {
            Weight = Mathf.Clamp01(weight);
            Target = Weight;
        }

        /// <summary>
        /// Once per frame. <paramref name="swinging"/>: a swing or its hitstop is running.
        /// <paramref name="suppressed"/>: a move that needs the arm is running. Returns the
        /// new <see cref="Weight"/>.
        /// </summary>
        public float Tick(float dt, bool swinging, bool suppressed, float suppressSeconds)
        {
            bool show = HasCarry && (swinging || !suppressed);
            Target = show ? _weaponWeight : 0f;

            // Out of the way fast for a move; otherwise the weapon's own blend.
            float seconds = !show && HasCarry ? suppressSeconds : _blendSeconds;
            float step = seconds > 1e-4f ? Mathf.Max(0f, dt) / seconds : 1f;
            Weight = Mathf.MoveTowards(Weight, Target, step);
            return Weight;
        }

        /// <summary>Drops everything at once: the component was disabled.</summary>
        public void Reset()
        {
            HasCarry = false;
            _weaponWeight = 0f;
            Weight = 0f;
            Target = 0f;
        }
    }
}
