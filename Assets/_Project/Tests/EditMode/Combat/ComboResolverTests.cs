using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Combat;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// Pins down the combo rules players learn by feel: Combo Plus adds a hit
    /// before the finisher, a finisher ability replaces the default cap, the
    /// air chain is its own thing, openers only ever replace the first hit, and
    /// the order never depends on the order abilities were learned in. A tuning
    /// pass can change any number; changing what a press does has to get past
    /// these first.
    /// </summary>
    public class ComboResolverTests : CombatTestFixture
    {
        ComboResolver _resolver;
        ComboStepDefinition _g1, _g2, _g3, _gFinish, _a1;
        SwordMoveset _moveset;

        [SetUp]
        public void BuildMoveset()
        {
            _resolver = new ComboResolver();
            _g1 = Step("G1");
            _g2 = Step("G2");
            _g3 = Step("G3");
            _gFinish = Step("GFinish");
            _a1 = Step("A1");
            _moveset = Moveset(new[] { _g1, _g2, _g3 }, _gFinish, new[] { _a1 }, null);
        }

        static readonly List<SwordAbilityDefinition> None = new();

        [Test]
        public void Resolve_BaseGroundChain_PlaysHitsThenDefaultFinisherThenStops()
        {
            Assert.That(_resolver.Resolve(Ground(0), _moveset, None).Step, Is.SameAs(_g1));
            Assert.That(_resolver.Resolve(Ground(1), _moveset, None).Step, Is.SameAs(_g2));
            Assert.That(_resolver.Resolve(Ground(2), _moveset, None).Step, Is.SameAs(_g3));

            var last = _resolver.Resolve(Ground(3), _moveset, None);
            Assert.That(last.Step, Is.SameAs(_gFinish));
            Assert.That(last.Role, Is.EqualTo(ComboRole.Finisher));
            Assert.That(last.IsLast, Is.True);

            Assert.That(_resolver.Resolve(Ground(4), _moveset, None).IsValid, Is.False);
        }

        [Test]
        public void Resolve_ComboPlus_InsertsBeforeTheFinisher()
        {
            var plus = Ability("PlusC", ComboRole.Extender, ComboTrack.Ground);
            var learned = new List<SwordAbilityDefinition> { plus };

            var fourth = _resolver.Resolve(Ground(3), _moveset, learned);
            Assert.That(fourth.Step, Is.SameAs(plus.Step));
            Assert.That(fourth.Role, Is.EqualTo(ComboRole.Extender));
            Assert.That(fourth.Source, Is.SameAs(plus));
            Assert.That(fourth.IsLast, Is.False);

            Assert.That(_resolver.Resolve(Ground(4), _moveset, learned).Step, Is.SameAs(_gFinish));
            Assert.That(_resolver.Resolve(Ground(5), _moveset, learned).IsValid, Is.False);
        }

        [Test]
        public void BuildSequence_Extenders_OrderedByChainOrderNotUnlockOrder()
        {
            var late = Ability("Late", ComboRole.Extender, ComboTrack.Ground, chainOrder: 20);
            var early = Ability("Early", ComboRole.Extender, ComboTrack.Ground, chainOrder: 10);

            var learnedLateFirst = new List<SwordAbilityDefinition> { late, early };
            var seq = _resolver.BuildSequence(Ground(0), _moveset, learnedLateFirst);
            Assert.That(seq[3].Step, Is.SameAs(early.Step));
            Assert.That(seq[4].Step, Is.SameAs(late.Step));

            var learnedEarlyFirst = new List<SwordAbilityDefinition> { early, late };
            seq = _resolver.BuildSequence(Ground(0), _moveset, learnedEarlyFirst);
            Assert.That(seq[3].Step, Is.SameAs(early.Step));
            Assert.That(seq[4].Step, Is.SameAs(late.Step));
        }

        [Test]
        public void BuildSequence_EqualChainOrder_FallsBackToNameSoItIsStable()
        {
            var b = Ability("B", ComboRole.Extender, ComboTrack.Ground);
            var a = Ability("A", ComboRole.Extender, ComboTrack.Ground);
            var seq = _resolver.BuildSequence(Ground(0), _moveset, new List<SwordAbilityDefinition> { b, a });
            Assert.That(seq[3].Source, Is.SameAs(a));
            Assert.That(seq[4].Source, Is.SameAs(b));
        }

        [Test]
        public void Resolve_FinisherAbility_ReplacesTheDefaultFinisher()
        {
            var ripple = Ability("Ripple", ComboRole.Finisher, ComboTrack.Ground);
            var learned = new List<SwordAbilityDefinition> { ripple };

            var seq = _resolver.BuildSequence(Ground(0), _moveset, learned);
            Assert.That(seq.Count, Is.EqualTo(4), "a finisher replaces the cap; it does not add a hit");
            Assert.That(seq[3].Step, Is.SameAs(ripple.Step));
            Assert.That(seq[3].IsLast, Is.True);
        }

        [Test]
        public void Resolve_SeveralFinishers_HighestPriorityWhoseConditionHolds()
        {
            var catchAll = Ability("CatchAll", ComboRole.Finisher, ComboTrack.Ground, priority: 0);
            var ranged = Ability("Ranged", ComboRole.Finisher, ComboTrack.Ground, priority: 10,
                                 condition: new ComboCondition(false, minTargetDistance: 3f));
            var learned = new List<SwordAbilityDefinition> { catchAll, ranged };

            var far = new ComboContext(ComboTrack.Ground, 3, hasTarget: true, targetDistance: 5f);
            Assert.That(_resolver.Resolve(far, _moveset, learned).Source, Is.SameAs(ranged));

            var near = new ComboContext(ComboTrack.Ground, 3, hasTarget: true, targetDistance: 1f);
            Assert.That(_resolver.Resolve(near, _moveset, learned).Source, Is.SameAs(catchAll));

            var noTarget = Ground(3);
            Assert.That(_resolver.Resolve(noTarget, _moveset, learned).Source, Is.SameAs(catchAll),
                        "a distance condition needs a target to measure to");
        }

        [Test]
        public void Resolve_AirTrack_UsesAirChainAndIgnoresGroundAbilities()
        {
            var plus = Ability("PlusC", ComboRole.Extender, ComboTrack.Ground);
            var ripple = Ability("Ripple", ComboRole.Finisher, ComboTrack.Ground);
            var learned = new List<SwordAbilityDefinition> { plus, ripple };

            var first = _resolver.Resolve(Air(0), _moveset, learned);
            Assert.That(first.Step, Is.SameAs(_a1));
            Assert.That(first.Track, Is.EqualTo(ComboTrack.Air));
            Assert.That(first.IsLast, Is.True, "base air chain is one hit with no default finisher");
            Assert.That(_resolver.Resolve(Air(1), _moveset, learned).IsValid, Is.False);
        }

        [Test]
        public void Resolve_AirComboPlusAndAirFinisher_ExtendTheAirChain()
        {
            var phase = Ability("PhaseShift", ComboRole.Extender, ComboTrack.Air);
            var circle = Ability("UnitCircle", ComboRole.Finisher, ComboTrack.Air);
            var learned = new List<SwordAbilityDefinition> { circle, phase };

            Assert.That(_resolver.Resolve(Air(0), _moveset, learned).Step, Is.SameAs(_a1));
            Assert.That(_resolver.Resolve(Air(1), _moveset, learned).Step, Is.SameAs(phase.Step));
            var finish = _resolver.Resolve(Air(2), _moveset, learned);
            Assert.That(finish.Step, Is.SameAs(circle.Step));
            Assert.That(finish.IsLast, Is.True);
            Assert.That(_resolver.Resolve(Air(3), _moveset, learned).IsValid, Is.False);
        }

        [Test]
        public void Resolve_Opener_ReplacesOnlyTheFirstHitWhenItsConditionHolds()
        {
            var dash = Ability("SecantLine", ComboRole.Opener, ComboTrack.Ground,
                               condition: new ComboCondition(false, minTargetDistance: 3f));
            var learned = new List<SwordAbilityDefinition> { dash };

            var far = new ComboContext(ComboTrack.Ground, 0, hasTarget: true, targetDistance: 5f);
            var opened = _resolver.Resolve(far, _moveset, learned);
            Assert.That(opened.Step, Is.SameAs(dash.Step));
            Assert.That(opened.Role, Is.EqualTo(ComboRole.Opener));

            var farSecond = new ComboContext(ComboTrack.Ground, 1, hasTarget: true, targetDistance: 5f);
            Assert.That(_resolver.Resolve(farSecond, _moveset, learned).Step, Is.SameAs(_g2),
                        "the chain carries on from its second hit after an opener");

            var near = new ComboContext(ComboTrack.Ground, 0, hasTarget: true, targetDistance: 1f);
            Assert.That(_resolver.Resolve(near, _moveset, learned).Step, Is.SameAs(_g1));
        }

        [Test]
        public void Resolve_IgnoresAbilitiesThatAreNullOrHaveNoStep()
        {
            var empty = Track(SwordAbilityDefinition.CreateRuntime("Empty", ComboRole.Extender,
                                                                  ComboTrack.Ground, null));
            var learned = new List<SwordAbilityDefinition> { null, empty };
            Assert.That(_resolver.BuildSequence(Ground(0), _moveset, learned).Count, Is.EqualTo(4));
        }

        [Test]
        public void Resolve_NoMovesetOrNegativeHits_IsInvalidRatherThanThrowing()
        {
            Assert.That(_resolver.Resolve(Ground(0), null, None).IsValid, Is.False);
            Assert.That(_resolver.Resolve(Ground(-1), _moveset, None).IsValid, Is.False);
            Assert.That(_resolver.Resolve(Ground(0), _moveset, null).Step, Is.SameAs(_g1));
        }

        [Test]
        public void Resolve_IndexMatchesPositionInChain()
        {
            var plus = Ability("PlusC", ComboRole.Extender, ComboTrack.Ground);
            var learned = new List<SwordAbilityDefinition> { plus };
            for (int i = 0; i < 5; i++)
                Assert.That(_resolver.Resolve(Ground(i), _moveset, learned).Index, Is.EqualTo(i));
        }
    }
}
