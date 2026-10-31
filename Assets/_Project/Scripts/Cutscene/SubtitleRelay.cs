using System;
using UnityEngine;

namespace Pickleball.Cutscene
{
    /// <summary>
    /// The scene object the subtitle track binds to. Timeline playables come and go with the graph,
    /// so they can't own events a HUD subscribes to once; this component is the stable middle.
    /// Raising is idempotent per card: showing the cue already on screen does nothing, so Timeline
    /// scrubbing and re-evaluation don't make the view flicker.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SubtitleRelay : MonoBehaviour, ISubtitleSource
    {
        private string _shownLineId;
        private int _shownCard = -1;

        /// <inheritdoc />
        public event Action<SubtitleCue> CueShown;

        /// <inheritdoc />
        public event Action<string> CueHidden;

        /// <inheritdoc />
        public event Action Cleared;

        /// <summary>The line id on screen, or null.</summary>
        public string ShownLineId => _shownLineId;

        /// <summary>Show a card unless it is already the one on screen.</summary>
        public void Show(in SubtitleCue cue)
        {
            if (cue.LineId == _shownLineId && cue.CardIndex == _shownCard) return;
            _shownLineId = cue.LineId;
            _shownCard = cue.CardIndex;
            CueShown?.Invoke(cue);
        }

        /// <summary>Hide a line's card if it is the one on screen.</summary>
        public void Hide(string lineId)
        {
            if (lineId != _shownLineId) return;
            _shownLineId = null;
            _shownCard = -1;
            CueHidden?.Invoke(lineId);
        }

        /// <summary>Remove everything (skip, end, disable).</summary>
        public void Clear()
        {
            _shownLineId = null;
            _shownCard = -1;
            Cleared?.Invoke();
        }

        private void OnDisable()
        {
            Clear();
        }
    }
}
