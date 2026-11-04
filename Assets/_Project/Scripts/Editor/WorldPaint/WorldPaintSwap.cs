using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.EditorTools.WorldPaint
{
    /// <summary>
    /// Replaces materials on renderers from a table. It's separate from the scene builder so the
    /// rule can be tested without opening a scene: only mapped slots change, every other slot keeps
    /// its material and position.
    /// </summary>
    public static class WorldPaintSwap
    {
        /// <summary>Applies <paramref name="map"/> to every slot of every renderer.</summary>
        /// <returns>The number of material slots replaced.</returns>
        public static int Apply(IEnumerable<Renderer> renderers, IReadOnlyDictionary<Material, Material> map)
        {
            int replaced = 0;
            foreach (var renderer in renderers)
            {
                var slots = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == null || !map.TryGetValue(slots[i], out var painted)) continue;
                    slots[i] = painted;
                    changed = true;
                    replaced++;
                }
                if (changed) renderer.sharedMaterials = slots;
            }
            return replaced;
        }
    }
}
