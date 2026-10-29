using System.Reflection;
using NUnit.Framework;
using Pickleball.Sandbox;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Boss
{
    /// <summary>
    /// Newton's inverse-square range rule exempts spells by default
    /// (<c>rangeScalesSpells</c> off); the sword stays scaled. Drives the real
    /// filter: OnEnable installs it on his Health, and the player is parked far
    /// enough away that the rule clamps to <c>farDamageMultiplier</c>.
    /// </summary>
    public class NewtonSpellExemptionTests : BossTestFixture
    {
        const float Hit = 40f;
        const float Far = 0.25f;

        NewtonBoss _boss;
        Health _health;

        [SetUp]
        public void MakeFight()
        {
            _boss = NewComponent<NewtonBoss>();
            Call(_boss, "Awake");
            _health = _boss.Health;
            SetTunable(_health, "maxHealth", 1000f);
            SetTunable(_health, "hitGraceSeconds", 0f);
            SetTunable(_boss, "weakPointRange", 4.5f);
            SetTunable(_boss, "farDamageMultiplier", Far);
            SetTunable(_boss, "closeDamageCap", 2f);
            SetSpellsScale(false);
            _health.ResetToFull();

            var player = NewComponent<ChibiLocomotion>();
            player.transform.position = new Vector3(0f, 0f, 40f);   // far: clamps to Far
            FieldInfo f = typeof(NewtonBoss).GetField("_player", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(f, Is.Not.Null, "NewtonBoss._player not found; it was probably renamed.");
            f.SetValue(_boss, player);

            Call(_boss, "OnEnable");   // installs the range filter
        }

        static void Call(MonoBehaviour target, string method)
        {
            MethodInfo m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(m, Is.Not.Null, $"{target.GetType().Name}.{method}() not found; it was probably renamed.");
            m.Invoke(target, null);
        }

        void SetSpellsScale(bool value)
        {
            var so = new SerializedObject(_boss);
            SerializedProperty p = so.FindProperty("rangeScalesSpells");
            Assert.That(p, Is.Not.Null, "NewtonBoss has no 'rangeScalesSpells' field.");
            p.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        float Lost(System.Action hit)
        {
            float before = _health.Current;
            hit();
            return before - _health.Current;
        }

        [Test]
        public void SpellDamage_IsUnscaled_AtFarRange()
        {
            float lost = Lost(() => _health.TakeHit(Hit, Vector3.zero, Vector3.zero, false, DamageKind.Spell));
            Assert.That(lost, Is.EqualTo(Hit).Within(1e-4f));
        }

        [Test]
        public void MeleeDamage_IsStillScaled_AtFarRange()
        {
            float lost = Lost(() => _health.TakeHit(Hit, Vector3.zero, Vector3.zero, false));
            Assert.That(lost, Is.EqualTo(Hit * Far).Within(1e-4f));
        }

        [Test]
        public void RangeScalesSpells_RestoresScaling()
        {
            SetSpellsScale(true);
            float lost = Lost(() => _health.Damage(Hit, Vector3.zero, kind: DamageKind.Spell));
            Assert.That(lost, Is.EqualTo(Hit * Far).Within(1e-4f));
        }
    }
}
