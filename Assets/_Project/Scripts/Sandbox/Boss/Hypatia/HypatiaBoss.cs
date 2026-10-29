using System;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia
{
    /// <summary>
    /// Hypatia, the Astrolabe: the fight's brain. CONTRACT STUB: the events,
    /// state and wiring below are the agreed surface; the timers and damage
    /// pipeline are filled in by the brain work (see Docs/Systems/HypatiaBoss.md
    /// once written).
    ///
    /// Owns WHEN things happen: independent timers for rings, conics and (phase 2)
    /// the burn, all on her slow clock. The attack components own HOW. Raises C#
    /// events for presentation (needle, window glow, HUD); never touches VFX, UI
    /// or audio itself.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class HypatiaBoss : MonoBehaviour
    {
        [Header("Data")]
        [Tooltip("Every tunable of the fight.")]
        [SerializeField] private HypatiaConfig config;

        [Header("Scene")]
        [Tooltip("Centre of the Rete plate; its Y is the floor.")]
        [SerializeField] private Transform arenaCentre;
        [Tooltip("The player root (moves with the player).")]
        [SerializeField] private Transform player;
        [Tooltip("The player's Health: takes her damage, and is credited the " +
                 "player's regain when she is hurt.")]
        [SerializeField] private Health playerHealth;

        [Header("Attacks (on this root)")]
        [Tooltip("An IRingAttack.")]
        [SerializeField] private MonoBehaviour rings;
        [Tooltip("An IConicAttack.")]
        [SerializeField] private MonoBehaviour conics;
        [Tooltip("An IArenaBurn.")]
        [SerializeField] private MonoBehaviour burn;

        [Header("Body")]
        [Tooltip("The visual model, moved for hover and bob.")]
        [SerializeField] private Transform body;
        [Tooltip("Her animator; its speed follows her slow in code.")]
        [SerializeField] private Animator animator;

        /// <summary>The needle should show <c>e</c> for this conic now; it fires
        /// after <c>lead</c> seconds (on her clock). Args: kind, e, lead.</summary>
        public event Action<ConicKind, float, float> ConicTelegraphed;

        /// <summary>The recalibration window opened, lasting this many seconds.</summary>
        public event Action<float> RecalibrationStarted;

        /// <summary>The recalibration window closed.</summary>
        public event Action RecalibrationEnded;

        /// <summary>Alexandria's Burning began. Raised once.</summary>
        public event Action PhaseTwoStarted;

        /// <summary>She died; every attack has been stopped. Raised once.</summary>
        public event Action Defeated;

        public HypatiaConfig Config => config;

        /// <summary>The live context; rebuilt in every OnEnable.</summary>
        public HypatiaContext Context { get; private set; }

        /// <summary>1 or 2.</summary>
        public int Phase { get; private set; } = 1;

        /// <summary>True during the recalibration window.</summary>
        public bool IsRecalibrating { get; private set; }

        // Keep the events "used" until the brain fills in; removed then.
        void SuppressUnusedEventWarnings()
        {
            ConicTelegraphed?.Invoke(ConicKind.Parabola, 1f, 0f);
            RecalibrationStarted?.Invoke(0f);
            RecalibrationEnded?.Invoke();
            PhaseTwoStarted?.Invoke();
            Defeated?.Invoke();
        }
    }
}
