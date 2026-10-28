using NUnit.Framework;
using Pickleball.Sandbox;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// Pins the gate's claim semantics: per-owner, per-flag, idempotent, and
    /// unable to clear another owner's claim. A conversation and a later
    /// "aiming" mode must be able to overlap without stomping each other.
    /// </summary>
    public class PlayerInputGateTests : DialogueTestFixture
    {
        PlayerInputGate NewGate() => NewHost("Gate").AddComponent<PlayerInputGate>();

        readonly object _dialogue = new();
        readonly object _aiming = new();

        [Test]
        public void NoClaims_NothingBlocked()
        {
            var gate = NewGate();
            Assert.That(gate.Blocked, Is.EqualTo(InputBlock.None));
            Assert.That(gate.IsBlocked(InputBlock.All), Is.False);
        }

        [Test]
        public void Claim_BlocksOnlyItsFlags()
        {
            var gate = NewGate();
            gate.Claim(_aiming, InputBlock.Sword | InputBlock.LockOn);

            Assert.That(gate.IsBlocked(InputBlock.Sword), Is.True);
            Assert.That(gate.IsBlocked(InputBlock.LockOn), Is.True);
            Assert.That(gate.IsBlocked(InputBlock.Movement), Is.False);
            Assert.That(gate.IsBlocked(InputBlock.InteractButton), Is.False);
        }

        [Test]
        public void TwoOverlappingClaimants_ReleasingOneKeepsTheOther()
        {
            var gate = NewGate();
            gate.Claim(_dialogue, InputBlock.All);
            gate.Claim(_aiming, InputBlock.Sword | InputBlock.LockOn);

            gate.Release(_dialogue);

            Assert.That(gate.IsBlocked(InputBlock.Movement), Is.False, "only dialogue blocked movement");
            Assert.That(gate.IsBlocked(InputBlock.Sword), Is.True, "aiming still blocks the sword");
            Assert.That(gate.ClaimOf(_aiming), Is.EqualTo(InputBlock.Sword | InputBlock.LockOn));

            gate.Release(_aiming);
            Assert.That(gate.Blocked, Is.EqualTo(InputBlock.None));
        }

        [Test]
        public void Release_IsIdempotent_AndAStrangerCannotRelease()
        {
            var gate = NewGate();
            gate.Claim(_dialogue, InputBlock.Movement);

            gate.Release(_aiming);          // never claimed
            Assert.That(gate.IsBlocked(InputBlock.Movement), Is.True);

            gate.Release(_dialogue);
            gate.Release(_dialogue);        // again
            Assert.That(gate.IsBlocked(InputBlock.Movement), Is.False);
        }

        [Test]
        public void Claim_ByTheSameOwner_ReplacesItsFlags()
        {
            var gate = NewGate();
            gate.Claim(_dialogue, InputBlock.All);
            gate.Claim(_dialogue, InputBlock.InteractButton);

            Assert.That(gate.Blocked, Is.EqualTo(InputBlock.InteractButton));
            gate.Claim(_dialogue, InputBlock.None);
            Assert.That(gate.Blocked, Is.EqualTo(InputBlock.None));
        }

        [Test]
        public void ReleaseAll_DropsEveryClaim()
        {
            var gate = NewGate();
            gate.Claim(_dialogue, InputBlock.All);
            gate.Claim(_aiming, InputBlock.Sword);

            gate.ReleaseAll();

            Assert.That(gate.Blocked, Is.EqualTo(InputBlock.None));
            Assert.That(gate.ClaimOf(_dialogue), Is.EqualTo(InputBlock.None));
        }

        // ------------------------------------------------- latched presses

        [Test]
        public void TakeLatched_PressDuringAClaim_IsDropped_AndDoesNotFireAfterRelease()
        {
            var gate = NewGate();
            gate.Claim(_dialogue, InputBlock.All);
            bool latched = true;                       // Tab pressed mid-conversation

            Assert.That(PlayerInputGate.TakeLatched(ref latched, gate, InputBlock.LockOn), Is.False);
            Assert.That(latched, Is.False, "cleared, not kept for later");

            gate.Release(_dialogue);
            Assert.That(PlayerInputGate.TakeLatched(ref latched, gate, InputBlock.LockOn), Is.False,
                        "nothing fires on the frame the claim ends");
        }

        [Test]
        public void TakeLatched_UnblockedPress_FiresOnce()
        {
            var gate = NewGate();
            gate.Claim(_aiming, InputBlock.Sword);      // a different flag
            bool latched = true;

            Assert.That(PlayerInputGate.TakeLatched(ref latched, gate, InputBlock.LockOn), Is.True);
            Assert.That(PlayerInputGate.TakeLatched(ref latched, gate, InputBlock.LockOn), Is.False);
        }

        [Test]
        public void TakeLatched_WithNoGate_PassesThroughAndClears()
        {
            bool latched = true;
            Assert.That(PlayerInputGate.TakeLatched(ref latched, null, InputBlock.Spacetime), Is.True);
            Assert.That(latched, Is.False);
        }

        [Test]
        public void Claim_WithANullOwner_Throws()
        {
            var gate = NewGate();
            Assert.Throws<System.ArgumentNullException>(() => gate.Claim(null, InputBlock.Sword));
        }
    }
}
