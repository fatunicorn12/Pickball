using System.Reflection;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Combat;
using Pickleball.Sandbox.Combat.Progression;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// A Health with <c>grantsXp</c> off (a friendly NPC such as Birdy) pays no
    /// Lemmas for being hurt or killed, while an ordinary Health still does.
    /// Drives the real <see cref="CombatXpSource"/> through the hit pair
    /// MeleeCombat raises (HitConnecting, the target's damage, HitLanded),
    /// calling its private handlers by reflection because EditMode has no sword.
    /// </summary>
    public class XpExemptionTests : BossTestFixture
    {
        static void Invoke(object target, string method, params object[] args)
        {
            var m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(m, Is.Not.Null, $"{target.GetType().Name}.{method} not found");
            m.Invoke(target, args);
        }

        Health Target(bool grantsXp)
        {
            var health = NewComponent<Health>();
            SetTunable(health, "maxHealth", 40f);
            var so = new SerializedObject(health);
            var flag = so.FindProperty("grantsXp");
            Assert.That(flag, Is.Not.Null, "Health has no serialized 'grantsXp'");
            flag.boolValue = grantsXp;
            so.ApplyModifiedPropertiesWithoutUndo();
            health.ResetToFull();
            health.gameObject.AddComponent<BoxCollider>();
            return health;
        }

        (PlayerLevel level, CombatXpSource source) Player()
        {
            var level = NewComponent<PlayerLevel>();
            var source = level.gameObject.AddComponent<CombatXpSource>();
            var so = new SerializedObject(source);
            so.FindProperty("level").objectReferenceValue = level;
            so.ApplyModifiedPropertiesWithoutUndo();
            Invoke(source, "OnEnable");          // builds the ledger; no MeleeCombat to subscribe to
            return (level, source);
        }

        static void Hit(CombatXpSource source, Health target, float damage)
        {
            var report = new MeleeHitReport(target, target.GetComponent<Collider>(), target.transform.position,
                                            damage, default, false);
            Invoke(source, "OnHitConnecting", report);
            target.Damage(damage, target.transform.position);
            Invoke(source, "OnHitLanded", report);
        }

        [Test]
        public void OrdinaryTarget_PaysForDamageAndTheKill()
        {
            var (level, source) = Player();
            var target = Target(grantsXp: true);
            Hit(source, target, 15f);
            Hit(source, target, 100f);
            Assert.That(target.IsDead, Is.True);
            Assert.That(level.TotalXp, Is.GreaterThan(0), "control: the same hits pay on an ordinary Health");
        }

        [Test]
        public void FriendlyTarget_PaysNothingForDamageOrTheKill()
        {
            var (level, source) = Player();
            var target = Target(grantsXp: false);
            Hit(source, target, 15f);
            Hit(source, target, 100f);
            Assert.That(target.IsDead, Is.True);
            Assert.That(level.TotalXp, Is.EqualTo(0));
        }

        [Test]
        public void GrantsXp_DefaultsToTrue()
        {
            Assert.That(NewComponent<Health>().GrantsXp, Is.True,
                        "every existing Health (boss, dummies, player) keeps paying as before");
        }
    }
}
