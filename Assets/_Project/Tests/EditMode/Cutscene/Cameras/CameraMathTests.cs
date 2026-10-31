using NUnit.Framework;
using Pickleball.Cutscene.Cameras;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Cameras
{
    /// <summary>The shot list's lens, projection and TCU formulas.</summary>
    public class CameraMathTests
    {
        [Test]
        public void FocalLengthsMatchTheShotListsLensTable()
        {
            Assert.AreEqual(15.6f, CameraMath.FocalLengthMm(75f), 0.05f);
            Assert.AreEqual(33.0f, CameraMath.FocalLengthMm(40f), 0.05f);
            Assert.AreEqual(44.8f, CameraMath.FocalLengthMm(30f), 0.05f);
            Assert.AreEqual(50.0f, CameraMath.FocalLengthMm(27f), 0.1f);
        }

        [Test]
        public void TcuRigGivesFrac045()
        {
            // Birdy.Head + 4.15·HB at 30°: frac = 1 / (2 · 4.15 · tan 15°).
            Assert.AreEqual(0.45f, CameraMath.TcuFraction(0.55f, 4.15f * 0.55f, 30f), 0.005f);
            Assert.AreEqual(0.48f, CameraMath.TcuFraction(0.55f, 3.9f * 0.55f, 30f), 0.005f);
            // S2_SH09's 8·HB at 40°: ≈ 0.17.
            Assert.AreEqual(0.17f, CameraMath.TcuFraction(1f, 8f, 40f), 0.005f);
        }

        [Test]
        public void DepthForFractionInvertsTcuFraction()
        {
            float d = CameraMath.DepthForFraction(0.53f, 0.28f, 75f);
            Assert.AreEqual(0.28f, CameraMath.TcuFraction(0.53f, d, 75f), 1e-4f);
        }

        [Test]
        public void ViewportMapsCentreEdgesAndBehind()
        {
            var rot = Quaternion.identity;
            Vector3 c = CameraMath.Viewport(Vector3.zero, rot, 40f, 16f / 9f, new Vector3(0f, 0f, 5f));
            Assert.AreEqual(0.5f, c.x, 1e-5f);
            Assert.AreEqual(0.5f, c.y, 1e-5f);
            Assert.AreEqual(5f, c.z, 1e-5f);
            float top = 5f * Mathf.Tan(20f * Mathf.Deg2Rad);
            Assert.AreEqual(1f, CameraMath.Viewport(Vector3.zero, rot, 40f, 16f / 9f, new Vector3(0f, top, 5f)).y, 1e-4f);
            Assert.Less(CameraMath.Viewport(Vector3.zero, rot, 40f, 16f / 9f, new Vector3(0f, 0f, -1f)).z, 0f);
        }

        [Test]
        public void ComposePutsTheTargetAtTheScreenPosition()
        {
            var pos = new Vector3(1f, 2f, -3f);
            var target = new Vector3(4f, 1f, 6f);
            var rot = CameraMath.Compose(pos, target, new Vector2(0.62f, 0.4f), 40f, 16f / 9f);
            Vector3 v = CameraMath.Viewport(pos, rot, 40f, 16f / 9f, target);
            Assert.AreEqual(0.62f, v.x, 0.002f);
            Assert.AreEqual(0.4f, v.y, 0.002f);
        }

        [Test]
        public void EasesHitTheirEndsAndShapes()
        {
            foreach (EaseKind e in System.Enum.GetValues(typeof(EaseKind)))
            {
                Assert.AreEqual(0f, CameraMath.Ease(e, 0f), 1e-6f);
                Assert.AreEqual(1f, CameraMath.Ease(e, 1f), 1e-6f);
            }
            Assert.AreEqual(0.5f, CameraMath.Ease(EaseKind.InOut, 0.5f), 1e-6f);
            Assert.Less(CameraMath.Ease(EaseKind.In, 0.5f), 0.5f);
            Assert.Greater(CameraMath.Ease(EaseKind.Out, 0.5f), 0.5f);
            Assert.AreEqual(1f, CameraMath.Progress(5f, 2f, 2f, EaseKind.Linear));
        }

        [Test]
        public void TimeRefsResolveAgainstBeats()
        {
            var beats = new[] { 0f, 3.03f, 3.63f, 6.63f };
            Assert.AreEqual(0.5f, TimeRef.Start(0.5f).Resolve(beats, 8.13f), 1e-5f);
            Assert.AreEqual(5.13f, TimeRef.Beat(2, 1.5f).Resolve(beats, 8.13f), 1e-5f);
            Assert.AreEqual(8.13f, TimeRef.End().Resolve(beats, 8.13f), 1e-5f);
            Assert.AreEqual(8.13f, TimeRef.End(1000f).Resolve(beats, 8.13f), 1e-5f);
        }

        [Test]
        public void ShakeBurstRampsHoldsAndDecays()
        {
            var b = new ShakeBurst { attack = 0.1f, hold = 0.4f, decay = 0.6f, peak = 1.2f };
            Assert.AreEqual(0f, b.GainAt(1f, 0.9f));
            Assert.AreEqual(0.6f, b.GainAt(1f, 1.05f), 1e-4f);
            Assert.AreEqual(1.2f, b.GainAt(1f, 1.3f), 1e-4f);
            Assert.AreEqual(0.6f, b.GainAt(1f, 1.8f), 1e-4f);
            Assert.AreEqual(0f, b.GainAt(1f, 2.2f));
        }
    }
}
