using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Hands;
using UnityEngine;

namespace Pickleball.Tests
{
    /// <summary>
    /// Pins down how the steel side of a blade is told from its painted side:
    /// the outermost strip on each side of the wide axis, by colour, with dark
    /// marks ignored and a tie reported as "can't tell" rather than a guess.
    /// </summary>
    public class BladeEdgeTests
    {
        static readonly Color Steel = new(0.62f, 0.64f, 0.66f);
        static readonly Color Ruler = new(0.93f, 0.66f, 0.12f);
        static readonly Color Tick = new(0.05f, 0.05f, 0.04f);

        /// <summary>A ruler-sword cross-section from -0.1 to +0.1: the given
        /// colour on each outer 17% (a little wider than the 15% rim band, so float
        /// rounding at its edge can't mix colours), yellow in between, ticks on the tick side.</summary>
        static void Blade(Color low, Color high, bool ticksOnLow, out List<float> offsets, out List<Color> colours)
        {
            offsets = new List<float>();
            colours = new List<Color>();
            for (int i = 0; i <= 40; i++)
            {
                float x = -0.1f + i * 0.005f;
                offsets.Add(x);
                colours.Add(x <= -0.065f ? low : x >= 0.065f ? high : Ruler);
                // Black tick marks reach into the tick side's strip.
                bool tickSide = ticksOnLow ? x <= -0.07f : x >= 0.07f;
                if (tickSide && i % 2 == 0)
                {
                    offsets.Add(x);
                    colours.Add(Tick);
                }
            }
        }

        [Test]
        public void SteelSide_IsTheGreySide()
        {
            Blade(Ruler, Steel, ticksOnLow: true, out var offsets, out var colours);
            Assert.AreEqual(1, BladeEdge.SteelSide(offsets, colours));

            Blade(Steel, Ruler, ticksOnLow: false, out offsets, out colours);
            Assert.AreEqual(-1, BladeEdge.SteelSide(offsets, colours));
        }

        [Test]
        public void SteelSide_IgnoresBlackTickMarks()
        {
            // Unsaturated black would look like steel if it counted.
            Assert.Less(BladeEdge.Steeliness(Tick), 0f);
            Blade(Ruler, Steel, ticksOnLow: true, out var offsets, out var colours);
            BladeEdge.Score(offsets, colours, 0.15f, out float low, out float high);

            Assert.Greater(high, 0.8f);
            Assert.Less(low, 0.3f);
        }

        [Test]
        public void SteelSide_WithBothSidesAlike_CantTell()
        {
            Blade(Ruler, Ruler, ticksOnLow: true, out var offsets, out var colours);
            Assert.AreEqual(0, BladeEdge.SteelSide(offsets, colours));

            Blade(Steel, Steel, ticksOnLow: false, out offsets, out colours);
            Assert.AreEqual(0, BladeEdge.SteelSide(offsets, colours));
        }

        [Test]
        public void SteelSide_WithNoSamples_CantTell()
        {
            Assert.AreEqual(0, BladeEdge.SteelSide(new List<float>(), new List<Color>()));
            Assert.AreEqual(0, BladeEdge.SteelSide(null, null));
        }
    }
}
