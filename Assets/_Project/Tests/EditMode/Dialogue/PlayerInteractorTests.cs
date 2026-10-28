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
        [Ignore("Known bug DLG-R2-1: mashing E to skip text reopens the same conversation 0.35 s after it " +
                "closes (playtest: 3 conversations in 7 s at 15 Hz and at 6.7 Hz). " +
                "See Pickball-playtest/reports/R-dialogue-birdnpc.md.")]
        public void MashingE_ThroughTheEnd_DoesNotReopenTheTalk()
        {
            NpcAt(new Vector3(0f, 0f, 1.5f));
            _interactor.Tick(Frame, false);
            _interactor.Tick(Frame, true);                      // talk
            _director.Tick(_config.TextStartDelaySeconds + 0.01f, false);
            int reopened = 0;
            _director.ConversationStarting += _ => reopened++;

            // A press every 4 frames (15 Hz) for 2 s: through both lines, the close,
            // the release and well past the restart cooldown, the way a player
            // mashes to skip text. Same frame order as the game: interactor, director.
            for (int frame = 0; frame < 120; frame++)
            {
                bool press = frame % 4 == 0;
                _interactor.Tick(Frame, press);
                _director.Tick(Frame, press);
            }

            Assert.That(reopened, Is.EqualTo(0), "a mashed E must not start the conversation again");

            // Once the player stops pressing, a fresh press after the cooldown talks again.
            for (int frame = 0; frame < 60; frame++) { _interactor.Tick(Frame, false); _director.Tick(Frame, false); }
            _interactor.Tick(Frame, true);
            Assert.That(_director.IsBusy, Is.True);
        }

        [Test]
        public void NpcWhosePromptIsOffScreen_IsNotATarget_AndEStaysDash()
        {
            // The prompt view only draws a pill that is on screen; E must only talk
            // when that pill is drawn.
            var npc = NpcAt(new Vector3(0f, 0f, 1.5f));
            var view = NewHost("ViewCamera", new Vector3(0f, 1.6f, 3f)).AddComponent<Camera>();
            view.transform.rotation = Quaternion.LookRotation(Vector3.forward);   // NPC behind it
            SetRef(_interactor, "viewCamera", view);

            _interactor.Tick(Frame, true);
            Assert.That(_interactor.CurrentTarget, Is.Null);
            Assert.That(_director.IsBusy, Is.False);
            Assert.That(_gate.IsBlocked(InputBlock.InteractButton), Is.False);

            view.transform.rotation = Quaternion.LookRotation(Vector3.back);      // now facing it
            _interactor.Tick(Frame, false);
            Assert.That(_interactor.CurrentTarget, Is.SameAs(npc));
            Assert.That(_gate.IsBlocked(InputBlock.InteractButton), Is.True);
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
