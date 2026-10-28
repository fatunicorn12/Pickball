using NUnit.Framework;
using Pickleball.Sandbox.Magic;
using UnityEngine;

namespace Pickleball.Tests.Magic
{
    /// <summary>
    /// The Thunderbolt's and Fireball's arithmetic: chain target picking
    /// (<see cref="StrikeMath.PickChain"/>: nearest first, within reach, never
    /// the struck), the telegraph clock, and blast falloff at the shipped
    /// spells' numbers (the falloff function itself is pinned by OrbMathTests).
    /// </summary>
    public class StrikeMathTests
    {
        static readonly Vector3[] Crowd =
        {
            new(4f, 0f, 0f),    // 0: 4 m
            new(1f, 0f, 0f),    // 1: 1 m
            new(0f, 0f, 9f),    // 2: 9 m, out of a 6 m reach
            new(0f, 2f, 0f),    // 3: 2 m (straight up counts)
            new(-3f, 0f, 0f),   // 4: 3 m
        };

        [Test]
        public void PickChain_NearestFirst_WithinReach()
        {
            var result = new int[4];
            int n = StrikeMath.PickChain(Vector3.zero, Crowd, null, Crowd.Length, 6f, 4, result);
            Assert.That(n, Is.EqualTo(4));
            Assert.That(result, Is.EqualTo(new[] { 1, 3, 4, 0 }));
        }

        [Test]
        public void PickChain_StopsAtCount()
        {
            var result = new int[4];
            Assert.That(StrikeMath.PickChain(Vector3.zero, Crowd, null, Crowd.Length, 6f, 2, result), Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(1));
            Assert.That(result[1], Is.EqualTo(3));
        }

        [Test]
        public void PickChain_SkipsTheStruck()
        {
            var result = new int[2];
            var excluded = new[] { false, true, false, true, false };
            Assert.That(StrikeMath.PickChain(Vector3.zero, Crowd, excluded, Crowd.Length, 6f, 2, result), Is.EqualTo(2));
            Assert.That(result, Is.EqualTo(new[] { 4, 0 }));
        }

        [Test]
        public void PickChain_NothingInReach()
        {
            var result = new int[2];
            Assert.That(StrikeMath.PickChain(Vector3.zero, Crowd, null, Crowd.Length, 0.5f, 2, result), Is.EqualTo(0));
            Assert.That(StrikeMath.PickChain(Vector3.zero, Crowd, null, Crowd.Length, 0f, 2, result), Is.EqualTo(0));
            Assert.That(StrikeMath.PickChain(Vector3.zero, Crowd, null, 0, 6f, 2, result), Is.EqualTo(0));
        }

        [Test]
        public void PickChain_TiesKeepTheLowerIndex()
        {
            var twins = new[] { new Vector3(2f, 0f, 0f), new Vector3(-2f, 0f, 0f) };
            var result = new int[1];
            StrikeMath.PickChain(Vector3.zero, twins, null, 2, 5f, 1, result);
            Assert.That(result[0], Is.EqualTo(0));
        }

        [Test]
        public void TelegraphProgress_ClampedAndSafe()
        {
            Assert.That(StrikeMath.TelegraphProgress(0f, 0.35f), Is.EqualTo(0f));
            Assert.That(StrikeMath.TelegraphProgress(0.175f, 0.35f), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(StrikeMath.TelegraphProgress(1f, 0.35f), Is.EqualTo(1f));
            Assert.That(StrikeMath.TelegraphProgress(0f, 0f), Is.EqualTo(1f), "no delay strikes at once");
        }

        // ------------------------------------------- blast falloff, as shipped

        [TestCase(0f, 12f)]      // centre: max
        [TestCase(1.6f, 8.5f)]   // halfway
        [TestCase(3.2f, 5f)]     // edge: min
        [TestCase(3.21f, 0f)]    // outside
        public void FireballBlast_Falloff(float distance, float expected)
        {
            Assert.That(OrbMath.BlastDamageAt(distance, 3.2f, 12f, 5f), Is.EqualTo(expected).Within(1e-4f));
        }

        [TestCase(0f, 12f)]
        [TestCase(0.8f, 8.5f)]
        [TestCase(1.6f, 5f)]
        [TestCase(2f, 0f)]
        public void ThunderboltSplash_Falloff(float distance, float expected)
        {
            Assert.That(OrbMath.BlastDamageAt(distance, 1.6f, 12f, 5f), Is.EqualTo(expected).Within(1e-4f));
        }
    }
}
