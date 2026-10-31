using System.Collections.Generic;
using System.IO;
using System.Text;
using Pickleball.Cutscene.Script;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Cutscene.Editor.Pipeline
{
    /// <summary>
    /// <c>Tools ▸ Pickleball ▸ Cutscene ▸ 3 Import Script</c>: parses
    /// <c>Docs/Story/OpeningCutscenes.md</c> into <c>OpeningScript.asset</c>.
    /// <list type="bullet">
    /// <item>Re-importing keeps every assigned AudioClip by line id and reports the ids added and
    /// removed, so a script edit never silently drops voice work.</item>
    /// <item>Auto-bind: a line whose slot is empty gets the clip in
    /// <see cref="PipelinePaths.AudioDir"/> whose file name (without extension) is the line id. A
    /// slot set by hand is never overwritten. A line with no clip is normal (OC_S5_06 never gets
    /// one): it keeps the placeholder timing.</item>
    /// </list>
    /// Idempotent: a second run with nothing changed reports nothing added, removed or bound.
    /// </summary>
    public static class CutsceneScriptImporter
    {
        /// <summary>Menu entry.</summary>
        [MenuItem(CutsceneMenus.ImportScript, false, 3)]
        public static void ImportMenu() => Import();

        /// <summary>Runs the import; returns the script asset (null on failure).</summary>
        public static CutsceneScript Import()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string mdPath = Path.Combine(projectRoot, PipelinePaths.ScriptMarkdown);
            if (!File.Exists(mdPath))
            {
                Debug.LogError($"Cutscene script not found at {PipelinePaths.ScriptMarkdown}.");
                return null;
            }
            var parsed = CutsceneScriptParser.Parse(File.ReadAllText(mdPath));

            EditorFolders.Ensure(PipelinePaths.ScriptDir);
            var script = AssetDatabase.LoadAssetAtPath<CutsceneScript>(PipelinePaths.ScriptAsset);
            bool created = script == null;
            if (created)
            {
                script = ScriptableObject.CreateInstance<CutsceneScript>();
                AssetDatabase.CreateAsset(script, PipelinePaths.ScriptAsset);
            }

            var report = script.Import(parsed, PipelinePaths.ScriptMarkdown);
            var bound = new List<string>();
            var missing = new List<string>();
            AutoBindAudio(script, bound, missing);

            EditorUtility.SetDirty(script);
            AssetDatabase.SaveAssets();

            var sb = new StringBuilder();
            sb.Append($"Imported {script.LineCount} lines in {parsed.Scenes.Count} scenes into {PipelinePaths.ScriptAsset}");
            sb.Append(created ? " (new asset)." : ".");
            sb.Append($"\n  Added ids: {Join(report.Added)}\n  Removed ids: {Join(report.Removed)}");
            sb.Append($"\n  Audio kept by id: {report.AudioKept}");
            sb.Append($"\n  Audio bound from {PipelinePaths.AudioDir}: {Join(bound)}");
            sb.Append($"\n  Still without audio ({missing.Count}, placeholder timing): {Join(missing)}");
            Debug.Log(sb.ToString(), script);
            return script;
        }

        /// <summary>
        /// Fills empty slots from the user's audio folder by file name. Never overwrites a slot
        /// that already has a clip. Does nothing (and never creates the folder) when it is absent.
        /// </summary>
        public static void AutoBindAudio(CutsceneScript script, List<string> bound, List<string> missing)
        {
            var clips = new Dictionary<string, AudioClip>();
            if (AssetDatabase.IsValidFolder(PipelinePaths.AudioDir))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { PipelinePaths.AudioDir }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (clips.ContainsKey(name)) continue;
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (clip != null) clips[name] = clip;
                }
            }
            for (int i = 0; i < script.LineCount; i++)
            {
                var line = script.GetLine(i);
                if (line.Audio == null && clips.TryGetValue(line.Id, out var clip))
                {
                    line.Audio = clip;
                    bound?.Add(line.Id);
                }
                if (line.Audio == null) missing?.Add(line.Id);
            }
        }

        static string Join(IReadOnlyList<string> ids) => ids == null || ids.Count == 0 ? "none" : string.Join(", ", ids);
    }
}
