using System.Collections.Generic;
using UnityEditor;

namespace Pickleball.Cutscene.Editor.Pipeline
{
    /// <summary>
    /// Whether a menu item exists, without calling it. The "all" menu and the scene builder run the
    /// other workstreams' steps by path through <see cref="EditorApplication.ExecuteMenuItem"/>, so
    /// nothing compiles against their code, and a step that isn't merged yet is skipped with a
    /// warning instead of an error.
    /// </summary>
    public static class MenuItemLookup
    {
        /// <summary>True when some loaded editor code declares a [MenuItem] at <paramref name="path"/>.</summary>
        public static bool Exists(string path)
        {
            foreach (var method in TypeCache.GetMethodsWithAttribute<MenuItem>())
            {
                foreach (var attr in method.GetCustomAttributes(typeof(MenuItem), false))
                    if (attr is MenuItem item && !item.validate && item.menuItem == path) return true;
            }
            return false;
        }

        /// <summary>
        /// Runs a menu item if it exists. Returns false (with a warning in <paramref name="log"/>)
        /// when it is missing or reports failure.
        /// </summary>
        public static bool TryRun(string path, List<string> log)
        {
            if (!Exists(path))
            {
                log?.Add($"SKIPPED (not in this build): {path}");
                return false;
            }
            bool ok = EditorApplication.ExecuteMenuItem(path);
            log?.Add((ok ? "ran: " : "FAILED: ") + path);
            return ok;
        }
    }
}
