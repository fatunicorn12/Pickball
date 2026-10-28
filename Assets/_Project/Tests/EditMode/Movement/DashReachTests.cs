using NUnit.Framework;
using Pickleball.Sandbox.Movement;
using Pickleball.Sandbox.Movement.Logic;
using UnityEngine;

namespace Pickleball.Tests.Movement
{
    /// <summary>
    /// The protractor dash is advertised as a fixed reach — 14 m/s for 0.22 s,
    /// 3.08 m along the 45° vector — so gaps and ledges can be designed against
    /// it. Stepping by whole frames overshot on the last frame (about 3.27 m,
    /// QA-Assessment MV1). These pin the reach at any frame rate, through the same
    /// FixedDuration stepping DashMove uses and the shipped config defaults.
    /// </summary>
    public class DashReachTests
    {
        MovementConfig _config;

        [SetUp]
        public void SetUp() => _config = ScriptableObject.CreateInstance<MovementConfig>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        [TestCase(7)]
        [TestCase(24)]
        [TestCase(30)]
        [TestCase(60)]
        [TestCase(144)]
        public void Dash_CoversExactlySpeedTimesDuration_AtAnyFrameRate(int fps)
        {
            float reach = Reach(1f / fps);
            Assert.That(reach, Is.EqualTo(_config.DashSpeed * _config.DashDuration).Within(1e-4f));
        }

        [Test]
        public void Dash_DefaultReachIsTheAdvertised308Metres()
        {
            Assert.That(Reach(1f / 60f), Is.EqualTo(3.08f).Within(1e-3f));
        }

        [Test]
        public void Dash_AHitchLongerThanTheDashStillStopsAtTheReach()
        {
            // One 0.3 s frame swallows the whole 0.22 s dash.
            Assert.That(Reach(0.3f), Is.EqualTo(3.08f).Within(1e-3f));
        }

        [Test]
        public void Consume_NeverSpendsMoreThanIsLeftOrGoesNegative()
        {
            float left = 0.05f;
            Assert.That(FixedDuration.Consume(ref left, 0.016f), Is.EqualTo(0.016f).Within(1e-6f));
            Assert.That(FixedDuration.Consume(ref left, 0.1f), Is.EqualTo(0.034f).Within(1e-6f));
            Assert.That(left, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(FixedDuration.Consume(ref left, 0.1f), Is.EqualTo(0f));
            Assert.That(left, Is.EqualTo(0f));
        }

        /// <summary>Distance along the dash vector, stepping as DashMove does.</summary>
        float Reach(float dt)
        {
            float left = _config.DashDuration, moved = 0f;
            for (int i = 0; i < 10000 && left > 0f; i++)
                moved += _config.DashSpeed * FixedDuration.Consume(ref left, dt);
            return moved;
        }
    }
}
