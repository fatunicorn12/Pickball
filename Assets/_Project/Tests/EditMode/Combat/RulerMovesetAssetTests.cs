using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Pickleball.Sandbox.Combat;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// The shipped ruler moveset keeps its brief against the shipped compass: big, slower
    /// swings that hit harder, paid for with commitment rather than with raw damage per
    /// second. Numbers come from <see cref="ComboTiming"/> over the real step assets.
    ///
    /// Unlike the structural asset checks these pin feel, on purpose: the ruler exists
    /// to be the heavy weapon, and a retune of either sword that quietly erased the
    /// difference should fail here. A deliberate change of the brief updates the ranges
    /// (and Docs/Systems/SwordAbilities.md, "Ruler").
    /// </summary>
    [Category("Assets")]
    public class RulerMovesetAssetTests
    {
        const string DataDir = "Assets/_Project/ScriptableObjects/Combat";
        const string CompassPath = DataDir + "/Moveset_Compass.asset";
        const string RulerPath = DataDir + "/Moveset_Ruler.asset";
        const string RulerStepDir = DataDir + "/Steps/Ruler/";

        SwordMoveset _compass, _ruler;
        ComboResolver _resolver;

        [SetUp]
        public void Load()
        {
            _compass = AssetDatabase.LoadAssetAtPath<SwordMoveset>(CompassPath);
            _ruler = AssetDatabase.LoadAssetAtPath<SwordMoveset>(RulerPath);
            Assert.That(_compass, Is.Not.Null, $"{CompassPath} is missing.");
            Assert.That(_ruler, Is.Not.Null, $"{RulerPath} is missing. Run Tools ▸ Pickleball ▸ Setup Ruler Moveset.");
            _resolver = new ComboResolver();
        }

        List<ResolvedStep> GroundChain(SwordMoveset m)
            => _resolver.BuildSequence(new ComboContext(ComboTrack.Ground, 0), m, null).ToList();

        [Test]
        public void GroundChain_IsAboutHalfAgainAsLong()
        {
            float compass = ComboTiming.ChainSeconds(GroundChain(_compass), withHitstop: true);
            float ruler = ComboTiming.ChainSeconds(GroundChain(_ruler), withHitstop: true);

            Assert.That(ruler / compass, Is.InRange(1.4f, 1.7f),
                        $"Mashed ground chain: ruler {ruler:0.00} s vs compass {compass:0.00} s.");
        }

        [Test]
        public void EachGroundHit_DealsTwoToTwoAndAHalfTimesTheCompassHitInItsSlot()
        {
            var compass = GroundChain(_compass);
            var ruler = GroundChain(_ruler);
            var problems = new List<string>();
            for (int i = 0; i < ruler.Count; i++)
            {
                // The finishers compare with each other; the rest slot by slot.
                var c = ruler[i].IsLast ? compass[^1] : compass[Mathf.Min(i, compass.Count - 2)];
                float ratio = ruler[i].Step.Hit.Damage / c.Step.Hit.Damage;
                if (ratio < 2f || ratio > 2.5f)
                    problems.Add($"{ruler[i].Step.name} {ruler[i].Step.Hit.Damage} vs {c.Step.name} " +
                                 $"{c.Step.Hit.Damage}: {ratio:0.00}x");
            }
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void GroundChain_TradesCommitmentNotDamagePerSecond()
        {
            float Dps(SwordMoveset m)
            {
                var chain = GroundChain(m);
                return ComboTiming.ChainDamage(chain, withBursts: false) / ComboTiming.ChainSeconds(chain, true);
            }

            Assert.That(Dps(_ruler) / Dps(_compass), Is.InRange(0.95f, 1.2f),
                        "Blade damage per second, mashed, one target.");
        }

        [Test]
        public void EveryRulerHit_WindsUpLongerAndFreezesHarderThanTheCompassHitInItsSlot()
        {
            var compass = GroundChain(_compass);
            var ruler = GroundChain(_ruler);
            var problems = new List<string>();
            for (int i = 0; i < ruler.Count; i++)
            {
                var r = ruler[i].Step;
                var c = (ruler[i].IsLast ? compass[^1] : compass[Mathf.Min(i, compass.Count - 2)]).Step;
                if (ComboTiming.WindupSeconds(r) < 1.4f * ComboTiming.WindupSeconds(c))
                    problems.Add($"{r.name}: wind-up {ComboTiming.WindupSeconds(r):0.00} s vs {c.name} " +
                                 $"{ComboTiming.WindupSeconds(c):0.00} s");
                if (r.Hit.Hitstop < 1.5f * c.Hit.Hitstop)
                    problems.Add($"{r.name}: hitstop {r.Hit.Hitstop} s vs {c.name} {c.Hit.Hitstop} s");
            }
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void LearnedAbilities_NeverHandTheRulerACompassSwing()
        {
            Assert.That(_ruler.AcceptsOtherAbilities, Is.False,
                        "The ruler opts in to each ability; an unlisted one would bring a compass-paced swing.");
            var abilities = AssetDatabase.FindAssets("t:SwordAbilityDefinition", new[] { DataDir })
                .Select(g => AssetDatabase.LoadAssetAtPath<SwordAbilityDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a => a != null).ToList();
            Assert.That(abilities, Is.Not.Empty);

            var problems = new List<string>();
            foreach (var a in abilities)
            {
                var step = _ruler.StepFor(a);
                if (step != null && !AssetDatabase.GetAssetPath(step).StartsWith(RulerStepDir))
                    problems.Add($"{a.name} plays {AssetDatabase.GetAssetPath(step)} with the ruler");
            }
            foreach (var row in _ruler.AbilitySteps)
                if (row.Ability == null) problems.Add("an ability row has no ability");
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void RulerSteps_HaveTheirOwnAnimatorStates()
        {
            var steps = AssetDatabase.FindAssets("t:ComboStepDefinition", new[] { RulerStepDir.TrimEnd('/') })
                .Select(g => AssetDatabase.LoadAssetAtPath<ComboStepDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(s => s != null).ToList();
            Assert.That(steps, Is.Not.Empty);
            Assert.That(steps.Where(s => !s.AnimatorState.StartsWith("Ruler_")).Select(s => s.name), Is.Empty,
                        "Ruler states are prefixed Ruler_ so they can never collide with the compass's.");
        }
    }
}
