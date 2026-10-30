using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Combat;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// How learned abilities apply to the weapon in hand (<see cref="SwordMoveset.StepFor"/>).
    /// Abilities belong to the player and outlive a weapon swap, but their swings are
    /// timed for the weapon they were authored on, so a weapon can give an ability its
    /// own swing, turn it off, or (the compass) take it as authored. The ability always
    /// keeps its place in the chain; only the swing changes.
    /// </summary>
    public class WeaponAbilityRuleTests : CombatTestFixture
    {
        ComboResolver _resolver;
        ComboStepDefinition _g1, _g2, _gFinish, _a1;
        SwordAbilityDefinition _plusC, _phase, _groundFinisher, _airFinisher, _opener;
        List<SwordAbilityDefinition> _learned;

        [SetUp]
        public void Build()
        {
            _resolver = new ComboResolver();
            _g1 = Step("G1");
            _g2 = Step("G2");
            _gFinish = Step("GFinish");
            _a1 = Step("A1");
            _plusC = Ability("PlusC", ComboRole.Extender, ComboTrack.Ground, chainOrder: 10);
            _phase = Ability("Phase", ComboRole.Extender, ComboTrack.Air, chainOrder: 10);
            _groundFinisher = Ability("Riemann", ComboRole.Finisher, ComboTrack.Ground);
            _airFinisher = Ability("UnitCircle", ComboRole.Finisher, ComboTrack.Air);
            _opener = Ability("Secant", ComboRole.Opener, ComboTrack.Ground);
            _learned = new List<SwordAbilityDefinition> { _plusC, _phase, _groundFinisher, _airFinisher, _opener };
        }

        SwordMoveset Weapon(bool acceptOthers, params AbilityStepOverride[] rows)
            => Moveset(new[] { _g1, _g2 }, _gFinish, new[] { _a1 }, null).WithAbilityRules(acceptOthers, rows);

        List<ComboStepDefinition> Chain(SwordMoveset moveset, ComboContext context)
        {
            var steps = new List<ComboStepDefinition>();
            foreach (var r in _resolver.BuildSequence(context, moveset, _learned)) steps.Add(r.Step);
            return steps;
        }

        // -------------------------------------------------------------- StepFor

        [Test]
        public void StepFor_DefaultMoveset_UsesTheAbilitysOwnStep()
        {
            var compass = Moveset(new[] { _g1 }, null, new[] { _a1 }, null);

            Assert.That(compass.AcceptsOtherAbilities, Is.True, "A moveset must accept abilities unless told not to.");
            Assert.That(compass.StepFor(_plusC), Is.SameAs(_plusC.Step));
            Assert.That(compass.StepFor(null), Is.Null);
        }

        [Test]
        public void StepFor_Row_WinsOverTheAbilitysStepEitherWay()
        {
            var heavy = Step("HeavyPlusC");
            var open = Weapon(true, new AbilityStepOverride(_plusC, heavy), new AbilityStepOverride(_phase, null));
            var closed = Weapon(false, new AbilityStepOverride(_plusC, heavy), new AbilityStepOverride(_phase, null));

            Assert.That(open.StepFor(_plusC), Is.SameAs(heavy));
            Assert.That(closed.StepFor(_plusC), Is.SameAs(heavy));
            Assert.That(open.StepFor(_phase), Is.Null, "An empty row blocks even when others are accepted.");
            Assert.That(closed.StepFor(_phase), Is.Null);
        }

        [Test]
        public void StepFor_ClosedMoveset_DeclinesUnlistedAbilities()
        {
            var closed = Weapon(false);

            Assert.That(closed.StepFor(_plusC), Is.Null);
            Assert.That(closed.StepFor(_groundFinisher), Is.Null);
        }

        // ------------------------------------------------------------- resolver

        [Test]
        public void Resolve_AcceptingMovesetWithNoRows_PlaysExactlyAsBefore()
        {
            var compass = Weapon(true);

            Assert.That(Chain(compass, Ground(1)),
                        Is.EqualTo(new[] { _g1, _g2, _plusC.Step, _groundFinisher.Step }));
            Assert.That(Chain(compass, Ground(0))[0], Is.SameAs(_opener.Step));
            Assert.That(Chain(compass, Air(0)), Is.EqualTo(new[] { _a1, _phase.Step, _airFinisher.Step }));
        }

        [Test]
        public void Resolve_OverriddenExtender_PlaysTheWeaponsStepInTheAbilitysPlace()
        {
            var heavy = Step("HeavyPlusC");
            var ruler = Weapon(false, new AbilityStepOverride(_plusC, heavy));

            var seq = _resolver.BuildSequence(Ground(1), ruler, _learned);

            Assert.That(seq.Count, Is.EqualTo(4), "G1, G2, + C, default finisher.");
            Assert.That(seq[2].Step, Is.SameAs(heavy));
            Assert.That(seq[2].Role, Is.EqualTo(ComboRole.Extender));
            Assert.That(seq[2].Source, Is.SameAs(_plusC), "The ability is still credited, for toasts and effects.");
            Assert.That(seq[3].Step, Is.SameAs(_gFinish), "Unlisted finisher declined: the default cap plays.");
            Assert.That(seq[3].IsLast, Is.True);
        }

        [Test]
        public void Resolve_BlockedExtender_DoesNotLengthenTheChain()
        {
            var ruler = Weapon(true, new AbilityStepOverride(_plusC, null));

            Assert.That(Chain(ruler, Ground(1)), Is.EqualTo(new[] { _g1, _g2, _groundFinisher.Step }));
        }

        [Test]
        public void Resolve_OverriddenFinisher_ReplacesTheDefaultCapWithTheWeaponsStep()
        {
            var heavyRiemann = Step("HeavyRiemann");
            var ruler = Weapon(false, new AbilityStepOverride(_groundFinisher, heavyRiemann));

            var last = _resolver.Resolve(Ground(2), ruler, _learned);

            Assert.That(last.Step, Is.SameAs(heavyRiemann));
            Assert.That(last.Role, Is.EqualTo(ComboRole.Finisher));
            Assert.That(last.Source, Is.SameAs(_groundFinisher));
            Assert.That(last.IsLast, Is.True);
        }

        [Test]
        public void Resolve_BlockedFinisher_FallsBackToTheNextQualifyingOneOrTheDefault()
        {
            var backup = Ability("Backup", ComboRole.Finisher, ComboTrack.Ground, priority: -5);
            _learned.Add(backup);
            var blocksTop = Weapon(true, new AbilityStepOverride(_groundFinisher, null));

            // Ground(3): G1, G2, + C (accepted), then the finisher slot.
            Assert.That(_resolver.Resolve(Ground(3), blocksTop, _learned).Step, Is.SameAs(backup.Step),
                        "A blocked ability must not win the slot and then play nothing.");

            var blocksBoth = Weapon(true, new AbilityStepOverride(_groundFinisher, null),
                                    new AbilityStepOverride(backup, null));
            Assert.That(_resolver.Resolve(Ground(3), blocksBoth, _learned).Step, Is.SameAs(_gFinish));
        }

        [Test]
        public void Resolve_BlockedAirFinisher_LeavesTheAirChainWithoutOne()
        {
            var ruler = Weapon(false, new AbilityStepOverride(_phase, Step("Chop")),
                               new AbilityStepOverride(_airFinisher, null));

            var seq = _resolver.BuildSequence(Air(0), ruler, _learned);

            Assert.That(seq.Count, Is.EqualTo(2));
            Assert.That(seq[1].Step.name, Is.EqualTo("Chop"));
            Assert.That(seq[1].IsLast, Is.True);
        }

        [Test]
        public void Resolve_DeclinedOpener_KeepsTheWeaponsFirstHit()
        {
            var ruler = Weapon(false);

            Assert.That(_resolver.Resolve(Ground(0), ruler, _learned).Step, Is.SameAs(_g1));
        }

        [Test]
        public void Resolve_OverriddenExtenders_KeepTheAbilitiesChainOrder()
        {
            var second = Ability("Induction", ComboRole.Extender, ComboTrack.Ground, chainOrder: 5);
            _learned.Add(second);         // learned after + C: irrelevant to the order
            var heavyPlus = Step("HeavyPlusC");
            var heavyInduction = Step("HeavyInduction");
            var ruler = Weapon(false, new AbilityStepOverride(_plusC, heavyPlus),
                               new AbilityStepOverride(second, heavyInduction));

            var chain = Chain(ruler, Ground(1));

            Assert.That(chain.IndexOf(heavyInduction), Is.LessThan(chain.IndexOf(heavyPlus)),
                        "chainOrder 5 before 10, as on the compass.");
        }
    }
}
