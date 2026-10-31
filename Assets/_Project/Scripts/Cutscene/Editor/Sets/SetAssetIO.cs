using UnityEditor;
using UnityEngine;

namespace Pickleball.Cutscene.Editor.Sets
{
    /// <summary>
    /// Saves generated set assets in place, so re-running the set menu keeps every asset's GUID
    /// and the prefabs that reference them stay wired.
    /// </summary>
    public static class SetAssetIO
    {
        /// <summary>Creates a folder path under Assets if it is missing (each level in turn).</summary>
        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }

        /// <summary>Writes a mesh buffer to a mesh asset, refilling the existing asset if there is one.</summary>
        public static Mesh SaveMesh(MeshBuffer buffer, string name, float boundsPadding = 0f)
        {
            string path = $"{SetLayout.MeshDir}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            mesh = buffer.ToMesh(mesh, name);
            if (boundsPadding > 0f)
            {
                Bounds b = mesh.bounds;
                b.Expand(boundsPadding * 2f);
                mesh.bounds = b;
            }
            if (created) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>Loads a ScriptableObject asset, creating it if missing.</summary>
        public static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
