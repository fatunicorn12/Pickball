using System;
using UnityEngine;

namespace Pickleball.Cutscene
{
    /// <summary>
    /// Maps the shot list's action verbs ("sit up", "panic circling", ...) to clips for one
    /// character. The animation workstream fills one asset per character; the timeline builder
    /// looks verbs up here and never names a clip itself, so a replacement model only needs a new
    /// library. A verb with no clip is reported by the builder and played as the idle.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Cutscene/Action Library", fileName = "ActionLibrary")]
    public sealed class CutsceneActionLibrary : ScriptableObject
    {
        /// <summary>One verb's clip and how it plays.</summary>
        [Serializable]
        public struct Entry
        {
            [Tooltip("Verb exactly as the shot list's Animation needs table writes it, lower case " +
                     "(\"sit up\", \"tiny wing spread\").")]
            public string verb;

            [Tooltip("The clip. Humanoid for MC; Humanoid or Generic for Birdy (see Cutscene.md).")]
            public AnimationClip clip;

            [Tooltip("Loop the clip to fill the time the shot gives it (walks, hovers, idles).")]
            public bool loop;

            [Tooltip("Blend-in time from the previous clip, in seconds.")]
            [Min(0f)]
            public float blendIn;
        }

        [Tooltip("Speaker id this library animates (\"MC\", \"BIRDY\").")]
        [SerializeField] private string actorId;

        [Tooltip("Verb the builder falls back to when a verb is missing (usually \"idle stand\" or " +
                 "\"hover idle\").")]
        [SerializeField] private string idleVerb = "idle stand";

        [SerializeField] private Entry[] entries = new Entry[0];

        /// <summary>Speaker id this library animates.</summary>
        public string ActorId => actorId;

        /// <summary>Fallback verb.</summary>
        public string IdleVerb => idleVerb;

        /// <summary>Number of entries.</summary>
        public int Count => entries != null ? entries.Length : 0;

        /// <summary>Entry at an index.</summary>
        public Entry Get(int index) => entries[index];

        /// <summary>Find a verb (case-insensitive). False when missing or clip-less.</summary>
        public bool TryGet(string verb, out Entry entry)
        {
            if (entries != null && verb != null)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].clip != null &&
                        string.Equals(entries[i].verb, verb, StringComparison.OrdinalIgnoreCase))
                    {
                        entry = entries[i];
                        return true;
                    }
                }
            }
            entry = default;
            return false;
        }

        /// <summary>Setup-menu entry point.</summary>
        public void Configure(string id, string idle, Entry[] newEntries)
        {
            actorId = id;
            idleVerb = idle;
            entries = newEntries ?? new Entry[0];
        }
    }
}
