using System.Reflection;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Magic;
using UnityEngine.InputSystem;

namespace Pickleball.Tests.Magic
{
    /// <summary>
    /// Spell selection presses are latched like the fire press (playtest M-1):
    /// seen by every reader through the frame, cleared by LateUpdate whether
    /// used or not, dropped (not saved) while Spell is blocked, and the pad
    /// cycle dropped while not aiming. The performed callbacks, LateUpdate and
    /// OnDisable are invoked by reflection: EditMode sends no input and no
    /// lifecycle messages.
    /// </summary>
    public class SpellSelectInputTests : MagicTestFixture
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        readonly object _dialogue = new();

        static void Call(object target, string method, params object[] args)
        {
            var info = target.GetType().GetMethod(method, Private);
            Assert.That(info, Is.Not.Null, $"{target.GetType().Name}.{method} was renamed; update the test.");
            info.Invoke(target, args);
        }

        static void PressSlot(SpellSelectInput input, int slot) => Call(input, "OnSlotPressed", slot);
        static void PressNext(SpellSelectInput input) => Call(input, "OnNext", default(InputAction.CallbackContext));
        static void PressPrevious(SpellSelectInput input) => Call(input, "OnPrevious", default(InputAction.CallbackContext));
        static void EndFrame(SpellSelectInput input) => Call(input, "LateUpdate");
        static void Scroll(SpellSelectInput input, float delta) => Call(input, "OnScroll", delta);

        [Test]
        public void SlotPress_IsSeenAllFrame_ThenClearedByLateUpdate()
        {
            var input = NewComponent<SpellSelectInput>();
            Assert.That(input.SlotPressedThisFrame, Is.EqualTo(-1));

            PressSlot(input, 2);
            Assert.That(input.SlotPressedThisFrame, Is.EqualTo(2));
            Assert.That(input.SlotPressedThisFrame, Is.EqualTo(2), "reading does not consume it");
            EndFrame(input);
            Assert.That(input.SlotPressedThisFrame, Is.EqualTo(-1));
        }

        [Test]
        public void SeveralSlots_TheLowestWins()
        {
            var input = NewComponent<SpellSelectInput>();
            PressSlot(input, 4);
            PressSlot(input, 1);
            PressSlot(input, 3);
            Assert.That(input.SlotPressedThisFrame, Is.EqualTo(1));
        }

        [Test]
        public void PressWhileSpellBlocked_IsDropped_NotSavedForTheRelease()
        {
            var input = NewComponent<SpellSelectInput>();
            var gate = input.gameObject.AddComponent<PlayerInputGate>();
            SetReference(input, "inputGate", gate);

            gate.Claim(_dialogue, InputBlock.All);
            PressSlot(input, 2);
            PressNext(input);
            Assert.That(input.SlotPressedThisFrame, Is.EqualTo(-1));
            Assert.That(input.CycleThisFrame, Is.EqualTo(0));
            EndFrame(input);

            gate.Release(_dialogue);
            Assert.That(input.SlotPressedThisFrame, Is.EqualTo(-1), "nothing was saved for the release");
            Assert.That(input.CycleThisFrame, Is.EqualTo(0));
        }

        [Test]
        public void Cycle_WithNoAimSource_AlwaysCounts()
        {
            var input = NewComponent<SpellSelectInput>();
            PressNext(input);
            Assert.That(input.CycleThisFrame, Is.EqualTo(1));
            EndFrame(input);
            PressPrevious(input);
            Assert.That(input.CycleThisFrame, Is.EqualTo(-1));
        }

        [Test]
        public void Cycle_WhileNotAiming_IsDropped()
        {
            var input = NewComponent<SpellSelectInput>();
            // A caster in EditMode never aims (no input, not enabled by a lifecycle).
            var caster = input.gameObject.AddComponent<SpellCaster>();
            SetReference(input, "aimSource", caster);

            PressNext(input);
            Assert.That(input.CycleThisFrame, Is.EqualTo(0), "d-pad up/down are camera zoom outside aim");
            PressSlot(input, 0);
            Assert.That(input.SlotPressedThisFrame, Is.EqualTo(0), "number keys work aiming or not");
        }

        [Test]
        public void NextAndPreviousTogether_CancelOut()
        {
            var input = NewComponent<SpellSelectInput>();
            PressNext(input);
            PressPrevious(input);
            Assert.That(input.CycleThisFrame, Is.EqualTo(0));
        }

        [Test]
        public void WheelDown_IsNext_WheelUp_IsPrevious()
        {
            var input = NewComponent<SpellSelectInput>();
            SetFloat(input, "scrollStepSeconds", 0f);
            Scroll(input, -120f);
            Assert.That(input.CycleThisFrame, Is.EqualTo(1));
            EndFrame(input);
            Assert.That(input.CycleThisFrame, Is.EqualTo(0), "cleared by LateUpdate");
            Scroll(input, 1f);
            Assert.That(input.CycleThisFrame, Is.EqualTo(-1));
        }

        [Test]
        public void Wheel_Inverted_SwapsDirection()
        {
            var input = NewComponent<SpellSelectInput>();
            SetBool(input, "invertScroll", true);
            Scroll(input, 120f);
            Assert.That(input.CycleThisFrame, Is.EqualTo(1));
        }

        [Test]
        public void Wheel_StepsAreSpacedByTheStepTime()
        {
            var input = NewComponent<SpellSelectInput>();
            SetFloat(input, "scrollStepSeconds", 0.08f);
            Scroll(input, -120f);
            EndFrame(input);
            // EditMode time stands still: a second event is inside the step gap.
            Scroll(input, -120f);
            Assert.That(input.CycleThisFrame, Is.EqualTo(0), "a trackpad's stream must not spin the row");
        }

        [Test]
        public void Wheel_TinyDelta_IsIgnored()
        {
            var input = NewComponent<SpellSelectInput>();
            Scroll(input, 0.001f);
            Assert.That(input.CycleThisFrame, Is.EqualTo(0));
        }

        [Test]
        public void Wheel_WhileNotAiming_IsDropped_EvenWithThePadCycleAlwaysOn()
        {
            var input = NewComponent<SpellSelectInput>();
            var caster = input.gameObject.AddComponent<SpellCaster>();
            SetReference(input, "aimSource", caster);
            SetBool(input, "padCycleOnlyWhileAiming", false);

            Scroll(input, -120f);
            Assert.That(input.CycleThisFrame, Is.EqualTo(0), "outside aim the wheel zooms the camera");
        }

        [Test]
        public void Wheel_WhileSpellBlocked_IsDropped()
        {
            var input = NewComponent<SpellSelectInput>();
            var gate = input.gameObject.AddComponent<PlayerInputGate>();
            SetReference(input, "inputGate", gate);
            gate.Claim(_dialogue, InputBlock.All);
            Scroll(input, -120f);
            Assert.That(input.CycleThisFrame, Is.EqualTo(0));
        }

        [Test]
        public void Disable_ClearsLatchedPresses()
        {
            var input = NewComponent<SpellSelectInput>();
            PressSlot(input, 1);
            PressNext(input);
            Scroll(input, -120f);
            Call(input, "OnDisable");
            Assert.That(input.SlotPressedThisFrame, Is.EqualTo(-1));
            Assert.That(input.CycleThisFrame, Is.EqualTo(0));
        }
    }
}
