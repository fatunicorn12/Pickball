using NUnit.Framework;
using Pickleball.Cutscene.Editor.Sets;
using Pickleball.Cutscene.Sets;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Sets
{
    /// <summary>
    /// CutsceneLook's blend math: blend 0 is the Void profile exactly, blend 1 is GoldenHour
    /// exactly (which must equal the greybox's gameplay lighting), and the result depends only on
    /// the blend value, never on the path taken to it.
    /// </summary>
    public class LookBlendTests
    {
        private static readonly LookState Void = CutsceneSetBuilder.VoidLookState();
        private static readonly LookState Golden = CutsceneSetBuilder.GoldenHourState();

        [Test]
        public void BlendZeroIsTheVoidExactly()
        {
            AssertSame(Void, LookBlend.Evaluate(Void, Golden, 0f));
            AssertSame(Void, LookBlend.Evaluate(Void, Golden, -0.5f));
        }

        [Test]
        public void BlendOneIsGoldenHourExactly()
        {
            AssertSame(Golden, LookBlend.Evaluate(Void, Golden, 1f));
            AssertSame(Golden, LookBlend.Evaluate(Void, Golden, 1.5f));
        }

        [Test]
        public void TheResultDependsOnlyOnTheBlend()
        {
            // Two different frame histories that land on t = 0.37 give the same look.
            LookBlend.Evaluate(Void, Golden, 0.9f);
            LookState direct = LookBlend.Evaluate(Void, Golden, 0.37f);
            for (float t = 0f; t < 0.37f; t += 0.013f) LookBlend.Evaluate(Void, Golden, t);
            AssertSame(direct, LookBlend.Evaluate(Void, Golden, 0.37f));
        }

        [Test]
        public void MidBlendLiesBetweenTheProfiles()
        {
            LookState mid = LookBlend.Evaluate(Void, Golden, 0.5f);
            Assert.That(mid.SunIntensity, Is.EqualTo((Void.SunIntensity + Golden.SunIntensity) * 0.5f).Within(1e-5f));
            Assert.That(mid.SunShadowStrength, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.AreEqual(Void.FogMode, mid.FogMode, "Mixed modes keep the void's mode until t = 1.");
            Assert.That(mid.FogDensity, Is.LessThan(Void.FogDensity).And.GreaterThan(Golden.FogBlendDensity));
        }

        [Test]
        public void FogThinsMonotonically()
        {
            float last = float.MaxValue;
            for (int i = 0; i <= 100; i++)
            {
                float d = LookBlend.Evaluate(Void, Golden, i / 100f).FogDensity;
                if (i < 100) Assert.LessOrEqual(d, last + 1e-7f, $"Fog thickened at t = {i / 100f}.");
                last = d;
            }
        }

        [Test]
        public void TheVoidIsWhiteAndSunless()
        {
            Assert.AreEqual(Color.white, Void.FogColor);
            Assert.AreEqual(FogMode.Exponential, Void.FogMode);
            Assert.AreEqual(0f, Void.SunIntensity);
            Assert.AreEqual(0f, Void.SunShadowStrength);
        }

        // Source: Scripts/Editor/AcademyGreyboxSetup.cs, ApplyLighting(), and the sun's saved
        // shadow strength (1) and fog density (0.01) in Scenes/AcademyGreybox.unity.
        [Test]
        public void GoldenHourEqualsTheGreyboxGameplayLighting()
        {
            var asset = AssetDatabase.LoadAssetAtPath<LookProfile>(SetLayout.ConfigDir + "/GoldenHourLook.asset");
            Assert.IsNotNull(asset, "GoldenHourLook.asset is missing; run 2 Build Sets.");
            LookState s = asset.State;
            Assert.AreEqual(FogMode.Linear, s.FogMode);
            Assert.AreEqual(new Color(0.86f, 0.82f, 0.74f), s.FogColor);
            Assert.AreEqual(300f, s.FogStart);
            Assert.AreEqual(2600f, s.FogEnd);
            Assert.AreEqual(0.01f, s.FogDensity);
            Assert.AreEqual(new Color(0.72f, 0.76f, 0.86f), s.AmbientSky);
            Assert.AreEqual(new Color(0.74f, 0.68f, 0.6f), s.AmbientEquator);
            Assert.AreEqual(new Color(0.42f, 0.38f, 0.34f), s.AmbientGround);
            Assert.AreEqual(1.35f, s.SunIntensity);
            Assert.AreEqual(new Color(1f, 0.88f, 0.72f), s.SunColor);
            Assert.AreEqual(1f, s.SunShadowStrength);
        }

        private static void AssertSame(in LookState expected, in LookState actual)
        {
            Assert.AreEqual(expected.FogMode, actual.FogMode);
            Assert.AreEqual(expected.FogColor, actual.FogColor);
            Assert.AreEqual(expected.FogDensity, actual.FogDensity);
            Assert.AreEqual(expected.FogStart, actual.FogStart);
            Assert.AreEqual(expected.FogEnd, actual.FogEnd);
            Assert.AreEqual(expected.FogBlendDensity, actual.FogBlendDensity);
            Assert.AreEqual(expected.AmbientSky, actual.AmbientSky);
            Assert.AreEqual(expected.AmbientEquator, actual.AmbientEquator);
            Assert.AreEqual(expected.AmbientGround, actual.AmbientGround);
            Assert.AreEqual(expected.SunIntensity, actual.SunIntensity);
            Assert.AreEqual(expected.SunColor, actual.SunColor);
            Assert.AreEqual(expected.SunShadowStrength, actual.SunShadowStrength);
        }
    }
}
