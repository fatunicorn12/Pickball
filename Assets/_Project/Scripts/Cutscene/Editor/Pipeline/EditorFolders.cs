using UnityEditor;

namespace Pickleball.Cutscene.Editor.Pipeline
{
    /// <summary>Creates asset folders the pipeline owns, one level at a time, so a fresh clone works.</summary>
    public static class EditorFolders
    {
        /// <summary>Creates <paramref name="path"/> (and its parents) if missing.</summary>
        public static void Ensure(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            Ensure(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
