using NUnit.Framework;
using Pickleball.Cutscene.Editor.Sets;
using Pickleball.Cutscene.Sets;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Sets
{
    /// <summary>
    /// The construction-line resolve's progress mapping: every stroke's drawn fraction only grows
    /// with progress, nothing shows at 0, everything is drawn by drawEnd, and the lines settle at
    /// the faint permanent brightness at 1. Also the colour flood's radius curve.
    /// </summary>
    public class ConstructionResolveTests
    {
        [Test]
        public void StrokeVisibilityIsMonotonicInProgress()
        {
            var windows = new[] { new Vector2(0f, 0.45f), new Vector2(0.2f, 0.8f), new Vector2(0.5f, 1f), new Vector2(0.3f, 0.3f) };
            foreach (Vector2 group in windows)
            for (float order = 0f; order <= 1f; order += 0.25f)
            {
                Vector2 w = ResolveSchedule.StrokeWindow(group, order, 0.35f);
                float last = -1f;
                for (int i = 0; i <= 200; i++)
                {
                    float v = ResolveSchedule.Visible(ResolveSchedule.DrawPhase(i / 200f, 0.6f), w);
                    Assert.GreaterOrEqual(v, last, $"Stroke {w} un-drew at progress {i / 200f}.");
                    Assert.That(v, Is.InRange(0f, 1f));
                    last = v;
                }
            }
        }

        [Test]
        public void StrokesStayInsideTheirGroupWindowAndFollowTheirOrder()
        {
            var group = new Vector2(0.2f, 0.8f);
            Vector2 first = ResolveSchedule.StrokeWindow(group, 0f, 0.35f);
            Vector2 last = ResolveSchedule.StrokeWindow(group, 1f, 0.35f);
            Assert.AreEqual(0.2f, first.x, 1e-6f);
            Assert.AreEqual(0.8f, last.y, 1e-6f);
            Assert.Less(first.x, last.x);
        }

        [Test]
        public void TheBakedShrineLinesDrawInOrderAndCompleteByDrawEnd()
        {
            var shrine = AssetDatabase.LoadAssetAtPath<GameObject>(SetLayout.PrefabPath(SetLayout.OuterShrineId));
            Assert.IsNotNull(shrine, "Run 2 Build Sets.");
            var lines = shrine.GetComponentInChildren<ConstructionLineResolve>(true);
            var config = AssetDatabase.LoadAssetAtPath<ResolveConfig>(SetLayout.ConfigDir + "/ResolveConfig.asset");
            Assert.IsNotNull(config);
            for (int s = 0; s < lines.StrokeCount; s++)
            {
                Assert.AreEqual(0f, lines.StrokeVisible(s, 0f), $"Stroke {s} visible at progress 0.");
                Assert.AreEqual(1f, lines.StrokeVisible(s, config.DrawEnd), 1e-5f, $"Stroke {s} unfinished at drawEnd.");
                Assert.AreEqual(1f, lines.StrokeVisible(s, 1f), 1e-5f, $"Stroke {s} unfinished at progress 1.");
                float last = 0f;
                for (int i = 0; i <= 100; i++)
                {
                    float v = lines.StrokeVisible(s, i / 100f);
                    Assert.GreaterOrEqual(v, last, $"Stroke {s} un-drew at {i / 100f}.");
                    last = v;
                }
            }
        }

        [Test]
        public void LinesHoldFullBrightnessThenFadeToFaint()
        {
            Assert.AreEqual(1f, ResolveSchedule.Alpha(0.3f, 0.6f, 0.22f));
            Assert.AreEqual(1f, ResolveSchedule.Alpha(0.6f, 0.6f, 0.22f));
            Assert.AreEqual(0.22f, ResolveSchedule.Alpha(1f, 0.6f, 0.22f), 1e-6f);
            float last = 1f;
            for (int i = 60; i <= 100; i++)
            {
                float a = ResolveSchedule.Alpha(i / 100f, 0.6f, 0.22f);
                Assert.LessOrEqual(a, last + 1e-6f);
                last = a;
            }
        }

        [Test]
        public void TheFloodStartsWhiteGrowsOutwardAndClearsTheDome()
        {
            Assert.Less(VoidFlood.Radius(0f, 80f, 1.5f, 3f), 0f, "At blend 0 even MC_Start is white.");
            Assert.AreEqual(1f, VoidFlood.Whiteness(0f, VoidFlood.Radius(0f, 80f, 1.5f, 3f), 3f, 1f), 1e-5f);
            float last = float.MinValue;
            for (int i = 0; i <= 100; i++)
            {
                float r = VoidFlood.Radius(i / 100f, 80f, 1.5f, 3f);
                Assert.GreaterOrEqual(r, last);
                last = r;
            }

            // The dome's highest flood distance (rise weight 1.5) must be revealed by blend 1.
            var config = AssetDatabase.LoadAssetAtPath<ResolveConfig>(SetLayout.ConfigDir + "/ResolveConfig.asset");
            Assert.IsNotNull(config);
            float maxDome = 0f;
            for (int deg = 0; deg <= 90; deg++)
            {
                float a = deg * Mathf.Deg2Rad;
                var p = SetLayout.ShrineCentre + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * SetLayout.DomeRadius;
                maxDome = Mathf.Max(maxDome, VoidFlood.Distance(p, SetLayout.ShrineCentre, SetLayout.DomeRise));
            }
            // Review round 2: the flood stops at 55 m so it reads over 2 s near MC; the dome beyond it
            // cross-fades with the sky (VoidFlood.SkyFade), fully gone at blend 1.
            float end = VoidFlood.Radius(1f, config.FloodMaxRadius, config.FloodExponent, config.FloodFront);
            float white = VoidFlood.Whiteness(maxDome, end, config.FloodFront, 1f) * (1f - VoidFlood.SkyFade(1f));
            Assert.AreEqual(0f, white, 1e-5f, $"The dome's top ({maxDome:0.0} m) is still white at blend 1 (radius {end}).");
            Assert.AreEqual(0f, VoidFlood.SkyFade(0f), 1e-6f);
            Assert.That(VoidFlood.SkyFade(0.25f), Is.GreaterThan(0.4f), "the sky turns with the flood, not after it");
            Assert.That(VoidFlood.SkyFade(0.5f), Is.GreaterThan(VoidFlood.SkyFade(0.25f)));
            Assert.AreEqual(1f, VoidFlood.SkyFade(1f), 1e-6f);
        }
    }
}
