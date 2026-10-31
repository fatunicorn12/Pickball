using System;
using System.Collections.Generic;
using System.IO;

namespace Pickleball.Cutscene.Editor.Actors
{
    /// <summary>
    /// Reads the action verbs out of the shot list's §6 "Animation needs" tables, so the action
    /// libraries and their coverage test follow the approved document instead of a copy of it.
    /// </summary>
    /// <remarks>
    /// Spelling rule (the timeline builder must use the same): the first cell of a row, with any
    /// parenthetical removed, split on " / ", trimmed and lower-cased. So
    /// "idle stand / idle look (breathing, cloak flutter)" gives "idle stand" and "idle look", and
    /// "crash tumble and bounce, scramble up" stays whole.
    /// </remarks>
    public static class CutsceneVerbs
    {
        /// <summary>Normalise one table cell into its verbs.</summary>
        public static List<string> FromCell(string cell)
        {
            var verbs = new List<string>();
            if (string.IsNullOrWhiteSpace(cell)) return verbs;
            string s = cell;
            int open;
            while ((open = s.IndexOf('(')) >= 0)
            {
                int close = s.IndexOf(')', open);
                s = close < 0 ? s.Substring(0, open) : s.Substring(0, open) + s.Substring(close + 1);
            }
            foreach (string part in s.Split(new[] { " / " }, StringSplitOptions.RemoveEmptyEntries))
            {
                string v = part.Trim().ToLowerInvariant();
                while (v.Contains("  ")) v = v.Replace("  ", " ");
                if (v.Length > 0 && !verbs.Contains(v)) verbs.Add(v);
            }
            return verbs;
        }

        /// <summary>
        /// The verbs of one character's §6 table ("MC" or "Birdy"), in table order, from the
        /// shot-list text.
        /// </summary>
        public static List<string> FromShotList(string markdown, string character)
        {
            var verbs = new List<string>();
            bool inSix = false, inTable = false;
            foreach (string raw in markdown.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    inSix = line.StartsWith("## 6.", StringComparison.Ordinal);
                    inTable = false;
                    continue;
                }
                if (!inSix) continue;
                if (line.StartsWith("### ", StringComparison.Ordinal))
                {
                    inTable = string.Equals(line.Substring(4).Trim(), character, StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inTable || !line.StartsWith("|", StringComparison.Ordinal)) continue;
                string[] cells = line.Split('|');
                if (cells.Length < 3) continue;
                string first = cells[1].Trim();
                if (first.Length == 0 || first == "Action" || first.StartsWith("---", StringComparison.Ordinal)) continue;
                foreach (string v in FromCell(first))
                    if (!verbs.Contains(v)) verbs.Add(v);
            }
            return verbs;
        }

        /// <summary>The verbs of one character's table, read from the shot list in the repo.</summary>
        public static List<string> FromRepo(string character)
        {
            string root = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            string path = Path.Combine(root, CutsceneActorPaths.ShotList);
            return File.Exists(path) ? FromShotList(File.ReadAllText(path), character) : new List<string>();
        }
    }
}
