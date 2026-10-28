namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// Why a channel was refused or cancelled. The HUD turns it into a word
    /// ("Hit!", "Moved"), so the player always learns which rule they broke.
    /// </summary>
    public enum ChannelStopReason
    {
        /// <summary>Not stopped.</summary>
        None = 0,
        /// <summary>Fire (or aim) was let go before the channel completed.</summary>
        Released,
        /// <summary>Any movement input: the stick past its deadzone, crouch, flip,
        /// mount or anchor.</summary>
        Moved,
        /// <summary>Jump was pressed, or the body left the ground.</summary>
        Airborne,
        /// <summary>A dash or tether was pressed, or an exclusive move owns the body
        /// (<c>ChibiLocomotion.IsBusy</c>: dash, tether, skateboard).</summary>
        Busy,
        /// <summary>The caster took damage.</summary>
        Hurt,
        /// <summary>The input gate blocks <c>InputBlock.Spell</c> (a conversation).</summary>
        Blocked,
        /// <summary>The cost could not be paid (at the start, or at completion).</summary>
        NoMana,
        /// <summary>Health is already full: nothing to restore, so no mana is risked.</summary>
        FullHealth,
        /// <summary>No health to restore: none wired, or the caster is dead.</summary>
        Unavailable,
        /// <summary>Another spell was selected mid-channel.</summary>
        Switched,
        /// <summary>The caster was disabled or destroyed mid-channel.</summary>
        Disabled,
        /// <summary>The body was moved more than the spell's
        /// <c>channelMaxDrift</c> from where the channel started, by anything at
        /// all (a gravity well's pull, a push, a moving floor), with no movement
        /// input of its own. Appended last: never reorder.</summary>
        Displaced,
    }
}
