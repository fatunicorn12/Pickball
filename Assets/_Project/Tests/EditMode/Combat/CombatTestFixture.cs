using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Combat;
using UnityEngine;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// Builds throwaway steps, abilities and movesets through their
    /// <c>CreateRuntime</c> factories — the same code path MeleeCombat's
    /// fallbacks use — and destroys them after each test, so a suite run leaves
    /// no assets and no scene objects behind.
    /// </summary>
    public abstract class CombatTestFixture
    {
        readonly List<Object> _made = new();

        [TearDown]
        public void DestroyMadeObjects()
        {
            foreach (var o in _made)
                if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
        }

        protected T Track<T>(T o) where T : Object
        {
            _made.Add(o);
            return o;
        }

        protected ComboStepDefinition Step(string name)
            => Track(ComboStepDefinition.CreateRuntime(name, name, 0.6f, SwingWindows.Default,
                                                      new HitSettings(10f, 1f)));

        protected SwordAbilityDefinition Ability(string name, ComboRole role, ComboTrack track,
                                                 int chainOrder = 0, int priority = 0,
                                                 ComboCondition condition = default)
            => Track(SwordAbilityDefinition.CreateRuntime(name, role, track, Step(name + "_Step"),
                                                         chainOrder, priority, condition));

        protected SwordMoveset Moveset(IEnumerable<ComboStepDefinition> ground, ComboStepDefinition groundFinisher,
                                       IEnumerable<ComboStepDefinition> air, ComboStepDefinition airFinisher)
            => Track(SwordMoveset.CreateRuntime(ground, groundFinisher, air, airFinisher));

        protected static ComboContext Ground(int hits) => new(ComboTrack.Ground, hits);
        protected static ComboContext Air(int hits) => new(ComboTrack.Air, hits);
    }
}
