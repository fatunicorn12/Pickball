using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia.Presentation;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Pulse
{
    /// <summary>
    /// The knockback pulse's look. What matters to the player: the drawn wave ends
    /// at exactly Pulse Reach (it is their evidence of whether they were inside),
    /// it arrives fast, and the camera kick never grows with distance.
    /// </summary>
    public class PulseLookTests
    {
        const float Reach = 10f;
        const float Expand = 0.25f;
        const float Fade = 0.45f;

        [Test]
        public void Wave_StartsAtHer_AndEndsAtExactlyPulseReach()
        {
            Assert.That(PulseLook.WaveRadius(0f, Expand, Reach), Is.EqualTo(0f));
            Assert.That(PulseLook.WaveRadius(Expand, Expand, Reach), Is.EqualTo(Reach).Within(1e-4f));
        }

        [Test]
        public void Wave_NeverPassesReach_AndNeverShrinks()
        {
            float previous = 0f;
            for (float age = 0f; age < Expand + Fade + 1f; age += 0.01f)
            {
                float r = PulseLook.WaveRadius(age, Expand, Reach);
                Assert.That(r, Is.LessThanOrEqualTo(Reach + 1e-4f), $"at {age}s");
                Assert.That(r, Is.GreaterThanOrEqualTo(previous - 1e-5f), $"at {age}s");
                previous = r;
            }
        }

        [Test]
        public void Wave_IsPastHalfwayInTheFirstThirdOfItsTime()
        {
            // Fast out, gentle in: it should hit, not grow.
            Assert.That(PulseLook.WaveRadius(Expand / 3f, Expand, Reach), Is.GreaterThan(Reach * 0.5f));
        }

        [Test]
        public void Wave_WithNoExpandTime_ArrivesAtOnce()
        {
            Assert.That(PulseLook.WaveRadius(0f, 0f, Reach), Is.EqualTo(Reach));
        }

        [Test]
        public void Fade_IsFullWhileTravelling_ThenFallsToNothing()
        {
            Assert.That(PulseLook.WaveFade(0f, Expand, Fade), Is.EqualTo(1f));
            Assert.That(PulseLook.WaveFade(Expand, Expand, Fade), Is.EqualTo(1f));
            Assert.That(PulseLook.WaveFade(Expand + Fade * 0.5f, Expand, Fade), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(PulseLook.WaveFade(Expand + Fade, Expand, Fade), Is.EqualTo(0f));
            Assert.That(PulseLook.WaveFade(Expand + 0.01f, Expand, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void Finished_OnlyOnceTravelledAndFaded()
        {
            Assert.That(PulseLook.WaveFinished(Expand + Fade - 0.01f, Expand, Fade), Is.False);
            Assert.That(PulseLook.WaveFinished(Expand + Fade, Expand, Fade), Is.True);
        }

        [Test]
        public void Flash_PeaksOnTheFirstFrame_AndDiesOnTime()
        {
            Assert.That(PulseLook.Flash(0f, 0.16f), Is.EqualTo(1f));
            Assert.That(PulseLook.Flash(0.08f, 0.16f), Is.LessThan(0.5f));
            Assert.That(PulseLook.Flash(0.16f, 0.16f), Is.EqualTo(0f));
            Assert.That(PulseLook.Flash(0f, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void Swell_RushesIntoTheRelease_AndStaysInRange()
        {
            Assert.That(PulseLook.Swell(0f), Is.EqualTo(0f));
            Assert.That(PulseLook.Swell(0.5f), Is.LessThan(0.5f));
            Assert.That(PulseLook.Swell(1f), Is.EqualTo(1f));
            Assert.That(PulseLook.Swell(2f), Is.EqualTo(1f));
            Assert.That(PulseLook.Swell(-1f), Is.EqualTo(0f));
        }

        [Test]
        public void Spin_RisesFromRestToPeakOverTheCharge()
        {
            Assert.That(PulseLook.SpinRate(0f, 25f, 280f), Is.EqualTo(25f));
            Assert.That(PulseLook.SpinRate(1f, 25f, 280f), Is.EqualTo(280f));
        }

        [Test]
        public void Shake_FallsWithDistance_ToNothingAtTwiceTheReach()
        {
            const float centre = 0.42f, edge = 0.14f;
            Assert.That(PulseLook.ShakeAmount(0f, Reach, centre, edge), Is.EqualTo(centre));
            Assert.That(PulseLook.ShakeAmount(Reach, Reach, centre, edge), Is.EqualTo(edge).Within(1e-5f));
            Assert.That(PulseLook.ShakeAmount(Reach * 2f, Reach, centre, edge), Is.EqualTo(0f));
            Assert.That(PulseLook.ShakeAmount(Reach * 5f, Reach, centre, edge), Is.EqualTo(0f));

            float previous = float.MaxValue;
            for (float d = 0f; d <= Reach * 2.5f; d += 0.25f)
            {
                float s = PulseLook.ShakeAmount(d, Reach, centre, edge);
                Assert.That(s, Is.LessThanOrEqualTo(previous + 1e-5f), $"at {d} m");
                previous = s;
            }
        }

        [Test]
        public void Shake_WithNoReach_IsNothing()
        {
            Assert.That(PulseLook.ShakeAmount(1f, 0f, 0.4f, 0.1f), Is.EqualTo(0f));
        }
    }
}
