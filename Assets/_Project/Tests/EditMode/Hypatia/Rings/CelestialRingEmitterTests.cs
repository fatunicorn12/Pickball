using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia;
using Pickleball.Sandbox.Boss.Hypatia.Rings;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Rings
{
    /// <summary>
    /// The emitter's contract with the brain and with presentation: it does
    /// nothing until bound, forecasts through <see cref="CelestialRingEmitter.GapForecast"/>,
    /// reads phase 2 from the context at queue time, respects the live cap and
    /// retires everything on StopAll. The frame-by-frame rules are covered by
    /// <see cref="RingFieldTests"/>; Update is not ticked here because EditMode
    /// has no reliable Time.deltaTime.
    /// </summary>
    public class CelestialRingEmitterTests : BossTestFixture
    {
        HypatiaConfig _config;

        [SetUp]
        public void MakeConfig()
        {
            _config = ScriptableObject.CreateInstance<HypatiaConfig>();
            _config.hideFlags = HideFlags.DontSave;
            SetConfig("maxLiveRings", 3);
            SetConfig("tallRingEvery", 0);
            SetConfig("gapForecastLead", 1f);
            SetConfig("gapWidthDegrees", 30f);
            SetConfig("gapSnapDegrees", 45f);
            SetConfig("gapStepDegrees", 45f);
            SetConfig("lowRingDamage", 12f);
            SetConfig("fireRingDamage", 18f);
        }

        [TearDown]
        public void DestroyConfig()
        {
            if (_config != null) Object.DestroyImmediate(_config);
        }

        void SetConfig(string field, float value)
        {
            var so = new SerializedObject(_config);
            SerializedProperty p = so.FindProperty(field);
            Assert.That(p, Is.Not.Null, $"HypatiaConfig has no serialized field '{field}'.");
            if (p.propertyType == SerializedPropertyType.Integer) p.intValue = Mathf.RoundToInt(value);
            else p.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        CelestialRingEmitter NewBoundEmitter(out HypatiaContext context)
        {
            var emitter = NewComponent<CelestialRingEmitter>();
            emitter.SetRandom(new System.Random(11));
            context = new HypatiaContext(_config, emitter.transform, null, null, null, null, null);
            emitter.Bind(context);
            return emitter;
        }

        [Test]
        public void Unbound_QueuesNothing()
        {
            var emitter = NewComponent<CelestialRingEmitter>();
            Assert.IsFalse(emitter.QueueRing());
            Assert.That(emitter.LiveRings, Is.EqualTo(0));
            Assert.DoesNotThrow(emitter.StopAll);
        }

        [Test]
        public void QueueRing_RaisesAForecastWithTheConfiguredGap()
        {
            var emitter = NewBoundEmitter(out _);
            var forecasts = new List<RingSnapshot>();
            emitter.GapForecast += forecasts.Add;

            Assert.IsTrue(emitter.QueueRing());

            Assert.That(forecasts.Count, Is.EqualTo(1));
            RingSnapshot f = forecasts[0];
            Assert.That(f.GapWidthDegrees, Is.EqualTo(30f));
            Assert.That(f.GapCentreDegrees % 45f, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(f.LeadSeconds, Is.EqualTo(1f));
            Assert.IsFalse(f.Burning);
            Assert.That(f.Damage, Is.EqualTo(12f));
            Assert.That(emitter.PendingForecasts, Is.EqualTo(1));
            Assert.That(emitter.LiveRings, Is.EqualTo(0), "a forecast is not a live ring");
            Assert.IsTrue(emitter.TryGetForecast(0, out RingSnapshot pending));
            Assert.That(pending.Id, Is.EqualTo(f.Id));
        }

        [Test]
        public void PhaseTwo_AtQueueTime_MakesTheRingBurn()
        {
            var emitter = NewBoundEmitter(out HypatiaContext context);
            var forecasts = new List<RingSnapshot>();
            emitter.GapForecast += forecasts.Add;

            emitter.QueueRing();
            context.EnterPhaseTwo();
            emitter.QueueRing();

            Assert.IsFalse(forecasts[0].Burning, "decided before phase 2");
            Assert.IsTrue(forecasts[1].Burning);
            Assert.IsTrue(forecasts[1].Tall);
            Assert.That(forecasts[1].Damage, Is.EqualTo(18f));
        }

        [Test]
        public void LiveCap_SkipsTheExtraRing()
        {
            var emitter = NewBoundEmitter(out _);
            Assert.IsTrue(emitter.QueueRing());
            Assert.IsTrue(emitter.QueueRing());
            Assert.IsTrue(emitter.QueueRing());
            Assert.IsFalse(emitter.QueueRing());
        }

        [Test]
        public void StopAll_RetiresEveryForecast_AndIsIdempotent()
        {
            var emitter = NewBoundEmitter(out _);
            var retired = new List<int>();
            emitter.RingRetired += retired.Add;
            emitter.QueueRing();
            emitter.QueueRing();

            emitter.StopAll();
            emitter.StopAll();

            Assert.That(retired, Is.EquivalentTo(new[] { 0, 1 }));
            Assert.That(emitter.PendingForecasts, Is.EqualTo(0));
            Assert.That(emitter.LiveRings, Is.EqualTo(0));
        }

        [Test]
        public void Rebind_KeepsTheRunningAttack()
        {
            var emitter = NewBoundEmitter(out HypatiaContext context);
            emitter.QueueRing();
            emitter.Bind(context); // the brain's OnEnable after a reload
            Assert.That(emitter.PendingForecasts, Is.EqualTo(1));
        }
    }
}
