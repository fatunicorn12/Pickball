using UnityEngine.InputSystem;

namespace Pickleball.Cutscene.Playback
{
    /// <summary>
    /// Reads whether the skip input is held: a touch anywhere, Space or Enter, or the gamepad's South
    /// button. Polls the Input System's current devices, so it needs no actions asset and allocates
    /// nothing. Gameplay input is gated off during a cutscene, so Space doesn't also jump.
    /// </summary>
    public sealed class SkipInputReader
    {
        /// <summary>True while any skip input is down.</summary>
        public bool IsHeld()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.spaceKey.isPressed || keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed))
                return true;

            var gamepad = Gamepad.current;
            if (gamepad != null && gamepad.buttonSouth.isPressed) return true;

            var touch = Touchscreen.current;
            return touch != null && touch.primaryTouch.press.isPressed;
        }
    }
}
