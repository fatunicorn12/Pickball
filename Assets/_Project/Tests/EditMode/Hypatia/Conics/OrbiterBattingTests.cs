using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Boss.Hypatia.Conics;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Conics
{
    /// <summary>
    /// The pure batting rule: which hits bat at all, which way the swing sent the
    /// ball, and the cone that counts as "at her".
    /// </summary>
    public class OrbiterBattingTests
    {
        static readonly Vector3 Ball = new(5f, 1f, 0f);
        static readonly Vector3 Her = new(0f, 3.45f, 0f);

        [Test]
        public void Spells_DoNotBat_ByDefault_PhysicalAlwaysDoes()
        {
            Assert.IsTrue(OrbiterBatting.CanBat(DamageKind.Physical, spellsBat: false));
            Assert.IsFalse(OrbiterBatting.CanBat(DamageKind.Spell, spellsBat: false));
            Assert.IsTrue(OrbiterBatting.CanBat(DamageKind.Spell, spellsBat: true));
        }

        [Test]
        public void SwingDirection_IsTheFlattenedKnockback()
        {
            Assert.IsTrue(OrbiterBatting.TrySwingDirection(new Vector3(-3f, 2f, 0f), Ball, Ball, out Vector3 dir));
            Assert.That((dir - Vector3.left).magnitude, Is.LessThan(1e-5f));
        }

        [Test]
        public void SwingDirection_WithNoKnockback_UsesHitPointThroughTheBall()
        {
            Assert.IsTrue(OrbiterBatting.TrySwingDirection(Vector3.zero, Ball + new Vector3(0f, 0f, -1f), Ball,
                                                           out Vector3 dir));
            Assert.That((dir - Vector3.forward).magnitude, Is.LessThan(1e-5f));
            Assert.IsFalse(OrbiterBatting.TrySwingDirection(Vector3.zero, Ball, Ball, out _), "no direction at all");
        }

        [Test]
        public void StraightAtHer_IsAccepted_EvenThoughSheIsHigher()
        {
            Assert.IsTrue(OrbiterBatting.IsTowardHer(Vector3.left, Ball, Her, 60f));
        }

        [Test]
        public void Cone_AcceptsInside_RejectsOutside()
        {
            Vector3 at55 = Quaternion.Euler(0f, 55f, 0f) * Vector3.left;
            Vector3 at65 = Quaternion.Euler(0f, 65f, 0f) * Vector3.left;
            Assert.IsTrue(OrbiterBatting.IsTowardHer(at55, Ball, Her, 60f));
            Assert.IsFalse(OrbiterBatting.IsTowardHer(at65, Ball, Her, 60f));
            Assert.IsTrue(OrbiterBatting.IsTowardHer(Quaternion.Euler(0f, 60f, 0f) * Vector3.left, Ball, Her, 60f),
                          "the edge counts");
        }

        [Test]
        public void AwayFromHer_IsRejected()
        {
            Assert.IsFalse(OrbiterBatting.IsTowardHer(Vector3.right, Ball, Her, 60f));
            Assert.IsFalse(OrbiterBatting.IsTowardHer(Vector3.forward, Ball, Her, 60f), "90° is outside 60°");
        }

        [Test]
        public void BallDirectlyOverHer_CountsAsAtHer()
        {
            Assert.IsTrue(OrbiterBatting.IsTowardHer(Vector3.right, new Vector3(0f, 1f, 0f), Her, 10f));
        }
    }
}
