using UnityEngine;

namespace Pickleball.Cutscene
{
    /// <summary>
    /// A named point in a cutscene set (<c>MC_Start</c>, <c>Propeller_Ledge</c>, ...). Shots and
    /// blocking are placed relative to markers, so a replacement set only has to provide markers
    /// with the same ids. The GameObject is named after the id too, for readability, but lookups
    /// use <see cref="Id"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CutsceneMarker : MonoBehaviour
    {
        [Tooltip("Marker id from CutsceneMarkerIds. Position and forward (+Z) are the meaning: " +
                 "an actor placed on a marker stands on it facing its forward.")]
        [SerializeField] private string id;

        /// <summary>The marker id.</summary>
        public string Id => id;

        /// <summary>Setup-menu entry point.</summary>
        public void Configure(string markerId)
        {
            id = markerId;
        }
    }
}
