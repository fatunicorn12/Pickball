using Pickleball.Sandbox.Movement;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// One frame of everything that can stop a channel, gathered by
    /// <see cref="SpellCaster"/> from input, movement and health, and judged by
    /// <see cref="SpellChannel"/>. A plain struct so the rules can be tested
    /// without a player, a device or a scene.
    /// </summary>
    public struct ChannelSignals
    {
        /// <summary>Fire is held (and the caster is aiming, or it would not be asking).</summary>
        public bool Held;

        /// <summary>Movement input this frame: stick past the deadzone, or crouch,
        /// flip, mount or anchor pressed.</summary>
        public bool Moving;

        /// <summary>Jump pressed this frame, or the body is not on the ground.</summary>
        public bool Airborne;

        /// <summary>Dash or tether pressed this frame, or an exclusive move owns
        /// the body (<c>ChibiLocomotion.IsBusy</c>).</summary>
        public bool Busy;

        /// <summary>Damage was taken since the last frame.</summary>
        public bool Hurt;

        /// <summary>The input gate blocks <c>InputBlock.Spell</c>.</summary>
        public bool Blocked;

        /// <summary>Health is already at its maximum. Only checked when starting.</summary>
        public bool HealthFull;

        /// <summary>No health to restore (none wired, or dead). Only checked when starting.</summary>
        public bool HealthUnavailable;

        /// <summary>
        /// Folds one frame of <see cref="MovementIntent"/> in: a stick deflection
        /// past <paramref name="stickDeadzone"/> or any movement button counts as
        /// movement; jump counts as leaving the ground and dash / tether as an
        /// exclusive move, a frame before the motor itself reports them, so the
        /// channel stops on the press rather than on its consequence.
        /// </summary>
        /// <param name="intent">This frame's movement input.</param>
        /// <param name="stickDeadzone">Raw stick magnitude (0-1) that still counts as
        /// "not moving", so a resting pad's drift never cancels a heal.</param>
        public void AddIntent(in MovementIntent intent, float stickDeadzone)
        {
            float deadzone = stickDeadzone < 0f ? 0f : stickDeadzone;
            if (intent.RawStick.sqrMagnitude > deadzone * deadzone
                || intent.CrouchPressed || intent.TrickPressed || intent.MountPressed || intent.AnchorPressed)
                Moving = true;
            if (intent.JumpPressed) Airborne = true;
            if (intent.DashPressed || intent.TetherPressed) Busy = true;
        }
    }
}
