using System;
using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// <see cref="PlayTimeClock"/> with a fake real-time source, and the whole
    /// cumulative-play-time path: a pick's time keeps counting from the play
    /// time banked in the save, even though the real-time source restarts from
    /// zero in every new session.
    /// </summary>
    public class PlayTimeClockTests : ChoiceTestFixture
    {
        [Test]
        public void Now_IsTheCarriedTimePlusRealTimeSinceTheStart()
        {
            double realtime = 100;
            var clock = new PlayTimeClock(() => realtime, 10);
            Assert.That(clock.Now, Is.EqualTo(10));

            realtime = 103.5;
            Assert.That(clock.Now, Is.EqualTo(13.5));
        }

        [Test]
        public void Restart_BanksANewValue_AndCountsFromNow()
        {
            double realtime = 7;
            var clock = new PlayTimeClock(() => realtime);
            realtime = 20;
            clock.Restart(50);
            Assert.That(clock.Now, Is.EqualTo(50));
            realtime = 21;
            Assert.That(clock.Now, Is.EqualTo(51));
        }

        [Test]
        public void FreezeAndResume_LeaveTheTimeInBetweenOut()
        {
            double realtime = 0;
            var clock = new PlayTimeClock(() => realtime);
            realtime = 5;
            clock.Freeze();
            Assert.That(clock.IsFrozen, Is.True);

            realtime = 500;
            Assert.That(clock.Now, Is.EqualTo(5), "Time in the background does not count.");

            clock.Resume();
            Assert.That(clock.IsFrozen, Is.False);
            Assert.That(clock.Now, Is.EqualTo(5));
            realtime = 502;
            Assert.That(clock.Now, Is.EqualTo(7));

            clock.Resume();
            Assert.That(clock.Now, Is.EqualTo(7), "Resume on a running clock changes nothing.");
        }

        [TestCase(double.NaN)]
        [TestCase(-1.0)]
        [TestCase(double.PositiveInfinity)]
        public void ABadCarriedValue_CountsAsZero(double carried)
        {
            var clock = new PlayTimeClock(() => 0, carried);
            Assert.That(clock.Now, Is.Zero);
        }

        [Test]
        public void ARealTimeSourceSteppingBack_NeverTakesPlayTimeBelowTheCarriedValue()
        {
            double realtime = 10;
            var clock = new PlayTimeClock(() => realtime, 3);
            realtime = 4;
            Assert.That(clock.Now, Is.EqualTo(3));
        }

        [Test]
        public void ARealTimeSourceSteppingBack_KeepsWhatWasCounted_AndCountsOnFromTheNewValue()
        {
            // DLG-C-3: Unity zeroes unscaled time before OnDisable at the end of Play.
            double realtime = 50;
            var clock = new PlayTimeClock(() => realtime, 100);
            realtime = 53;
            Assert.That(clock.Now, Is.EqualTo(103));

            realtime = 0;
            Assert.That(clock.Now, Is.EqualTo(103), "The 3 s already counted are kept.");
            realtime = 2;
            Assert.That(clock.Now, Is.EqualTo(105), "Counting continues from the new source value.");

            realtime = 1;
            Assert.That(clock.Now, Is.EqualTo(105), "A second step back is survived the same way.");
            realtime = 1.5;
            Assert.That(clock.Now, Is.EqualTo(105.5));
        }

        [Test]
        public void Now_NeverDecreases_UnderAJitteringSource()
        {
            double[] readings = { 5, 7, 6, 9, 0, 0, 3, 2, 10, 4 };
            int i = 0;
            var clock = new PlayTimeClock(() => readings[i], 1);
            double last = clock.Now;
            for (i = 1; i < readings.Length; i++)
            {
                double now = clock.Now;
                Assert.That(now, Is.GreaterThanOrEqualTo(last), $"reading {i} ({readings[i]})");
                last = now;
            }
        }

        [Test]
        public void Restart_IsTheOneWayToSetTheClockLower()
        {
            double realtime = 0;
            var clock = new PlayTimeClock(() => realtime, 100);
            realtime = 10;
            Assert.That(clock.Now, Is.EqualTo(110));
            clock.Restart(0);
            Assert.That(clock.Now, Is.Zero);
            realtime = 12;
            Assert.That(clock.Now, Is.EqualTo(2));
        }

        [Test]
        public void ANullSource_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new PlayTimeClock(null));
        }

        [Test]
        public void PickTimes_AreCumulativeAcrossSessions()
        {
            // Session 1: real time starts at 1000 (the process has been up a while).
            double realtime1 = 1000;
            var clock1 = new PlayTimeClock(() => realtime1);
            var session1 = new ChoiceStore(() => clock1.Now);
            realtime1 += 5;
            session1.RecordChoice("birdy", "ask", "polite");
            realtime1 += 5;
            Assert.That(ChoiceStoreSave.Save(SavePath, session1, clock1.Now, out string error), Is.True, error);

            // Session 2: a new process, real time from 0 again.
            double realtime2 = 0;
            var clock2 = new PlayTimeClock(() => realtime2);
            var session2 = new ChoiceStore(() => clock2.Now);
            ChoiceLoadResult loaded = ChoiceStoreSave.Load(SavePath, session2);
            clock2.Restart(loaded.PlayTime);
            realtime2 += 3;
            session2.RecordChoice("birdy", "ask", "rude");
            realtime2 += 1;
            Assert.That(ChoiceStoreSave.Save(SavePath, session2, clock2.Now, out error), Is.True, error);

            // Session 3 reads both picks with their play times.
            var session3 = NewStore();
            ChoiceLoadResult again = ChoiceStoreSave.Load(SavePath, session3);

            Assert.That(loaded.PlayTime, Is.EqualTo(10).Within(1e-9));
            Assert.That(session3.Choices[0].Time, Is.EqualTo(5).Within(1e-9));
            Assert.That(session3.Choices[1].Time, Is.EqualTo(13).Within(1e-9));
            Assert.That(again.PlayTime, Is.EqualTo(14).Within(1e-9));
        }
    }
}
