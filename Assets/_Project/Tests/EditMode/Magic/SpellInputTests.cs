using System.Reflection;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Magic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pickleball.Tests.Magic
{
    /// <summary>
    /// The fire press is latched from the actions' performed callbacks
    /// (playtest M-1): a press processed outside the frame's own input update
    /// still fires on the next Update, every reader in a frame sees it, and
    /// LateUpdate clears it whether used or not, so a press made while not
    /// aiming, or while the spell is gated, is dropped rather than saved.
    /// Callbacks, LateUpdate and OnDisable are invoked by reflection: EditMode
    /// sends no input and no lifecycle messages.
    ///
    /// Not covered: the mouse's cursor-capture check needs a live orbit camera
    /// (CursorCaptured flips in its LateUpdate), and the held state (aim,
    /// auto-repeat) is polled from devices, which needs InputTestFixture.
    /// </summary>
    public class SpellInputTests : BossTestFixture
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        readonly object _dialogue = new();

        static void Call(object target, string method, params object[] args)
        {
            var info = target.GetType().GetMethod(method, Private);
            Assert.That(info, Is.Not.Null, $"{target.GetType().Name}.{method} was renamed; update the test.");
            info.Invoke(target, args);
        }

        static void SetReference(Component target, string fieldName, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            Assert.That(property, Is.Not.Null, $"{target.GetType().Name} has no serialized '{fieldName}'");
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void PressPad(SpellInput input) => Call(input, "OnFirePad", default(InputAction.CallbackContext));

        static void EndFrame(SpellInput input) => Call(input, "LateUpdate");

        [Test]
        public void PadPress_IsSeenByEveryReaderThisFrame_ThenClearedByLateUpdate()
        {
            var input = NewComponent<SpellInput>();

            PressPad(input);

            Assert.That(input.FirePressedThisFrame, Is.True);
            Assert.That(input.FirePressedThisFrame, Is.True, "reading does not consume it");
            EndFrame(input);
            Assert.That(input.FirePressedThisFrame, Is.False, "one press, one frame");
        }

        [Test]
        public void PressAfterLateUpdate_IsKeptForTheNextFrame()
        {
            // M-1: a press processed between frames (a manual InputSystem.Update)
            // used to be lost to WasPressedThisFrame.
            var input = NewComponent<SpellInput>();
            EndFrame(input);

            PressPad(input);

            Assert.That(input.FirePressedThisFrame, Is.True);
        }

        [Test]
        public void MousePress_WithNoCameraRig_Counts()
        {
            var input = NewComponent<SpellInput>();

            Call(input, "OnFireMouse", default(InputAction.CallbackContext));

            Assert.That(input.FirePressedThisFrame, Is.True);
        }

        [Test]
        public void PressWhileSpellBlocked_IsDropped_NotSavedForTheRelease()
        {
            var input = NewComponent<SpellInput>();
            var gate = input.gameObject.AddComponent<PlayerInputGate>();
            SetReference(input, "inputGate", gate);

            gate.Claim(_dialogue, InputBlock.All);
            PressPad(input);
            Assert.That(input.FirePressedThisFrame, Is.False);
            EndFrame(input);

            gate.Release(_dialogue);
            Assert.That(input.FirePressedThisFrame, Is.False, "nothing was saved for the release");
            PressPad(input);
            Assert.That(input.FirePressedThisFrame, Is.True);
        }

        [Test]
        public void Disable_ClearsALatchedPress()
        {
            var input = NewComponent<SpellInput>();
            PressPad(input);

            Call(input, "OnDisable");

            Assert.That(input.FirePressedThisFrame, Is.False);
        }
    }
}
