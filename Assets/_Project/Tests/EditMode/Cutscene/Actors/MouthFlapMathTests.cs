using NUnit.Framework;
using Pickleball.Cutscene.Actors;

namespace Pickleball.Tests.Cutscene.Actors
{
    /// <summary>The voice-level → flap mapping: silence stays shut, smoothing is frame-rate independent.</summary>
    public class MouthFlapMathTests
    {
        [Test]
        public void Silence_GivesZeroEverywhere()
        {
            var buffer = new float[256];
            float rms = MouthFlapMath.Rms(buffer, buffer.Length);
            Assert.AreEqual(0f, rms);
            float open = MouthFlapMath.Openness(rms, 0.01f, 0.12f);
            Assert.AreEqual(0f, open);
            float shown = 0f;
            for (int i = 0; i < 60; i++) shown = MouthFlapMath.Smooth(shown, open, 30f, 12f, 1f / 60f);
            Assert.AreEqual(0f, shown);
            Assert.AreEqual(0f, MouthFlapMath.Angle(shown, 6f));
        }

        [Test]
        public void Rms_OfConstantSignal_IsItsMagnitude()
        {
            var buffer = new float[128];
            for (int i = 0; i < buffer.Length; i++) buffer[i] = i % 2 == 0 ? 0.5f : -0.5f;
            Assert.AreEqual(0.5f, MouthFlapMath.Rms(buffer, buffer.Length), 1e-6f);
            Assert.AreEqual(0f, MouthFlapMath.Rms(null, 10));
            Assert.AreEqual(0f, MouthFlapMath.Rms(buffer, 0));
        }

        [Test]
        public void Openness_IsLinearBetweenFloorAndFullOpen()
        {
            Assert.AreEqual(0f, MouthFlapMath.Openness(0.005f, 0.01f, 0.11f));
            Assert.AreEqual(0.5f, MouthFlapMath.Openness(0.06f, 0.01f, 0.11f), 1e-5f);
            Assert.AreEqual(1f, MouthFlapMath.Openness(0.5f, 0.01f, 0.11f));
        }

        [TestCase(30f, 240f)]
        [TestCase(20f, 120f)]
        public void Attack_IsFrameRateIndependent(float lowFps, float highFps)
        {
            float a = Run(0f, 1f, lowFps, 0.1f);
            float b = Run(0f, 1f, highFps, 0.1f);
            Assert.AreEqual(a, b, 1e-4f);
        }

        [TestCase(30f, 240f)]
        public void Release_IsFrameRateIndependent(float lowFps, float highFps)
        {
            float a = Run(1f, 0f, lowFps, 0.2f);
            float b = Run(1f, 0f, highFps, 0.2f);
            Assert.AreEqual(a, b, 1e-4f);
        }

        [Test]
        public void Attack_IsFasterThanRelease()
        {
            float opened = MouthFlapMath.Smooth(0f, 1f, 30f, 12f, 0.05f);
            float closed = 1f - MouthFlapMath.Smooth(1f, 0f, 30f, 12f, 0.05f);
            Assert.Greater(opened, closed);
        }

        private static float Run(float from, float target, float fps, float seconds)
        {
            float v = from;
            int frames = UnityEngine.Mathf.RoundToInt(seconds * fps);
            for (int i = 0; i < frames; i++) v = MouthFlapMath.Smooth(v, target, 30f, 12f, 1f / fps);
            return v;
        }
    }
}
