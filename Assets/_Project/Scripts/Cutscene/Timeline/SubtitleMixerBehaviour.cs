using UnityEngine.Playables;

namespace Pickleball.Cutscene.Timeline
{
    /// <summary>
    /// Shows the card under the playhead on the bound <see cref="SubtitleRelay"/> and hides it in the
    /// gaps. The relay ignores a repeat of the card already shown, so calling it every frame (and
    /// scrubbing) never makes the view flicker. Cues are cached in the clip behaviours; nothing
    /// allocates per frame.
    /// </summary>
    public sealed class SubtitleMixerBehaviour : PlayableBehaviour
    {
        SubtitleRelay _relay;
        string _shownLine;

        /// <inheritdoc />
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var relay = playerData as SubtitleRelay;
            if (relay == null) return;
            _relay = relay;

            int count = playable.GetInputCount();
            for (int i = 0; i < count; i++)
            {
                if (playable.GetInputWeight(i) <= 0f) continue;
                var input = (ScriptPlayable<SubtitleBehaviour>)playable.GetInput(i);
                var behaviour = input.GetBehaviour();
                relay.Show(behaviour.Cue);
                _shownLine = behaviour.Cue.LineId;
                return;
            }
            if (_shownLine != null)
            {
                relay.Hide(_shownLine);
                _shownLine = null;
            }
        }

        /// <inheritdoc />
        public override void OnPlayableDestroy(Playable playable)
        {
            // The graph is going (end of a timeline, a skip): take the last card down with it.
            if (_relay != null && _shownLine != null) _relay.Hide(_shownLine);
            _shownLine = null;
            _relay = null;
        }
    }
}
