using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Cutscene.Script
{
    /// <summary>
    /// The imported opening script: every voiced line with its subtitle cards and an AudioClip slot,
    /// plus the scenes' stage directions. Written by <c>Tools ▸ Pickleball ▸ Cutscene ▸ 3 Import
    /// Script</c> from <c>Docs/Story/OpeningCutscenes.md</c>; the only hand edit is dragging voice
    /// clips into the slots. The timeline builder reads it, so assigning a clip and running
    /// <c>Rebuild Timelines</c> retimes the whole sequence.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Cutscene/Script", fileName = "OpeningScript")]
    public sealed class CutsceneScript : ScriptableObject
    {
        [Tooltip("Repo-relative path of the markdown this was imported from.")]
        [SerializeField] private string sourcePath = "Docs/Story/OpeningCutscenes.md";

        [SerializeField] private CutsceneScriptScene[] scenes = new CutsceneScriptScene[0];

        [SerializeField] private CutsceneScriptLine[] lines = new CutsceneScriptLine[0];

        /// <summary>Path the script was imported from.</summary>
        public string SourcePath => sourcePath;

        /// <summary>Number of lines.</summary>
        public int LineCount => lines != null ? lines.Length : 0;

        /// <summary>Line at an index, script order.</summary>
        public CutsceneScriptLine GetLine(int index) => lines[index];

        /// <summary>Number of scenes.</summary>
        public int SceneCount => scenes != null ? scenes.Length : 0;

        /// <summary>Scene at an index.</summary>
        public CutsceneScriptScene GetScene(int index) => scenes[index];

        /// <summary>Find a line by id. Linear; editor and build-time use only.</summary>
        public bool TryGetLine(string id, out CutsceneScriptLine line)
        {
            if (lines != null)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i] != null && lines[i].Id == id)
                    {
                        line = lines[i];
                        return true;
                    }
                }
            }
            line = null;
            return false;
        }

        /// <summary>
        /// Replaces the lines and scenes with <paramref name="parsed"/>, carrying each assigned
        /// AudioClip over by id. Returns the ids added and removed.
        /// </summary>
        public ScriptImportReport Import(ParsedScript parsed, string fromPath)
        {
            var oldAudio = new Dictionary<string, AudioClip>();
            var oldIds = new HashSet<string>();
            if (lines != null)
            {
                foreach (var l in lines)
                {
                    if (l == null || string.IsNullOrEmpty(l.Id)) continue;
                    oldIds.Add(l.Id);
                    if (l.Audio != null) oldAudio[l.Id] = l.Audio;
                }
            }

            var added = new List<string>();
            var newIds = new HashSet<string>();
            int kept = 0;
            var newLines = new CutsceneScriptLine[parsed.Lines.Count];
            for (int i = 0; i < newLines.Length; i++)
            {
                var p = parsed.Lines[i];
                var line = new CutsceneScriptLine();
                line.SetParsed(p);
                if (oldAudio.TryGetValue(p.Id, out var clip))
                {
                    line.Audio = clip;
                    kept++;
                }
                if (!oldIds.Contains(p.Id)) added.Add(p.Id);
                newIds.Add(p.Id);
                newLines[i] = line;
            }

            var removed = new List<string>();
            if (lines != null)
                foreach (var l in lines)
                    if (l != null && !string.IsNullOrEmpty(l.Id) && !newIds.Contains(l.Id)) removed.Add(l.Id);

            var newScenes = new CutsceneScriptScene[parsed.Scenes.Count];
            for (int i = 0; i < newScenes.Length; i++)
            {
                newScenes[i] = new CutsceneScriptScene();
                newScenes[i].SetParsed(parsed.Scenes[i]);
            }

            lines = newLines;
            scenes = newScenes;
            if (!string.IsNullOrEmpty(fromPath)) sourcePath = fromPath;
            return new ScriptImportReport(added, removed, kept);
        }
    }
}
