using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Cutscene.Cameras;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Cameras
{
    /// <summary>The validator's checks (a)–(d) as pure functions.</summary>
    public class FramingChecksTests
    {
        [Test]
        public void SafeFrameIsTheCentralNinetyPercent()
        {
            Assert.IsTrue(FramingChecks.InSafeFrame(new Vector3(0.5f, 0.5f, 3f), 0.9f));
            Assert.IsTrue(FramingChecks.InSafeFrame(new Vector3(0.051f, 0.949f, 3f), 0.9f));
            Assert.IsFalse(FramingChecks.InSafeFrame(new Vector3(0.04f, 0.5f, 3f), 0.9f));
            Assert.IsFalse(FramingChecks.InSafeFrame(new Vector3(0.5f, 0.96f, 3f), 0.9f));
            Assert.IsFalse(FramingChecks.InSafeFrame(new Vector3(0.5f, 0.5f, -1f), 0.9f), "behind the lens");
            Assert.IsFalse(FramingChecks.InSafeFrame(new Vector3(0.06f, 0.5f, 3f), 0.9f, 0.02f), "noise margin");
        }

        [Test]
        public void TcuRuleFlagsOnlyVisibleCloseUpsOutsideTheirWindows()
        {
            Assert.IsTrue(FramingChecks.TcuViolation(0.30f, true, false, 0.30f), "the banned band starts at 0.30");
            Assert.IsFalse(FramingChecks.TcuViolation(0.29f, true, false, 0.30f));
            Assert.IsFalse(FramingChecks.TcuViolation(0.45f, true, true, 0.30f), "allowed TCU");
            Assert.IsFalse(FramingChecks.TcuViolation(1.5f, false, false, 0.30f), "an INSERT with his eyes out of frame");
        }

        [Test]
        public void LensTooCloseUsesTheNearClip()
        {
            var b = new Bounds(Vector3.zero, Vector3.one);
            Assert.IsTrue(FramingChecks.LensTooClose(b, Vector3.zero, 0.1f), "inside");
            Assert.IsTrue(FramingChecks.LensTooClose(b, new Vector3(0.55f, 0f, 0f), 0.1f));
            Assert.IsFalse(FramingChecks.LensTooClose(b, new Vector3(0.65f, 0f, 0f), 0.1f));
        }

        [Test]
        public void InvisibleCutToleranceIsOneCentimetreAndATenthOfADegree()
        {
            var a = new Pose(Vector3.zero, Quaternion.identity);
            var b = new Pose(new Vector3(0.009f, 0f, 0f), Quaternion.Euler(0.09f, 0f, 0f));
            Assert.IsTrue(FramingChecks.IsInvisibleCut(FramingChecks.PoseDelta(a, 60f, b, 60f)));
            var c = new Pose(new Vector3(0.011f, 0f, 0f), Quaternion.identity);
            Assert.IsFalse(FramingChecks.IsInvisibleCut(FramingChecks.PoseDelta(a, 60f, c, 60f)));
            Assert.IsFalse(FramingChecks.IsInvisibleCut(FramingChecks.PoseDelta(a, 60f, a, 61f)), "FOV must match");
        }

        [Test]
        public void DegreesOutsideFrameMeasuresTheBlackSunMargin()
        {
            // 40° vertical: the top edge is at 20°. A point 25° up is 5° outside.
            Vector3 p = new Vector3(0f, Mathf.Tan(25f * Mathf.Deg2Rad), 1f) * 100f;
            Assert.AreEqual(5f, FramingChecks.DegreesOutsideFrame(Vector3.zero, Quaternion.identity, 40f, 16f / 9f, p), 1e-3f);
            Assert.Less(FramingChecks.DegreesOutsideFrame(Vector3.zero, Quaternion.identity, 40f, 16f / 9f, Vector3.forward), 0f);
        }

        [Test]
        public void NoiseMarginGrowsWithTheWobble()
        {
            Assert.AreEqual(0f, FramingChecks.NoiseMargin(0f, 40f));
            Assert.Greater(FramingChecks.NoiseMargin(1f, 40f), FramingChecks.NoiseMargin(0.5f, 40f));
        }

        [Test]
        public void TheOldDollyStaircaseCountsAsStutter()
        {
            // As measured on S4_SH01: seven 1 cm steps, then a 20 cm jump, over and over.
            var steps = new List<float>();
            for (int i = 0; i < 40; i++) steps.Add(i % 8 == 0 ? 0.2f : 0.01f);
            Assert.AreEqual(5, FramingChecks.CountStutters(steps, 0.05f, 4f, 4));
        }

        [Test]
        public void SmoothMovesDoNotCountAsStutter()
        {
            var even = new List<float>();
            var eased = new List<float>();
            var fromRest = new List<float>();
            for (int i = 0; i < 90; i++)
            {
                even.Add(0.35f);
                eased.Add(0.3f * Mathf.Sin(i / 89f * Mathf.PI));
                fromRest.Add(i < 30 ? 0f : Mathf.Min(0.3f, (i - 30) * 0.01f));
            }
            Assert.AreEqual(0, FramingChecks.CountStutters(even, 0.05f, 4f, 4));
            Assert.AreEqual(0, FramingChecks.CountStutters(eased, 0.05f, 4f, 4));
            Assert.AreEqual(0, FramingChecks.CountStutters(fromRest, 0.05f, 4f, 4));
        }
    }
}
