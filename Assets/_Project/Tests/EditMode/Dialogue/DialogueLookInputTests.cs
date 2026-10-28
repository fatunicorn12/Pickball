using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;
using UnityEngine;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// Look input in a talk feels like the orbit camera's: the same mouse
    /// sensitivity and directions, the same stick deadzone and curve, rates per
    /// unscaled second, and a hitch cannot fling the camera.
    /// </summary>
    public class DialogueLookInputTests
    {
        static DialogueLookSettings Settings(bool invert = false) => new()
        {
            MouseSensitivity = 0.12f,
            StickYawSpeed = 200f,
            StickPitchSpeed = 120f,
            StickDeadzone = 0.15f,
            StickExponent = 1.6f,
            InvertStickY = invert,
        };

        [Test]
        public void Mouse_RightTurnsRight_UpLooksUp()
        {
            Vector2 right = DialogueLookInput.ToDegrees(new Vector2(10f, 0f), Vector2.zero, Settings(), 0.016f);
            Vector2 up = DialogueLookInput.ToDegrees(new Vector2(0f, 10f), Vector2.zero, Settings(), 0.016f);

            Assert.That(right.x, Is.EqualTo(1.2f).Within(1e-5f));
            Assert.That(right.y, Is.EqualTo(0f));
            Assert.That(up.y, Is.EqualTo(-1.2f).Within(1e-5f), "camera lowers, looking up, as in the orbit camera");
        }

        [Test]
        public void Stick_FullTilt_TurnsAtTheConfiguredRate_PerUnscaledSecond()
        {
            Vector2 right = DialogueLookInput.ToDegrees(Vector2.zero, new Vector2(1f, 0f), Settings(), 0.05f);
            Vector2 up = DialogueLookInput.ToDegrees(Vector2.zero, new Vector2(0f, 1f), Settings(), 0.05f);
            Vector2 upInverted = DialogueLookInput.ToDegrees(Vector2.zero, new Vector2(0f, 1f), Settings(true), 0.05f);

            Assert.That(right.x, Is.EqualTo(10f).Within(1e-4f));
            Assert.That(up.y, Is.EqualTo(-6f).Within(1e-4f));
            Assert.That(upInverted.y, Is.EqualTo(6f).Within(1e-4f));
        }

        [Test]
        public void Stick_AHitch_IsCappedToATenthOfASecond()
        {
            Vector2 look = DialogueLookInput.ToDegrees(Vector2.zero, new Vector2(1f, 0f), Settings(), 2f);

            Assert.That(look.x, Is.EqualTo(20f).Within(1e-4f));
        }

        [Test]
        public void Stick_InsideTheDeadzone_DoesNothing()
        {
            Assert.That(DialogueLookInput.ShapeStick(new Vector2(0.1f, 0.05f), 0.15f, 1.6f), Is.EqualTo(Vector2.zero));
            Assert.That(DialogueLookInput.ToDegrees(Vector2.zero, new Vector2(0.1f, 0f), Settings(), 0.016f),
                        Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void Stick_RampsFromTheDeadzoneEdge_AlongTheCurve()
        {
            Vector2 half = DialogueLookInput.ShapeStick(new Vector2(0.575f, 0f), 0.15f, 1.6f);   // halfway past the edge
            Vector2 full = DialogueLookInput.ShapeStick(new Vector2(0f, 1f), 0.15f, 1.6f);

            Assert.That(half.x, Is.EqualTo(Mathf.Pow(0.5f, 1.6f)).Within(1e-4f));
            Assert.That(full.x, Is.EqualTo(0f));
            Assert.That(full.y, Is.EqualTo(1f).Within(1e-5f), "full tilt is full speed");
        }

        [Test]
        public void NoInput_IsNoLook()
        {
            Assert.That(DialogueLookInput.ToDegrees(Vector2.zero, Vector2.zero, Settings(), 0.016f), Is.EqualTo(Vector2.zero));
        }
    }
}
