using System;
using UnityEngine;

namespace Pickleball.Cutscene.Script
{
    /// <summary>A scene's heading and stage directions as stored in <see cref="CutsceneScript"/>.</summary>
    [Serializable]
    public sealed class CutsceneScriptScene
    {
        [Tooltip("Scene number.")]
        [SerializeField] private int number;

        [Tooltip("Scene title from the heading.")]
        [SerializeField] private string title = string.Empty;

        [Tooltip("The italic stage directions, in order. Not voiced; notes for the shot builders.")]
        [TextArea(1, 4)]
        [SerializeField] private string[] notes = new string[0];

        /// <summary>Scene number.</summary>
        public int Number => number;

        /// <summary>Title.</summary>
        public string Title => title;

        /// <summary>Stage directions.</summary>
        public string[] Notes => notes;

        /// <summary>Copies a parsed scene.</summary>
        public void SetParsed(ParsedScene scene)
        {
            number = scene.Number;
            title = scene.Title;
            notes = new string[scene.Notes.Count];
            for (int i = 0; i < notes.Length; i++) notes[i] = scene.Notes[i];
        }
    }
}
