using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia.Rings;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Rings
{
    /// <summary>
    /// Celestial Rings driven frame by frame: forecasts on her clock, rings on
    /// real time, one hit per ring, the live cap, phase-2 fire and a clean stop.
    /// Every tunable is written here (<see cref="Settings"/>), so tuning the
    /// fight cannot change what these tests prove.
    /// </summary>
    public class RingFieldTests
    {
        const float Dt = 1f / 60f;

        static RingSettings Settings(int maxLive = 6, int tallEvery = 3) => new RingSettings
        {
            Speed = 4f,
            StartRadius = 1.5f,
            ArenaRadius = 13f,
            Thickness = 0.5f,
            LowRingHeight = 0.8f,
            TallRingVisualHeight = 7f,
            TallRingEvery = tallEvery,
            GapSnapDegrees = 45f,
            GapWidthDegrees = 30f,
            GapStepDegrees = 45f,
            ForecastLead = 1f,
            LowRingDamage = 12f,
            TallRingDamage = 15f,
            FireRingDamage = 18f,
            MaxLiveRings = maxLive,
        };

        static Vector2 At(float azimuth, float distance)
        {
            Vector3 d = RingMath.DirectionFromAzimuth(azimuth) * distance;
            return new Vector2(d.x, d.z);
        }

        static RingField NewField(out List<int> hits, int seed = 7)
        {
            var field = new RingField(6, new System.Random(seed));
            var h = new List<int>();
            field.PlayerHit += (id, dmg, at) => h.Add(id);
            hits = h;
            return field;
        }

        /// <summary>Queue one ring and launch it at once (her full lead in one tick).</summary>
        static RingSnapshot QueueAndLaunch(RingField field, in RingSettings s, bool phaseTwo = false)
        {
            Assert.IsTrue(field.TryQueue(in s, phaseTwo, out RingSnapshot forecast));
            field.Tick(in s, s.ForecastLead, 0f, false, Vector2.zero, 0f, 0.35f);
            return forecast;
        }

        [Test]
        public void RingPassesAStandingPlayer_HitsOnce()
        {
            var s = Settings();
            var field = NewField(out var hits);
            RingSnapshot ring = QueueAndLaunch(field, s);
            Vector2 player = At(ring.GapCentreDegrees + 180f, 5f);

            for (int f = 0; f < 240; f++) field.Tick(in s, Dt, Dt, true, player, 0f, 0.35f);

            Assert.That(hits, Is.EqualTo(new[] { ring.Id }));
            Assert.That(field.LiveCount, Is.EqualTo(0), "the ring should have reached the rim");
        }

        [Test]
        public void PlayerInTheGap_IsNotHit()
        {
            var s = Settings();
            var field = NewField(out var hits);
            RingSnapshot ring = QueueAndLaunch(field, s);
            Vector2 player = At(ring.GapCentreDegrees, 5f);

            for (int f = 0; f < 240; f++) field.Tick(in s, Dt, Dt, true, player, 0f, 0.35f);

            Assert.That(hits, Is.Empty);
        }

        [Test]
        public void LowRing_PlayerAboveItsTop_IsNotHit()
        {
            var s = Settings(tallEvery: 0);
            var field = NewField(out var hits);
            RingSnapshot ring = QueueAndLaunch(field, s);
            Assert.IsFalse(ring.Tall);
            Vector2 player = At(ring.GapCentreDegrees + 180f, 5f);

            for (int f = 0; f < 240; f++) field.Tick(in s, Dt, Dt, true, player, 1f, 0.35f);

            Assert.That(hits, Is.Empty);
        }

        [Test]
        public void TallRing_HitsAPlayer20mUp()
        {
            var s = Settings(tallEvery: 1);
            var field = NewField(out var hits);
            RingSnapshot ring = QueueAndLaunch(field, s);
            Assert.IsTrue(ring.Tall);
            Assert.That(ring.Damage, Is.EqualTo(15f));
            Assert.That(ring.VisualHeight, Is.EqualTo(7f));
            Vector2 player = At(ring.GapCentreDegrees + 180f, 5f);

            for (int f = 0; f < 240; f++) field.Tick(in s, Dt, Dt, true, player, 20f, 0.35f);

            Assert.That(hits.Count, Is.EqualTo(1));
        }

        [Test]
        public void PhaseTwoRing_BurnsActsTallAndDealsFireDamage()
        {
            var s = Settings(tallEvery: 0);
            var field = NewField(out var hits);
            RingSnapshot ring = QueueAndLaunch(field, s, phaseTwo: true);
            Assert.IsTrue(ring.Burning);
            Assert.IsTrue(ring.Tall);
            Assert.That(ring.Damage, Is.EqualTo(18f));
            Vector2 player = At(ring.GapCentreDegrees + 180f, 5f);

            for (int f = 0; f < 240; f++) field.Tick(in s, Dt, Dt, true, player, 20f, 0.35f);

            Assert.That(hits.Count, Is.EqualTo(1));
        }

        [Test]
        public void LowRing_DealsLowDamage()
        {
            var s = Settings(tallEvery: 0);
            var field = NewField(out _);
            RingSnapshot ring = QueueAndLaunch(field, s);
            Assert.That(ring.Damage, Is.EqualTo(12f));
            Assert.That(ring.VisualHeight, Is.EqualTo(0.8f));
        }

        [Test]
        public void FastRing_OneFrameStepLargerThanThickness_StillHits()
        {
            var s = Settings();
            s.Speed = 200f; // 3.3 m per frame against a 0.5 m band
            var field = NewField(out var hits);
            RingSnapshot ring = QueueAndLaunch(field, s);
            Vector2 player = At(ring.GapCentreDegrees + 180f, 5f);

            for (int f = 0; f < 10; f++) field.Tick(in s, Dt, Dt, true, player, 0f, 0.35f);

            Assert.That(hits.Count, Is.EqualTo(1));
        }

        [Test]
        public void PlayerCaughtThenRunningOutwardWithTheRing_IsHitOnlyOnce()
        {
            var s = Settings();
            var field = NewField(out var hits);
            RingSnapshot ring = QueueAndLaunch(field, s);
            float az = ring.GapCentreDegrees + 180f;
            float d = 3f;

            for (int f = 0; f < 240; f++)
            {
                // Stand until hit, then run outward at ring speed, staying in the band.
                if (hits.Count > 0) d = Mathf.Min(d + s.Speed * Dt, 12.5f);
                field.Tick(in s, Dt, Dt, true, At(az, d), 0f, 0.35f);
            }

            Assert.That(hits.Count, Is.EqualTo(1));
        }

        [Test]
        public void Forecast_RunsOnHerClock_AndFreezesWithHer()
        {
            var s = Settings();
            var field = NewField(out _);
            Assert.IsTrue(field.TryQueue(in s, false, out _));

            for (int f = 0; f < 300; f++) field.Tick(in s, 0f, Dt, false, Vector2.zero, 0f, 0.35f);
            Assert.That(field.PendingCount, Is.EqualTo(1), "frozen: the forecast must hold");
            Assert.That(field.LiveCount, Is.EqualTo(0));

            // Half speed: 1 s lead needs 2 s of real time.
            for (int f = 0; f < 110; f++) field.Tick(in s, Dt * 0.5f, Dt, false, Vector2.zero, 0f, 0.35f);
            Assert.That(field.LiveCount, Is.EqualTo(0), "launched early under a half slow");
            for (int f = 0; f < 15; f++) field.Tick(in s, Dt * 0.5f, Dt, false, Vector2.zero, 0f, 0.35f);
            Assert.That(field.LiveCount, Is.EqualTo(1));
        }

        [Test]
        public void LaunchedRing_KeepsExpandingWhileSheIsFrozen()
        {
            var s = Settings();
            var field = NewField(out _);
            QueueAndLaunch(field, s);
            Assert.IsTrue(field.TryGetLive(0, out RingSnapshot before));

            field.Tick(in s, 0f, 0.5f, false, Vector2.zero, 0f, 0.35f);

            Assert.IsTrue(field.TryGetLive(0, out RingSnapshot after));
            Assert.That(after.Radius - before.Radius, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(after.LeadSeconds, Is.EqualTo(0f));
        }

        [Test]
        public void Lifecycle_LaunchedThenRetiredOnce_AtTheRim()
        {
            var s = Settings();
            var field = new RingField(6, new System.Random(1));
            var launched = new List<int>();
            var retired = new List<int>();
            field.Launched += launched.Add;
            field.Retired += retired.Add;

            Assert.IsTrue(field.TryQueue(in s, false, out RingSnapshot forecast));
            Assert.That(forecast.LeadSeconds, Is.EqualTo(1f));
            Assert.That(forecast.Radius, Is.EqualTo(1.5f));
            for (int f = 0; f < 600; f++) field.Tick(in s, Dt, Dt, false, Vector2.zero, 0f, 0.35f);

            Assert.That(launched, Is.EqualTo(new[] { forecast.Id }));
            Assert.That(retired, Is.EqualTo(new[] { forecast.Id }));
        }

        [Test]
        public void LiveCap_SkipsWithoutAdvancingTheSpiral()
        {
            var s = Settings(maxLive: 2);
            var field = NewField(out _);
            Assert.IsTrue(field.TryQueue(in s, false, out RingSnapshot a));
            Assert.IsTrue(field.TryQueue(in s, false, out RingSnapshot b));
            Assert.IsFalse(field.TryQueue(in s, false, out _), "live + pending at the cap");

            // Launch both and let them reach the rim; the next ring continues the spiral.
            for (int f = 0; f < 600; f++) field.Tick(in s, Dt, Dt, false, Vector2.zero, 0f, 0.35f);
            Assert.IsTrue(field.TryQueue(in s, false, out RingSnapshot c));

            Assert.That(c.Id, Is.EqualTo(b.Id + 1));
            float step = 45f * field.SpiralSign;
            Assert.That(RingMath.AngularDistance(b.GapCentreDegrees, a.GapCentreDegrees + step),
                        Is.LessThan(1e-3f));
            Assert.That(RingMath.AngularDistance(c.GapCentreDegrees, b.GapCentreDegrees + step),
                        Is.LessThan(1e-3f));
        }

        [Test]
        public void Spiral_FollowsTheFightsFirstGapAndDirection()
        {
            var s = Settings(maxLive: 8);
            var field = NewField(out _, seed: 123);
            for (int i = 0; i < 8; i++)
            {
                Assert.IsTrue(field.TryQueue(in s, false, out RingSnapshot r));
                Assert.That(r.Id, Is.EqualTo(i));
                Assert.That(r.GapCentreDegrees,
                            Is.EqualTo(RingMath.GapCentreDegrees(i, field.FirstGapDegrees,
                                                                 field.SpiralSign, 45f, 45f)).Within(1e-3f));
                Assert.That(r.GapCentreDegrees % 45f, Is.EqualTo(0f).Within(1e-3f), "on the 45° grid");
                Assert.That(r.GapWidthDegrees, Is.EqualTo(30f));
            }
            Assert.That(field.SpiralSign == 1 || field.SpiralSign == -1);
        }

        [Test]
        public void SameSeed_SameSpiral()
        {
            var s = Settings();
            var a = new RingField(6, new System.Random(42));
            var b = new RingField(6, new System.Random(42));
            a.TryQueue(in s, false, out RingSnapshot ra);
            b.TryQueue(in s, false, out RingSnapshot rb);
            Assert.That(ra.GapCentreDegrees, Is.EqualTo(rb.GapCentreDegrees));
            Assert.That(a.SpiralSign, Is.EqualTo(b.SpiralSign));
        }

        [Test]
        public void TallCadence_EveryThird()
        {
            var s = Settings(maxLive: 6, tallEvery: 3);
            var field = NewField(out _);
            for (int i = 0; i < 6; i++)
            {
                Assert.IsTrue(field.TryQueue(in s, false, out RingSnapshot r));
                Assert.That(r.Tall, Is.EqualTo(i == 2 || i == 5), $"ring {i}");
            }
        }

        [Test]
        public void Clear_RetiresEveryRingAndForecast_AndRestartsTheFight()
        {
            var s = Settings();
            var field = NewField(out _);
            var retired = new List<int>();
            field.Retired += retired.Add;
            QueueAndLaunch(field, s);
            field.TryQueue(in s, false, out _);

            field.Clear();
            field.Clear(); // idempotent

            Assert.That(retired, Is.EquivalentTo(new[] { 0, 1 }));
            Assert.That(field.LiveCount, Is.EqualTo(0));
            Assert.That(field.PendingCount, Is.EqualTo(0));
            Assert.IsFalse(field.FightStarted);
            Assert.IsTrue(field.TryQueue(in s, false, out RingSnapshot next));
            Assert.That(next.Id, Is.EqualTo(0));
        }

        [Test]
        public void ClearFromInsideAHit_StopsTheTickCleanly()
        {
            // A ring kills the player; the brain's StopAll runs inside the hit.
            var s = Settings(maxLive: 3);
            var field = new RingField(3, new System.Random(3));
            int hits = 0;
            field.PlayerHit += (id, dmg, at) => { hits++; field.Clear(); };
            RingSnapshot ring = QueueAndLaunch(field, s);
            field.TryQueue(in s, false, out _);
            Vector2 player = At(ring.GapCentreDegrees + 180f, 2.5f);

            Assert.DoesNotThrow(() =>
            {
                for (int f = 0; f < 120; f++) field.Tick(in s, Dt, Dt, true, player, 0f, 0.35f);
            });
            Assert.That(hits, Is.EqualTo(1));
            Assert.That(field.LiveCount + field.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void HitPoint_IsOnTheRingTowardThePlayer()
        {
            var s = Settings();
            var field = new RingField(6, new System.Random(5));
            Vector3 hitAt = Vector3.zero;
            field.PlayerHit += (id, dmg, at) => hitAt = at;
            RingSnapshot ring = QueueAndLaunch(field, s);
            float az = ring.GapCentreDegrees + 180f;

            for (int f = 0; f < 240; f++) field.Tick(in s, Dt, Dt, true, At(az, 5f), 0f, 0.35f);

            var flat = new Vector2(hitAt.x, hitAt.z);
            Assert.That(RingMath.AngularDistance(RingMath.AzimuthDegrees(flat), az), Is.LessThan(0.1f));
            Assert.That(flat.magnitude, Is.EqualTo(5f).Within(0.7f));
        }
    }
}
