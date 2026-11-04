using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia.Presentation;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Pulse
{
    /// <summary>
    /// The shader noise has to tile (a shader wraps it round a circle, so a seam
    /// would show as a hard spoke across the shockwave), fill its range, and come
    /// out identical every time (a rebuild must not rewrite the texture).
    /// </summary>
    public class TilingNoiseTests
    {
        const int Size = 64;

        [Test]
        public void Values_FillZeroToOne()
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (float v in TilingNoise.Generate(Size, 11, 5, 4))
            {
                min = Mathf.Min(min, v);
                max = Mathf.Max(max, v);
            }
            Assert.That(min, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(max, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void SameSeed_IsIdentical_DifferentSeed_IsNot()
        {
            float[] a = TilingNoise.Generate(Size, 11, 5, 4);
            Assert.That(TilingNoise.Generate(Size, 11, 5, 4), Is.EqualTo(a));
            Assert.That(TilingNoise.Generate(Size, 12, 5, 4), Is.Not.EqualTo(a));
        }

        [Test]
        public void Seams_AreNoRougherThanTheInterior()
        {
            float[] n = TilingNoise.Generate(Size, 11, 5, 4);

            float interior = 0f;
            for (int y = 0; y < Size; y++)
            for (int x = 1; x < Size; x++)
                interior = Mathf.Max(interior, Mathf.Abs(n[y * Size + x] - n[y * Size + x - 1]));

            for (int y = 0; y < Size; y++)
            {
                float across = Mathf.Abs(n[y * Size] - n[y * Size + Size - 1]);
                Assert.That(across, Is.LessThanOrEqualTo(interior + 1e-5f), $"left/right seam, row {y}");
            }
            for (int x = 0; x < Size; x++)
            {
                float across = Mathf.Abs(n[x] - n[(Size - 1) * Size + x]);
                Assert.That(across, Is.LessThanOrEqualTo(interior * 1.5f), $"top/bottom seam, column {x}");
            }
        }
    }
}
