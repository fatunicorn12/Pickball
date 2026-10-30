using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Pickleball.Sandbox.Combat;
using Pickleball.Sandbox.Combat.Presentation;
using Pickleball.Sandbox.Combat.Progression;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Pickleball.Tests.Validation
{
    /// <summary>
    /// The shipped sword data agrees with itself and with the Animator.
    ///
    /// The combo is data-driven: steps, abilities, the moveset and the level
    /// table are separate assets wired by reference, and the Attack-layer states
    /// are built from the steps by Setup Sword Abilities. Each link can break on
    /// its own: a step nobody plays, an ability no level teaches, a state
    /// that was never rebuilt after a clip changed. None of these throw; the
    /// swing just never happens. The logic tests use runtime-built data, so only
    /// these tests look at what actually ships.
    ///
    /// They check structure, not tuning: retiming a swing or reshaping the XP
    /// curve should not need a test change, only breaking a contract should.
    /// </summary>
    [Category("Assets")]
    public class CombatAssetTests
    {
        const string DataDir = "Assets/_Project/ScriptableObjects/Combat";
        const string MovesetPath = DataDir + "/Moveset_Compass.asset";
        const string LevelingPath = DataDir + "/Leveling_Player.asset";
        const string ControllerPath = "Assets/_Project/Animation/Controllers/ChibiLocomotion.controller";
        const string AttackLayer = "Attack";

        /// <summary>Every shipped asset of a type. Fails rather than returning an
        /// empty list, so a moved folder can't turn these tests into silent passes.</summary>
        static List<T> LoadAll<T>() where T : Object
        {
            var assets = AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { DataDir })
                                      .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)))
                                      .Where(a => a != null)
                                      .ToList();
            Assert.That(assets, Is.Not.Empty, $"No {typeof(T).Name} assets found under {DataDir}.");
            return assets;
        }

        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, $"{path} is missing or does not load as {typeof(T).Name}.");
            return asset;
        }

        // ----------------------------------------------------------- the graph

        [Test]
        public void EveryShippedStep_IsPlayedByTheMovesetOrAnAbility()
        {
            Load<SwordMoveset>(MovesetPath);
            // Every weapon's moveset: its own hits, and the swings its ability rows give
            // learned abilities (the ruler plays its own + C, for instance).
            var reachable = new HashSet<ComboStepDefinition>();
            foreach (var moveset in LoadAll<SwordMoveset>())
            {
                reachable.UnionWith(moveset.AllBaseSteps());
                foreach (var row in moveset.AbilitySteps)
                    if (row.Step != null) reachable.Add(row.Step);
            }
            foreach (var ability in LoadAll<SwordAbilityDefinition>())
                if (ability.Step != null) reachable.Add(ability.Step);

            var orphans = LoadAll<ComboStepDefinition>().Where(s => !reachable.Contains(s)).Select(s => s.name);

            Assert.That(orphans, Is.Empty,
                        "Steps nothing can play. Add them to a moveset, a moveset's ability rows or an " +
                        "ability, or delete them.");
        }

        [Test]
        public void EveryShippedAbility_IsWellFormed()
        {
            var problems = new List<string>();
            foreach (var ability in LoadAll<SwordAbilityDefinition>())
            {
                if (ability.Step == null) problems.Add($"{ability.name}: no step");
                if (ability.Role == ComboRole.Regular)
                    problems.Add($"{ability.name}: role Regular is reserved for the moveset's own hits");

                var c = ability.Condition;
                if (c.MinTargetDistance > 0f && c.MaxTargetDistance > 0f && c.MinTargetDistance > c.MaxTargetDistance)
                    problems.Add($"{ability.name}: condition min {c.MinTargetDistance} m is beyond max " +
                                 $"{c.MaxTargetDistance} m, so it can never qualify");
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void EveryShippedAbility_IsTaughtExactlyOnce()
        {
            var leveling = Load<LevelingConfig>(LevelingPath);
            var taught = leveling.Unlocks.Where(u => u.Ability != null)
                                         .GroupBy(u => u.Ability)
                                         .ToDictionary(g => g.Key, g => g.Count());

            var problems = new List<string>();
            foreach (var ability in LoadAll<SwordAbilityDefinition>())
            {
                taught.TryGetValue(ability, out int count);
                if (count != 1) problems.Add($"{ability.name} is taught {count} times by Leveling_Player");
            }

            Assert.That(problems, Is.Empty,
                        string.Join("\n", problems) +
                        "\nIf an ability is meant to come from a boss drop instead, exclude it here.");
        }

        [Test]
        public void ShippedUnlockTable_OnlyUsesReachableLevels()
        {
            var leveling = Load<LevelingConfig>(LevelingPath);
            int maxLevel = leveling.Curve.MaxLevel;

            for (int i = 0; i < leveling.Unlocks.Count; i++)
            {
                var row = leveling.Unlocks[i];
                Assert.That(row.Ability, Is.Not.Null, $"Unlock row {i} has no ability.");
                Assert.That(row.Level, Is.InRange(2, maxLevel),
                            $"Unlock row {i} ({row.Ability.name}) is at level {row.Level}. Level 1 is " +
                            $"where everyone starts and anything past {maxLevel} is never reached, so " +
                            "either way the ability is silently never taught.");
            }
        }

        /// <summary>The curve must climb all the way to the cap. A total that stops
        /// rising is an int overflow (<c>ExperienceCurve.TotalXpFor</c> sums ints)
        /// or a zero-cost level; either makes later levels unreachable.</summary>
        [Test]
        public void ShippedLevelingCurve_RisesStrictlyToTheCap()
        {
            var curve = Load<LevelingConfig>(LevelingPath).Curve;

            int previous = curve.TotalXpFor(1);
            Assert.That(previous, Is.EqualTo(0));
            for (int level = 2; level <= curve.MaxLevel; level++)
            {
                int total = curve.TotalXpFor(level);
                Assert.That(total, Is.GreaterThan(previous),
                            $"Total XP stops rising at level {level} ({previous} → {total}).");
                previous = total;
            }
        }

        // -------------------------------------------------------------- timing

        [Test]
        public void EveryShippedStep_HasACoherentSwing()
        {
            var problems = new List<string>();
            foreach (var step in LoadAll<ComboStepDefinition>())
            {
                var w = step.Windows;
                string n = step.name;

                if (step.Clip == null) problems.Add($"{n}: no clip, so Duration falls back to a placeholder");
                if (string.IsNullOrEmpty(step.AnimatorState)) problems.Add($"{n}: no Animator state");
                if (!(w.ActiveStart < w.ActiveEnd))
                    problems.Add($"{n}: blade window is empty ({w.ActiveStart} → {w.ActiveEnd})");
                if (w.ActiveEnd > w.ComboClose)
                    problems.Add($"{n}: chain closes ({w.ComboClose}) before the blade does ({w.ActiveEnd})");
                if (w.ComboOpen > w.ComboClose)
                    problems.Add($"{n}: chain opens ({w.ComboOpen}) after it closes ({w.ComboClose})");

                var burst = step.Burst;
                if (burst.Enabled)
                {
                    if (burst.Radius <= 0f) problems.Add($"{n}: burst enabled with radius 0");
                    if (burst.TriggerAt > w.ComboClose)
                        problems.Add($"{n}: burst at {burst.TriggerAt} comes after the chain closes " +
                                     $"({w.ComboClose}), so it never fires");
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        /// <summary>Two steps may share an Attack-layer state only if they would
        /// build it identically. Otherwise Setup Sword Abilities keeps whichever
        /// it meets first and the other step plays the wrong clip.</summary>
        [Test]
        public void StepsSharingAnAnimatorState_AgreeOnClipAndSpeed()
        {
            var problems = new List<string>();
            foreach (var group in LoadAll<ComboStepDefinition>().GroupBy(s => s.AnimatorState))
            {
                var first = group.First();
                foreach (var other in group.Skip(1))
                    if (other.Clip != first.Clip || !Mathf.Approximately(other.PlaybackSpeed, first.PlaybackSpeed))
                        problems.Add($"'{first.name}' and '{other.name}' share state '{group.Key}' " +
                                     "with a different clip or speed");
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        // -------------------------------------------------------- presentation

        /// <summary>An effect prefab without a PooledEffect is thrown away by the
        /// EffectPool at runtime, so the step silently loses its effect.</summary>
        [Test]
        public void EveryShippedStepEffect_IsPoolable()
        {
            var problems = new List<string>();
            foreach (var step in LoadAll<ComboStepDefinition>())
            {
                var look = step.Presentation;
                if (look.HitEffect != null && look.HitEffect.GetComponent<PooledEffect>() == null)
                    problems.Add($"{step.name}: hit effect '{look.HitEffect.name}' has no PooledEffect");
                if (look.BurstEffect != null && look.BurstEffect.GetComponent<PooledEffect>() == null)
                    problems.Add($"{step.name}: burst effect '{look.BurstEffect.name}' has no PooledEffect");
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        // ------------------------------------------------------------ animator

        /// <summary>Every step can actually play: its Attack-layer state exists
        /// and plays the step's clip at the step's speed. Catches a step asset
        /// changed without re-running Setup Sword Abilities, which leaves the
        /// timer and the animation out of step.</summary>
        [Test]
        public void EveryShippedStep_HasAMatchingAttackLayerState()
        {
            var controller = Load<AnimatorController>(ControllerPath);
            var layer = controller.layers.FirstOrDefault(l => l.name == AttackLayer);
            Assert.That(layer, Is.Not.Null, $"{ControllerPath} has no '{AttackLayer}' layer.");

            var states = layer.stateMachine.states.Select(s => s.state).ToDictionary(s => s.name);
            var problems = new List<string>();
            foreach (var step in LoadAll<ComboStepDefinition>())
            {
                if (!states.TryGetValue(step.AnimatorState, out var state))
                {
                    problems.Add($"{step.name}: no '{step.AnimatorState}' state on the Attack layer");
                    continue;
                }
                if (state.motion != step.Clip)
                    problems.Add($"{step.name}: state '{state.name}' plays '{(state.motion ? state.motion.name : "nothing")}', " +
                                 $"the step uses '{(step.Clip ? step.Clip.name : "nothing")}'");
                if (!Mathf.Approximately(state.speed, step.PlaybackSpeed))
                    problems.Add($"{step.name}: state '{state.name}' plays at {state.speed}×, the step at " +
                                 $"{step.PlaybackSpeed}×");
            }

            Assert.That(problems, Is.Empty,
                        string.Join("\n", problems) + "\nRe-run Tools ▸ Pickleball ▸ Setup Sword Abilities.");
        }
    }
}
