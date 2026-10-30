using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia;

namespace Pickleball.Tests.Hypatia.Brain
{
    /// <summary>The topple's clock and edges, and her damage rule with it.</summary>
    public class ToppleStateTests
    {
        [Test]
        public void Begin_RaisesStarted_AndCountsDownOnHerClock()
        {
            var t = new ToppleState();
            float started = -1f;
            int ended = 0;
            t.Started += d => started = d;
            t.Ended += () => ended++;

            Assert.IsTrue(t.Begin(7f));
            Assert.That(started, Is.EqualTo(7f));
            Assert.IsTrue(t.IsToppled);

            for (int i = 0; i < 69; i++) t.Tick(0.1f);
            Assert.IsTrue(t.IsToppled, "6.9 s in");
            Assert.That(t.Remaining, Is.EqualTo(0.1f).Within(1e-3f));
            t.Tick(0.11f);
            Assert.IsFalse(t.IsToppled);
            Assert.That(ended, Is.EqualTo(1));
            Assert.That(t.Remaining, Is.EqualTo(0f));
        }

        [Test]
        public void CannotRetopple_WhileToppled()
        {
            var t = new ToppleState();
            int started = 0;
            t.Started += _ => started++;
            t.Begin(7f);
            t.Tick(3f);
            Assert.IsFalse(t.Begin(7f));
            Assert.That(started, Is.EqualTo(1));
            Assert.That(t.Remaining, Is.EqualTo(4f).Within(1e-4f), "not restarted or extended");
        }

        [Test]
        public void ZeroOrNaNSlow_FreezesTheCountdown()
        {
            var t = new ToppleState();
            t.Begin(7f);
            for (int i = 0; i < 1000; i++) t.Tick(0f);
            t.Tick(float.NaN);
            Assert.IsTrue(t.IsToppled);
            Assert.That(t.Remaining, Is.EqualTo(7f));
        }

        [Test]
        public void End_IsIdempotent_AndAZeroDurationStillRaisesBothEdges()
        {
            var t = new ToppleState();
            int started = 0, ended = 0;
            t.Started += _ => started++;
            t.Ended += () => ended++;
            t.End();
            Assert.That(ended, Is.EqualTo(0));
            Assert.IsTrue(t.Begin(0f));
            Assert.That(started, Is.EqualTo(1));
            Assert.That(ended, Is.EqualTo(1));
            Assert.IsFalse(t.IsToppled);
            t.End();
            Assert.That(ended, Is.EqualTo(1));
        }

        [Test]
        public void DamageRule_ToppleDoubles_AndNeverStacksWithRecalibration()
        {
            Assert.That(HypatiaBoss.ScaleIncomingDamage(10f, toppled: true, recalibrating: false, 2f, 2f, 1f),
                        Is.EqualTo(20f).Within(1e-5f));
            Assert.That(HypatiaBoss.ScaleIncomingDamage(10f, toppled: true, recalibrating: true, 2f, 2f, 1f),
                        Is.EqualTo(20f).Within(1e-5f), "replaces, not 40");
            Assert.That(HypatiaBoss.ScaleIncomingDamage(10f, toppled: true, recalibrating: true, 3f, 2f, 1f),
                        Is.EqualTo(30f).Within(1e-5f), "topple wins");
            Assert.That(HypatiaBoss.ScaleIncomingDamage(10f, toppled: false, recalibrating: true, 3f, 2f, 1f),
                        Is.EqualTo(20f).Within(1e-5f));
            Assert.That(HypatiaBoss.ScaleIncomingDamage(10f, toppled: false, recalibrating: false, 3f, 2f, 1f),
                        Is.EqualTo(10f).Within(1e-5f));
            Assert.That(HypatiaBoss.ScaleIncomingDamage(float.NaN, true, false, 2f, 2f, 1f), Is.EqualTo(0f));
            Assert.That(HypatiaBoss.ScaleIncomingDamage(10f, true, false, float.NaN, 2f, 1f), Is.EqualTo(0f));
        }
    }
}
