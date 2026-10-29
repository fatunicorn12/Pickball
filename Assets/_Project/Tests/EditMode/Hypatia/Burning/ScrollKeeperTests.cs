using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia.Burning;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Burning
{
    /// <summary>
    /// The optional burning scrolls: spawn gating (disabled, nothing burned,
    /// the live cap, her clock), where they appear, the swept pickup, the cost
    /// being permanent, the per-fight count rising once per scroll, burning
    /// away, and the sweep-up when the fight ends.
    /// </summary>
    public class ScrollKeeperTests : BurnTestFixture
    {
        ScrollKeeper _keeper;

        [SetUp]
        public void AddKeeper()
        {
            _keeper = Burn.gameObject.AddComponent<ScrollKeeper>();
            var so = new UnityEditor.SerializedObject(_keeper);
            so.FindProperty("burn").objectReferenceValue = Burn; // Awake does not run in EditMode
            so.ApplyModifiedPropertiesWithoutUndo();
            _keeper.SeedRandom(12345);
            // Spawned scrolls are real GameObjects; keep them out of the open scene.
            _keeper.ScrollSpawned += s => s.gameObject.hideFlags = HideFlags.DontSave;
        }

        [TearDown]
        public void SweepScrolls()
        {
            if (_keeper != null) _keeper.DespawnAll();
        }

        BurningScroll SpawnOne()
        {
            int before = _keeper.LiveCount;
            _keeper.Tick(4f + 0.01f);
            Assert.That(_keeper.LiveCount, Is.EqualTo(before + 1), "a scroll should have spawned");
            return _keeper.GetLive(_keeper.LiveCount - 1);
        }

        // --------------------------------------------------- ScrollSpawnRule

        [Test]
        public void Gate_NeedsEnabledLiveAndABurnedBand()
        {
            Assert.That(ScrollSpawnRule.GateOpen(true, false, 1), Is.True);
            Assert.That(ScrollSpawnRule.GateOpen(false, false, 1), Is.False, "disabled");
            Assert.That(ScrollSpawnRule.GateOpen(true, true, 1), Is.False, "fight over");
            Assert.That(ScrollSpawnRule.GateOpen(true, false, 0), Is.False, "nothing burned");
        }

        [Test]
        public void Advance_SpawnsEachInterval_OneAtATime()
        {
            float t = ScrollSpawnRule.Advance(0f, 3f, 4f, true, 0, 2, out bool spawn);
            Assert.That(spawn, Is.False);
            t = ScrollSpawnRule.Advance(t, 1f, 4f, true, 0, 2, out spawn);
            Assert.That(spawn, Is.True);
            Assert.That(t, Is.EqualTo(0f));

            t = ScrollSpawnRule.Advance(0f, 100f, 4f, true, 0, 2, out spawn);
            Assert.That(spawn, Is.True, "a long frame spawns once");
            Assert.That(t, Is.LessThanOrEqualTo(4f), "and does not bank a burst");
        }

        [Test]
        public void Advance_ClosedGateHoldsTheTimerAtZero()
        {
            float t = ScrollSpawnRule.Advance(3f, 10f, 4f, false, 0, 2, out bool spawn);
            Assert.That(spawn, Is.False);
            Assert.That(t, Is.EqualTo(0f));
        }

        [Test]
        public void Advance_AtTheCap_WaitsReady()
        {
            float t = ScrollSpawnRule.Advance(0f, 50f, 4f, true, 2, 2, out bool spawn);
            Assert.That(spawn, Is.False);
            Assert.That(t, Is.EqualTo(4f));
            ScrollSpawnRule.Advance(t, 0f, 4f, true, 1, 2, out spawn);
            Assert.That(spawn, Is.True, "a freed slot fills at once");
        }

        // ------------------------------------------------------ the keeper

        [Test]
        public void Disabled_NeverSpawns()
        {
            SetConfigBool("scrollsEnabled", false);
            BurnNextBand();
            for (int i = 0; i < 10; i++) _keeper.Tick(10f);
            Assert.That(_keeper.LiveCount, Is.EqualTo(0));
        }

        [Test]
        public void NoBurnedBand_NeverSpawns()
        {
            Burn.IgniteNext(); // smouldering is not burned
            BossScale = 0f;
            Burn.Tick(1f);
            BossScale = 1f;
            for (int i = 0; i < 10; i++) _keeper.Tick(10f);
            Assert.That(_keeper.LiveCount, Is.EqualTo(0));
        }

        [Test]
        public void Spawns_EachIntervalOnHerClock_UpToTheCap()
        {
            BurnNextBand();

            BossScale = 0f;
            _keeper.Tick(100f);
            Assert.That(_keeper.LiveCount, Is.EqualTo(0), "frozen, she makes none");

            BossScale = 1f;
            _keeper.Tick(3.9f);
            Assert.That(_keeper.LiveCount, Is.EqualTo(0));
            _keeper.Tick(0.2f);
            Assert.That(_keeper.LiveCount, Is.EqualTo(1));
            _keeper.Tick(4f);
            Assert.That(_keeper.LiveCount, Is.EqualTo(2));
            for (int i = 0; i < 5; i++) _keeper.Tick(4f);
            Assert.That(_keeper.LiveCount, Is.EqualTo(2), "capped at maxLiveScrolls");
        }

        [Test]
        public void AScroll_AppearsInTheFire_AtScrollHeight_WithASlowTrigger()
        {
            BurnNextBand();
            var spawned = new List<BurningScroll>();
            _keeper.ScrollSpawned += spawned.Add;

            var scroll = SpawnOne();

            Assert.That(spawned, Is.EqualTo(new[] { scroll }));
            Vector3 p = scroll.transform.position;
            Assert.That(p.y, Is.EqualTo(0.6f).Within(1e-4f));
            Assert.That(Burn.IsBurnedAt(new Vector2(p.x, p.z).magnitude), Is.True);
            var trigger = scroll.GetComponent<SphereCollider>();
            Assert.That(trigger.isTrigger, Is.True, "the Freeze Zone finds it by this trigger");
            Assert.That(trigger.radius, Is.EqualTo(0.8f));
        }

        [Test]
        public void Collecting_CostsPermanentHealth_AndCountsOnce()
        {
            BurnNextBand();
            var totals = new List<int>();
            _keeper.ScrollCollected += totals.Add;
            var scroll = SpawnOne();

            Assert.That(_keeper.Collect(scroll), Is.True);
            Assert.That(_keeper.Collect(scroll), Is.False, "the same scroll counts once");

            Assert.That(PlayerHealth.Current, Is.EqualTo(94f).Within(1e-4f));
            Assert.That(PlayerHealth.Recoverable, Is.EqualTo(0f), "nothing to win back");
            Assert.That(PlayerHealth.LastHitRecoverable, Is.False);
            Assert.That(_keeper.CollectedThisFight, Is.EqualTo(1));
            Assert.That(totals, Is.EqualTo(new[] { 1 }));
            Assert.That(_keeper.LiveCount, Is.EqualTo(0));

            Assert.That(_keeper.Collect(SpawnOne()), Is.True);
            Assert.That(totals, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void WalkingIntoAScroll_CollectsIt()
        {
            BurnNextBand();
            var scroll = SpawnOne();
            Vector3 p = scroll.transform.position;

            Player.position = new Vector3(p.x, 0f, p.z);
            scroll.Tick(0f); // even a frozen scroll is collectable

            Assert.That(_keeper.CollectedThisFight, Is.EqualTo(1));
            Assert.That(_keeper.LiveCount, Is.EqualTo(0));
        }

        [Test]
        public void DashingThroughAScrollInOneFrame_CollectsIt()
        {
            BurnNextBand();
            var scroll = SpawnOne();
            Vector3 p = scroll.transform.position;
            Vector3 side = Vector3.Cross(Vector3.up, new Vector3(p.x, 0f, p.z).normalized);

            Player.position = new Vector3(p.x, 0f, p.z) - side * 1.6f;
            scroll.Tick(0f);
            Assert.That(_keeper.CollectedThisFight, Is.EqualTo(0), "out of reach to start");

            Player.position = new Vector3(p.x, 0f, p.z) + side * 1.6f;
            scroll.Tick(0f);
            Assert.That(_keeper.CollectedThisFight, Is.EqualTo(1), "swept, not sampled");
        }

        [Test]
        public void APlayerFarAway_DoesNotCollect_AndTheScrollStaysInTheFire()
        {
            BurnNextBand();
            var scroll = SpawnOne();
            for (int i = 0; i < 100; i++) scroll.Tick(0.1f); // 10 s of drift, inside its 14 s
            Assert.That(_keeper.CollectedThisFight, Is.EqualTo(0));
            Vector3 p = scroll.transform.position;
            Assert.That(new Vector2(p.x, p.z).magnitude, Is.InRange(10.67f - 1e-3f, 13f + 1e-3f));
        }

        [Test]
        public void AScroll_BurnsAway_Uncounted()
        {
            BurnNextBand();
            var despawned = new List<bool>();
            _keeper.ScrollDespawning += (s, collected) => despawned.Add(collected);
            var scroll = SpawnOne();

            scroll.Tick(14f + 0.01f);

            Assert.That(despawned, Is.EqualTo(new[] { false }));
            Assert.That(_keeper.LiveCount, Is.EqualTo(0));
            Assert.That(_keeper.CollectedThisFight, Is.EqualTo(0));
            Assert.That(PlayerHealth.Current, Is.EqualTo(100f));
        }

        [Test]
        public void WhenTheFightEnds_ScrollsAreSweptUp_AndNoneReturn()
        {
            BurnNextBand();
            var scroll = SpawnOne();

            Burn.StopAll();
            _keeper.Tick(0f);
            Assert.That(_keeper.LiveCount, Is.EqualTo(0));
            Assert.That(_keeper.Collect(scroll), Is.False);

            for (int i = 0; i < 10; i++) _keeper.Tick(10f);
            Assert.That(_keeper.LiveCount, Is.EqualTo(0));
        }
    }
}
