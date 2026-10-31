using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pickleball.Cutscene.Editor.Pipeline
{
    /// <summary>
    /// Finds the sets workstream's look components (<c>CutsceneLook</c>,
    /// <c>ConstructionLineResolve</c>) in the open scene by type name, and the serialized float field
    /// a look track animates, so the pipeline never compiles against code it doesn't own.
    /// </summary>
    public static class LookTargets
    {
        /// <summary>
        /// The first MonoBehaviour whose type is named <paramref name="typeName"/> and that has a
        /// serialized float field named <paramref name="field"/> (case-insensitive, an optional
        /// leading underscore). False when there is none.
        /// </summary>
        public static bool TryFind(string typeName, string field, out MonoBehaviour component, out string propertyPath)
        {
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                if (mb == null || mb.GetType().Name != typeName) continue;
                var so = new SerializedObject(mb);
                var it = so.GetIterator();
                bool enter = true;
                while (it.NextVisible(enter))
                {
                    enter = false;
                    if (it.propertyType != SerializedPropertyType.Float) continue;
                    string name = it.name.TrimStart('_');
                    if (!string.Equals(name, field, StringComparison.OrdinalIgnoreCase)) continue;
                    component = mb;
                    propertyPath = it.propertyPath;
                    return true;
                }
            }
            component = null;
            propertyPath = null;
            return false;
        }
    }
}
