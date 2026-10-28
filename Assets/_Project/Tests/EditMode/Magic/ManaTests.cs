using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Combat;
using Pickleball.Sandbox.Magic;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Magic
{
    /// <summary>
    /// The <see cref="Mana"/> component around the pool: that it works with no
    /// lifecycle callback at all (EditMode never runs <c>Awake</c> or
    /// <c>OnEnable</c> for it, and neither does editor tooling), that its events
    /// fire exactly when the HUD and cues need them, and that standing still is
    /// read from an <see cref="ICombatMovement"/>. Time is driven through the
    /// public <see cref="Mana.Tick"/> seam, one fixed tick per call; the
    /// arithmetic itself is pinned by <see cref="ManaPoolTests"/>.
    /// </summary>
    public class ManaTests : BossTestFixture
    {
        const float OneTick = 1f / ManaPool.TicksPerSecond;

        ManaConfig _config;
        ManaConfig _fallback;   // a component's own default config, when a test makes one

        [SetUp]
        public void CreateConfig() => _config = ScriptableObject.CreateInstance<ManaConfig>();

        [TearDown]
        public void DestroyConfigs()
        {
            Object.DestroyImmediate(_config);
            // OnDestroy does not run for an EditMode object, so the component
            // cannot clean up its own fallback here.
            if (_fallback != null) Object.DestroyImmediate(_fallback);
            _fallback = null;
        }

        Mana NewMana()
        {
            Mana mana = NewComponent<Mana>();
            SetReference(mana, "config", _config);
            return mana;
        }

        /// <summary>What <c>Update</c> does, one fixed tick at a time, until full.</summary>
        static int TicksUntilFull(Mana mana)
        {
            int ticks = 0;
            while (!mana.IsFull && ticks < 100_000)
            {
                mana.Tick(OneTick, mana.IsStandingStill);
                ticks++;
            }
            return ticks;
        }

        static void SetReference(Component target, string fieldName, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            Assert.That(property, Is.Not.Null,
                        $"{target.GetType().Name} has no serialized field '{fieldName}' — " +
                        "it was probably renamed. Update the test's field name (and the " +
                        "setup script that writes it).");
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------ lazy init

        [Test]
        public void StartsFull_WithoutAnyLifecycleCallback()
        {
            Mana mana = NewMana();

            Assert.That(mana.Config, Is.SameAs(_config));
            Assert.That(mana.Current, Is.EqualTo(100));
            Assert.That(mana.Max, Is.EqualTo(100));
            Assert.That(mana.Normalised, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(mana.IsFull, Is.True);
            Assert.That(mana.IsRegenerating, Is.False);
            Assert.That(mana.RegenDelayRemaining, Is.Zero);
            Assert.That(mana.RegenDelayNormalised, Is.Zero);
        }

        [Test]
        public void MissingConfig_FallsBackToTheDefaults()
        {
            Mana mana = NewComponent<Mana>();

            _fallback = mana.Config;

            Assert.That(_fallback, Is.Not.Null);
            Assert.That(mana.Config, Is.SameAs(_fallback), "Made once, not on every read.");
            Assert.That(mana.Max, Is.EqualTo(100));
            Assert.That(mana.Current, Is.EqualTo(100));
        }

        [Test]
        public void AssigningAConfigLater_KeepsTheBar()
        {
            // Setup tooling may wire the asset after something already read the
            // bar; the pool rebinds instead of resetting.
            Mana mana = NewComponent<Mana>();
            _fallback = mana.Config;
            mana.TrySpend(30);

            SetReference(mana, "config", _config);

            Assert.That(mana.Config, Is.SameAs(_config));
            Assert.That(mana.Current, Is.EqualTo(70));
        }

        // ---------------------------------------------------------------- spend

        [Test]
        public void TrySpend_RaisesChangedThenSpent_AndStartsTheDelay()
        {
            Mana mana = NewMana();
            var events = new List<string>();
            mana.Changed += m => events.Add("Changed " + m.Current);
            mana.Spent += cost => events.Add("Spent " + cost);
            mana.SpendFailed += cost => events.Add("SpendFailed " + cost);

            Assert.That(mana.TrySpend(15), Is.True);

            Assert.That(events, Is.EqualTo(new[] { "Changed 85", "Spent 15" }));
            Assert.That(mana.RegenDelayNormalised, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(mana.RegenDelayRemaining, Is.EqualTo(0.9f).Within(1e-4f),
                        "15 of 100: 0.896 s, rounded up to 54 ticks.");
            Assert.That(mana.IsRegenerating, Is.False);
        }

        [Test]
        public void TrySpend_Unaffordable_RaisesOnlySpendFailed()
        {
            Mana mana = NewMana();
            mana.TrySpend(90);
            var events = new List<string>();
            mana.Changed += m => events.Add("Changed " + m.Current);
            mana.Spent += cost => events.Add("Spent " + cost);
            mana.SpendFailed += cost => events.Add("SpendFailed " + cost);

            Assert.That(mana.CanAfford(20), Is.False);
            Assert.That(mana.TrySpend(20), Is.False);

            Assert.That(events, Is.EqualTo(new[] { "SpendFailed 20" }));
            Assert.That(mana.Current, Is.EqualTo(10));
        }

        [Test]
        public void TrySpend_Free_RaisesNothing()
        {
            Mana mana = NewMana();
            int raised = 0;
            mana.Changed += _ => raised++;
            mana.Spent += _ => raised++;
            mana.SpendFailed += _ => raised++;

            Assert.That(mana.TrySpend(0), Is.True);

            Assert.That(raised, Is.Zero);
            Assert.That(mana.RegenDelayRemaining, Is.Zero);
        }

        // ---------------------------------------------------------------- delay

        [Test]
        public void RegenDelayNormalised_FallsFromOneToZero_ThenRegenStarts()
        {
            Mana mana = NewMana();
            mana.TrySpend(100);                                   // 180 ticks of delay

            for (int i = 0; i < 90; i++) mana.Tick(OneTick, false);
            Assert.That(mana.RegenDelayNormalised, Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(mana.IsRegenerating, Is.False);

            for (int i = 0; i < 90; i++) mana.Tick(OneTick, false);
            Assert.That(mana.RegenDelayNormalised, Is.Zero);
            Assert.That(mana.IsRegenerating, Is.True);
        }

        // --------------------------------------------------------------- events

        [Test]
        public void Changed_FiresOncePerPoint_AndFilledOncePerRefill_AfterTheLastChanged()
        {
            Mana mana = NewMana();
            mana.TrySpend(100);
            var events = new List<string>();
            int changed = 0, filled = 0;
            mana.Changed += m => { changed++; events.Add("Changed " + m.Current); };
            mana.Filled += () => { filled++; events.Add("Filled"); };

            // 20 s at one tick a frame; the refill takes 13.7 s, so the bar also
            // sits full for a while and must stay quiet.
            for (int i = 0; i < 1200; i++) mana.Tick(OneTick, false);

            Assert.That(changed, Is.EqualTo(100),
                        "At one tick a frame no tick gains two points, so every point " +
                        "is its own Changed.");
            Assert.That(filled, Is.EqualTo(1));
            Assert.That(events[events.Count - 2], Is.EqualTo("Changed 100"));
            Assert.That(events[events.Count - 1], Is.EqualTo("Filled"),
                        "The cue fires after the bar already reads full.");

            mana.TrySpend(40);
            for (int i = 0; i < 1200; i++) mana.Tick(OneTick, false);

            Assert.That(filled, Is.EqualTo(2), "Once per refill.");
        }

        [Test]
        public void Refill_RaisesChanged_ButNotFilled()
        {
            Mana mana = NewMana();
            mana.TrySpend(40);
            int changed = 0, filled = 0;
            mana.Changed += _ => changed++;
            mana.Filled += () => filled++;

            mana.Refill();

            Assert.That(mana.Current, Is.EqualTo(100));
            Assert.That(mana.RegenDelayNormalised, Is.Zero);
            Assert.That(changed, Is.EqualTo(1));
            Assert.That(filled, Is.Zero, "The chime belongs to regen, not to an instant refill.");
        }

        // ------------------------------------------------------- standing still

        [Test]
        public void IsStandingStill_ReadsTheVelocityOfAMovementSourceOnThisObject()
        {
            Mana mana = NewMana();
            var body = mana.gameObject.AddComponent<ManaStubMovement>();

            body.Velocity = Vector3.zero;
            Assert.That(mana.IsStandingStill, Is.True);

            body.Velocity = new Vector3(0.1f, 0f, 0.05f);         // settling jitter
            Assert.That(mana.IsStandingStill, Is.True);

            body.Velocity = new Vector3(0f, 0f, 2f);              // walking
            Assert.That(mana.IsStandingStill, Is.False);

            body.Velocity = new Vector3(0f, -3f, 0f);             // falling
            Assert.That(mana.IsStandingStill, Is.False, "Vertical speed counts too.");
        }

        [Test]
        public void SerializedMovementSource_WinsOverOneOnThisObject()
        {
            Mana mana = NewMana();
            mana.gameObject.AddComponent<ManaStubMovement>().Velocity = new Vector3(5f, 0f, 0f);
            var wired = NewComponent<ManaStubMovement>();
            wired.Velocity = Vector3.zero;

            SetReference(mana, "movementSource", wired);

            Assert.That(mana.IsStandingStill, Is.True);
        }

        [Test]
        public void NoMovementSource_CountsAsStandingStill()
        {
            Mana mana = NewMana();

            Assert.That(mana.IsStandingStill, Is.True,
                        "A body that cannot move has zero velocity, as the adapter " +
                        "reports without locomotion.");
        }

        [Test]
        public void UpdateLoop_RefillsTwiceAsFast_WhileTheBodyStandsStill()
        {
            Mana mana = NewMana();
            var body = mana.gameObject.AddComponent<ManaStubMovement>();

            body.Velocity = new Vector3(4f, 0f, 0f);
            mana.TrySpend(100);
            int movingTicks = TicksUntilFull(mana);

            body.Velocity = Vector3.zero;
            mana.TrySpend(100);
            int stillTicks = TicksUntilFull(mana);

            // The same exact counts ManaPoolTests derives: 13.733 s and 6.867 s.
            Assert.That(movingTicks, Is.EqualTo(824));
            Assert.That(stillTicks, Is.EqualTo(412));
        }
    }

    /// <summary>A body whose velocity the test sets directly — the slice of
    /// movement <see cref="Mana"/> reads.</summary>
    internal sealed class ManaStubMovement : MonoBehaviour, ICombatMovement
    {
        public Vector3 Velocity { get; set; }
        public bool IsGrounded => true;
        public bool IsBusy => false;
        public bool IsMovementLocked => false;
    }
}
