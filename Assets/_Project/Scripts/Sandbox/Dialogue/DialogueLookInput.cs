using UnityEngine;
using UnityEngine.InputSystem;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// Look input during a conversation: mouse delta while the cursor is captured,
    /// plus the gamepad right stick, turned into degrees of yaw and pitch for
    /// <see cref="DialogueOrbit"/>.
    ///
    /// <b>Why the dialogue reads its own look.</b> The orbit camera is disabled
    /// for the whole talk (it would tick at dt 0), and the director holds
    /// <see cref="InputBlock.All"/> on the input gate, so no normal-play reader
    /// runs. This reads the same devices the same way as
    /// <see cref="SandboxOrbitCamera"/>: raw mouse delta times a sensitivity, only
    /// while the cursor is captured; the raw right stick through a radial
    /// deadzone and response curve, at a rate per unscaled second. It reads
    /// nothing else: look input can never advance a line, which only
    /// <see cref="InteractInput.Pressed"/> does.
    ///
    /// Pitch is positive when the camera should rise and look down, the
    /// orbit camera's convention: mouse up and (uninverted) stick up look up.
    /// </summary>
    public static class DialogueLookInput
    {
        // Longest unscaled step one frame's stick input may cover, so a hitch
        // cannot fling the camera.
        const float MaxStep = 0.1f;

        /// <summary>This frame's look, in degrees (x yaw, y pitch), from the
        /// current mouse and gamepad. <paramref name="mouseCaptured"/> gates the
        /// mouse; the stick never waits for the cursor.</summary>
        public static Vector2 Read(in DialogueLookSettings settings, bool mouseCaptured, float unscaledDeltaTime)
        {
            Vector2 mouse = Vector2.zero, stick = Vector2.zero;
            var m = Mouse.current;
            if (mouseCaptured && m != null) mouse = m.delta.ReadValue();
            var pad = Gamepad.current;
            if (pad != null) stick = pad.rightStick.ReadUnprocessedValue();
            return ToDegrees(mouse, stick, settings, unscaledDeltaTime);
        }

        /// <summary>
        /// Degrees of yaw (x) and pitch (y) for a mouse delta in counts and a raw
        /// stick over <paramref name="unscaledDeltaTime"/> seconds. Pure, so it
        /// is tested without devices.
        /// </summary>
        public static Vector2 ToDegrees(Vector2 mouseDelta, Vector2 rawStick, in DialogueLookSettings settings,
                                        float unscaledDeltaTime)
        {
            float dt = Mathf.Clamp(unscaledDeltaTime, 0f, MaxStep);
            Vector2 stick = ShapeStick(rawStick, settings.StickDeadzone, settings.StickExponent);
            float rise = settings.InvertStickY ? -stick.y : stick.y;
            float yaw = mouseDelta.x * settings.MouseSensitivity + stick.x * settings.StickYawSpeed * dt;
            float pitch = -mouseDelta.y * settings.MouseSensitivity - rise * settings.StickPitchSpeed * dt;
            return new Vector2(yaw, pitch);
        }

        /// <summary>Radial deadzone, rescaled so tilt ramps up from zero at its
        /// edge instead of jumping, then bent by the response curve: the orbit
        /// camera's shaping, so the stick feels the same.</summary>
        public static Vector2 ShapeStick(Vector2 raw, float deadzone, float exponent)
        {
            float magnitude = raw.magnitude;
            if (magnitude <= deadzone || magnitude < 1e-6f) return Vector2.zero;
            float t = Mathf.Clamp01((magnitude - deadzone) / Mathf.Max(1f - deadzone, 1e-4f));
            return raw / magnitude * Mathf.Pow(t, Mathf.Max(exponent, 0.01f));
        }
    }
}
