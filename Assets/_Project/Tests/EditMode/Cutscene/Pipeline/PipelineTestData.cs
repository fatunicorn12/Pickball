using System.Collections.Generic;
using System.IO;
using Pickleball.Cutscene.Script;
using Pickleball.Cutscene.Timeline.Plan;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Pipeline
{
    /// <summary>
    /// Shared inputs for the pipeline tests: the real script parsed from
    /// <c>Docs/Story/OpeningCutscenes.md</c> (read-only) and line specs built from it, optionally
    /// with fake audio lengths.
    /// </summary>
    public static class PipelineTestData
    {
        /// <summary>The script markdown, read from the project root.</summary>
        public static string Markdown()
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            return File.ReadAllText(Path.Combine(root, "Docs/Story/OpeningCutscenes.md"));
        }

        /// <summary>The parsed script.</summary>
        public static ParsedScript Script() => CutsceneScriptParser.Parse(Markdown());

        /// <summary>Line specs for every line; <paramref name="audio"/> overrides lengths by id (negative = none).</summary>
        public static Dictionary<string, LineSpec> Lines(IDictionary<string, float> audio = null)
        {
            var result = new Dictionary<string, LineSpec>();
            foreach (var line in Script().Lines)
            {
                var spec = LineSpec.FromParsed(line);
                if (audio != null && audio.TryGetValue(line.Id, out float seconds)) spec = spec.WithAudio(seconds);
                result[line.Id] = spec;
            }
            return result;
        }

        /// <summary>Lays out one scene.</summary>
        public static SceneLayout Layout(int scene, IDictionary<string, float> audio = null) =>
            TimelineLayout.Layout(scene, OpeningTimingPlan.Scene(scene), Lines(audio));
    }
}
