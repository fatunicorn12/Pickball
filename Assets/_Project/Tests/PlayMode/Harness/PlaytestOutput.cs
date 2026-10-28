using System;
using System.IO;

namespace Pickleball.Tests.PlayMode.Harness
{
    /// <summary>
    /// Where playtest artefacts go: outside the repository, so a run never dirties the
    /// worktree. Root is <c>PICKBALL_PLAYTEST_OUT</c> or the default below; each run gets
    /// a folder named by <c>PICKBALL_PLAYTEST_RUN</c> or a timestamp, shared by every
    /// scenario in the same editor session.
    /// </summary>
    public static class PlaytestOutput
    {
        /// <summary>Environment variable that overrides the output root.</summary>
        public const string RootVariable = "PICKBALL_PLAYTEST_OUT";

        /// <summary>Environment variable that fixes the run id (default: a timestamp).</summary>
        public const string RunVariable = "PICKBALL_PLAYTEST_RUN";

        const string DefaultRoot = @"C:\Users\willr\Pickball-playtest\screens";

        static string _runId;

        /// <summary>Output root; files live in <c>Root/&lt;runId&gt;/&lt;scenario&gt;/</c>.</summary>
        public static string Root
        {
            get
            {
                string env = Environment.GetEnvironmentVariable(RootVariable);
                return string.IsNullOrWhiteSpace(env) ? DefaultRoot : env;
            }
        }

        /// <summary>Id of this run (stable for the whole editor session).</summary>
        public static string RunId
        {
            get
            {
                if (_runId != null) return _runId;
                string env = Environment.GetEnvironmentVariable(RunVariable);
                _runId = string.IsNullOrWhiteSpace(env) ? DateTime.Now.ToString("yyyyMMdd-HHmmss") : env;
                return _runId;
            }
        }

        /// <summary>The (created) folder for one scenario's files.</summary>
        public static string ScenarioDir(string scenario)
        {
            string dir = Path.Combine(Root, RunId, scenario);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
