using Pickleball.Sandbox.Movement;
using UnityEngine;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// What aiming the spell takes from the rest of the player's input while it
    /// lasts, Expedition 33 style: the lock is dropped, fire (left mouse / RT)
    /// no longer swings the sword or toggles lock-on, and the player walks.
    ///
    /// On <see cref="SpellCaster.AimStarted"/> it releases any held lock-on,
    /// then claims <see cref="InputBlock.Sword"/> | <see cref="InputBlock.LockOn"/>
    /// on the <see cref="PlayerInputGate"/>. Every aiming frame it sets
    /// <see cref="PlayerMovementInput.DriveLimit"/> to the spell's
    /// <see cref="SpellDefinition.AimMoveScale"/>, which also blocks sprint. On
    /// <see cref="SpellCaster.AimEnded"/>, and on disable, it releases the
    /// claim and puts the limit back to 1.
    ///
    /// This is the one place magic's input rules live: the caster, movement,
    /// sword and lock-on never learn about each other.
    ///
    /// <b>Lock-on is also released explicitly, before the claim.</b> Since the
    /// lock-on fix (1d10103) any claim on <see cref="InputBlock.LockOn"/>, by
    /// whoever makes it, drops a held lock and hides the lock-on hint, so with
    /// the gate wired the explicit <c>Release()</c> is redundant. It stays for a
    /// player without a gate, where nothing else would drop the lock and it
    /// would fight the crosshair for the body's facing.
    ///
    /// Hot reload: claims are not serialized, and the gate drops them all on
    /// disable. The caster resets its aim latch on enable and raises
    /// <see cref="SpellCaster.AimStarted"/> again on its first Update if aim is
    /// still held; this component has re-subscribed by then (every OnEnable runs
    /// before any Update), so the claim comes back by itself.
    /// </summary>
    public class AimInputClaims : MonoBehaviour
    {
        const InputBlock AimBlocks = InputBlock.Sword | InputBlock.LockOn;

        [Tooltip("The spell caster whose aim this follows. Found on this object when empty.")]
        [SerializeField] private SpellCaster caster;
        [Tooltip("The player's input gate. Found on this object when empty; without one " +
                 "the sword and lock-on toggle still answer fire while aiming.")]
        [SerializeField] private PlayerInputGate inputGate;
        [Tooltip("Released when aiming starts. Found on this object when empty.")]
        [SerializeField] private LockOnTargeting lockOn;
        [Tooltip("Capped to a walk while aiming. Found on this object when empty.")]
        [SerializeField] private PlayerMovementInput movementInput;

        bool _claiming;

        /// <summary>True between an aim start and its end: the claim is held.</summary>
        public bool IsClaiming => _claiming;

        void Awake()
        {
            // Explicit null checks: `??` on a Unity object skips the fake-null check.
            if (caster == null) caster = GetComponent<SpellCaster>();
            if (inputGate == null) inputGate = GetComponent<PlayerInputGate>();
            if (lockOn == null) lockOn = GetComponent<LockOnTargeting>();
            if (movementInput == null) movementInput = GetComponent<PlayerMovementInput>();
        }

        void OnEnable()
        {
            if (caster == null)
            {
                Debug.LogWarning($"[AimInputClaims] {name} has no SpellCaster; aiming will not " +
                                 "block the sword or cap movement. Run Tools ▸ Pickleball ▸ Setup Magic.", this);
                return;
            }
            caster.AimStarted += ClaimForAim;
            caster.AimEnded += ReleaseAim;
        }

        void OnDisable()
        {
            if (caster != null)
            {
                caster.AimStarted -= ClaimForAim;
                caster.AimEnded -= ReleaseAim;
            }
            ReleaseAim();
        }

        void Update()
        {
            // Every frame, so the walk speed can be tuned live on the spell.
            if (_claiming) ApplyDriveLimit();
        }

        /// <summary>
        /// Drops the lock, claims the sword and lock-on inputs and caps movement.
        /// <see cref="SpellCaster.AimStarted"/> calls it; public so EditMode tests
        /// can drive it (the caster raises its events from Update, which EditMode
        /// never runs). Idempotent.
        /// </summary>
        public void ClaimForAim()
        {
            if (lockOn != null && lockOn.IsLocked) lockOn.Release();
            if (inputGate != null) inputGate.Claim(this, AimBlocks);
            _claiming = true;
            ApplyDriveLimit();
        }

        /// <summary>
        /// Releases this component's claim (never another owner's) and gives
        /// movement its full drive back. <see cref="SpellCaster.AimEnded"/> calls
        /// it, and so does OnDisable. Idempotent.
        /// </summary>
        public void ReleaseAim()
        {
            if (inputGate != null) inputGate.Release(this);
            if (_claiming && movementInput != null) movementInput.DriveLimit = 1f;
            _claiming = false;
        }

        void ApplyDriveLimit()
        {
            if (movementInput == null) return;
            var spell = caster != null ? caster.Spell : null;
            movementInput.DriveLimit = spell != null ? spell.AimMoveScale : 1f;
        }
    }
}
