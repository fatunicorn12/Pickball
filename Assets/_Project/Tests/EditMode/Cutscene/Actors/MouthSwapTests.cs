using NUnit.Framework;
using Pickleball.Cutscene.Actors;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Actors
{
    /// <summary>Openness → painted mouth shape: hysteresis, minimum hold, silence, fallback.</summary>
    public class MouthSwapTests
    {
        private const float HalfAt = 0.18f, OpenAt = 0.55f, CloseBelow = 0.10f, HalfBelow = 0.40f, Hold = 0.06f;
        private const float Dt = 1f / 60f;

        private static int Step(MouthShapePicker p, float o) => p.Step(o, Dt, HalfAt, OpenAt, CloseBelow, HalfBelow, Hold);

        [Test]
        public void Silence_StaysClosed()
        {
            var p = new MouthShapePicker();
            for (int i = 0; i < 300; i++) Assert.AreEqual(MouthShapePicker.Closed, Step(p, 0f));
        }

        [Test]
        public void Ramp_GoesHalfThenOpen_AndBackToClosed()
        {
            var p = new MouthShapePicker();
            for (int i = 0; i < 10; i++) Step(p, 0.3f);
            Assert.AreEqual(MouthShapePicker.Half, p.Shape);
            for (int i = 0; i < 10; i++) Step(p, 0.8f);
            Assert.AreEqual(MouthShapePicker.Open, p.Shape);
            for (int i = 0; i < 10; i++) Step(p, 0.3f);
            Assert.AreEqual(MouthShapePicker.Half, p.Shape, "open drops to half below halfBelow");
            for (int i = 0; i < 10; i++) Step(p, 0.02f);
            Assert.AreEqual(MouthShapePicker.Closed, p.Shape);
        }

        [Test]
        public void Hysteresis_HoldsInsideTheBand()
        {
            Assert.AreEqual(MouthShapePicker.Closed, MouthShapePicker.Target(MouthShapePicker.Closed, 0.15f, HalfAt, OpenAt, CloseBelow, HalfBelow));
            Assert.AreEqual(MouthShapePicker.Half, MouthShapePicker.Target(MouthShapePicker.Half, 0.15f, HalfAt, OpenAt, CloseBelow, HalfBelow));
            Assert.AreEqual(MouthShapePicker.Half, MouthShapePicker.Target(MouthShapePicker.Half, 0.50f, HalfAt, OpenAt, CloseBelow, HalfBelow));
            Assert.AreEqual(MouthShapePicker.Open, MouthShapePicker.Target(MouthShapePicker.Open, 0.50f, HalfAt, OpenAt, CloseBelow, HalfBelow));
            Assert.AreEqual(MouthShapePicker.Closed, MouthShapePicker.Target(MouthShapePicker.Open, 0.05f, HalfAt, OpenAt, CloseBelow, HalfBelow));
        }

        [TestCase(0.18f)]
        [TestCase(0.55f)]
        public void NoisySignalAtAThreshold_DoesNotFlicker(float threshold)
        {
            // +/-0.05 frame-to-frame noise straddling a threshold for 3 s: the band (and the hold)
            // must keep the shape from changing more than once or twice.
            var p = new MouthShapePicker();
            var rng = new System.Random(7);
            int changes = 0, last = p.Shape;
            for (int i = 0; i < 180; i++)
            {
                float o = threshold + (float)(rng.NextDouble() * 0.1 - 0.05);
                int s = Step(p, o);
                if (s != last) changes++;
                last = s;
            }
            Assert.LessOrEqual(changes, 2, $"flickered {changes} times at {threshold}");
        }

        [Test]
        public void MinimumHold_KeepsAShapeOnScreen()
        {
            var p = new MouthShapePicker();
            Assert.AreEqual(MouthShapePicker.Open, Step(p, 0.8f), "the first syllable opens at once");
            int frames = 0;
            while (Step(p, 0f) != MouthShapePicker.Closed) frames++;
            // 0.06 s at 60 fps: three silent frames keep it open, the fourth closes it.
            Assert.AreEqual(3, frames);
        }

        [Test]
        public void FaceWithoutPaintedMouths_IgnoresMouthAndSwapIsInert()
        {
            var go = new GameObject("Model");
            try
            {
                var face = go.AddComponent<EyeSwap>();
                face.Configure(new Renderer[0], "_BaseMap", new string[0], new Texture[0]);
                Assert.IsFalse(face.HasMouths);
                face.SetMouth(2);
                Assert.AreEqual(0, face.MouthShown, "no painted mouths: stays closed");

                var flap = go.AddComponent<MouthFlap>();
                Assert.IsTrue(flap.Nods, "the nod is the default (fallback) behaviour");
                var swap = go.AddComponent<MouthSwap>();
                var config = ScriptableObject.CreateInstance<MouthSwapConfig>();
                swap.Configure(config, flap, face);
                swap.Tick(1f, 0.5f);
                Assert.AreEqual(0, face.MouthShown);
                Object.DestroyImmediate(config);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void FaceWithPaintedMouths_ShowsTheGridTexture()
        {
            var go = new GameObject("Model");
            var textures = new Texture2D[6];
            try
            {
                for (int i = 0; i < textures.Length; i++) textures[i] = new Texture2D(2, 2) { name = "t" + i };
                var face = go.AddComponent<EyeSwap>();
                var grid = new Texture[] { null, textures[1], textures[2], null, textures[4], textures[5] };
                face.Configure(new Renderer[0], "_BaseMap", new[] { "Closed" }, new Texture[] { textures[3] }, 3, grid);
                Assert.IsTrue(face.HasMouths);
                Assert.AreSame(textures[2], face.FaceTexture(0, 2));
                Assert.AreSame(textures[4], face.FaceTexture(1, 1));
                Assert.IsNull(face.FaceTexture(1, 0), "mouth 0 comes from the eye variant");

                var flap = go.AddComponent<MouthFlap>();
                var swap = go.AddComponent<MouthSwap>();
                var config = ScriptableObject.CreateInstance<MouthSwapConfig>();
                swap.Configure(config, flap, face);
                for (int i = 0; i < 10; i++) swap.Tick(0.9f, Dt);
                Assert.AreEqual(MouthShapePicker.Open, face.MouthShown);
                for (int i = 0; i < 30; i++) swap.Tick(0f, Dt);
                Assert.AreEqual(MouthShapePicker.Closed, face.MouthShown);
                Object.DestroyImmediate(config);
            }
            finally
            {
                Object.DestroyImmediate(go);
                foreach (var t in textures) Object.DestroyImmediate(t);
            }
        }
    }
}
