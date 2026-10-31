using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Pickleball.Cutscene.Editor.Pipeline
{
    /// <summary>
    /// What a scene or timeline build did and what it had to fall back on (missing verbs played as
    /// the idle, missing markers, skipped look tracks, lines without audio). Written to
    /// <see cref="PipelinePaths.BuildReport"/> (Logs/ is gitignored) and summarised in the console,
    /// so a reviewer can see at a glance what is still placeholder.
    /// </summary>
    public sealed class BuildReport
    {
        readonly List<string> _lines = new List<string>();
        int _warnings;

        /// <summary>Warnings so far.</summary>
        public int Warnings => _warnings;

        /// <summary>Every line so far.</summary>
        public IReadOnlyList<string> Lines => _lines;

        /// <summary>Adds an informational line.</summary>
        public void Info(string line) => _lines.Add(line);

        /// <summary>Adds a warning (counted, prefixed).</summary>
        public void Warn(string line)
        {
            _warnings++;
            _lines.Add("WARNING: " + line);
        }

        /// <summary>
        /// Writes the report to <paramref name="relativePath"/> (default <see cref="PipelinePaths.BuildReport"/>)
        /// and logs a summary. Returns the file path.
        /// </summary>
        public string Write(string title, string relativePath = PipelinePaths.BuildReport)
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string path = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var sb = new StringBuilder();
            sb.AppendLine(title);
            foreach (var l in _lines) sb.AppendLine(l);
            File.WriteAllText(path, sb.ToString());
            string summary = $"{title} — {_warnings} warning(s). Full report: {relativePath}\n{sb}";
            if (_warnings > 0) Debug.LogWarning(summary);
            else Debug.Log(summary);
            return path;
        }
    }
}
