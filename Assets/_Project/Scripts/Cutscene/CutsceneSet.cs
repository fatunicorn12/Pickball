using UnityEngine;

namespace Pickleball.Cutscene
{
    /// <summary>
    /// The root of one cutscene set prefab (the void, the shrine, the olive path, the crest). It
    /// lists its markers so shots can find them by id without searching the hierarchy. A set is
    /// swappable when its replacement lists markers with the same ids.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CutsceneSet : MonoBehaviour
    {
        [Tooltip("Set id (\"Void\", \"OuterShrine\", \"OlivePath\", \"SouthCrest\", ...). Used in " +
                 "logs and by the scene builder to place the set.")]
        [SerializeField] private string setId;

        [Tooltip("Every marker in this set. Wired by the set builder menu.")]
        [SerializeField] private CutsceneMarker[] markers = new CutsceneMarker[0];

        /// <summary>The set id.</summary>
        public string SetId => setId;

        /// <summary>Number of markers, for iteration without allocating.</summary>
        public int MarkerCount => markers != null ? markers.Length : 0;

        /// <summary>The marker at an index, for iteration without allocating.</summary>
        public CutsceneMarker GetMarker(int index) => markers[index];

        /// <summary>Find a marker by id. Linear over a short array; no allocation.</summary>
        public bool TryGetMarker(string id, out Transform marker)
        {
            if (markers != null)
            {
                for (int i = 0; i < markers.Length; i++)
                {
                    CutsceneMarker m = markers[i];
                    if (m != null && m.Id == id)
                    {
                        marker = m.transform;
                        return true;
                    }
                }
            }
            marker = null;
            return false;
        }

        /// <summary>Setup-menu entry point.</summary>
        public void Configure(string id, CutsceneMarker[] setMarkers)
        {
            setId = id;
            markers = setMarkers ?? new CutsceneMarker[0];
        }
    }
}
