using System;
using UnityEngine;

namespace Pickleball.Cutscene.Hooks
{
    /// <summary>
    /// The platforming goal: a trigger volume on Propeller_Ledge that fires <see cref="PickedUp"/>
    /// once, the first time the player's own collider enters it. Other colliders (Birdy, props,
    /// projectiles) are ignored. It only raises an event; <see cref="OpeningSequence"/> decides what
    /// that starts.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class PropellerPickup : MonoBehaviour
    {
        [Tooltip("The player root. Only colliders on it or its children count.")]
        [SerializeField] private Transform player;

        bool _fired;

        /// <summary>Raised once, when the player reaches the propeller.</summary>
        public event Action PickedUp;

        /// <summary>True once the pickup has fired.</summary>
        public bool HasFired => _fired;

        /// <summary>The player root this pickup listens for.</summary>
        public Transform Player => player;

        /// <summary>Setup-menu entry point.</summary>
        public void Configure(Transform playerRoot) => player = playerRoot;

        /// <summary>Makes the pickup fire again (a dev reset or a test).</summary>
        public void ResetPickup() => _fired = false;

        /// <summary>
        /// Fires if <paramref name="other"/> belongs to the player and the pickup hasn't fired yet.
        /// Public so tests can drive it without physics. Returns whether it fired.
        /// </summary>
        public bool TryPickUp(Transform other)
        {
            if (_fired || player == null || other == null) return false;
            if (other != player && !other.IsChildOf(player)) return false;
            _fired = true;
            PickedUp?.Invoke();
            return true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (other != null) TryPickUp(other.transform);
        }
    }
}
