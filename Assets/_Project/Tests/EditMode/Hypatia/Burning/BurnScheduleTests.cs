using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia.Burning;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Burning
{
    /// <summary>
    /// The burn order: outermost unburned band first, smouldering bands
    /// skipped, monotonic inward, stopping at the minimum safe radius, never
    /// un-burning; the smoulder countdown (frozen by a 0 dt); cancelling at the
    /// end of the fight; and the safe radius along the way.
    /// </summary>
    public class BurnScheduleTests
    {
        static readonly float[] Radii = { 6f, 8.33f, 10.67f, 13f };

        static BurnSchedule NewSchedule(float minSafe = 6f) => new(Radii, 13f, minSafe);

        [Test]
        public void FirstSmoulder_IsTheOutermostBand()
        {
            var s = NewSchedule();
            Assert.That(s.NextToIgnite, Is.EqualTo(3));
            Assert.That(s.StartSmoulder(2f), Is.EqualTo(3));
            Assert.That(s.StateOf(3), Is.EqualTo(BandState.Smouldering));
            Assert.That(s.StateOf(2), Is.EqualTo(BandState.Unburned));
        }

        [Test]
        public void ASmoulderingBand_IsSkipped()
        {
            var s = NewSchedule();
            Assert.That(s.StartSmoulder(2f), Is.EqualTo(3));
            Assert.That(s.StartSmoulder(2f), Is.EqualTo(2), "3 is spoken for");
            Assert.That(s.SmoulderingBands, Is.EqualTo(2));
        }

        [Test]
        public void RepeatedCalls_BurnInwardAndStopAtTheSafeRadius()
        {
            var s = NewSchedule();
            var order = new List<int>();
            for (int call = 0; call < 6; call++)
            {
                int band = s.StartSmoulder(1f);
                if (band >= 0) order.Add(band);
                s.Tick(1f, null);
            }

            Assert.That(order, Is.EqualTo(new[] { 3, 2, 1 }));
            Assert.That(s.BurnedBands, Is.EqualTo(3));
            Assert.That(s.CanIgnite, Is.False);
            Assert.That(s.StartSmoulder(1f), Is.EqualTo(-1));
            Assert.That(s.StateOf(0), Is.EqualTo(BandState.Unburned), "the hub stays safe");
            Assert.That(s.SafeRadius, Is.EqualTo(6f));
        }

        [Test]
        public void ALargerSafeRadius_StopsSooner()
        {
            var s = NewSchedule(minSafe: 9f);
            Assert.That(s.StartSmoulder(0f), Is.EqualTo(3));
            Assert.That(s.CanIgnite, Is.False, "band 2's inner edge, 8.33 m, is inside 9 m");
            Assert.That(s.StartSmoulder(0f), Is.EqualTo(-1));
        }

        [Test]
        public void Ignition_WaitsForTheLead()
        {
            var s = NewSchedule();
            var ignited = new List<int>();
            s.StartSmoulder(2f);

            s.Tick(1.5f, ignited.Add);
            Assert.That(s.StateOf(3), Is.EqualTo(BandState.Smouldering));
            Assert.That(s.SmoulderRemaining(3), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(ignited, Is.Empty);

            s.Tick(0.5f, ignited.Add);
            Assert.That(s.StateOf(3), Is.EqualTo(BandState.Burned));
            Assert.That(ignited, Is.EqualTo(new[] { 3 }));
            Assert.That(s.SmoulderRemaining(3), Is.EqualTo(0f));
        }

        [Test]
        public void AFrozenClock_HoldsTheSmoulder()
        {
            var s = NewSchedule();
            s.StartSmoulder(2f);
            for (int i = 0; i < 100; i++) s.Tick(0f, null);
            s.Tick(float.NaN, null);
            s.Tick(-5f, null);
            Assert.That(s.StateOf(3), Is.EqualTo(BandState.Smouldering));
            Assert.That(s.SmoulderRemaining(3), Is.EqualTo(2f));
        }

        [Test]
        public void AZeroLead_IgnitesOnTheNextTick_EvenFrozen()
        {
            var s = NewSchedule();
            var ignited = new List<int>();
            s.StartSmoulder(0f);
            s.Tick(0f, ignited.Add);
            Assert.That(ignited, Is.EqualTo(new[] { 3 }));
        }

        [Test]
        public void TwoBandsCatchingInOneTick_ReportOutermostFirst()
        {
            var s = NewSchedule();
            var ignited = new List<int>();
            s.StartSmoulder(1f);
            s.StartSmoulder(1f);
            s.Tick(5f, ignited.Add);
            Assert.That(ignited, Is.EqualTo(new[] { 3, 2 }));
        }

        [Test]
        public void CancelSmoulders_CallsOffPendingBands_AndNothingUnburns()
        {
            var s = NewSchedule();
            s.StartSmoulder(0f);
            s.Tick(0f, null); // band 3 burned
            s.StartSmoulder(2f); // band 2 smouldering

            Assert.That(s.CancelSmoulders(), Is.EqualTo(1));
            Assert.That(s.StateOf(2), Is.EqualTo(BandState.Unburned));
            Assert.That(s.StateOf(3), Is.EqualTo(BandState.Burned));

            s.Tick(100f, null);
            Assert.That(s.StateOf(3), Is.EqualTo(BandState.Burned), "never recovers");
            Assert.That(s.StateOf(2), Is.EqualTo(BandState.Unburned), "a cancelled smoulder never catches");
            Assert.That(s.NextToIgnite, Is.EqualTo(2));
        }

        [Test]
        public void SafeRadius_CountsSmoulderingAsFloor_ProjectedDoesNot()
        {
            var s = NewSchedule();
            Assert.That(s.SafeRadius, Is.EqualTo(13f));
            s.StartSmoulder(1f);
            Assert.That(s.SafeRadius, Is.EqualTo(13f));
            Assert.That(s.ProjectedSafeRadius, Is.EqualTo(10.67f));
            s.Tick(1f, null);
            Assert.That(s.SafeRadius, Is.EqualTo(10.67f));
        }

        [Test]
        public void SafeRadius_MatchesBandMathAfterEachBurn()
        {
            var s = NewSchedule();
            for (int k = 1; k <= 3; k++)
            {
                s.StartSmoulder(0f);
                s.Tick(0f, null);
                Assert.That(s.SafeRadius, Is.EqualTo(BandMath.SafeRadiusAfter(Radii, k, 6f)), $"after {k}");
            }
        }

        [Test]
        public void IsBurnedAt_FollowsTheBandsAndTheEdgeRule()
        {
            var s = NewSchedule();
            s.StartSmoulder(0f);
            s.Tick(0f, null);
            Assert.That(s.IsBurnedAt(12f), Is.True);
            Assert.That(s.IsBurnedAt(13f), Is.True, "the rim");
            Assert.That(s.IsBurnedAt(10.7f), Is.True);
            Assert.That(s.IsBurnedAt(10.67f), Is.False, "the inner edge belongs to band 2");
            Assert.That(s.IsBurnedAt(13.5f), Is.False, "off the plate");
            Assert.That(s.IsBurnedAt(3f), Is.False);
        }

        [Test]
        public void MalformedRadii_AreRepairedBeforeScheduling()
        {
            var s = new BurnSchedule(new[] { 10.67f, 6f, 8.33f }, 13f, 6f); // unsorted, short
            Assert.That(s.BandCount, Is.EqualTo(4));
            Assert.That(s.OuterRadius(3), Is.EqualTo(13f));
            Assert.That(s.StartSmoulder(0f), Is.EqualTo(3));
            Assert.That(s.InnerRadius(3), Is.EqualTo(10.67f));
        }

        [Test]
        public void AnUnbuiltSchedule_IsInert()
        {
            // What a script reload can leave behind: a default instance.
            var s = new BurnSchedule();
            Assert.That(s.IsBuilt, Is.False);
            Assert.That(s.BandCount, Is.EqualTo(0));
            Assert.That(s.CanIgnite, Is.False);
            Assert.That(s.StartSmoulder(1f), Is.EqualTo(-1));
            Assert.That(s.IsBurnedAt(5f), Is.False);
            Assert.That(s.CancelSmoulders(), Is.EqualTo(0));
            Assert.That(s.TryPointOnBurnedFloor(0.5f, 0.5f, 0.5f, out _), Is.False);
            Assert.DoesNotThrow(() => s.Tick(1f, null));
        }

        [Test]
        public void TryPointOnBurnedFloor_OnlyWhenBurned_AndAlwaysInTheFire()
        {
            var s = NewSchedule();
            Assert.That(s.TryPointOnBurnedFloor(0.5f, 0.5f, 0.5f, out _), Is.False, "nothing burned");

            s.StartSmoulder(0f);
            s.StartSmoulder(0f);
            s.Tick(0f, null); // bands 3 and 2 burned
            for (int i = 0; i < 10; i++)
            for (int j = 0; j < 10; j++)
            {
                float pick = (i + 0.5f) / 10f, u = (j + 0.5f) / 10f;
                Assert.That(s.TryPointOnBurnedFloor(pick, u, 0.3f, out Vector3 p), Is.True);
                Assert.That(s.IsBurnedAt(p.magnitude), Is.True, $"pick {pick}, u {u}: r = {p.magnitude}");
            }
        }
    }
}
