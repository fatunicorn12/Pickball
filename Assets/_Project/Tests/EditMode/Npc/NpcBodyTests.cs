using System.Reflection;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Npc;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Npc
{
    /// <summary>
    /// The NPC's solid collider blocks and takes hits while it lives, and is
    /// switched off when it dies so the corpse is not an invisible pillar.
    /// </summary>
    public class NpcBodyTests : BossTestFixture
    {
        static void Invoke(Object target, string method)
        {
            var m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(m, Is.Not.Null, $"{target.GetType().Name}.{method} not found");
            m.Invoke(target, null);
        }

        (Health health, CapsuleCollider capsule, NpcBody body) Build()
        {
            var health = NewComponent<Health>();
            SetTunable(health, "maxHealth", 40f);
            health.ResetToFull();
            var capsule = health.gameObject.AddComponent<CapsuleCollider>();
            var body = health.gameObject.AddComponent<NpcBody>();
            var so = new SerializedObject(body);
            so.FindProperty("blocker").objectReferenceValue = capsule;
            so.FindProperty("health").objectReferenceValue = health;
            so.ApplyModifiedPropertiesWithoutUndo();
            Invoke(body, "OnEnable");
            return (health, capsule, body);
        }

        [Test]
        public void Alive_KeepsTheColliderThroughHits()
        {
            var (health, capsule, _) = Build();
            health.Damage(15f, Vector3.zero);
            Assert.That(capsule.enabled, Is.True);
        }

        [Test]
        public void Death_SwitchesTheColliderOff()
        {
            var (health, capsule, body) = Build();
            health.Damage(100f, Vector3.zero);
            Assert.That(body.Blocker, Is.SameAs(capsule));
            Assert.That(capsule.enabled, Is.False);
        }

        [Test]
        public void Disabled_IgnoresDeath()
        {
            var (health, capsule, body) = Build();
            Invoke(body, "OnDisable");
            health.Damage(100f, Vector3.zero);
            Assert.That(capsule.enabled, Is.True, "unsubscribed in OnDisable");
        }
    }
}
