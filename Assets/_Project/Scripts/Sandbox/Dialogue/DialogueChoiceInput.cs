using UnityEngine;
using UnityEngine.InputSystem;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// Option-list input while a choice is up: the <b>left</b> stick, the d-pad
    /// and the up/down arrow and W/S keys move the cursor (through
    /// <see cref="ChoiceNavigator"/>), and the number keys 1–9 (top row or
    /// numpad) pick an option outright. Confirming the highlighted option is
    /// Interact (E / RB), which the director already hears; the mouse is the
    /// dialogue view's.
    ///
    /// <b>Why the dialogue reads its own.</b> As with
    /// <see cref="DialogueLookInput"/>: the director holds
    /// <see cref="InputBlock.All"/> for the whole talk, so no normal-play
    /// reader runs, and those buttons mean nothing else while the world is
    /// stopped. The right stick is left alone: it still orbits the talk camera.
    /// Read only while options are up, so it costs nothing otherwise.
    /// </summary>
    public static class DialogueChoiceInput
    {
        static readonly Key[] DigitKeys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9,
        };

        static readonly Key[] NumpadKeys =
        {
            Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8,
            Key.Numpad9,
        };

        /// <summary>
        /// This frame's up/down axis (+1 up) from the current devices, and in
        /// <paramref name="numberPressed"/> the option a number key picked this
        /// frame (0 for "1"), or -1.
        /// </summary>
        public static float Read(out int numberPressed)
        {
            numberPressed = -1;
            bool keyUp = false, keyDown = false;
            var kb = Keyboard.current;
            if (kb != null)
            {
                keyUp = kb.upArrowKey.isPressed || kb.wKey.isPressed;
                keyDown = kb.downArrowKey.isPressed || kb.sKey.isPressed;
                for (int i = 0; i < DigitKeys.Length; i++)
                {
                    if (!kb[DigitKeys[i]].wasPressedThisFrame && !kb[NumpadKeys[i]].wasPressedThisFrame) continue;
                    numberPressed = i;
                    break;
                }
            }

            float stickY = 0f;
            bool padUp = false, padDown = false;
            var pad = Gamepad.current;
            if (pad != null)
            {
                stickY = pad.leftStick.ReadValue().y;
                padUp = pad.dpad.up.isPressed;
                padDown = pad.dpad.down.isPressed;
            }
            return Axis(stickY, padUp || keyUp, padDown || keyDown);
        }

        /// <summary>
        /// One up/down axis (+1 up, −1 down) from the left stick's y and the
        /// digital buttons, clamped to ±1. Up and down together cancel. Pure.
        /// </summary>
        public static float Axis(float stickY, bool up, bool down)
        {
            float digital = (up ? 1f : 0f) - (down ? 1f : 0f);
            return Mathf.Clamp(stickY + digital, -1f, 1f);
        }

        /// <summary>The option index (0-based) for number key <paramref name="digit"/>
        /// (1–9), or -1 for anything else. Pure.</summary>
        public static int IndexForDigit(int digit) => digit >= 1 && digit <= DigitKeys.Length ? digit - 1 : -1;
    }
}
