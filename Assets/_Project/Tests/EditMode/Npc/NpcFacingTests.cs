using NUnit.Framework;
using Pickleball.Sandbox.Npc;
using UnityEngine;

namespace Pickleball.Tests.Npc
{
    /// <summary>
    /// Turning to face the player: the right direction, the short way round,
    /// capped, and the same at 30, 60 and 144 fps (it runs on unscaled time
    /// while the game is paused, at whatever rate the Editor ticks).
    /// </summary>
    public class NpcFacingTests
    {
        [TestCase(0f, 1f, 0f)]
        [TestCase(1f, 0f, 90f)]
        [TestCase(-1f, 0f, -90f)]
        [TestCase(1f, 1f, 45f)]
        public void TryYawTowards_PointsAcrossTheGround(float dx, float dz, float expected)
        {
            var from = new Vector3(3f, 1f, -2f);
            Assert.That(NpcFacing.TryYawTowards(from, from + new Vector3(dx, 5f, dz), out float yaw), Is.True);
            Assert.That(Mathf.DeltaAngle(yaw, expected), Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void TryYawTowards_RefusesAPointOverhead()
        {
            Assert.That(NpcFacing.TryYawTowards(Vector3.zero, new Vector3(0.01f, 2f, 0f), out _), Is.False);
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void StepYaw_IsFrameRateIndependent(float dt)
        {
            const float rate = 6f, seconds = 0.5f;
            float yaw = 0f;
            int frames = Mathf.RoundToInt(seconds / dt);
            for (int i = 0; i < frames; i++) yaw = NpcFacing.StepYaw(yaw, 90f, rate, 0f, dt);

            float expected = 90f * (1f - Mathf.Exp(-rate * frames * dt));
            Assert.That(yaw, Is.EqualTo(expected).Within(0.05f));
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void StepYaw_SettlesAnAboutFaceInOneAndAHalfSecondsEvenCapped(float dt)
        {
            float yaw = 0f;
            int frames = Mathf.RoundToInt(1.5f / dt);
            for (int i = 0; i < frames; i++) yaw = NpcFacing.StepYaw(yaw, 179f, 6f, 300f, dt);
            Assert.That(Mathf.DeltaAngle(yaw, 179f), Is.EqualTo(0f).Within(1f));
        }

        [Test]
        public void StepYaw_TurnsTheShortWayRound()
        {
            float yaw = NpcFacing.StepYaw(170f, -170f, 6f, 0f, 1f / 60f);
            Assert.That(yaw, Is.GreaterThan(170f), "170° to -170° is 20° clockwise, not 340° back");
        }

        [Test]
        public void StepYaw_RespectsTheSpeedCap()
        {
            const float dt = 1f / 60f;
            float yaw = 0f;
            for (int i = 0; i < 30; i++) yaw = NpcFacing.StepYaw(yaw, 180f, 50f, 100f, dt);
            Assert.That(yaw, Is.LessThanOrEqualTo(100f * 30 * dt + 0.01f));
        }

        [Test]
        public void StepYaw_NoTimeNoTurn()
        {
            Assert.That(NpcFacing.StepYaw(10f, 90f, 6f, 300f, 0f), Is.EqualTo(10f));
        }
    }
}
