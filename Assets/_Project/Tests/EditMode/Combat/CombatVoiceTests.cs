using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Combat;

namespace Pickleball.Tests.Combat
{
    /// <summary>The grunt pattern and the no-repeat dealing behind the combat voice.</summary>
    public class CombatVoiceTests
    {
        const GruntTier L = GruntTier.Light, M = GruntTier.Medium, H = GruntTier.Heavy;

        static GruntTier[] Ramp(int hits)
        {
            var tiers = new GruntTier[hits];
            for (int i = 0; i < hits; i++) tiers[i] = GruntLadder.TierForOrdinaryHit(i, hits);
            return tiers;
        }

        [Test]
        public void Lone_hit_sits_in_the_middle() => Assert.That(Ramp(1), Is.EqualTo(new[] { M }));

        [Test]
        public void Ruler_chain_opens_light_and_peaks_heavy() => Assert.That(Ramp(2), Is.EqualTo(new[] { L, H }));

        [Test]
        public void Compass_chain_climbs_one_tier_per_hit() => Assert.That(Ramp(3), Is.EqualTo(new[] { L, M, H }));

        [Test]
        public void Extended_chains_stretch_the_ramp()
        {
            Assert.That(Ramp(4), Is.EqualTo(new[] { L, M, M, H }));
            Assert.That(Ramp(5), Is.EqualTo(new[] { L, M, M, H, H }));
            Assert.That(Ramp(6), Is.EqualTo(new[] { L, L, M, M, H, H }));
        }

        [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(9)]
        public void Ramp_never_falls_and_spans_light_to_heavy(int hits)
        {
            var tiers = Ramp(hits);
            Assert.That(tiers[0], Is.EqualTo(L));
            Assert.That(tiers[^1], Is.EqualTo(H));
            for (int i = 1; i < hits; i++) Assert.That(tiers[i], Is.GreaterThanOrEqualTo(tiers[i - 1]));
        }

        [Test]
        public void Index_past_the_end_stays_heavy()
            => Assert.That(GruntLadder.TierForOrdinaryHit(7, 3), Is.EqualTo(H));

        static List<ResolvedStep> Chain(params ComboRole[] roles)
        {
            var chain = new List<ResolvedStep>();
            for (int i = 0; i < roles.Length; i++)
                chain.Add(new ResolvedStep(null, roles[i], ComboTrack.Ground, i, i == roles.Length - 1));
            return chain;
        }

        [Test]
        public void Finisher_gets_the_finisher_tier_at_any_chain_length()
        {
            var shortChain = Chain(ComboRole.Regular, ComboRole.Finisher);
            var longChain = Chain(ComboRole.Regular, ComboRole.Regular, ComboRole.Regular,
                                  ComboRole.Extender, ComboRole.Extender, ComboRole.Finisher);
            Assert.That(GruntLadder.TierFor(shortChain[^1], shortChain), Is.EqualTo(GruntTier.Finisher));
            Assert.That(GruntLadder.TierFor(longChain[^1], longChain), Is.EqualTo(GruntTier.Finisher));
        }

        [Test]
        public void Hit_before_the_finisher_is_heavy_whatever_the_length()
        {
            var chain = Chain(ComboRole.Regular, ComboRole.Regular, ComboRole.Regular,
                              ComboRole.Extender, ComboRole.Finisher);
            Assert.That(GruntLadder.TierFor(chain[^2], chain), Is.EqualTo(GruntTier.Heavy));
        }

        [Test]
        public void Last_hit_of_a_chain_with_no_finisher_does_not_yell()
        {
            var air = Chain(ComboRole.Regular);
            Assert.That(air[0].IsLast, Is.True);
            Assert.That(GruntLadder.TierFor(air[0], air), Is.EqualTo(GruntTier.Medium));
        }

        [Test]
        public void Bag_deals_every_index_before_any_repeat()
        {
            var bag = new ShuffleBag(8, new System.Random(1));
            for (int round = 0; round < 20; round++)
            {
                var seen = new HashSet<int>();
                for (int i = 0; i < 8; i++) Assert.That(seen.Add(bag.Next()), Is.True);
            }
        }

        [TestCase(2)] [TestCase(3)] [TestCase(8)]
        public void Bag_never_repeats_across_a_reshuffle(int count)
        {
            var bag = new ShuffleBag(count, new System.Random(2));
            int previous = bag.Next();
            for (int i = 0; i < 2000; i++)
            {
                int next = bag.Next();
                Assert.That(next, Is.Not.EqualTo(previous));
                previous = next;
            }
        }

        [Test]
        public void Small_bags_are_safe()
        {
            Assert.That(new ShuffleBag(0, new System.Random(3)).Next(), Is.EqualTo(-1));
            var one = new ShuffleBag(1, new System.Random(3));
            Assert.That(one.Next(), Is.EqualTo(0));
            Assert.That(one.Next(), Is.EqualTo(0));
        }
    }
}
