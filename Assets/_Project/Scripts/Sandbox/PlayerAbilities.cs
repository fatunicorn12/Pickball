using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>Abilities a pickup can grant. Add a case here and the pickup,
    /// HUD and locomotion all pick it up without further plumbing.</summary>
    public enum AbilityKind
    {
        DoubleJump,
        Skateboard,
        Dash,
        Tether,
    }

    /// <summary>
    /// What the player has unlocked. Kept deliberately separate from
    /// ChibiLocomotion so pickups, UI and future systems can ask "can he do X?"
    /// without any of them knowing about each other.
    /// </summary>
    public class PlayerAbilities : MonoBehaviour
    {
        readonly HashSet<AbilityKind> _unlocked = new();

        /// <summary>Raised when something new is granted. Carries the ability so
        /// listeners (VFX, HUD, audio) can react per kind.</summary>
        public event Action<AbilityKind> Unlocked;

        public bool Has(AbilityKind kind) => _unlocked.Contains(kind);

        public bool Grant(AbilityKind kind)
        {
            if (!_unlocked.Add(kind)) return false;   // already had it
            Debug.Log($"Ability unlocked: {kind}");
            Unlocked?.Invoke(kind);
            return true;
        }

        public void Revoke(AbilityKind kind) => _unlocked.Remove(kind);
    }
}
