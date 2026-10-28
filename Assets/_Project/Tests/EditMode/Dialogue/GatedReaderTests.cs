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
    /// press made during the claim never fires once it ends. Targeting ends too:
    /// a LockOn claim releases the lock and hides its ring, and a Movement claim
    /// hides the tether marker. Driven through the readers' own callbacks and
    /// Update by reflection, since EditMode sends no input and no lifecycle
    /// messages; the reticle's fade through its Tick, with explicit time.
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
        public void LockOn_FlicksDuringAClaim_DoNotBuildUp()
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
        }

        // Breath of the Wild: lock on to Birdy, press E, and the targeting ends.

        /// <summary>A lock-on rig that can see: an aim transform (no Camera, so
        /// everything counts as on screen) behind the player, and Birdy ahead.</summary>
        LockOnTargeting NewSeeingLockOn(out PlayerInputGate gate, out Health birdy)
        {
            var lockOn = NewLockOn(out gate);
            SetRef(lockOn, "aimSource", NewHost("Eye", new Vector3(0f, 1.5f, -3f)).transform);
            // Not a DontSave host: FindObjectsByType, which gathers the lock
            // candidates, skips DontSave objects. Still destroyed in TearDown.
            var host = Track(new GameObject("Test_Birdy"));
            host.transform.position = new Vector3(0f, 0f, 3f);
            birdy = host.AddComponent<Health>();
            birdy.ResetToFull();
            Physics.SyncTransforms();
            return lockOn;
        }

        void PressToggle(LockOnTargeting lockOn)
        {
            SetField(lockOn, "_togglePressed", true);       // Tab
            Call(lockOn, "Update");
        }

        static LockOnReticle ReticleOf(LockOnTargeting lockOn) => GetField<LockOnReticle>(lockOn, "_reticle");

        /// <summary>What the reticle was last told to show: None, Hint or Locked.</summary>
        static string Showing(LockOnReticle reticle) => GetField<object>(reticle, "_mode").ToString();

        static bool AnyRendererOn(LockOnReticle reticle)
        {
            foreach (var r in reticle.GetComponentsInChildren<MeshRenderer>(true))
                if (r.enabled) return true;
            return false;
        }

        [Test]
        public void LockOn_AClaimReleasesTheLock_AndHidesTheRing()
        {
            var lockOn = NewSeeingLockOn(out var gate, out var birdy);
            PressToggle(lockOn);
            Assert.That(lockOn.Target, Is.SameAs(birdy), "precondition: Tab locks Birdy");
            Assert.That(Showing(ReticleOf(lockOn)), Is.EqualTo("Locked"));

            gate.Claim(_dialogue, InputBlock.All);           // the talk press
            Call(lockOn, "Update");

            Assert.That(lockOn.IsLocked, Is.False, "released, as a second Tab would");
            Assert.That(lockOn.Target, Is.Null);
            Assert.That(Showing(ReticleOf(lockOn)), Is.EqualTo("None"), "no ring, and no hint over Birdy");
        }

        [Test]
        public void LockOn_NoHintWhileClaimed_AndItComesBackAfter()
        {
            var lockOn = NewSeeingLockOn(out var gate, out _);
            Call(lockOn, "Update");
            Assert.That(Showing(ReticleOf(lockOn)), Is.EqualTo("Hint"), "precondition: Birdy is targetable");

            gate.Claim(_dialogue, InputBlock.LockOn);
            for (int i = 0; i < 3; i++) Call(lockOn, "Update");
            Assert.That(Showing(ReticleOf(lockOn)), Is.EqualTo("None"));

            gate.Release(_dialogue);
            Call(lockOn, "Update");
            Assert.That(Showing(ReticleOf(lockOn)), Is.EqualTo("Hint"));
            Assert.That(lockOn.IsLocked, Is.False, "nothing is locked after the talk");
        }

        [Test]
        public void LockOn_AfterTheClaim_TabLocksAgainAtOnce()
        {
            var lockOn = NewSeeingLockOn(out var gate, out var birdy);
            PressToggle(lockOn);
            gate.Claim(_dialogue, InputBlock.All);
            Call(lockOn, "Update");
            gate.Release(_dialogue);
            Call(lockOn, "Update");

            PressToggle(lockOn);

            Assert.That(lockOn.Target, Is.SameAs(birdy), "one press, not a stale toggle to undo first");
            Assert.That(Showing(ReticleOf(lockOn)), Is.EqualTo("Locked"));
        }

        /// <summary>Generic, not dialogue-only: any LockOn claimant (a spell aim)
        /// ends targeting, and nothing comes back until the last claim lifts.</summary>
        [Test]
        public void LockOn_TwoClaimants_NothingReturnsUntilBothRelease()
        {
            var aiming = new object();
            var lockOn = NewSeeingLockOn(out var gate, out var birdy);
            PressToggle(lockOn);

            gate.Claim(_dialogue, InputBlock.All);
            gate.Claim(aiming, InputBlock.LockOn);
            Call(lockOn, "Update");
            Assert.That(lockOn.IsLocked, Is.False);

            gate.Release(_dialogue);
            SetField(lockOn, "_togglePressed", true);       // Tab while the aim still holds LockOn
            Call(lockOn, "Update");
            Assert.That(lockOn.IsLocked, Is.False, "one claim left: still no lock");
            Assert.That(Showing(ReticleOf(lockOn)), Is.EqualTo("None"), "one claim left: still no marker");

            gate.Release(aiming);
            Call(lockOn, "Update");
            Assert.That(lockOn.IsLocked, Is.False, "the dropped Tab never fires");
            Assert.That(Showing(ReticleOf(lockOn)), Is.EqualTo("Hint"), "both gone: the hint is back");

            PressToggle(lockOn);
            Assert.That(lockOn.Target, Is.SameAs(birdy), "and Tab locks again");
        }

        [Test]
        public void LockOn_OtherFlagsKeepTheLock()
        {
            var lockOn = NewSeeingLockOn(out var gate, out var birdy);
            PressToggle(lockOn);

            gate.Claim(_dialogue, InputBlock.All & ~InputBlock.LockOn);
            Call(lockOn, "Update");

            Assert.That(lockOn.Target, Is.SameAs(birdy));
            Assert.That(Showing(ReticleOf(lockOn)), Is.EqualTo("Locked"));
        }

        /// <summary>
        /// The ring's release fade must be over before the conversation's settle
        /// ends and time stops; a fade on scaled time would freeze half-visible.
        /// Time is stopped from the start here, the worst case, and the reticle
        /// is driven with the unscaled step its LateUpdate passes.
        /// </summary>
        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void LockOn_TheRingIsGoneWithinTheSettle_WithTimeStopped(float dt)
        {
            var lockOn = NewSeeingLockOn(out var gate, out _);
            PressToggle(lockOn);
            var reticle = ReticleOf(lockOn);
            for (float t = 0f; t < 1f; t += dt) reticle.Tick(dt);
            Assert.That(AnyRendererOn(reticle), Is.True, "precondition: the ring is up");

            float settle = NewConfig().SettleSeconds;
            Time.timeScale = 0f;
            gate.Claim(_dialogue, InputBlock.All);
            for (float t = 0f; t < settle; t += dt)
            {
                Call(lockOn, "Update");
                reticle.Tick(dt);
            }

            Assert.That(AnyRendererOn(reticle), Is.False, $"still drawn {settle} s after the talk press");
        }

        // -------------------------------------------------------------- tether

        [Test]
        public void Tether_MarkerHidesWhileMovementIsClaimed()
        {
            var host = NewHost("Player");
            var gate = host.AddComponent<PlayerInputGate>();
            var tether = host.AddComponent<TetherSystem>();
            SetRef(tether, "aimSource", NewHost("Eye", new Vector3(0f, 1.5f, -3f)).transform);
            Call(tether, "Awake");                           // builds the marker; finds the gate
            var reticle = GetField<LockOnReticle>(tether, "_reticle");
            tether.Attach(NewHost("Anchor", new Vector3(0f, 4f, 4f)).AddComponent<TetherAnchor>());
            Call(tether, "LateUpdate");
            Assert.That(Showing(reticle), Is.EqualTo("Locked"), "precondition: the marker is on the anchor");

            gate.Claim(_dialogue, InputBlock.Sword | InputBlock.LockOn);
            Call(tether, "LateUpdate");
            Assert.That(Showing(reticle), Is.EqualTo("Locked"), "the tether is Movement's; other flags keep it");

            gate.Claim(_dialogue, InputBlock.All);
            Call(tether, "LateUpdate");
            Assert.That(Showing(reticle), Is.EqualTo("None"));

            gate.Release(_dialogue);
            Call(tether, "LateUpdate");
            Assert.That(Showing(reticle), Is.EqualTo("Locked"), "back once the claim ends");
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
