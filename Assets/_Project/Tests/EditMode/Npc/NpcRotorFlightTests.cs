using NUnit.Framework;
using Pickleball.Sandbox.Npc;
using UnityEngine;

namespace Pickleball.Tests.Npc
{
    /// <summary>
    /// The knocked-off rotor's path and the living rotor's spin rules: it
    /// leaves from exactly where it was, lands exactly upright on the spot,
    /// is upright before it comes down, never lands on its edge whatever the
    /// somersaults, hops and stops; the spin eases the same at any frame rate.
    /// </summary>
    public class NpcRotorFlightTests
    {
        static readonly Vector3 From = new(0.1f, 0.45f, -0.5f);
        // Lying on its back: the rotor's up points back and down.
        static readonly Quaternion FromTilt = Quaternion.LookRotation(Vector3.down, new Vector3(0f, -0.3f, -1f).normalized);
        static readonly Vector3 Landing = new(-0.2f, 0f, -1.6f);

        static NpcRotorFlight Flight(int turns = 1, float bounce = 0.05f)
            => new(From, FromTilt, Landing, seconds: 0.6f, height: 0.45f, turns: turns,
                   bounceHeight: bounce, bounceSeconds: 0.2f, spin: 720f, spinDownSeconds: 1.5f);

        [Test]
        public void StartsExactlyWhereTheRotorWas()
        {
            Flight().Sample(0f, Landing, out var p, out var r);
            Assert.That(Vector3.Distance(p, From), Is.LessThan(1e-5f));
            Assert.That(Quaternion.Angle(r, FromTilt), Is.LessThan(0.01f));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void LandsUprightOnTheSpot(int turns)
        {
            var f = Flight(turns);
            f.Sample(f.SettledTime + 1f, Landing, out var p, out var r);
            Assert.That(Vector3.Distance(p, Landing), Is.LessThan(1e-5f));
            Assert.That(Vector3.Angle(r * Vector3.up, Vector3.up), Is.LessThan(0.01f));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void IsUprightForTheLastStretchOfTheDescent(int turns)
        {
            var f = Flight(turns);
            for (float s = NpcRotorFlight.UprightBy; s < 1f; s += 0.02f)
            {
                f.Sample(s * f.LandingTime, Landing, out _, out var r);
                Assert.That(Vector3.Angle(r * Vector3.up, Vector3.up), Is.LessThan(0.05f), $"at {s:0.00} of the flight");
            }
        }

        [Test]
        public void TheArcRisesAboveBothEnds()
        {
            var f = Flight();
            f.Sample(f.LandingTime * 0.5f, Landing, out var mid, out _);
            Assert.That(mid.y, Is.GreaterThan(Mathf.Max(From.y, Landing.y)));
        }

        [Test]
        public void NeverGoesBelowTheLandingHeight()
        {
            var f = Flight();
            for (float t = 0f; t < f.SettledTime + 0.5f; t += 1f / 120f)
            {
                f.Sample(t, Landing, out var p, out _);
                Assert.That(p.y, Is.GreaterThanOrEqualTo(Landing.y - 1e-5f), $"at {t:0.000}s");
            }
        }

        [Test]
        public void HopsOnLandingThenRests()
        {
            var f = Flight(bounce: 0.05f);
            f.Sample(f.LandingTime + 0.1f, Landing, out var hop, out _);
            Assert.That(hop.y - Landing.y, Is.EqualTo(0.05f).Within(1e-4f), "top of the hop, half way through it");
            f.Sample(f.LandingTime + 0.25f, Landing, out var rest, out _);
            Assert.That(rest.y, Is.EqualTo(Landing.y).Within(1e-6f));
        }

        [Test]
        public void FollowsAMovingLandingSpot()
        {
            var f = Flight();
            var moved = Landing + new Vector3(0.2f, 0f, -0.1f);
            f.Sample(f.LandingTime, moved, out var p, out _);
            Assert.That(Vector3.Distance(p, moved), Is.LessThan(1e-5f));
        }

        [Test]
        public void SpinsWhileFlyingThenWindsDownToAStop()
        {
            var f = Flight();
            Assert.That(f.SpinAt(0.1f), Is.EqualTo(720f));
            Assert.That(f.SpinAt(f.LandingTime + 0.75f), Is.EqualTo(360f).Within(1e-3f));
            Assert.That(f.SpinAt(f.LandingTime + 1.5f), Is.EqualTo(0f).Within(1e-3f));
            Assert.That(f.SpinAt(f.SettledTime + 5f), Is.EqualTo(0f));
            Assert.That(f.SettledTime, Is.EqualTo(f.LandingTime + 1.5f).Within(1e-5f));
            Assert.That(f.HasLanded(f.LandingTime - 0.01f), Is.False);
            Assert.That(f.HasLanded(f.LandingTime), Is.True);
        }

        [Test]
        public void Upright_KeepsTheHeading()
        {
            var tilted = Quaternion.Euler(0f, 30f, 0f) * Quaternion.Euler(70f, 0f, 0f);
            var up = NpcRotorFlight.Upright(tilted);
            Assert.That(Vector3.Angle(up * Vector3.up, Vector3.up), Is.LessThan(0.01f));
            Vector3 Heading(Quaternion q) => Vector3.ProjectOnPlane(q * Vector3.right, Vector3.up).normalized;
            Assert.That(Vector3.Angle(Heading(up), Heading(tilted)), Is.LessThan(0.5f));
        }

        // ------------------------------------------------------------- spin

        [Test]
        public void Spin_TargetsTheExcitedSpeedOnlyWhileAnExcitedLinePlays()
        {
            Assert.That(NpcRotorSpin.Target(false, 180f, 900f), Is.EqualTo(180f));
            Assert.That(NpcRotorSpin.Target(true, 180f, 900f), Is.EqualTo(900f));
        }

        [Test]
        public void Spin_EasesTheSameAtAnyFrameRate()
        {
            float At(int fps)
            {
                float v = 180f;
                for (int i = 0; i < fps; i++) v = NpcRotorSpin.Approach(v, 900f, 3f, 1f / fps);
                return v;
            }
            Assert.That(At(30), Is.EqualTo(At(144)).Within(0.01f));
            Assert.That(At(60), Is.EqualTo(900f - 720f * Mathf.Exp(-3f)).Within(0.01f));
        }

        [Test]
        public void Spin_PausedTimeChangesNothing()
        {
            Assert.That(NpcRotorSpin.Approach(180f, 900f, 3f, 0f), Is.EqualTo(180f));
            Assert.That(NpcRotorSpin.Advance(90f, 900f, 0f), Is.EqualTo(90f));
        }

        [Test]
        public void Spin_AngleWrapsIntoOneTurn()
        {
            Assert.That(NpcRotorSpin.Advance(350f, 180f, 0.5f), Is.EqualTo(80f).Within(1e-3f));
        }
    }
}
