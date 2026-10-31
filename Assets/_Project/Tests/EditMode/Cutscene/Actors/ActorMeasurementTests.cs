using NUnit.Framework;
using Pickleball.Cutscene.Actors;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Actors
{
    /// <summary>The shot list's size definitions (H, HB, Feet, eye line) and the spin rules.</summary>
    public class ActorMeasurementTests
    {
        [Test]
        public void Height_IsTopMinusFeet()
        {
            Assert.AreEqual(1.6f, ActorMeasurement.Height(1.7f, 0.1f), 1e-5f);
            Assert.AreEqual(ActorMeasurement.MinSize, ActorMeasurement.Height(0f, 1f));
        }

        [Test]
        public void HeadBox_IsTwiceHeadToTop()
        {
            Assert.AreEqual(0.55f, ActorMeasurement.HeadBox(1.0f, 0.725f), 1e-5f);
            Assert.AreEqual(ActorMeasurement.MinSize, ActorMeasurement.HeadBox(1f, 1.2f));
        }

        [Test]
        public void FeetPoint_IsBetweenTheFeetOnTheGround()
        {
            Vector3 p = ActorMeasurement.FeetPoint(new Vector3(-0.1f, 0.05f, 0.02f), new Vector3(0.1f, 0.07f, 0f), 0f);
            Assert.AreEqual(0f, p.x, 1e-6f);
            Assert.AreEqual(0f, p.y);
            Assert.AreEqual(0.01f, p.z, 1e-6f);
        }

        [Test]
        public void EyeHeight_ScalesTheHeadBoneHeight()
        {
            Assert.AreEqual(1.1f, ActorMeasurement.EyeHeight(0f, 1f, 1.1f), 1e-5f);
            Assert.AreEqual(2.1f, ActorMeasurement.EyeHeight(1f, 2f, 1.1f), 1e-5f);
        }

        [Test]
        public void Spin_ApproachIsFrameRateIndependent()
        {
            float a = 0f, b = 0f;
            for (int i = 0; i < 15; i++) a = PropellerSpinMath.Approach(a, 1440f, 4f, 1f / 30f);
            for (int i = 0; i < 120; i++) b = PropellerSpinMath.Approach(b, 1440f, 4f, 1f / 240f);
            Assert.AreEqual(a, b, 0.05f);
            Assert.AreEqual(1440f, PropellerSpinMath.Approach(0f, 1440f, 0f, 0.1f));
        }

        [Test]
        public void Spin_WindDownEndsAtRest()
        {
            Assert.AreEqual(900f, PropellerSpinMath.WindDown(900f, 0f, 1.5f));
            Assert.AreEqual(225f, PropellerSpinMath.WindDown(900f, 0.75f, 1.5f), 1e-3f);
            Assert.AreEqual(0f, PropellerSpinMath.WindDown(900f, 1.5f, 1.5f));
            Assert.AreEqual(0f, PropellerSpinMath.WindDown(900f, 9f, 1.5f));
        }

        [Test]
        public void Spin_AdvanceWraps()
        {
            Assert.AreEqual(10f, PropellerSpinMath.Advance(350f, 200f, 0.1f), 1e-4f);
        }
    }
}
