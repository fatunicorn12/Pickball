using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Magic;
using UnityEngine;

namespace Pickleball.Tests.Magic
{
    /// <summary>
    /// The Thunderbolt's chain starts from the enemy it struck (QA F-10): a bolt
    /// on empty ground is a miss and never arcs to a bystander, while a bolt on
    /// an enemy still chains. Driven with a zero telegraph, so <c>Begin</c>
    /// strikes at once.
    /// </summary>
    public class LightningStrikeTests : MagicTestFixture
    {
        const float Direct = 34f, ChainDamage = 10f;

        // Far from the origin, clear of anything else in the edit scene.
        static readonly Vector3 Site = new(500f, 0f, -500f);

        SpellDefinition NewBolt()
        {
            var spell = NewAsset<SpellDefinition>();
            SetFloat(spell, "strikeDelay", 0f);
            SetFloat(spell, "directDamage", Direct);
            SetFloat(spell, "blastRadius", 1.6f);
            SetFloat(spell, "blastDamageMax", 12f);
            SetFloat(spell, "blastDamageMin", 5f);
            SetFloat(spell, "knockback", 0f);
            SetFloat(spell, "staggerSeconds", 0f);
            SetInt(spell, "chainCount", 2);
            SetFloat(spell, "chainRadius", 6f);
            SetFloat(spell, "chainDamage", ChainDamage);
            return spell;
        }

        Health NewDummy(Vector3 feet)
        {
            var health = NewComponent<Health>();
            var col = health.gameObject.AddComponent<CapsuleCollider>();
            col.radius = 0.4f;
            col.height = 1.6f;
            col.center = new Vector3(0f, 0.8f, 0f);
            health.transform.position = feet;
            health.ResetToFull();   // Awake does not run in EditMode
            return health;
        }

        /// <summary>Strikes at once; returns (links chained, hits reported).</summary>
        (int chained, int hits) Strike(Vector3 point, Transform target)
        {
            var strike = NewComponent<LightningStrike>();
            int chained = 0, hits = -1;
            strike.Chained += (_, _, _) => chained++;
            strike.Struck += (_, _, n) => hits = n;
            Physics.SyncTransforms();
            strike.Begin(point, target, NewBolt(), null);
            return (chained, hits);
        }

        [Test]
        public void BoltOnEmptyGround_NeverChains()
        {
            // 2.8 m off: outside the splash, inside the 6 m chain reach (the QA case).
            var bystander = NewDummy(Site + new Vector3(2.8f, 0f, 0f));
            var (chained, hits) = Strike(Site, null);
            Assert.That(chained, Is.Zero);
            Assert.That(hits, Is.Zero);
            Assert.That(bystander.Current, Is.EqualTo(bystander.Max), "a miss is free for bystanders");
        }

        [Test]
        public void BoltOnAnEnemy_StillChains()
        {
            var enemy = NewDummy(Site);
            var bystander = NewDummy(Site + new Vector3(3f, 0f, 0f));
            var (chained, hits) = Strike(Site, enemy.transform);
            Assert.That(chained, Is.EqualTo(1));
            Assert.That(hits, Is.EqualTo(2));
            Assert.That(enemy.Current, Is.EqualTo(enemy.Max - Direct).Within(1e-4f));
            Assert.That(bystander.Current, Is.EqualTo(bystander.Max - ChainDamage).Within(1e-4f));
        }

        [Test]
        public void BoltOnTheGroundUnderAnEnemy_HitsAndChains()
        {
            // No target picked, but someone stands on the point: that is a hit.
            var enemy = NewDummy(Site);
            var bystander = NewDummy(Site + new Vector3(3f, 0f, 0f));
            var (chained, _) = Strike(Site, null);
            Assert.That(enemy.Current, Is.EqualTo(enemy.Max - Direct).Within(1e-4f));
            Assert.That(chained, Is.EqualTo(1));
            Assert.That(bystander.Current, Is.LessThan(bystander.Max));
        }
    }
}
