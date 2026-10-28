using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Dialogue;
using UnityEngine;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// E is both Talk and Dash. These pin the arbitration through the gate: a
    /// prompt claims the Interact button (so the movement input drops the dash),
    /// no prompt leaves E to dash, a press talks only with a target, the claim
    /// outlasts the conversation by its cooldown, and lock-on decides who is
    /// talkable. The movement side of the contract (Read drops DashPressed while
    /// InteractButton is claimed) is a one-line check on the gate state tested here.
    /// </summary>
    public class PlayerInteractorTests : DialogueTestFixture
    {
        const float Frame = 1f / 60f;

        DialogueConfig _config;
        TalkableSet _set;
        PlayerInputGate _gate;
        DialogueDirector _director;
        PlayerInteractor _interactor;
        GameObject _player;

        [SetUp]
        public void SetUpPlayer()
        {
            _config = NewConfig();
            SetFloat(_config, "settleSeconds", 0f);
            SetFloat(_config, "advanceLockoutSeconds", 0f);
            SetBool(_config, "pauseAudio", false);
            _set = NewSet();

            _player = NewHost("Player");
            _gate = _player.AddComponent<PlayerInputGate>();
            _director = NewDirector(_config, _gate);
            _interactor = _player.AddComponent<PlayerInteractor>();
            SetRef(_interactor, "config", _config);
            SetRef(_interactor, "talkables", _set);
            SetRef(_interactor, "director", _director);
            SetRef(_interactor, "inputGate", _gate);
        }

        Talkable NpcAt(Vector3 position)
        {
            var npc = NewTalkable(Definition("Hi.", "Bye."), position);
            _set.Add(npc);
            return npc;
        }

        [Test]
        public void Prompt_ClaimsTheInteractButton_SoEDoesNotDash()
        {
            var npc = NpcAt(new Vector3(0f, 0f, 1.5f));

            _interactor.Tick(Frame, false);

            Assert.That(_interactor.CurrentTarget, Is.SameAs(npc));
            Assert.That(_gate.IsBlocked(InputBlock.InteractButton), Is.True);
            Assert.That(_gate.IsBlocked(InputBlock.Movement), Is.False, "walking is untouched");
        }

        [Test]
        public void NoPrompt_LeavesEToDash()
        {
            NpcAt(new Vector3(0f, 0f, 12f));

            _interactor.Tick(Frame, false);

            Assert.That(_interactor.CurrentTarget, Is.Null);
            Assert.That(_gate.IsBlocked(InputBlock.InteractButton), Is.False);
        }

        [Test]
        public void Press_WithATarget_StartsTheTalk_AndHidesThePrompt()
        {
            var npc = NpcAt(new Vector3(0f, 0f, 1.5f));
            Talkable lastChange = npc;
            _interactor.TargetChanged += t => lastChange = t;
            _interactor.Tick(Frame, false);

            _interactor.Tick(Frame, true);

            Assert.That(_director.Current, Is.SameAs(npc));
            Assert.That(_interactor.CurrentTarget, Is.Null);
            Assert.That(lastChange, Is.Null, "the prompt heard that it should hide");
            Assert.That(_gate.IsBlocked(InputBlock.InteractButton), Is.True);
        }

        [Test]
        public void Press_WithoutATarget_DoesNothing()
        {
            NpcAt(new Vector3(0f, 0f, 12f));
            _interactor.Tick(Frame, true);
            Assert.That(_director.IsBusy, Is.False);
        }

        [Test]
        public void Press_OnTheFrameTheTalkCloses_NeitherReopensNorDashes()
        {
            NpcAt(new Vector3(0f, 0f, 1.5f));
            _interactor.Tick(Frame, false);
            _interactor.Tick(Frame, true);                     // talk
            _director.Tick(_config.TextStartDelaySeconds + 0.01f, false);
            _director.Tick(Frame, true);                        // reveal "Hi."
            _director.Tick(Frame, true);                        // "Bye."
            _director.Tick(Frame, true);                        // reveal
            _director.Tick(Frame, true);                        // finish → closing
            _director.Tick(_config.CloseSeconds + 0.01f, false);   // ended, releasing

            _interactor.Tick(Frame, true);                      // the closing E, again
            Assert.That(_director.Current, Is.Null, "no reopen while releasing");
            Assert.That(_gate.IsBlocked(InputBlock.InteractButton), Is.True, "no dash either");

            _director.Tick(Frame, false);                       // input released, cooldown starts
            _interactor.Tick(Frame, true);
            Assert.That(_director.IsBusy, Is.False, "no reopen during the cooldown");
            Assert.That(_gate.IsBlocked(InputBlock.InteractButton), Is.True, "still no dash");

            _director.Tick(_config.RestartCooldownSeconds + 0.01f, false);
            _interactor.Tick(Frame, false);
            Assert.That(_interactor.CurrentTarget, Is.Not.Null, "the prompt is back");
            _interactor.Tick(Frame, true);
            Assert.That(_director.IsBusy, Is.True, "a fresh press talks again");
        }

        [Test]
        public void LockedOnToANonTalker_NoPrompt_EStaysDash()
        {
            NpcAt(new Vector3(0f, 0f, 1.5f));
            var newton = NewHost("Newton", new Vector3(0f, 0f, 8f)).AddComponent<Health>();
            newton.ResetToFull();
            LockOn(newton);

            _interactor.Tick(Frame, false);

            Assert.That(_interactor.CurrentTarget, Is.Null);
            Assert.That(_gate.IsBlocked(InputBlock.InteractButton), Is.False);
        }

        [Test]
        public void LockedOnToATalker_ItWins_EvenBehindThePlayer()
        {
            NpcAt(new Vector3(0f, 0f, 1.5f));
            var behind = NpcAt(new Vector3(0f, 0f, -3f));
            var health = behind.gameObject.AddComponent<Health>();
            health.ResetToFull();
            LockOn(health);

            _interactor.Tick(Frame, false);

            Assert.That(_interactor.CurrentTarget, Is.SameAs(behind));
        }

        void LockOn(Health target)
        {
            var lockOn = _player.AddComponent<LockOnTargeting>();
            typeof(LockOnTargeting).GetProperty(nameof(LockOnTargeting.Target)).SetValue(lockOn, target);
            SetRef(_interactor, "lockOn", lockOn);
            Assert.That(lockOn.IsLocked, Is.True);
        }
    }
}
