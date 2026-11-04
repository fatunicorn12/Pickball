using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.EditorTools.WorldPaint;
using UnityEngine;

namespace Pickleball.Tests.EditorTools.WorldPaint
{
    /// <summary>The swap may only change mapped slots: a wrong swap would repaint gates and props.</summary>
    public class WorldPaintSwapTests
    {
        readonly List<Object> _created = new();

        Material NewMaterial(string name)
        {
            var mat = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = name };
            _created.Add(mat);
            return mat;
        }

        MeshRenderer NewRenderer(params Material[] slots)
        {
            var go = new GameObject("Renderer") { hideFlags = HideFlags.DontSave };
            _created.Add(go);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = slots;
            return renderer;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _created) Object.DestroyImmediate(obj);
            _created.Clear();
        }

        [Test]
        public void Apply_ReplacesMappedSlot_AndCountsIt()
        {
            Material grey = NewMaterial("grey"), painted = NewMaterial("painted");
            var renderer = NewRenderer(grey);

            int replaced = WorldPaintSwap.Apply(new Renderer[] { renderer }, new Dictionary<Material, Material> { [grey] = painted });

            Assert.That(replaced, Is.EqualTo(1));
            Assert.That(renderer.sharedMaterial, Is.SameAs(painted));
        }

        [Test]
        public void Apply_LeavesUnmappedSlots_InTheirPlace()
        {
            Material grey = NewMaterial("grey"), gate = NewMaterial("gate"), painted = NewMaterial("painted");
            var renderer = NewRenderer(gate, grey, null);

            int replaced = WorldPaintSwap.Apply(new Renderer[] { renderer }, new Dictionary<Material, Material> { [grey] = painted });

            Assert.That(replaced, Is.EqualTo(1));
            Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { gate, painted, null }));
        }

        [Test]
        public void Apply_WithNothingMapped_ChangesNothing()
        {
            Material gate = NewMaterial("gate");
            var renderer = NewRenderer(gate);

            int replaced = WorldPaintSwap.Apply(new Renderer[] { renderer }, new Dictionary<Material, Material>());

            Assert.That(replaced, Is.EqualTo(0));
            Assert.That(renderer.sharedMaterial, Is.SameAs(gate));
        }
    }
}
