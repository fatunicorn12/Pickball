using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Cutscene.Playback
{
    /// <summary>
    /// The set prefabs the opening scene builder (menu 4) places, in order, each at the world
    /// origin. This is the swap point for a set: point an entry at another prefab that carries a
    /// <see cref="CutsceneSet"/> root and the same <see cref="CutsceneMarker"/> ids, then rebuild.
    /// The shots and blocking only ever read marker ids, never set geometry. Created by menu 4
    /// from the sets workstream's prefabs when missing, then left alone so a swap survives a
    /// rebuild. An empty list makes the builder fall back to placeholder sets.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Cutscene/Set List", fileName = "CutsceneSetList")]
    public sealed class CutsceneSetList : ScriptableObject
    {
        [Header("Sets")]
        [Tooltip("Set prefabs placed by Build Opening Scene, in order. Each root carries a CutsceneSet " +
                 "and its markers; together they must provide every marker id the shots use (the " +
                 "build report lists any that are missing). Swap one by pointing its entry at another " +
                 "prefab and re-running Setup Opening Cutscenes.")]
        [SerializeField] private GameObject[] sets = new GameObject[0];

        /// <summary>The set prefabs, in placement order (entries may be null).</summary>
        public IReadOnlyList<GameObject> Sets => sets;

        /// <summary>Replaces the list (setup menu, swap scripts and tests).</summary>
        public void Configure(GameObject[] prefabs)
        {
            sets = prefabs ?? new GameObject[0];
        }
    }
}
