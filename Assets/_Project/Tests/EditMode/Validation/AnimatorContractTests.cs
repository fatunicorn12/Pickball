using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Pickleball.Sandbox.Movement;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Pickleball.Tests.Validation
{
    /// <summary>
    /// The string and integer contracts between gameplay code and the shipped
    /// Animator Controllers.
    ///
    /// Drivers address parameters by hashed name, and the movement controller's
    /// transitions compare the int <c>Move</c> / <c>JumpKind</c> parameters with
    /// enum values. A renamed parameter or a renumbered enum compiles and runs
    /// without error: the animation silently stops changing, or the wrong state
    /// plays. The names below are the ones the code hashes; the setup scripts
    /// that build the controllers (MovementSetup, WeaponSetup, NewtonBossSetup)
    /// must produce them.
    /// </summary>
    [Category("Assets")]
    public class AnimatorContractTests
    {
        const string ChibiControllerPath = "Assets/_Project/Animation/Controllers/ChibiLocomotion.controller";
        const string NewtonControllerPath = "Assets/_Project/Animation/Controllers/NewtonBoss.controller";

        const AnimatorControllerParameterType F = AnimatorControllerParameterType.Float;
        const AnimatorControllerParameterType B = AnimatorControllerParameterType.Bool;
        const AnimatorControllerParameterType I = AnimatorControllerParameterType.Int;
        const AnimatorControllerParameterType T = AnimatorControllerParameterType.Trigger;

        static AnimatorController LoadController(string path)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            Assert.That(controller, Is.Not.Null, $"{path} is missing.");
            return controller;
        }

        static void AssertHasParameters(AnimatorController controller,
                                        params (string name, AnimatorControllerParameterType type)[] expected)
        {
            var actual = controller.parameters.ToDictionary(p => p.name, p => p.type);
            var problems = new List<string>();
            foreach (var (name, type) in expected)
            {
                if (!actual.TryGetValue(name, out var found)) problems.Add($"missing {type} '{name}'");
                else if (found != type) problems.Add($"'{name}' is {found}, the code sets it as {type}");
            }

            Assert.That(problems, Is.Empty, $"{controller.name}: " + string.Join("; ", problems));
        }

        /// <summary>Every transition on every layer, including AnyState and
        /// nested state machines.</summary>
        static IEnumerable<AnimatorStateTransition> AllTransitions(AnimatorController controller)
        {
            foreach (var layer in controller.layers)
                foreach (var t in Walk(layer.stateMachine))
                    yield return t;

            static IEnumerable<AnimatorStateTransition> Walk(AnimatorStateMachine sm)
            {
                foreach (var t in sm.anyStateTransitions) yield return t;
                foreach (var child in sm.states)
                    foreach (var t in child.state.transitions)
                        yield return t;
                foreach (var sub in sm.stateMachines)
                    foreach (var t in Walk(sub.stateMachine))
                        yield return t;
            }
        }

        // -------------------------------------------------------------- chibi

        /// <summary>The parameters MovementAnimatorDriver and SwordAnimatorDriver
        /// set. The drivers tolerate some of these being absent on an old
        /// controller, but the shipped one must have them all or the newer move
        /// states never play.</summary>
        [Test]
        public void ChibiController_HasEveryParameterTheDriversSet()
        {
            AssertHasParameters(LoadController(ChibiControllerPath),
                ("Speed", F), ("SpeedMultiplier", F), ("VerticalSpeed", F), ("FlipSpeed", F),
                ("Grounded", B), ("Moving", B), ("Crouch", B), ("Skating", B), ("Tethered", B),
                ("Jump", T), ("JumpVariant", T), ("Stop", T), ("Flip", T), ("Dash", T),
                ("Move", I), ("JumpKind", I), ("Combo", I));
        }

        /// <summary>The values the controller's int conditions were built with.
        /// Renumbering an enum leaves those conditions pointing at the wrong move,
        /// so a change here means re-running Setup Movement in the same commit.</summary>
        [Test]
        public void MoveKindAndJumpKind_KeepTheirAnimatorValues()
        {
            var moves = new Dictionary<string, int>
            {
                ["Ground"] = 0, ["Sprint"] = 1, ["RunStop"] = 2, ["Crouch"] = 3, ["SkidTurn"] = 4,
                ["Jump"] = 5, ["Fall"] = 6, ["AirJump"] = 7, ["Dive"] = 8, ["BellySlide"] = 9,
                ["SpinJump"] = 10, ["Dash"] = 11, ["Tether"] = 12, ["Skate"] = 13,
            };
            var jumps = new Dictionary<string, int>
            {
                ["None"] = 0, ["Single"] = 1, ["Double"] = 2, ["Triple"] = 3, ["SideFlip"] = 4, ["Rollout"] = 5,
            };

            Assert.That(Enum.GetNames(typeof(MoveKind)).ToDictionary(n => n, n => (int)Enum.Parse(typeof(MoveKind), n)),
                        Is.EquivalentTo(moves));
            Assert.That(Enum.GetNames(typeof(JumpKind)).ToDictionary(n => n, n => (int)Enum.Parse(typeof(JumpKind), n)),
                        Is.EquivalentTo(jumps));
        }

        [Test]
        public void ChibiController_MoveAndJumpKindConditions_NameRealValues()
        {
            var problems = new List<string>();
            foreach (var t in AllTransitions(LoadController(ChibiControllerPath)))
            {
                foreach (var c in t.conditions)
                {
                    if (c.mode != AnimatorConditionMode.Equals && c.mode != AnimatorConditionMode.NotEqual) continue;
                    int value = Mathf.RoundToInt(c.threshold);
                    if (c.parameter == "Move" && !Enum.IsDefined(typeof(MoveKind), value))
                        problems.Add($"transition to '{Destination(t)}' tests Move == {value}, which is no MoveKind");
                    if (c.parameter == "JumpKind" && !Enum.IsDefined(typeof(JumpKind), value))
                        problems.Add($"transition to '{Destination(t)}' tests JumpKind == {value}, which is no JumpKind");
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        static string Destination(AnimatorStateTransition t)
            => t.destinationState != null ? t.destinationState.name
             : t.destinationStateMachine != null ? t.destinationStateMachine.name
             : t.isExit ? "Exit" : "?";

        // ------------------------------------------------------------- newton

        [Test]
        public void NewtonController_HasEveryParameterTheBossSets()
        {
            AssertHasParameters(LoadController(NewtonControllerPath),
                ("Speed", F), ("Dead", B), ("Channeling", B),
                ("Summon", T), ("Volley", T), ("Beam", T), ("Barrage", T), ("Hit", T));
        }

        /// <summary>
        /// The boss's casts are AnyState transitions, so their order and guards
        /// decide what wins. Death must be first (it beats a trigger fired the
        /// same frame), every other AnyState transition must refuse to fire once
        /// he is dead, and every cast except the beam must wait out a channel.
        /// </summary>
        [Test]
        public void NewtonController_DeathWinsAndCastsRespectChanneling()
        {
            var sm = LoadController(NewtonControllerPath).layers[0].stateMachine;
            var any = sm.anyStateTransitions;
            Assert.That(any, Is.Not.Empty, "Newton has no AnyState casts.");

            static bool Has(AnimatorStateTransition t, AnimatorConditionMode mode, string parameter)
                => t.conditions.Any(c => c.mode == mode && c.parameter == parameter);

            Assert.That(Has(any[0], AnimatorConditionMode.If, "Dead"), Is.True,
                        $"The first AnyState transition goes to '{Destination(any[0])}', not Death.");

            var casts = new Dictionary<string, AnimatorStateTransition>();
            foreach (var t in any.Skip(1))
            {
                Assert.That(Has(t, AnimatorConditionMode.IfNot, "Dead"), Is.True,
                            $"AnyState → '{Destination(t)}' can still fire after death.");
                var trigger = t.conditions.FirstOrDefault(c => c.mode == AnimatorConditionMode.If);
                if (trigger.parameter != null) casts[trigger.parameter] = t;
            }

            foreach (string cast in new[] { "Summon", "Volley", "Beam", "Barrage" })
                Assert.That(casts.ContainsKey(cast), Is.True, $"No AnyState transition fires on '{cast}'.");

            foreach (string cast in new[] { "Summon", "Volley", "Barrage" })
                Assert.That(Has(casts[cast], AnimatorConditionMode.IfNot, "Channeling"), Is.True,
                            $"'{cast}' can interrupt the beam's channel.");
        }
    }
}
