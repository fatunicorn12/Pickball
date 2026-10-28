using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// The talkable characters that are enabled right now: a ScriptableObject
    /// runtime set that each <see cref="Talkable"/> joins in <c>OnEnable</c> and
    /// leaves in <c>OnDisable</c>.
    ///
    /// It exists so the player's interactor can find the nearest talker without
    /// a scene search or a physics query every frame. A physics overlap would
    /// need every NPC to carry a collider on a layer nothing else uses, and in
    /// the academy world a few metres of radius already holds dozens of prop
    /// colliders. The set holds a handful of entries, so a linear scan is
    /// cheaper than either.
    ///
    /// It follows the <c>SpacetimeSettings</c> pattern: an asset as the meeting
    /// point, runtime state in a <c>[NonSerialized]</c> field. The list is
    /// rebuilt lazily, so after a script reload the field is empty and the
    /// Talkables' own <c>OnEnable</c> refills it. It is never cleared in this
    /// asset's <c>OnEnable</c>, which could run after the Talkables had already
    /// re-registered.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Dialogue/Talkable Set", fileName = "TalkableSet")]
    public class TalkableSet : ScriptableObject
    {
        [System.NonSerialized] private List<Talkable> _items;

        List<Talkable> Items => _items ??= new List<Talkable>(8);

        /// <summary>Number of registered Talkables. Entries may be destroyed
        /// objects if one was removed without <c>OnDisable</c>; consumers skip nulls.</summary>
        public int Count => Items.Count;

        /// <summary>The Talkable at <paramref name="index"/> (0-based, below <see cref="Count"/>).</summary>
        public Talkable this[int index] => Items[index];

        /// <summary>Adds <paramref name="talkable"/> once. A second add is ignored.</summary>
        public void Add(Talkable talkable)
        {
            if (talkable == null || Items.Contains(talkable)) return;
            Items.Add(talkable);
        }

        /// <summary>Removes <paramref name="talkable"/> if present.</summary>
        public void Remove(Talkable talkable) => Items.Remove(talkable);
    }
}
