using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Dialogue;
using Pickleball.Sandbox.Movement;
using UnityEngine;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// The player turns to face the NPC while a talk settles, through the
    /// motor's existing facing hook, and the hook is handed back exactly: to the
    /// source that was there, never over one that replaced it, and at the pause.
    /// </summary>
    public class TalkFacingTests : DialogueTestFixture
    {
        sealed class OtherSource : IFacingSource
        {
            public bool TryGetFacingTarget(out Vector3 point, out bool urgent)
            {
                point = default;
                urgent = false;
                return false;
            }
        }

        static readonly Vector3 NpcHead = new(1f, 1.2f, 3f);

        [Test]
        public void Begin_AsksUrgentlyToFaceTheNpc_AndEndHandsBackTheOriginal()
        {
            var player = NewHost("Player");
            var motor = player.AddComponent<MovementMotor>();
            var original = new OtherSource();
            motor.FacingSource = original;
            var facing = new TalkFacing();

            Assert.That(facing.Begin(player.transform, NpcHead), Is.True);
            Assert.That(motor.FacingSource, Is.SameAs(facing));
            Assert.That(motor.FacingSource.TryGetFacingTarget(out Vector3 point, out bool urgent), Is.True);
            Assert.That(point, Is.EqualTo(NpcHead));
            Assert.That(urgent, Is.True, "a fast turn, even while still sliding to a stop");

            facing.End();
            Assert.That(motor.FacingSource, Is.SameAs(original));
            facing.End();
            Assert.That(motor.FacingSource, Is.SameAs(original), "a second End changes nothing");
        }

        [Test]
        public void End_NeverOverwritesASourceThatReplacedIt()
        {
            var player = NewHost("Player");
            var motor = player.AddComponent<MovementMotor>();
            var facing = new TalkFacing();
            facing.Begin(player.transform, NpcHead);
            var newer = new OtherSource();
            motor.FacingSource = newer;

            facing.End();

            Assert.That(motor.FacingSource, Is.SameAs(newer));
        }

        [Test]
        public void Begin_WithoutAMotor_ChangesNothing()
        {
            var facing = new TalkFacing();

            Assert.That(facing.Begin(NewHost("Player").transform, NpcHead), Is.False);
            Assert.That(facing.Begin(null, NpcHead), Is.False);
            Assert.That(facing.IsActive, Is.False);
        }

        [Test]
        public void Director_FacesTheNpcDuringTheSettle_AndHandsBackAtThePause()
        {
            var config = NewConfig();
            SetBool(config, "pauseAudio", false);
            var player = NewHost("Player");
            var gate = player.AddComponent<PlayerInputGate>();
            var motor = player.AddComponent<MovementMotor>();
            var original = new OtherSource();
            motor.FacingSource = original;
            var director = NewDirector(config, gate);
            var npc = NewTalkable(Definition("Hello."), new Vector3(0f, 0f, 2f));

            Assert.That(director.TryStart(npc, player.transform), Is.True);
            Assert.That(director.State, Is.EqualTo(DialogueDirector.Phase.Settling));
            Assert.That(motor.FacingSource, Is.InstanceOf<TalkFacing>(), "turning toward the NPC while settling");
            motor.FacingSource.TryGetFacingTarget(out Vector3 point, out _);
            Assert.That(point, Is.EqualTo(npc.FocusPosition));

            director.Tick(config.SettleSeconds + 0.01f, false);
            Assert.That(Time.timeScale, Is.EqualTo(0f));
            Assert.That(motor.FacingSource, Is.SameAs(original), "handed back once time stops");

            director.Abort();
        }

        [Test]
        public void Director_AbortDuringTheSettle_HandsFacingBack()
        {
            var config = NewConfig();
            SetBool(config, "pauseAudio", false);
            var player = NewHost("Player");
            var gate = player.AddComponent<PlayerInputGate>();
            var motor = player.AddComponent<MovementMotor>();
            var original = new OtherSource();
            motor.FacingSource = original;
            var director = NewDirector(config, gate);
            var npc = NewTalkable(Definition("Hello."), new Vector3(0f, 0f, 2f));

            director.TryStart(npc, player.transform);
            director.Abort();

            Assert.That(motor.FacingSource, Is.SameAs(original));
        }

        [Test]
        public void Director_TurningOff_LeavesFacingAlone()
        {
            var config = NewConfig();
            SetBool(config, "pauseAudio", false);
            SetBool(config, "turnToFaceNpc", false);
            var player = NewHost("Player");
            var gate = player.AddComponent<PlayerInputGate>();
            var motor = player.AddComponent<MovementMotor>();
            var original = new OtherSource();
            motor.FacingSource = original;
            var director = NewDirector(config, gate);
            var npc = NewTalkable(Definition("Hello."), new Vector3(0f, 0f, 2f));

            director.TryStart(npc, player.transform);

            Assert.That(motor.FacingSource, Is.SameAs(original));
            director.Abort();
        }
    }
}
