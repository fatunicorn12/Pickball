#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Pickleball.EditorTools
{
    /// <summary>
    /// Creates the ScriptableObject assets the modular components reference in the Inspector
    /// (3 configs + 5 event channels) and registers the court-zone tags. Re-running is safe:
    /// existing assets are kept. Run via: Tools ▸ Pickleball ▸ Create Assets.
    ///
    /// This intentionally does NOT build the scene — see the setup steps in CLAUDE.md and wire
    /// the components yourself so each system stays a separate, inspectable piece.
    /// </summary>
    public static class PickleballSceneBuilder
    {
        private const string ConfigDir = "Assets/_Project/ScriptableObjects";
        private const string EventDir = "Assets/_Project/ScriptableObjects/Events";

        private static readonly string[] ZoneTags = { "Kitchen", "CourtLeft", "CourtRight", "OutOfBounds" };

        [MenuItem("Tools/Pickleball/Create Assets")]
        public static void CreateAssets()
        {
            CreateFolderIfMissing("Assets/_Project");
            CreateFolderIfMissing(ConfigDir);
            CreateFolderIfMissing(EventDir);
            RegisterTags();

            // Tunable configs
            GetOrCreate<BallConfig>(ConfigDir + "/BallConfig.asset");
            GetOrCreate<AIConfig>(ConfigDir + "/AIConfig.asset");
            GetOrCreate<GameConfig>(ConfigDir + "/GameConfig.asset");

            // Event channels (one asset per logical event)
            GetOrCreate<BallContactEventChannelSO>(EventDir + "/BounceEvent.asset");
            GetOrCreate<BallContactEventChannelSO>(EventDir + "/BallDeadEvent.asset");
            GetOrCreate<CourtSideEventChannelSO>(EventDir + "/BallHitEvent.asset");
            GetOrCreate<GameStateEventChannelSO>(EventDir + "/GameStateEvent.asset");
            GetOrCreate<ScoreEventChannelSO>(EventDir + "/ScoreEvent.asset");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Pickleball] Created config + channel assets in " + ConfigDir +
                      " and registered court-zone tags. Now wire the scene per CLAUDE.md.");
        }

        private static void CreateFolderIfMissing(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) CreateFolderIfMissing(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static T GetOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void RegisterTags()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets.Length == 0) return;

            var so = new SerializedObject(assets[0]);
            SerializedProperty tags = so.FindProperty("tags");

            foreach (string tag in ZoneTags)
            {
                bool exists = false;
                for (int i = 0; i < tags.arraySize; i++)
                {
                    if (tags.GetArrayElementAtIndex(i).stringValue == tag) { exists = true; break; }
                }
                if (exists) continue;
                tags.InsertArrayElementAtIndex(tags.arraySize);
                tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
