using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Pickleball.Cutscene.Cameras.Editor
{
    /// <summary>
    /// The framing validator's findings: <b>flags</b> break the shot list's automated checks (a)–(d)
    /// and must be zero; <b>warnings</b> are the extra review aids (an actor in front of a subject,
    /// the black sun in scene 5, the void dome, a camera on the wrong shot). Written to
    /// <c>Logs/CutsceneFramingReport.txt</c>.
    /// </summary>
    public sealed class FramingReport
    {
        readonly List<string> _flags = new List<string>();
        readonly List<string> _warnings = new List<string>();
        readonly List<string> _summary = new List<string>();
        readonly List<string> _poses = new List<string>();

        /// <summary>Check failures.</summary>
        public IReadOnlyList<string> Flags => _flags;

        /// <summary>Review warnings.</summary>
        public IReadOnlyList<string> Warnings => _warnings;

        /// <summary>One line per shot.</summary>
        public IReadOnlyList<string> Summary => _summary;

        /// <summary>The hand-off pose lines.</summary>
        public IReadOnlyList<string> Poses => _poses;

        /// <summary>Samples evaluated.</summary>
        public int Samples { get; set; }

        /// <summary>The S6 final-frame delta to pose G (metres, degrees, FOV degrees).</summary>
        public Vector3 FinalDelta { get; set; }

        /// <summary>Adds a flag.</summary>
        public void Flag(string line) => _flags.Add(line);

        /// <summary>Adds a warning.</summary>
        public void Warn(string line) => _warnings.Add(line);

        /// <summary>Adds a per-shot summary line.</summary>
        public void Shot(string line) => _summary.Add(line);

        /// <summary>Adds a pose line.</summary>
        public void Pose(string line) => _poses.Add(line);

        /// <summary>The report as text.</summary>
        public string Text(string header)
        {
            var sb = new StringBuilder();
            sb.AppendLine(header);
            sb.AppendLine($"Samples: {Samples}. Flags: {_flags.Count}. Warnings: {_warnings.Count}.");
            sb.AppendLine();
            sb.AppendLine("== Flags (checks a–d) ==");
            foreach (var f in _flags) sb.AppendLine(f);
            if (_flags.Count == 0) sb.AppendLine("(none)");
            sb.AppendLine();
            sb.AppendLine("== Hand-off poses (check d) ==");
            foreach (var p in _poses) sb.AppendLine(p);
            sb.AppendLine();
            sb.AppendLine("== Warnings (review aids) ==");
            foreach (var w in _warnings) sb.AppendLine(w);
            if (_warnings.Count == 0) sb.AppendLine("(none)");
            sb.AppendLine();
            sb.AppendLine("== Per shot ==");
            sb.AppendLine("shot      samples  subject x-range   y-range      max frac  min clearance(m)");
            foreach (var s in _summary) sb.AppendLine(s);
            return sb.ToString();
        }

        /// <summary>Writes the report under the project folder and returns its text.</summary>
        public string Write(string header, string relativePath)
        {
            string text = Text(header);
            string root = Path.GetDirectoryName(Application.dataPath);
            string path = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            return text;
        }
    }
}
