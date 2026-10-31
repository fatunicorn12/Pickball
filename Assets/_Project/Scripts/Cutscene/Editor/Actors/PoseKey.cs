using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Cutscene.Editor.Actors
{
    /// <summary>
    /// One key of a hand-authored humanoid clip (<see cref="PoseClipSpec"/>): muscle values at a
    /// time, plus how the body is turned, moved and grounded. Authored in Unity's muscle space,
    /// so a clip made this way is a true Humanoid clip that retargets onto any humanoid avatar
    /// (MC's or Birdy's) exactly like a Mixamo clip.
    /// </summary>
    /// <remarks>
    /// Channels and how they interpolate between keys (eased, smoothstep):
    /// <list type="bullet">
    /// <item><b>Set</b>: absolute muscle values. Only keys that set a muscle take part in its
    /// curve. Before its first key it eases in from the base clip's value (starting at the clip's
    /// first key); after its last key it holds. Muscles no key sets come from the base clip.</item>
    /// <item><b>Add</b>: muscle deltas on top of whatever the muscle is (base or Set). A key
    /// that doesn't mention a delta counts as 0 there.</item>
    /// <item><b>Body</b>: absolute body rotation in the actor's frame (keys that set it only).</item>
    /// <item><b>Turn</b>: Euler degrees (x pitch forward, y yaw right, z roll left) applied on top
    /// of the body rotation; 0 where unmentioned.</item>
    /// <item><b>Offset</b>: metres in the actor's frame, added after grounding (jumps, bobs).</item>
    /// <item><b>Ground</b>: 0..1, how much the lowest skin point is pulled onto y = 0.</item>
    /// </list>
    /// </remarks>
    public sealed class PoseKey
    {
        /// <summary>Key time, seconds from the clip start.</summary>
        public float Time;

        /// <summary>Absolute muscle values by Unity muscle name ("Right Arm Down-Up").</summary>
        public readonly Dictionary<string, float> Set = new Dictionary<string, float>();

        /// <summary>Muscle deltas by name.</summary>
        public readonly Dictionary<string, float> Add = new Dictionary<string, float>();

        /// <summary>Absolute body rotation, or null to keep the base clip's.</summary>
        public Quaternion? Body;

        /// <summary>Extra body rotation, Euler degrees in the actor's frame.</summary>
        public Vector3 Turn;

        /// <summary>Body offset, metres in the actor's frame.</summary>
        public Vector3 Offset;

        /// <summary>Grounding weight, or null for the spec's default.</summary>
        public float? Ground;

        /// <summary>A key at a time.</summary>
        public PoseKey(float time)
        {
            Time = time;
        }

        /// <summary>Set absolute muscle values from (name, value) pairs.</summary>
        public PoseKey S(IEnumerable<KeyValuePair<string, float>> muscles)
        {
            foreach (var m in muscles) Set[m.Key] = m.Value;
            return this;
        }

        /// <summary>Set one absolute muscle value.</summary>
        public PoseKey S(string muscle, float value)
        {
            Set[muscle] = value;
            return this;
        }

        /// <summary>Add muscle deltas from (name, delta) pairs.</summary>
        public PoseKey A(IEnumerable<KeyValuePair<string, float>> muscles)
        {
            foreach (var m in muscles) Add[m.Key] = (Add.TryGetValue(m.Key, out float v) ? v : 0f) + m.Value;
            return this;
        }

        /// <summary>Add one muscle delta.</summary>
        public PoseKey A(string muscle, float delta)
        {
            Add[muscle] = (Add.TryGetValue(muscle, out float v) ? v : 0f) + delta;
            return this;
        }

        /// <summary>Set the absolute body rotation.</summary>
        public PoseKey Rot(Quaternion body)
        {
            Body = body;
            return this;
        }

        /// <summary>Set the extra body rotation (Euler degrees: pitch forward, yaw right, roll left).</summary>
        public PoseKey T(float pitch, float yaw, float roll)
        {
            Turn = new Vector3(pitch, yaw, roll);
            return this;
        }

        /// <summary>Set the body offset in metres (x right, y up, z forward).</summary>
        public PoseKey O(float x, float y, float z)
        {
            Offset = new Vector3(x, y, z);
            return this;
        }

        /// <summary>Set the grounding weight.</summary>
        public PoseKey G(float weight)
        {
            Ground = weight;
            return this;
        }

        /// <summary>A copy at another time (a hold: the same pose later).</summary>
        public PoseKey At(float time)
        {
            var k = new PoseKey(time) { Body = Body, Turn = Turn, Offset = Offset, Ground = Ground };
            foreach (var m in Set) k.Set[m.Key] = m.Value;
            foreach (var m in Add) k.Add[m.Key] = m.Value;
            return k;
        }
    }
}
