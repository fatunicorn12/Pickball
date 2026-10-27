using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Combat;
using Pickleball.Sandbox.Combat.Progression;
using UnityEngine;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// Level table rules and the components that apply them: a level-up teaches
    /// every row it crossed, once, in table order; a big XP gain raises one
    /// LeveledUp per level so no unlock or toast is skipped.
    /// </summary>
    public class ProgressionTests : CombatTestFixture
    {
        readonly List<GameObject> _hosts = new();

        [TearDown]
        public void DestroyHosts()
        {
            foreach (var h in _hosts) if (h != null) Object.DestroyImmediate(h);
            _hosts.Clear();
        }

        T NewComponent<T>() where T : Component
        {
            var go = new GameObject($"Test_{typeof(T).Name}") { hideFlags = HideFlags.DontSave };
            _hosts.Add(go);
            return go.AddComponent<T>();
        }

        [Test]
        public void Collect_TeachesRowsAboveTheOldLevelUpToTheNewOne_InTableOrder()
        {
            var a = Ability("A", ComboRole.Extender, ComboTrack.Ground);
            var b = Ability("B", ComboRole.Extender, ComboTrack.Air);
            var c = Ability("C", ComboRole.Finisher, ComboTrack.Ground);
            var table = new List<LevelUnlock> { new(4, c), new(2, a), new(3, b), new(3, null) };

            var got = new List<SwordAbilityDefinition>();
            LevelUnlockRules.Collect(table, 1, 3, got);
            Assert.That(got, Is.EqualTo(new[] { a, b }));

            got.Clear();
            LevelUnlockRules.Collect(table, 3, 3, got);
            Assert.That(got, Is.Empty, "no level crossed, nothing taught");

            got.Clear();
            LevelUnlockRules.Collect(table, 0, 10, got);
            Assert.That(got, Is.EqualTo(new[] { c, a, b }));
        }

        [Test]
        public void Collect_SkipsAbilitiesAlreadyCollected()
        {
            var a = Ability("A", ComboRole.Extender, ComboTrack.Ground);
            var table = new List<LevelUnlock> { new(2, a), new(3, a) };
            var got = new List<SwordAbilityDefinition>();
            LevelUnlockRules.Collect(table, 0, 5, got);
            Assert.That(got.Count, Is.EqualTo(1));
        }

        [Test]
        public void PlayerLevel_BigGain_RaisesOneLevelUpPerLevelInOrder()
        {
            var level = NewComponent<PlayerLevel>();     // no config: default 50 × L curve
            var seen = new List<(int now, int was)>();
            level.LeveledUp += (now, was) => seen.Add((now, was));

            level.AddXp(300);                            // exactly level 4

            Assert.That(level.Level, Is.EqualTo(4));
            Assert.That(seen, Is.EqualTo(new[] { (2, 1), (3, 2), (4, 3) }));
            Assert.That(level.XpIntoLevel, Is.EqualTo(0));
            Assert.That(level.XpForLevel, Is.EqualTo(200));
        }

        [Test]
        public void PlayerLevel_IgnoresNonPositiveXp()
        {
            var level = NewComponent<PlayerLevel>();
            int raised = 0;
            level.XpGained += _ => raised++;
            level.AddXp(0);
            level.AddXp(-5);
            Assert.That(level.TotalXp, Is.EqualTo(0));
            Assert.That(raised, Is.EqualTo(0));
        }

        [Test]
        public void SwordAbilities_GrantIsIdempotentAndAnnouncesOnce()
        {
            var abilities = NewComponent<SwordAbilities>();
            var a = Ability("A", ComboRole.Extender, ComboTrack.Ground);
            int announced = 0;
            abilities.Unlocked += _ => announced++;

            Assert.That(abilities.Grant(a), Is.True);
            Assert.That(abilities.Grant(a), Is.False);
            Assert.That(abilities.Has(a), Is.True);
            Assert.That(abilities.All.Count, Is.EqualTo(1));
            Assert.That(announced, Is.EqualTo(1));
        }
    }
}
