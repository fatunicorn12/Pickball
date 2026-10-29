using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pickleball.UI
{
    /// <summary>
    /// The parts of one row of <see cref="DialogueView"/>'s option list, as
    /// Setup Dialogue builds them: the row (scaled when highlighted), its dark
    /// plate (tinted when highlighted, and the mouse's hit area), the answer
    /// text, and the ► cursor beside it.
    /// </summary>
    [Serializable]
    public struct DialogueViewOptionRow
    {
        [Tooltip("The row; shown for each option offered, hidden otherwise.")]
        [SerializeField] private RectTransform row;
        [Tooltip("The row's plate: its fill brightens on the highlighted option, and it is " +
                 "where the mouse hovers and clicks.")]
        [SerializeField] private Image plate;
        [Tooltip("The option's text.")]
        [SerializeField] private TMP_Text label;
        [Tooltip("The ► shown beside the highlighted option.")]
        [SerializeField] private Graphic cursor;

        public DialogueViewOptionRow(RectTransform row, Image plate, TMP_Text label, Graphic cursor)
        {
            this.row = row;
            this.plate = plate;
            this.label = label;
            this.cursor = cursor;
        }

        /// <summary>The row's transform.</summary>
        public RectTransform Row => row;
        /// <summary>The plate fill and hit area.</summary>
        public Image Plate => plate;
        /// <summary>The answer text.</summary>
        public TMP_Text Label => label;
        /// <summary>The highlight cursor.</summary>
        public Graphic Cursor => cursor;
    }
}
