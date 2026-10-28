using System;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Which parts of the player's input a <see cref="PlayerInputGate"/> claim
    /// quiets. Each input reader checks only its own flag, so a claim can block
    /// exactly what it needs: a conversation blocks <see cref="All"/>, a talk
    /// prompt only <see cref="InteractButton"/>, an aiming mode might block
    /// <see cref="Sword"/> and <see cref="LockOn"/> but leave movement free.
    /// </summary>
    [Flags]
    public enum InputBlock
    {
        /// <summary>Nothing blocked.</summary>
        None = 0,
        /// <summary><c>PlayerMovementInput</c>: stick, jump, sprint, crouch, flip, mount, dash, tether, anchor.
        /// <c>TetherSystem</c> hides its selector marker.</summary>
        Movement = 1 << 0,
        /// <summary><c>SwordInput</c>: attack presses are dropped, not buffered.</summary>
        Sword = 1 << 1,
        /// <summary><c>LockOnTargeting</c>: the toggle and target flicks are dropped (never saved
        /// for the release), a held lock is released and no hint shows. Any claimant ends targeting.</summary>
        LockOn = 1 << 2,
        /// <summary>Camera look. No reader checks it yet: the dialogue camera disables the
        /// orbit camera instead, since it needs the transform as well as the input.</summary>
        Camera = 1 << 3,
        /// <summary><c>SpacetimeView</c>: the 4D toggle.</summary>
        Spacetime = 1 << 4,
        /// <summary>The shared Interact / Dash button (E, RB) belongs to an interaction, so it
        /// must not dash. Checked by <c>PlayerMovementInput</c> for the dash only.</summary>
        InteractButton = 1 << 5,
        /// <summary><c>SpellInput</c>: the spell's aim and fire buttons read as released.</summary>
        Spell = 1 << 6,
        /// <summary>Every flag.</summary>
        All = Movement | Sword | LockOn | Camera | Spacetime | InteractButton | Spell,
    }
}
