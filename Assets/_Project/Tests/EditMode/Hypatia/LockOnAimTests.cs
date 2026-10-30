using NUnit.Framework;
using Pickleball.Sandbox;
using UnityEngine;

namespace Pickleball.Tests.Hypatia
{
    /// <summary>
    /// Lock-on measures a target from its body, not the scenery it carries: the
    /// regression was Hypatia's astrolabe dial pulling the aim 3 m above her head.
    /// </summary>
    public class LockOnAimTests : BossTestFixture
    {
        Health MakeTargetWithBodyAndDial(bool ignoreDial)
        {
            var health = NewComponent<Health>();
            health.transform.position = new Vector3(0f, 2.6f, 0f);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);   // 1 × 2 × 1
            body.hideFlags = HideFlags.DontSave;
            body.transform.SetParent(health.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);

            var dial = GameObject.CreatePrimitive(PrimitiveType.Cube);
            dial.hideFlags = HideFlags.DontSave;
            dial.transform.SetParent(health.transform, false);
            dial.transform.localPosition = new Vector3(0f, 6f, -1.8f);
            dial.transform.localScale = new Vector3(4f, 4f, 0.2f);
            if (ignoreDial) dial.AddComponent<LockOnIgnore>();
            return health;
        }

        [Test]
        public void IgnoredScenery_IsLeftOutOfAimAndRadius()
        {
            var target = MakeTargetWithBodyAndDial(ignoreDial: true);
            Assert.IsTrue(LockOnTargeting.MeasureAim(target, out Vector3 aim, out float radius, out _));
            // Body centre 1 m up plus a fifth of its 1 m half-height.
            Assert.That(aim.y, Is.EqualTo(1.2f).Within(0.01f));
            Assert.That(aim.z, Is.EqualTo(0f).Within(0.01f));
            Assert.That(radius, Is.EqualTo(0.5f).Within(0.01f));
        }

        [Test]
        public void WithoutTheMarker_TheDialStillCounts()
        {
            // Documents the old behaviour the marker exists to avoid.
            var target = MakeTargetWithBodyAndDial(ignoreDial: false);
            Assert.IsTrue(LockOnTargeting.MeasureAim(target, out Vector3 aim, out float radius, out _));
            Assert.That(aim.y, Is.GreaterThan(3f));
            Assert.That(radius, Is.EqualTo(2f).Within(0.01f));
        }

        [Test]
        public void OnlyScenery_FallsBackToADefaultChest()
        {
            var health = NewComponent<Health>();
            var dial = GameObject.CreatePrimitive(PrimitiveType.Cube);
            dial.hideFlags = HideFlags.DontSave;
            dial.transform.SetParent(health.transform, false);
            dial.AddComponent<LockOnIgnore>();

            Assert.IsFalse(LockOnTargeting.MeasureAim(health, out Vector3 aim, out float radius, out _));
            Assert.That(aim, Is.EqualTo(Vector3.up * 1.2f));
            Assert.That(radius, Is.EqualTo(0.6f));
        }
    }
}
