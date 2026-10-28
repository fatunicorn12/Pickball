using System.Reflection;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Combat;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// Each gated input reader drops its input while its flag is claimed, and a
    /// press made during the claim never fires once it ends. Driven through the
    /// readers' own callbacks and Update by reflection, since EditMode sends no
    /// input and no lifecycle messages.
    ///
    /// Not covered here: <c>PlayerMovementInput.Read</c> polls its actions, so
    /// showing that it drops a held key needs simulated devices
    /// (<c>InputTestFixture</c>, not in this project's manifest). Its gate check
    /// is one line on the same <see cref="PlayerInputGate.IsBlocked"/> tested in
    /// <see cref="PlayerInputGateTests"/>.
    /// </summary>
    public class GatedReaderTests : DialogueTestFixture
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        readonly object _dialogue = new();

        static void Call(object target, string method, params object[] args)
        {
            var info = target.GetType().GetMethod(method, Private);
            Assert.That(info, Is.Not.Null, $"{target.GetType().Name}.{method} was renamed; update the test.");
            info.Invoke(target, args);
        }

        static void SetField(object target, string field, object value)
        {
            var info = target.GetType().GetField(field, Private);
            Assert.That(info, Is.Not.Null, $"{target.GetType().Name}.{field} was renamed; update the test.");
            info.SetValue(target, value);
        }

        static T GetField<T>(object target, string field)
        {
            var info = target.GetType().GetField(field, Private);
            Assert.That(info, Is.Not.Null, $"{target.GetType().Name}.{field} was renamed; update the test.");
            return (T)info.GetValue(target);
        }

        // --------------------------------------------------------------- sword

        [Test]
        public void Sword_PressWhileBlocked_IsDropped_NotQueued()
        {
            var host = NewHost("Player");
            var gate = host.AddComponent<PlayerInputGate>();
            var sword = host.AddComponent<SwordInput>();
            SetRef(sword, "inputGate", gate);
            int attacks = 0;
            sword.AttackPressed += () => attacks++;

            gate.Claim(_dialogue, InputBlock.Sword);
            Call(sword, "OnPerformed", default(InputAction.CallbackContext));
            Assert.That(attacks, Is.EqualTo(0));

            gate.Release(_dialogue);
            Assert.That(attacks, Is.EqualTo(0), "nothing was saved for the release");
            Call(sword, "OnPerformed", default(InputAction.CallbackContext));
            Assert.That(attacks, Is.EqualTo(1));
        }

        [Test]
        public void Sword_OtherFlagsDoNotBlockIt()
        {
            var host = NewHost("Player");
            var gate = host.AddComponent<PlayerInputGate>();
            var sword = host.AddComponent<SwordInput>();
            SetRef(sword, "inputGate", gate);
            int attacks = 0;
            sword.AttackPressed += () => attacks++;

            gate.Claim(_dialogue, InputBlock.Movement | InputBlock.InteractButton);
            Call(sword, "OnPerformed", default(InputAction.CallbackContext));

            Assert.That(attacks, Is.EqualTo(1));
        }

        // ------------------------------------------------------------- lock-on

        LockOnTargeting NewLockOn(out PlayerInputGate gate)
        {
            var host = NewHost("Player");
            gate = host.AddComponent<PlayerInputGate>();
            var lockOn = host.AddComponent<LockOnTargeting>();
            SetRef(lockOn, "inputGate", gate);
            return lockOn;
        }

        [TearDown]
        public void DestroyReticles()
        {
            // LockOnTargeting builds its reticle at runtime; Edit mode never runs
            // its OnDestroy, so clean up here.
            foreach (var reticle in Object.FindObjectsByType<LockOnReticle>(FindObjectsInactive.Include,
                                                                           FindObjectsSortMode.None))
                Object.DestroyImmediate(reticle.gameObject);
        }

        [Test]
        public void LockOn_TogglePressedDuringAClaim_IsCleared_AndNeverFiresAfter()
        {
            var lockOn = NewLockOn(out var gate);
            var target = NewHost("Enemy", new Vector3(0f, 0f, 3f)).AddComponent<Health>();
            target.ResetToFull();
            gate.Claim(_dialogue, InputBlock.LockOn);

            SetField(lockOn, "_togglePressed", true);       // Tab mid-conversation
            Call(lockOn, "Update");
            Assert.That(GetField<bool>(lockOn, "_togglePressed"), Is.False, "cleared, not kept");
            Assert.That(lockOn.IsLocked, Is.False);

            gate.Release(_dialogue);
            Call(lockOn, "Update");
            Assert.That(lockOn.IsLocked, Is.False, "nothing fires when the claim ends");
        }

        [Test]
        public void LockOn_FlicksDuringAClaim_DoNotBuildUp_AndTheLockIsKept()
        {
            var lockOn = NewLockOn(out var gate);
            var target = NewHost("Birdy", new Vector3(0f, 0f, 3f)).AddComponent<Health>();
            target.ResetToFull();
            typeof(LockOnTargeting).GetProperty(nameof(LockOnTargeting.Target)).SetValue(lockOn, target);
            gate.Claim(_dialogue, InputBlock.LockOn);

            SetField(lockOn, "_flickAccum", 65f);            // mouse travel during the talk
            SetField(lockOn, "_stickArmed", true);
            Call(lockOn, "Update");

            Assert.That(GetField<float>(lockOn, "_flickAccum"), Is.EqualTo(0f));
            Assert.That(GetField<bool>(lockOn, "_stickArmed"), Is.False, "a held stick must re-centre first");
            Assert.That(lockOn.Target, Is.SameAs(target), "the lock survives the conversation");
        }

        // ----------------------------------------------------------- spacetime

        [Test]
        public void Spacetime_TogglePressedDuringAClaim_IsDropped()
        {
            var settings = Track(ScriptableObject.CreateInstance<SpacetimeSettings>());
            settings.SetEnabled(true);
            var recorder = NewHost("Recorder").AddComponent<WorldlineRecorder>();
            SetRef(recorder, "settings", settings);
            for (int i = 0; i <= 20; i++) recorder.Sample(new Vector3(0f, 0f, i * 0.2f), i * 0.1f);
            settings.Live.Add(recorder);

            var host = NewHost("View");
            var gate = host.AddComponent<PlayerInputGate>();
            var view = host.AddComponent<SpacetimeView>();
            SetRef(view, "settings", settings);
            SetRef(view, "inputGate", gate);
            view.Tick(togglePressed: false, now: 2f, deltaTime: 0.1f);   // registers its input
            try
            {
                gate.Claim(_dialogue, InputBlock.Spacetime);
                SetField(view, "_togglePressed", true);                 // T mid-conversation
                Call(view, "Update");
                Assert.That(settings.In4D, Is.False);
                Assert.That(GetField<bool>(view, "_togglePressed"), Is.False, "cleared, not kept");

                gate.Release(_dialogue);
                Call(view, "Update");
                Assert.That(settings.In4D, Is.False, "nothing fires when the claim ends");

                SetField(view, "_togglePressed", true);                 // a real press afterwards
                Call(view, "Update");
                Assert.That(settings.In4D, Is.True, "precondition: the path works when unblocked");
            }
            finally
            {
                settings.SetEnabled(false);
                view.Tick(togglePressed: false, now: 3f, deltaTime: 0.1f);   // exits 4D, drops its input
            }
        }
    }
}
