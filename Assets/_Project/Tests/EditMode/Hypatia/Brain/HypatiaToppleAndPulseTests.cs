using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Boss.Hypatia;
using Pickleball.Sandbox.Boss.Hypatia.Conics;
using Pickleball.Sandbox.Boss.Hypatia.Pulse;
using Pickleball.Sandbox.Magic;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Brain
{
    /// <summary>
    /// The brain with the real caster and pulse: batting an orbiter into her
    /// topples her, the topple freezes her timers and shatters the other
    /// orbiters, it cannot be re-triggered, it ends after Topple Duration on her
    /// clock (and not while she is frozen), and the pulse waits out both the
    /// recalibration window and a topple.
    ///
    /// EditMode has no game loop, so the brain's OnEnable and Start are invoked by
    /// reflection (as NewtonSpellExemptionTests does) and it is driven through its
    /// public <see cref="HypatiaBoss.Tick"/> seam; the orbiters through
    /// Advance/ResolveContacts. Every config value the tests depend on is set here.
    /// </summary>
    public class HypatiaToppleAndPulseTests
    {
        const float Frame = 1f / 60f;

        readonly List<GameObject> _spawned = new();
        HypatiaConfig _config;
        HypatiaBoss _boss;
        Health _health, _playerHealth;
        ConicCaster _caster;
        KnockbackPulse _pulse;
        StatusReceiver _status;
        Transform _player;
        int _toppled, _recovered, _pulsesFired, _pulsesCancelled, _telegraphs;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<HypatiaConfig>();
            _config.hideFlags = HideFlags.DontSave;
            Set("maxHealth", 700f);
            Set("phaseTwoAt", 0f);                       // never phase 2 here
            Set("unpromptedBarkInterval", 1000f);
            Set("hoverHeight", 2.6f);
            Set("hoverSettleRate", 4f);
            Set("recalibrationDuration", 2.5f);
            Set("recalibrationDamageMultiplier", 2f);
            Set("normalDamageMultiplier", 1f);
            Set("toppleDuration", 7f);
            Set("toppleHoverHeight", 0f);
            Set("toppleDamageMultiplier", 2f);
            Set("battedOrbiterDamage", 20f);
            Set("battedSpeed", 16f);
            Set("battedTurnRate", 540f);
            Set("battingConeDegrees", 60f);
            Set("battedHomingTimeout", 2.5f);
            Set("battedMissLifetime", 1.2f);
            SetBool("spellsBatOrbiters", false);
            Set("firstConicDelay", 50f);                 // the brain's own conics stay out of the way
            Set("conicIntervalPhaseOne", 6f);
            Set("needleLead", 1.2f);
            Set("parabolaWeight", 0f);
            Set("ellipseWeight", 1f);
            Set("hyperbolaWeight", 0f);
            Set("firstPulseDelay", 6f);
            Set("pulseInterval", 10f);
            Set("pulseChargeTime", 0.8f);
            Set("pulseReach", 10f);
            Set("pulseDamage", 5f);
            Set("pulseTargetRadius", 10.5f);
            Set("pulseAssumedGroundDrag", 11f);
            Set("pulseExtraSpeed", 0f);
            SetInt("orbiterCountMin", 3);
            SetInt("orbiterCountMax", 3);
            SetInt("maxLiveOrbiters", 8);
            Set("ellipseEccentricityMin", 0f);
            Set("ellipseEccentricityMax", 0f);
            Set("orbitEccentricityJitter", 0f);
            Set("orbitPeriapsisMin", 2.5f);
            Set("orbitApoapsisMax", 11.5f);
            Set("orbitPeriodMin", 6f);
            Set("orbitPeriodMax", 9f);
            Set("orbiterLifetime", 60f);
            Set("orbiterSpawnStagger", 0.25f);
            Set("orbitHeight", 1f);
            Set("orbiterRadius", 0.45f);
            Set("orbiterDamage", 10f);

            Transform arena = NewObject("ArenaCentre", Vector3.zero).transform;
            GameObject bossGo = NewObject("Hypatia", Vector3.zero);
            var body = new GameObject("Body").transform;
            body.SetParent(bossGo.transform, false);
            body.localPosition = new Vector3(0f, 2.6f, 0f);
            var capsule = body.gameObject.AddComponent<CapsuleCollider>();
            capsule.height = 1.7f;
            capsule.radius = 0.34f;
            capsule.center = new Vector3(0f, 0.85f, 0f);

            _status = bossGo.AddComponent<StatusReceiver>();
            _boss = bossGo.AddComponent<HypatiaBoss>();
            _health = bossGo.GetComponent<Health>();
            bossGo.AddComponent<OrbiterRegistry>();
            _caster = bossGo.AddComponent<ConicCaster>();
            _pulse = bossGo.AddComponent<KnockbackPulse>();
            _caster.SetRandom(new System.Random(7));
            Tune(_health, 700f);

            _player = NewObject("Player", new Vector3(0f, 0f, 6f)).transform;
            _playerHealth = _player.gameObject.AddComponent<Health>();
            Tune(_playerHealth, 100f);

            var so = new SerializedObject(_boss);
            so.FindProperty("config").objectReferenceValue = _config;
            so.FindProperty("arenaCentre").objectReferenceValue = arena;
            so.FindProperty("player").objectReferenceValue = _player;
            so.FindProperty("playerHealth").objectReferenceValue = _playerHealth;
            so.FindProperty("conics").objectReferenceValue = _caster;
            so.FindProperty("pulse").objectReferenceValue = _pulse;
            so.FindProperty("body").objectReferenceValue = body;
            so.ApplyModifiedPropertiesWithoutUndo();

            Call(_boss, "OnEnable");
            Call(_boss, "Start");

            _toppled = _recovered = _pulsesFired = _pulsesCancelled = _telegraphs = 0;
            _boss.Toppled += _ => _toppled++;
            _boss.Recovered += () => _recovered++;
            _boss.ConicTelegraphed += (_, _, _) => _telegraphs++;
            _pulse.PulseFired += _ => _pulsesFired++;
            _pulse.PulseCancelled += () => _pulsesCancelled++;
        }

        [TearDown]
        public void TearDown()
        {
            if (_caster != null) _caster.StopAll();
            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null) Object.DestroyImmediate(_spawned[i]);
            _spawned.Clear();
            if (_config != null) Object.DestroyImmediate(_config);
        }

        // ─── Topple ─────────────────────────────────────────────────────────

        [Test]
        public void BattingAnOrbiterIntoHer_ToppleS_Damages_AndShattersTheRest()
        {
            List<ConicOrbiter> orbiters = SpawnVolley();
            Assert.That(orbiters.Count, Is.EqualTo(3));
            float before = _health.Current;

            BatIntoHer(orbiters[0]);

            Assert.That(_toppled, Is.EqualTo(1));
            Assert.IsTrue(_boss.IsToppled);
            Assert.That(_boss.ToppleRemaining, Is.EqualTo(7f).Within(1e-4f));
            Assert.That(before - _health.Current, Is.EqualTo(20f).Within(1e-4f), "priced at the multiplier at impact (1)");
            Assert.That(_caster.Registry.Count, Is.EqualTo(0), "the other orbiters shattered");
            Assert.That(_caster.BattedCount, Is.EqualTo(0));
            Assert.IsTrue(orbiters[1] == null && orbiters[2] == null, "shattered orbiters are despawned");
        }

        [Test]
        public void BattingAway_DoesNotTopple_AndTheBallExpires()
        {
            List<ConicOrbiter> orbiters = SpawnVolley();
            ConicOrbiter o = orbiters[0];
            Vector3 away = Flat(o.Position - _boss.Context.BossCentre).normalized * 5f;
            float before = _health.Current;
            o.TakeHit(10f, o.Position, away, false);
            Assert.IsTrue(o.IsBatted);
            Assert.IsFalse(o.IsBattedTowardHer);
            Assert.That(_caster.Registry.Count, Is.EqualTo(2), "left the registry at once");

            Fly(o, 2f);
            Assert.IsTrue(o == null, "expired after Batted Miss Lifetime");
            Assert.That(_toppled, Is.EqualTo(0));
            Assert.That(_health.Current, Is.EqualTo(before));
            Assert.That(_caster.Registry.Count, Is.EqualTo(2), "the others keep orbiting");
        }

        [Test]
        public void Spells_DoNotBat_ByDefault()
        {
            ConicOrbiter o = SpawnVolley()[0];
            o.TakeHit(10f, o.Position, Flat(_boss.Context.BossCentre - o.Position).normalized * 5f, false,
                      DamageKind.Spell);
            Assert.IsFalse(o.IsBatted);
            Assert.That(_caster.Registry.Count, Is.EqualTo(3));
        }

        [Test]
        public void ABattedBall_NeverHurtsThePlayer()
        {
            ConicOrbiter o = SpawnVolley()[0];
            // Park the player right on the ball's way out.
            Vector3 away = Flat(o.Position - _boss.Context.BossCentre).normalized;
            _player.position = new Vector3(o.Position.x, 0f, o.Position.z) + away * 2f;
            o.TakeHit(10f, o.Position, away * 5f, false);
            Fly(o, 2f);
            Assert.That(_playerHealth.Current, Is.EqualTo(100f));
        }

        [Test]
        public void Topple_FreezesEveryAttackTimer_UntilSheRecovers()
        {
            List<ConicOrbiter> orbiters = SpawnVolley();
            Step(1f);
            float conic = Timer("_conicTimer"), pulse = Timer("_pulseTimer");
            Assert.That(pulse, Is.EqualTo(5f).Within(0.02f));

            BatIntoHer(orbiters[0]);
            Step(3f);
            Assert.That(Timer("_conicTimer"), Is.EqualTo(conic).Within(1e-4f), "conic interval frozen");
            Assert.That(Timer("_pulseTimer"), Is.EqualTo(pulse).Within(1e-4f), "pulse interval frozen");
            Assert.IsFalse(_pulse.IsCharging);

            Step(4.1f);
            Assert.IsFalse(_boss.IsToppled);
            Assert.That(Timer("_conicTimer"), Is.LessThan(conic), "running again after recovery");
        }

        [Test]
        public void Topple_CancelsAConicWaitingOnItsNeedle()
        {
            List<ConicOrbiter> orbiters = SpawnVolley();
            Set("firstConicDelay", 0.5f);
            Call(_boss, "Start");               // restart the brain's timers with the new delay
            Step(0.6f);
            Assert.That(_telegraphs, Is.EqualTo(1), "needle up");
            Assert.That(Timer("_needleTimer"), Is.GreaterThan(0f));

            BatIntoHer(orbiters[0]);
            Assert.That(Timer("_needleTimer"), Is.EqualTo(0f), "needle cancelled");
            Step(3f);
            Assert.IsFalse(_caster.IsBusy, "the announced conic never fired");
            Assert.That(_caster.Registry.Count, Is.EqualTo(0));
        }

        [Test]
        public void Topple_DoublesDamage_ClosesTheWindow_AndNeverStacksWithIt()
        {
            List<ConicOrbiter> orbiters = SpawnVolley();
            OpenWindow(2.5f);
            Assert.IsTrue(_boss.IsRecalibrating);
            Assert.That(Lost(() => _health.Damage(10f, Vector3.zero)), Is.EqualTo(20f).Within(1e-4f), "window ×2");

            float before = _health.Current;
            BatIntoHer(orbiters[0]);
            Assert.That(before - _health.Current, Is.EqualTo(40f).Within(1e-4f), "the ball lands in the window");
            Assert.IsTrue(_boss.IsToppled);
            Assert.IsFalse(_boss.IsRecalibrating, "the topple closed the window");
            Assert.That(Lost(() => _health.Damage(10f, Vector3.zero)), Is.EqualTo(20f).Within(1e-4f), "×2, not ×4");

            Step(7.1f);
            Assert.IsFalse(_boss.IsToppled);
            Assert.That(Lost(() => _health.Damage(10f, Vector3.zero)), Is.EqualTo(10f).Within(1e-4f), "back to ×1");
        }

        [Test]
        public void CannotRetopple_WhileToppled()
        {
            BatIntoHer(SpawnVolley()[0]);
            Step(2f);
            float remaining = _boss.ToppleRemaining;
            Assert.That(remaining, Is.EqualTo(5f).Within(0.02f));

            List<ConicOrbiter> second = SpawnVolley();
            Assert.That(second.Count, Is.EqualTo(3));
            float before = _health.Current;
            BatIntoHer(second[0]);

            Assert.That(_toppled, Is.EqualTo(1), "no second topple");
            Assert.That(_boss.ToppleRemaining, Is.EqualTo(remaining).Within(1e-4f), "not restarted or extended");
            Assert.That(before - _health.Current, Is.EqualTo(40f).Within(1e-4f), "still damages, at the topple's ×2");
            Assert.That(_caster.Registry.Count, Is.EqualTo(2), "no second shatter either");
        }

        [Test]
        public void Recovers_After7Seconds_OnHerClock()
        {
            BatIntoHer(SpawnVolley()[0]);
            Step(6.9f);
            Assert.IsTrue(_boss.IsToppled);
            Assert.That(_recovered, Is.EqualTo(0));
            Step(0.2f);
            Assert.IsFalse(_boss.IsToppled);
            Assert.That(_recovered, Is.EqualTo(1));
        }

        [Test]
        public void AZeroSlow_FreezesTheToppleCountdown()
        {
            BatIntoHer(SpawnVolley()[0]);
            _status.ApplySlow(this, 0f, 1000f);
            Step(10f);
            Assert.IsTrue(_boss.IsToppled);
            Assert.That(_boss.ToppleRemaining, Is.EqualTo(7f).Within(1e-4f));
        }

        [Test]
        public void HerDeath_EndsTheToppleCleanly()
        {
            BatIntoHer(SpawnVolley()[0]);
            _health.Damage(10000f, Vector3.zero);
            Assert.IsTrue(_boss.IsDefeated);
            Assert.IsFalse(_boss.IsToppled);
            Assert.That(_recovered, Is.EqualTo(1));
        }

        // ─── Pulse ──────────────────────────────────────────────────────────

        [Test]
        public void Pulse_FiresAfterTheFirstDelay_AndHurtsAPlayerInReach()
        {
            Step(5.9f);
            Assert.IsFalse(_pulse.IsCharging);
            Step(0.2f);
            Assert.IsTrue(_pulse.IsCharging);
            Step(0.8f);
            Assert.That(_pulsesFired, Is.EqualTo(1));
            Assert.That(_playerHealth.Current, Is.EqualTo(95f).Within(1e-4f), "player 6 m out takes 5");
        }

        [Test]
        public void Pulse_MissesAPlayerBeyondReach()
        {
            _player.position = new Vector3(12f, 0f, 0f);
            Step(7f);
            Assert.That(_pulsesFired, Is.EqualTo(1));
            Assert.That(_playerHealth.Current, Is.EqualTo(100f));
        }

        [Test]
        public void Pulse_WaitsOutTheRecalibrationWindow()
        {
            Step(5.9f);
            OpenWindow(2.5f);
            Step(2f);
            Assert.IsFalse(_pulse.IsCharging, "due during the window: waits");
            Assert.That(Timer("_pulseTimer"), Is.EqualTo(0.1f).Within(0.02f), "and does not count down");
            Step(0.7f);                             // the window closes at 2.5 s
            Assert.IsFalse(_boss.IsRecalibrating);
            Assert.IsTrue(_pulse.IsCharging, "then charges");
        }

        [Test]
        public void Pulse_DueDuringATopple_FiresAfterSheRecovers()
        {
            List<ConicOrbiter> orbiters = SpawnVolley();
            Step(5.9f);
            BatIntoHer(orbiters[0]);
            Step(6.9f);
            Assert.IsFalse(_pulse.IsCharging);
            Assert.That(_pulsesFired, Is.EqualTo(0));
            Step(0.3f);                             // recovered at 7 s
            Assert.IsFalse(_boss.IsToppled);
            Assert.IsTrue(_pulse.IsCharging);
            Step(0.85f);
            Assert.That(_pulsesFired, Is.EqualTo(1));
        }

        [Test]
        public void Pulse_ChargingWhenSheTopples_IsDropped_AndRedueAfterwards()
        {
            List<ConicOrbiter> orbiters = SpawnVolley();
            Step(6.2f);
            Assert.IsTrue(_pulse.IsCharging);
            BatIntoHer(orbiters[0]);
            Assert.IsFalse(_pulse.IsCharging);
            Assert.That(_pulsesCancelled, Is.EqualTo(1));
            Step(7.1f);
            Assert.IsTrue(_pulse.IsCharging, "due again as soon as she is up");
            Assert.That(_pulsesFired, Is.EqualTo(0));
        }

        [Test]
        public void Pulse_ChargeFreezesWithHer()
        {
            Step(6.1f);
            Assert.IsTrue(_pulse.IsCharging);
            _status.ApplySlow(this, 0f, 1000f);
            for (int i = 0; i < 120; i++) _pulse.Tick(Frame);
            Assert.IsTrue(_pulse.IsCharging);
            Assert.That(_pulsesFired, Is.EqualTo(0));
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        /// <summary>Fire an ellipse volley straight from the caster and let all of
        /// it spawn. Returns the live orbiters, oldest first.</summary>
        List<ConicOrbiter> SpawnVolley()
        {
            _caster.Fire(ConicKind.Ellipse, 0f);
            _caster.Tick(1f);
            Assert.IsFalse(_caster.IsBusy, "the volley should be fully out");
            var list = new List<ConicOrbiter>();
            for (int i = 0; i < _caster.Registry.Count; i++) list.Add(_caster.Registry.At(i));
            // Only this volley's (a second volley during a topple keeps the first's none).
            return list.GetRange(list.Count - 3, 3);
        }

        void BatIntoHer(ConicOrbiter o)
        {
            Vector3 toHer = Flat(_boss.Context.BossCentre - o.Position).normalized;
            o.TakeHit(10f, o.Position, toHer * 5f, false);
            Assert.IsTrue(o.IsBattedTowardHer, "a swing straight at her should count");
            Fly(o, 3f);
            Assert.IsTrue(o == null, "the ball should have reached her and been despawned");
        }

        static void Fly(ConicOrbiter o, float seconds)
        {
            for (float t = 0f; t < seconds && o != null; t += Frame)
            {
                o.Advance(Frame);
                o.ResolveContacts();
            }
        }

        void Step(float seconds)
        {
            int frames = Mathf.RoundToInt(seconds / Frame);
            for (int i = 0; i < frames; i++)
            {
                _boss.Tick(Frame);
                _pulse.Tick(Frame);
            }
        }

        float Timer(string field)
        {
            FieldInfo f = typeof(HypatiaBoss).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(f, Is.Not.Null, $"HypatiaBoss.{field} not found; it was probably renamed.");
            return ((BossTimer)f.GetValue(_boss)).Remaining;
        }

        void OpenWindow(float seconds)
        {
            FieldInfo f = typeof(HypatiaBoss).GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(f, Is.Not.Null, "HypatiaBoss._window not found; it was probably renamed.");
            Assert.IsTrue(((RecalibrationWindow)f.GetValue(_boss)).Open(seconds));
        }

        float Lost(System.Action hit)
        {
            float before = _health.Current;
            hit();
            return before - _health.Current;
        }

        static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        static void Call(MonoBehaviour target, string method)
        {
            MethodInfo m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(m, Is.Not.Null, $"{target.GetType().Name}.{method}() not found; it was probably renamed.");
            m.Invoke(target, null);
        }

        static void Tune(Health health, float max)
        {
            var so = new SerializedObject(health);
            so.FindProperty("maxHealth").floatValue = max;
            so.FindProperty("hitGraceSeconds").floatValue = 0f;
            so.FindProperty("regainShare").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();
            health.ResetToFull();
        }

        GameObject NewObject(string name, Vector3 position)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.position = position;
            _spawned.Add(go);
            return go;
        }

        SerializedProperty Property(SerializedObject so, string field)
        {
            SerializedProperty p = so.FindProperty(field);
            Assert.That(p, Is.Not.Null, $"HypatiaConfig has no field '{field}'; update the test.");
            return p;
        }

        void Set(string field, float value)
        {
            var so = new SerializedObject(_config);
            Property(so, field).floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        void SetInt(string field, int value)
        {
            var so = new SerializedObject(_config);
            Property(so, field).intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        void SetBool(string field, bool value)
        {
            var so = new SerializedObject(_config);
            Property(so, field).boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
