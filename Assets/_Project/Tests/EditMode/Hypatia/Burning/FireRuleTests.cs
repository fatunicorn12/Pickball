using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia.Burning;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Burning
{
    /// <summary>Who is standing in the fire: a burned band, below the flames.</summary>
    public class FireRuleTests
    {
        const float FireHeight = 1.2f;

        static BurnSchedule OuterBandBurned()
        {
            var s = new BurnSchedule(new[] { 6f, 8.33f, 10.67f, 13f }, 13f, 6f);
            s.StartSmoulder(0f);
            s.Tick(0f, null);  // band 3 burned
            s.StartSmoulder(5f); // band 2 smouldering
            return s;
        }

        [Test]
        public void InABurnedBand_OnTheFloor_Burns()
        {
            Assert.That(FireRule.IsBurning(OuterBandBurned(), Vector3.zero, 0f, new Vector3(0f, 0f, 12f), FireHeight),
                        Is.True);
            Assert.That(FireRule.IsBurning(OuterBandBurned(), Vector3.zero, 0f, new Vector3(8.5f, 1.19f, 8.5f), FireHeight),
                        Is.True, "just under the flames' top, diagonally out 12 m");
        }

        [Test]
        public void AboveTheFlames_DoesNotBurn()
        {
            Assert.That(FireRule.IsBurning(OuterBandBurned(), Vector3.zero, 0f, new Vector3(0f, 1.2f, 12f), FireHeight),
                        Is.False, "at the top");
            Assert.That(FireRule.IsBurning(OuterBandBurned(), Vector3.zero, 0f, new Vector3(0f, 3f, 12f), FireHeight),
                        Is.False, "mid-jump");
        }

        [Test]
        public void TheFloorHeightIsTheReference()
        {
            var centre = new Vector3(100f, 5f, -40f);
            Assert.That(FireRule.IsBurning(OuterBandBurned(), centre, 5f, centre + new Vector3(12f, 0.5f, 0f), FireHeight),
                        Is.True);
            Assert.That(FireRule.IsBurning(OuterBandBurned(), centre, 5f, centre + new Vector3(12f, 1.5f, 0f), FireHeight),
                        Is.False);
        }

        [Test]
        public void HubSmoulderingBandAndOffThePlate_DoNotBurn()
        {
            var s = OuterBandBurned();
            Assert.That(FireRule.IsBurning(s, Vector3.zero, 0f, new Vector3(3f, 0f, 0f), FireHeight), Is.False, "hub");
            Assert.That(FireRule.IsBurning(s, Vector3.zero, 0f, new Vector3(9.5f, 0f, 0f), FireHeight), Is.False,
                        "smouldering is a warning, not fire");
            Assert.That(FireRule.IsBurning(s, Vector3.zero, 0f, new Vector3(14f, 0f, 0f), FireHeight), Is.False,
                        "beyond the plate");
            Assert.That(FireRule.IsBurning(null, Vector3.zero, 0f, new Vector3(12f, 0f, 0f), FireHeight), Is.False);
        }

        [Test]
        public void DamageThisFrame_IsDpsTimesDt_NeverNegative()
        {
            Assert.That(FireRule.DamageThisFrame(8f, 0.5f), Is.EqualTo(4f));
            Assert.That(FireRule.DamageThisFrame(8f, -1f), Is.EqualTo(0f));
            Assert.That(FireRule.DamageThisFrame(8f, float.NaN), Is.EqualTo(0f));
            Assert.That(FireRule.DamageThisFrame(0f, 1f), Is.EqualTo(0f));
        }
    }
}
