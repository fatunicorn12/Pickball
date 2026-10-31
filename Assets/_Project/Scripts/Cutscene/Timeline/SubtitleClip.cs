using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Pickleball.Cutscene.Timeline
{
    /// <summary>
    /// One subtitle card on the timeline. The text is already tag-free and split by the pipeline;
    /// the clip only says when it is on screen. Rebuilt from the script asset by the timeline
    /// builder, never edited by hand.
    /// </summary>
    [Serializable]
    public sealed class SubtitleClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("Line id, e.g. OC_S3_07.")]
        [SerializeField] private string lineId = string.Empty;

        [Tooltip("Speaker id (MC, BIRDY).")]
        [SerializeField] private string speaker = string.Empty;

        [Tooltip("Card text, tags stripped.")]
        [TextArea(1, 3)]
        [SerializeField] private string text = string.Empty;

        [Tooltip("0-based card index in the line.")]
        [SerializeField] private int cardIndex;

        [Tooltip("Cards in the line.")]
        [SerializeField] private int cardCount = 1;

        /// <summary>Line id.</summary>
        public string LineId => lineId;

        /// <summary>Speaker.</summary>
        public string Speaker => speaker;

        /// <summary>Card text.</summary>
        public string Text => text;

        /// <summary>Card index.</summary>
        public int CardIndex => cardIndex;

        /// <summary>Card count.</summary>
        public int CardCount => cardCount;

        /// <inheritdoc />
        public ClipCaps clipCaps => ClipCaps.None;

        /// <summary>Builder entry point.</summary>
        public void Configure(string line, string who, string cardText, int index, int count)
        {
            lineId = line;
            speaker = who;
            text = cardText;
            cardIndex = index;
            cardCount = count;
        }

        /// <inheritdoc />
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<SubtitleBehaviour>.Create(graph);
            playable.GetBehaviour().Cue = new SubtitleCue(lineId, speaker, text, cardIndex, cardCount);
            return playable;
        }
    }
}
