using NUnit.Framework;
using Pickleball.Sandbox.Combat;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// The two chain counters run on different clocks: the ground chain is
    /// dropped whenever a swing goes unchained, the air chain only refills on
    /// landing. That asymmetry is what makes Air Combo Plus worth learning, so
    /// it is pinned here.
    /// </summary>
    public class ComboProgressTests
    {
        [Test]
        public void GroundChain_CountsUpAndDropsWhenTheChainEnds()
        {
            var p = new ComboProgress();
            p.RecordStep(ComboTrack.Ground);
            p.RecordStep(ComboTrack.Ground);
            Assert.That(p.HitsIn(ComboTrack.Ground), Is.EqualTo(2));

            p.DropChain();
            Assert.That(p.HitsIn(ComboTrack.Ground), Is.EqualTo(0));
        }

        [Test]
        public void AirChain_SurvivesADroppedChainButRefillsOnLanding()
        {
            var p = new ComboProgress();
            p.RecordStep(ComboTrack.Air);
            p.DropChain();
            Assert.That(p.HitsIn(ComboTrack.Air), Is.EqualTo(1), "one jump buys one air combo");

            p.Land();
            Assert.That(p.HitsIn(ComboTrack.Air), Is.EqualTo(0));
        }

        [Test]
        public void StartingAnAirStep_AbandonsTheGroundChain()
        {
            var p = new ComboProgress();
            p.RecordStep(ComboTrack.Ground);
            p.RecordStep(ComboTrack.Ground);
            p.RecordStep(ComboTrack.Air);
            Assert.That(p.HitsIn(ComboTrack.Ground), Is.EqualTo(0));
            Assert.That(p.HitsIn(ComboTrack.Air), Is.EqualTo(1));
        }

        [Test]
        public void LeavingTheGround_DropsTheGroundChainOnly()
        {
            var p = new ComboProgress();
            p.RecordStep(ComboTrack.Air);
            p.Land();
            p.RecordStep(ComboTrack.Ground);
            p.LeaveGround();
            Assert.That(p.HitsIn(ComboTrack.Ground), Is.EqualTo(0));
            Assert.That(p.HitsIn(ComboTrack.Air), Is.EqualTo(0));
        }
    }
}
