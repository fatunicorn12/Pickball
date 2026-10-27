using NUnit.Framework;
using Pickleball.Sandbox.Movement.Logic;
using UnityEngine;

namespace Pickleball.Tests.Movement
{
    /// <summary>
    /// The field channel moved out of ChibiLocomotion during the movement
    /// extraction. These pin the two promises other systems were built on: a
    /// steady pull settles at a/k at any frame rate (GravityWell's tuning), and
    /// achieved − Applied is exactly the character's own motion (the Fluxion
    /// Beam's damage).
    /// </summary>
    public class ForceFieldChannelTests
    {
        [TestCase(30)]
        [TestCase(144)]
        public void Integrate_SettlesAtAccelerationOverDamping(int fps)
        {
            var ch = new ForceFieldChannel();
            for (int i = 0; i < fps * 10; i++) ch.Integrate(new Vector3(9f, 0f, 0f), 1f / fps, 2.5f, 18f);
            Assert.That(ch.Velocity.x, Is.EqualTo(9f / 2.5f).Within(1e-3f));
        }

        [Test]
        public void Integrate_HasNoThresholdForWeakPulls()
        {
            var ch = new ForceFieldChannel();
            ch.Integrate(new Vector3(0.1f, 0f, 0f), 0.1f, 2.5f, 18f);
            Assert.That(ch.Velocity.x, Is.GreaterThan(0f));
        }

        [Test]
        public void Integrate_IsCappedByMaxSpeed()
        {
            var ch = new ForceFieldChannel();
            for (int i = 0; i < 600; i++) ch.Integrate(new Vector3(500f, 0f, 0f), 1f / 60f, 2.5f, 18f);
            Assert.That(ch.Velocity.magnitude, Is.EqualTo(18f).Within(1e-3f));
        }

        [Test]
        public void Attribute_HeldMotionlessInAField_HasNoOwnVelocity()
        {
            // Requesting nothing of his own, pulled 3 m/s into a wall that eats it.
            var ch = new ForceFieldChannel();
            var field = new Vector3(3f, 0f, 0f);
            Vector3 achieved = Vector3.zero;
            ch.Attribute(field, field, grounded: true, achieved);
            Assert.That((achieved - ch.Applied).magnitude, Is.LessThan(1e-5f));
        }

        [Test]
        public void Attribute_StandingInAField_IgnoresTheGroundHoldDown()
        {
            // Grounded stick is -2 m/s of request the floor always absorbs.
            var ch = new ForceFieldChannel();
            var field = new Vector3(1f, 0f, 0f);
            var requested = field + new Vector3(0f, -2f, 0f);
            var achieved = new Vector3(1f, 0f, 0f);
            ch.Attribute(requested, field, grounded: true, achieved);
            Assert.That((achieved - ch.Applied).magnitude, Is.LessThan(1e-5f));
        }

        [Test]
        public void Attribute_InOpenSpace_SplitsCleanly()
        {
            var ch = new ForceFieldChannel();
            var own = new Vector3(0f, 0f, 4f);
            var field = new Vector3(2f, 0f, 0f);
            ch.Attribute(own + field, field, grounded: false, own + field);
            Assert.That((ch.Applied - field).magnitude, Is.LessThan(1e-5f));
        }

        [Test]
        public void Clear_DropsBothChannels()
        {
            var ch = new ForceFieldChannel();
            ch.Integrate(Vector3.one * 10f, 1f, 2.5f, 18f);
            ch.Attribute(Vector3.one, Vector3.one, false, Vector3.one);
            ch.Clear();
            Assert.That(ch.Velocity, Is.EqualTo(Vector3.zero));
            Assert.That(ch.Applied, Is.EqualTo(Vector3.zero));
        }
    }
}
