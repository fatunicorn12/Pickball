using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Combat;
using UnityEngine;

namespace Pickleball.Tests.PlayMode.Harness
{
    /// <summary>
    /// Scenario set-up shortcuts. Each is a test-side convenience that a human player gets
    /// some other way (walking there, picking something up), and each one logs that it was used
    /// so a report never claims the game delivered what the harness handed over.
    /// </summary>
    public static class SessionExtensions
    {
        /// <summary>Moves the player to <paramref name="position"/> facing <paramref name="lookAt"/>
        /// (planar) and swings the camera behind. Not something a player can do: noted in the summary.</summary>
        public static async UniTask Teleport(this PlaytestSession s, Vector3 position, Vector3? lookAt = null, float yaw = 0f)
        {
            var player = s.A.Player.transform;
            if (lookAt.HasValue)
            {
                Vector3 d = lookAt.Value - position;
                d.y = 0f;
                if (d.sqrMagnitude > 1e-4f) yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            }
            var cc = s.A.Controller;
            if (cc != null) cc.enabled = false;
            player.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            if (cc != null) cc.enabled = true;
            Physics.SyncTransforms();
            s.A.Motor?.SetVerticalVelocity(0f);
            s.A.CameraRig?.RecenterBehind(Quaternion.Euler(0f, yaw, 0f) * Vector3.forward);
            s.Rec.Mark("teleport", $"{position} yaw {yaw:0}");
            await s.Frames(45);   // land, and let the camera swing round
        }

        /// <summary>Grants every movement ability through <c>PlayerAbilities.Grant</c> (what a pickup does).</summary>
        public static void GrantAllMovementAbilities(this PlaytestSession s)
        {
            if (s.A.Abilities == null) { s.CannotDrive("no PlayerAbilities on the player: cannot grant movement abilities"); return; }
            foreach (AbilityKind kind in Enum.GetValues(typeof(AbilityKind))) s.A.Abilities.Grant(kind);
            s.Note("SET-UP: granted every movement ability through PlayerAbilities.Grant (a pickup does the same).");
        }

        /// <summary>Grants every shipped <c>SwordAbilityDefinition</c> through <c>SwordAbilities.Grant</c>
        /// (what levelling up does). Read-only use of the AssetDatabase; nothing is written.</summary>
        public static void GrantAllSwordAbilities(this PlaytestSession s)
        {
#if UNITY_EDITOR
            if (s.A.SwordAbilities == null) { s.CannotDrive("no SwordAbilities on the player: cannot grant sword abilities"); return; }
            int n = 0;
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:SwordAbilityDefinition"))
            {
                var def = UnityEditor.AssetDatabase.LoadAssetAtPath<SwordAbilityDefinition>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (def != null && s.A.SwordAbilities.Grant(def)) n++;
            }
            s.Note($"SET-UP: granted {n} sword abilities through SwordAbilities.Grant (levelling up does the same).");
#else
            s.CannotDrive("sword abilities can only be loaded in the Editor");
#endif
        }

        /// <summary>One line describing the player's motion since sample index <paramref name="fromSample"/>.</summary>
        public static string Describe(this PlaytestSession s, int fromSample)
        {
            var list = s.Rec.Samples;
            if (list.Count <= fromSample) return "(no samples)";
            var seg = list.Skip(fromSample).ToList();
            float dx = seg[seg.Count - 1].px - seg[0].px, dz = seg[seg.Count - 1].pz - seg[0].pz;
            string moves = string.Join(">", seg.Select(x => x.move).Distinct());
            return $"moved {Mathf.Sqrt(dx * dx + dz * dz):0.0} m planar, y {seg[0].py:0.00}->{seg[seg.Count - 1].py:0.00} (max {seg.Max(x => x.py):0.00}), " +
                   $"speed max {seg.Max(x => x.speed):0.0} m/s, end {seg[seg.Count - 1].speed:0.0} m/s, moves [{moves}]";
        }

        /// <summary>Waits until the player is grounded (or the timeout), returns whether they landed.</summary>
        public static async UniTask<bool> UntilGrounded(this PlaytestSession s, float timeoutSeconds = 3f)
        {
            int frames = Mathf.RoundToInt(timeoutSeconds / PlaytestSession.Step);
            for (int i = 0; i < frames; i++)
            {
                if (s.A.Motor != null && s.A.Motor.IsGrounded) return true;
                await UniTask.Yield();
            }
            return false;
        }

        /// <summary>Waits until the player has left the ground (or the timeout).</summary>
        public static async UniTask<bool> UntilAirborne(this PlaytestSession s, float timeoutSeconds = 1f)
        {
            int frames = Mathf.RoundToInt(timeoutSeconds / PlaytestSession.Step);
            for (int i = 0; i < frames; i++)
            {
                if (s.A.Motor != null && !s.A.Motor.IsGrounded) return true;
                await UniTask.Yield();
            }
            return false;
        }
    }
}
