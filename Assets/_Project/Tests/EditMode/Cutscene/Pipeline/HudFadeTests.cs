using NUnit.Framework;
using Pickleball.Cutscene.Playback;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Pipeline
{
    /// <summary>The HUD fade's exponential smoothing.</summary>
    public class HudFadeTests
    {
        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void SameFadeAtAnyFrameRate(float dt)
        {
            float a = 1f;
            int steps = Mathf.RoundToInt(0.25f / dt);
            for (int i = 0; i < steps; i++) a = HudFade.Step(a, 0f, 6f, dt);
            Assert.That(a, Is.EqualTo(Mathf.Exp(-6f * steps * dt)).Within(1e-4f));
        }

        [Test]
        public void SnapsToTheTarget()
        {
            float a = 0f;
            for (int i = 0; i < 300; i++) a = HudFade.Step(a, 1f, 6f, 1f / 60f);
            Assert.That(a, Is.EqualTo(1f));
        }

        [Test]
        public void NeverOvershoots()
        {
            float a = 1f;
            for (int i = 0; i < 10; i++)
            {
                a = HudFade.Step(a, 0f, 50f, 0.5f);
                Assert.That(a, Is.GreaterThanOrEqualTo(0f));
            }
            Assert.That(HudFade.Step(0.4f, 0.4f, 6f, 0.1f), Is.EqualTo(0.4f));
        }

        [Test]
        public void ZeroTimeChangesNothing()
        {
            Assert.That(HudFade.Step(0.5f, 0f, 6f, 0f), Is.EqualTo(0.5f));
        }
    }
}
