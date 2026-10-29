using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia.Burning;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Burning
{
    /// <summary>
    /// <see cref="ArenaBurn"/> driven by hand: the warning runs on her clock
    /// and the fire on real time, fire damage is recoverable and continuous,
    /// the hub never burns, the burning edge event fires once per change, and
    /// StopAll leaves burned bands visible but inert.
    /// </summary>
    public class ArenaBurnTests : BurnTestFixture
    {
        [Test]
        public void Bind_BuildsTheBandsFromTheConfig()
        {
            Assert.That(Burn.BandCount, Is.EqualTo(4));
            Assert.That(Burn.GetBandInnerRadius(3), Is.EqualTo(10.67f));
            Assert.That(Burn.GetBandOuterRadius(3), Is.EqualTo(13f));
            Assert.That(Burn.SafeRadius, Is.EqualTo(13f));
            Assert.That(Burn.CanIgnite, Is.True);
            Assert.That(Burn.BurnedBands, Is.EqualTo(0));
        }

        [Test]
        public void IgniteNext_SmouldersTheOutermostBand_AndSaysSo()
        {
            var raised = new List<(int band, float inner, float outer, float lead)>();
            Burn.BandSmouldering += (b, i, o, l) => raised.Add((b, i, o, l));

            Burn.IgniteNext();

            Assert.That(raised, Is.EqualTo(new[] { (3, 10.67f, 13f, Lead) }));
            Assert.That(Burn.GetBandState(3), Is.EqualTo(BandState.Smouldering));
            Assert.That(Burn.SafeRadius, Is.EqualTo(13f), "still floor while smouldering");
            Assert.That(Burn.ProjectedSafeRadius, Is.EqualTo(10.67f));
        }

        [Test]
        public void TheWarning_RunsOnHerClock()
        {
            var ignited = new List<int>();
            Burn.BandIgnited += (b, i, o) => ignited.Add(b);
            Burn.IgniteNext();

            BossScale = 0f;
            Burn.Tick(10f);
            Assert.That(Burn.GetBandState(3), Is.EqualTo(BandState.Smouldering), "frozen holds it");

            BossScale = 0.5f;
            Burn.Tick(3f); // 1.5 s of her 2 s
            Assert.That(Burn.GetBandState(3), Is.EqualTo(BandState.Smouldering));
            Assert.That(Burn.GetSmoulderRemaining(3), Is.EqualTo(0.5f).Within(1e-4f));

            Burn.Tick(1f);
            Assert.That(Burn.GetBandState(3), Is.EqualTo(BandState.Burned));
            Assert.That(ignited, Is.EqualTo(new[] { 3 }));
            Assert.That(Burn.SafeRadius, Is.EqualTo(10.67f));
        }

        [Test]
        public void AZeroLead_IgnitesAtOnce()
        {
            SetConfigFloat("burnWarningLead", 0f);
            var ignited = new List<int>();
            Burn.BandIgnited += (b, i, o) => ignited.Add(b);

            Burn.IgniteNext();

            Assert.That(ignited, Is.EqualTo(new[] { 3 }));
        }

        [Test]
        public void TheFire_IsRecoverableContinuousDamage_OnRealTime()
        {
            BurnNextBand();
            Player.position = new Vector3(0f, 0f, 12f);
            BossScale = 0f; // freezing her does not put the fire out

            Burn.Tick(0.5f);

            float expected = FireDps * 0.5f;
            Assert.That(PlayerHealth.Current, Is.EqualTo(100f - expected).Within(1e-4f));
            Assert.That(PlayerHealth.LastHitRecoverable, Is.True);
            Assert.That(PlayerHealth.Recoverable, Is.EqualTo(expected).Within(1e-4f), "all of it is grey");
            Assert.That(Burn.IsPlayerBurning, Is.True);
        }

        [Test]
        public void Jumping_OverTheFlames_IsSafe()
        {
            BurnNextBand();
            Player.position = new Vector3(0f, FireHeight + 0.5f, 12f);

            Burn.Tick(1f);

            Assert.That(PlayerHealth.Current, Is.EqualTo(100f));
            Assert.That(Burn.IsPlayerBurning, Is.False);
        }

        [Test]
        public void ASmoulderingBand_DoesNotHurt()
        {
            Burn.IgniteNext();
            Player.position = new Vector3(0f, 0f, 12f);
            Burn.Tick(1f);
            Assert.That(PlayerHealth.Current, Is.EqualTo(100f));
        }

        [Test]
        public void PlayerBurning_IsAnEdgeEvent()
        {
            var edges = new List<bool>();
            Burn.PlayerBurning += edges.Add;
            BurnNextBand();

            Player.position = new Vector3(12f, 0f, 0f);
            for (int i = 0; i < 5; i++) Burn.Tick(0.1f);
            Player.position = Vector3.zero;
            for (int i = 0; i < 5; i++) Burn.Tick(0.1f);

            Assert.That(edges, Is.EqualTo(new[] { true, false }));
        }

        [Test]
        public void TheHub_NeverBurns()
        {
            for (int i = 0; i < 10; i++)
            {
                Burn.IgniteNext();
                Burn.Tick(Lead + 0.1f);
            }

            Assert.That(Burn.BurnedBands, Is.EqualTo(3));
            Assert.That(Burn.CanIgnite, Is.False);
            Assert.That(Burn.GetBandState(0), Is.EqualTo(BandState.Unburned));
            Assert.That(Burn.SafeRadius, Is.EqualTo(6f));

            Player.position = new Vector3(5.9f, 0f, 0f);
            Burn.Tick(1f);
            Assert.That(PlayerHealth.Current, Is.EqualTo(100f));
        }

        [Test]
        public void StopAll_CancelsSmoulders_AndLeavesTheFireVisibleButInert()
        {
            BurnNextBand();          // band 3 burned
            Burn.IgniteNext();       // band 2 smouldering
            Player.position = new Vector3(0f, 0f, 12f);
            Burn.Tick(0.1f);
            Assert.That(Burn.IsPlayerBurning, Is.True);
            float healthAtStop = PlayerHealth.Current;

            int extinguished = 0;
            var edges = new List<bool>();
            var ignited = new List<int>();
            Burn.Extinguished += () => extinguished++;
            Burn.PlayerBurning += edges.Add;
            Burn.BandIgnited += (b, i, o) => ignited.Add(b);

            Burn.StopAll();
            Burn.StopAll(); // idempotent
            Burn.Tick(10f);
            Burn.IgniteNext();

            Assert.That(extinguished, Is.EqualTo(1));
            Assert.That(edges, Is.EqualTo(new[] { false }));
            Assert.That(Burn.GetBandState(3), Is.EqualTo(BandState.Burned), "still drawn burned");
            Assert.That(Burn.GetBandState(2), Is.EqualTo(BandState.Unburned), "the warning was called off");
            Assert.That(ignited, Is.Empty);
            Assert.That(Burn.CanIgnite, Is.False);
            Assert.That(PlayerHealth.Current, Is.EqualTo(healthAtStop), "inert");
            Assert.That(Burn.IsStopped, Is.True);
        }

        [Test]
        public void Rebinding_KeepsTheFloor()
        {
            // The brain re-binds in OnEnable, e.g. after a script reload.
            BurnNextBand();
            Burn.Bind(Context);
            Assert.That(Burn.GetBandState(3), Is.EqualTo(BandState.Burned));
            Assert.That(Burn.BurnedBands, Is.EqualTo(1));
        }

        [Test]
        public void Unbound_IsInert()
        {
            var loose = NewComponent<ArenaBurn>();
            Assert.That(loose.CanIgnite, Is.False);
            Assert.That(loose.BandCount, Is.EqualTo(0));
            Assert.DoesNotThrow(() =>
            {
                loose.IgniteNext();
                loose.Tick(1f);
                loose.StopAll();
            });
        }
    }
}
