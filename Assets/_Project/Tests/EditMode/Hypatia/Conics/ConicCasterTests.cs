using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia;
using Pickleball.Sandbox.Boss.Hypatia.Conics;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Conics
{
    /// <summary>
    /// The caster's volleys, driven by <see cref="ConicCaster.Tick"/> and the
    /// projectiles' Advance/ResolveContacts seams (EditMode has no game loop). No
    /// prefabs are assigned, so this also proves the bare-object fallback works.
    /// Every config value a test depends on is set here, not inherited.
    /// </summary>
    public class ConicCasterTests
    {
        readonly List<GameObject> _spawned = new();
        HypatiaConfig _config;
        ConicCaster _caster;
        Transform _player;
        float _bossScale;
        int _finished;
        ConicKind _finishedKind;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<HypatiaConfig>();
            _config.hideFlags = HideFlags.DontSave;
            Set("arenaRadius", 13f);

            Transform boss = NewObject("Hypatia", new Vector3(0f, 2.6f, 0f)).transform;
            Transform centre = NewObject("ArenaCentre", Vector3.zero).transform;
            _player = NewObject("Player", new Vector3(6f, 0f, 0f)).transform;

            _caster = boss.gameObject.AddComponent<ConicCaster>();
            _bossScale = 1f;
            var context = new HypatiaContext(_config, boss, centre, _player, null, null, () => _bossScale);
            _caster.Bind(context);
            _caster.SetRandom(new System.Random(12345));
            _finished = 0;
            _caster.VolleyFinished += k => { _finished++; _finishedKind = k; };
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

        // ─── Parabola ───────────────────────────────────────────────────────

        void SetUpLobs(int count, float flight, float markerLead)
        {
            SetInt("lobCountPhaseOne", count);
            Set("lobFlightTime", flight);
            Set("lobMarkerLead", markerLead);
            Set("lobStagger", 0.4f);
            Set("lobGravity", 14f);
            Set("lobLeadFraction", 1f);
            Set("lobMaxLead", 6f);
            Set("lobBlastRadius", 1.6f);
        }

        [Test]
        public void Parabola_AimsAtAStillPlayer_AndFinishesWhenTheLastLobLands()
        {
            SetUpLobs(2, 1.4f, 1f);
            var aims = new List<Vector3>();
            var seconds = new List<float>();
            int landed = 0;
            _caster.LobAimed += (p, s) => { aims.Add(p); seconds.Add(s); };
            _caster.LobLanded += (_, _) => landed++;

            _caster.Fire(ConicKind.Parabola, 1f);
            Assert.IsTrue(_caster.IsBusy);
            Assert.That(aims.Count, Is.EqualTo(1), "first lob aimed on Fire");
            Assert.That(Vector3.Distance(aims[0], new Vector3(6f, 0f, 0f)), Is.LessThan(1e-4f));
            Assert.That(seconds[0], Is.EqualTo(1.4f).Within(1e-5f));
            Assert.That(_caster.LiveLobCount, Is.EqualTo(1));

            _caster.Tick(0.39f);
            Assert.That(aims.Count, Is.EqualTo(1));
            _caster.Tick(0.02f);
            Assert.That(aims.Count, Is.EqualTo(2), "second lob after the stagger");
            Assert.That(_caster.LiveLobCount, Is.EqualTo(2));

            ConicLob first = _caster.LiveLobAt(0);
            first.Advance(2f);
            first.ResolveContacts();
            Assert.IsTrue(first.HitPlayer, "the player stood still under the marker");
            Assert.That(landed, Is.EqualTo(1));
            Assert.That(_finished, Is.EqualTo(0), "one lob still in the air");

            ConicLob second = _caster.LiveLobAt(0);
            second.Advance(2f);
            second.ResolveContacts();
            Assert.That(_finished, Is.EqualTo(1));
            Assert.That(_finishedKind, Is.EqualTo(ConicKind.Parabola));
            Assert.IsFalse(_caster.IsBusy);
        }

        [Test]
        public void Parabola_LobFollowsItsArcAndLandsOnTheMarker()
        {
            SetUpLobs(1, 1.4f, 1f);
            Vector3 aimed = Vector3.zero;
            _caster.LobAimed += (p, _) => aimed = p;
            _caster.Fire(ConicKind.Parabola, 1f);

            ConicLob lob = _caster.LiveLobAt(0);
            lob.Advance(0.7f);
            Assert.That(lob.transform.position.y, Is.GreaterThan(0.5f), "mid-flight, above the plate");
            lob.Advance(0.7f);
            Assert.That(Vector3.Distance(lob.transform.position, aimed), Is.LessThan(1e-4f));
        }

        [Test]
        public void Parabola_PlayerWhoLeavesTheMarker_IsNotHit()
        {
            SetUpLobs(1, 1.4f, 1f);
            _caster.Fire(ConicKind.Parabola, 1f);
            ConicLob lob = _caster.LiveLobAt(0);
            _player.position = new Vector3(12f, 0f, 0f);
            lob.Advance(2f);
            lob.ResolveContacts();
            Assert.IsFalse(lob.HitPlayer);
        }

        [Test]
        public void Parabola_ShortFlight_DelaysLaunchSoTheMarkerLeads()
        {
            SetUpLobs(1, 0.5f, 1.2f);
            float seconds = 0f;
            _caster.LobAimed += (_, s) => seconds = s;

            _caster.Fire(ConicKind.Parabola, 1f);
            Assert.That(seconds, Is.EqualTo(1.2f).Within(1e-5f));
            Assert.That(_caster.LiveLobCount, Is.EqualTo(0), "not thrown yet");
            _caster.Tick(0.69f);
            Assert.That(_caster.LiveLobCount, Is.EqualTo(0));
            _caster.Tick(0.02f);
            Assert.That(_caster.LiveLobCount, Is.EqualTo(1), "thrown flight-time before impact");
        }

        [Test]
        public void Parabola_StaggerRunsOnHerClock()
        {
            SetUpLobs(2, 1.4f, 1f);
            int aims = 0;
            _caster.LobAimed += (_, _) => aims++;
            _caster.Fire(ConicKind.Parabola, 1f);
            _bossScale = 0f;
            _caster.Tick(5f);
            Assert.That(aims, Is.EqualTo(1), "frozen: the second lob waits");
            _bossScale = 1f;
            _caster.Tick(0.41f);
            Assert.That(aims, Is.EqualTo(2));
        }

        [Test]
        public void StopAll_CancelsPendingWork_AndNeverFinishesTheVolley()
        {
            SetUpLobs(3, 1.4f, 1f);
            int stopped = 0;
            _caster.Stopped += () => stopped++;
            _caster.Fire(ConicKind.Parabola, 1f);
            _caster.StopAll();

            Assert.IsFalse(_caster.IsBusy);
            Assert.That(_caster.LiveLobCount, Is.EqualTo(0));
            Assert.That(stopped, Is.EqualTo(1));
            _caster.Tick(10f);
            Assert.That(_finished, Is.EqualTo(0));

            _caster.StopAll(); // idempotent
            Assert.That(_finished, Is.EqualTo(0));
        }

        // ─── Ellipse ────────────────────────────────────────────────────────

        void SetUpOrbiters(int count, int maxLive)
        {
            SetInt("orbiterCountMin", count);
            SetInt("orbiterCountMax", count);
            SetInt("maxLiveOrbiters", maxLive);
            Set("orbiterSpawnStagger", 0.25f);
            Set("orbiterLifetime", 12f);
            Set("orbitPeriapsisMin", 2.5f);
            Set("orbitApoapsisMax", 11.5f);
            Set("ellipseEccentricityMin", 0.35f);
            Set("ellipseEccentricityMax", 0.75f);
            Set("orbitPeriodMin", 6f);
            Set("orbitPeriodMax", 9f);
            Set("orbitHeight", 1f);
        }

        [Test]
        public void Ellipse_SpawnsOnePerStagger_WithinTheBounds_AndFinishesWhenAllAreOut()
        {
            SetUpOrbiters(3, 5);
            var spawned = new List<ConicOrbiter>();
            _caster.OrbiterSpawned += spawned.Add;

            _caster.Fire(ConicKind.Ellipse, 0.55f);
            Assert.That(spawned.Count, Is.EqualTo(1));
            _caster.Tick(0.25f);
            Assert.That(spawned.Count, Is.EqualTo(2));
            Assert.That(_finished, Is.EqualTo(0));
            _caster.Tick(0.25f);
            Assert.That(spawned.Count, Is.EqualTo(3));
            Assert.That(_finished, Is.EqualTo(1));
            Assert.That(_finishedKind, Is.EqualTo(ConicKind.Ellipse));
            Assert.That(_caster.Registry.Orbiters.Count, Is.EqualTo(3));

            foreach (ConicOrbiter o in spawned)
            {
                Assert.That(o.Orbit.Periapsis, Is.GreaterThanOrEqualTo(2.5f - 1e-3f));
                Assert.That(o.Orbit.Apoapsis, Is.LessThanOrEqualTo(11.5f + 1e-3f));
                Assert.That(o.Orbit.Eccentricity, Is.InRange(0.35f - 1e-4f, 0.75f + 1e-4f));
                Assert.That(o.Orbit.Period, Is.InRange(6f, 9f));
                Assert.That(o.Plane.Focus, Is.EqualTo(new Vector3(0f, 1f, 0f)), "her, projected to the orbit plane");
                Assert.IsTrue(o.IsAlive);
            }
        }

        [Test]
        public void Ellipse_LiveCap_RetiresTheOldestFirst()
        {
            SetUpOrbiters(3, 4);
            Set("orbiterSpawnStagger", 0f);
            var spawned = new List<ConicOrbiter>();
            var retired = new List<ConicOrbiter>();
            _caster.OrbiterSpawned += spawned.Add;
            _caster.OrbiterRetired += retired.Add;

            _caster.Fire(ConicKind.Ellipse, 0.5f);
            _caster.Fire(ConicKind.Ellipse, 0.5f);

            Assert.That(spawned.Count, Is.EqualTo(6));
            Assert.That(_caster.Registry.Count, Is.EqualTo(4));
            Assert.That(retired.Count, Is.EqualTo(2));
            Assert.That(retired[0], Is.SameAs(spawned[0]));
            Assert.That(retired[1], Is.SameAs(spawned[1]));
            Assert.That(_caster.Registry.Orbiters[0], Is.SameAs(spawned[2]), "oldest first");
        }

        [Test]
        public void Orbiter_ExpiresOnItsOwnClock_AndIsUnregistered()
        {
            SetUpOrbiters(1, 5);
            Set("orbiterLifetime", 2f);
            ConicOrbiter orbiter = null;
            int retired = 0;
            _caster.OrbiterSpawned += o => orbiter = o;
            _caster.OrbiterRetired += _ => retired++;
            _caster.Fire(ConicKind.Ellipse, 0.5f);

            _bossScale = 0f; // freezing her does not stop what she threw
            orbiter.Advance(1f);
            orbiter.ResolveContacts();
            Assert.That(retired, Is.EqualTo(0));
            orbiter.Advance(1.1f);
            orbiter.ResolveContacts();
            Assert.That(retired, Is.EqualTo(1));
            Assert.That(_caster.Registry.Count, Is.EqualTo(0));
        }

        [Test]
        public void Orbiter_FrozenBySlow_ReportsZeroVelocity()
        {
            SetUpOrbiters(1, 5);
            ConicOrbiter orbiter = null;
            _caster.OrbiterSpawned += o => orbiter = o;
            _caster.Fire(ConicKind.Ellipse, 0.5f);

            Assert.That(orbiter.Velocity.magnitude, Is.GreaterThan(0.1f));
            orbiter.ApplySlow(this, 0f, 1000f);
            Assert.That(orbiter.Velocity, Is.EqualTo(Vector3.zero));
        }

        // ─── Hyperbola ──────────────────────────────────────────────────────

        void SetUpHyperbola()
        {
            Set("asymptoteLead", 0.8f);
            Set("hyperbolaPeriapsis", 2.5f);
            Set("hyperbolaReach", 16f);
            Set("hyperbolaHeight", 1f);
            Set("hyperbolaShotSpeed", 16f);
            Set("hyperbolaShotRadius", 0.5f);
            Set("hyperbolaEccentricityMin", 1.3f);
            SetBool("hyperbolaExitAtPlayer", true);
            _player.position = new Vector3(9f, 0f, 3f);
        }

        [Test]
        public void Hyperbola_TelegraphsAimedAtThePlayer_ThenReleasesOnHerClock_ThenFinishes()
        {
            SetUpHyperbola();
            var telegraphs = new List<HyperbolaTelegraph>();
            HyperbolicShot released = null;
            int shotFinished = 0;
            _caster.AsymptotesTelegraphed += telegraphs.Add;
            _caster.ShotReleased += s => released = s;
            _caster.ShotFinished += _ => shotFinished++;

            _caster.Fire(ConicKind.Hyperbola, 1.5f);
            Assert.That(telegraphs.Count, Is.EqualTo(1));
            HyperbolaTelegraph t = telegraphs[0];
            Assert.That(t.LeadSeconds, Is.EqualTo(0.8f).Within(1e-5f));
            Assert.That(t.Eccentricity, Is.EqualTo(1.5f).Within(1e-5f));

            // The PATH's outgoing leg passes through the player (7377d1b), not the
            // asymptote: aiming the asymptote let the curve bend past a player
            // who stood still.
            var player = new Vector2(9f, 3f);
            float best = float.MaxValue;
            for (int i = t.Path.Count / 2; i < t.Path.Count - 1; i++)
            {
                Vector2 a = new(t.Path[i].x, t.Path[i].z), b = new(t.Path[i + 1].x, t.Path[i + 1].z);
                Vector2 ab = b - a;
                float u = Mathf.Clamp01(Vector2.Dot(player - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude));
                best = Mathf.Min(best, (a + ab * u - player).magnitude);
            }
            Assert.That(best, Is.LessThan(0.15f), "outgoing path runs through the player");

            _caster.Tick(0.79f);
            Assert.IsNull(released);
            _caster.Tick(0.02f);
            Assert.IsNotNull(released);
            Assert.That(_caster.LiveShot, Is.SameAs(released));
            Assert.That(_finished, Is.EqualTo(0));

            released.Advance(0.5f);
            Assert.That(released.Travelled, Is.EqualTo(8f).Within(1e-4f), "16 m/s on its own clock");
            released.Advance(100f);
            released.ResolveContacts();
            Assert.That(shotFinished, Is.EqualTo(1));
            Assert.That(_finished, Is.EqualTo(1));
            Assert.That(_finishedKind, Is.EqualTo(ConicKind.Hyperbola));
            Assert.IsNull(_caster.LiveShot);
        }

        [Test]
        public void Hyperbola_StopAllDuringTheTelegraph_NeverReleases()
        {
            SetUpHyperbola();
            int released = 0;
            _caster.ShotReleased += _ => released++;
            _caster.Fire(ConicKind.Hyperbola, 1.5f);
            _caster.StopAll();
            _caster.Tick(5f);
            Assert.That(released, Is.EqualTo(0));
            Assert.That(_finished, Is.EqualTo(0));
        }

        [Test]
        public void Fire_WhileBusy_IsIgnored()
        {
            SetUpHyperbola();
            int telegraphs = 0;
            _caster.AsymptotesTelegraphed += _ => telegraphs++;
            _caster.Fire(ConicKind.Hyperbola, 1.5f);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("while a Hyperbola volley"));
            _caster.Fire(ConicKind.Hyperbola, 1.5f);
            Assert.That(telegraphs, Is.EqualTo(1));
        }

        // ─── Helpers ────────────────────────────────────────────────────────

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
