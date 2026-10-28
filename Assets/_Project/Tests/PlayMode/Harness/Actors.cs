using Pickleball.Sandbox;
using Pickleball.Sandbox.Combat;
using Pickleball.Sandbox.Dialogue;
using Pickleball.Sandbox.Magic;
using Pickleball.Sandbox.Movement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pickleball.Tests.PlayMode.Harness
{
    /// <summary>
    /// The scene objects a scenario drives or watches, found once by type after the scene
    /// loads (a test may search; the game itself must not). Any field can be null: the scene
    /// might not carry that system, and each scenario logs what it could not drive.
    /// </summary>
    public sealed class Actors
    {
        public ChibiLocomotion Player;
        public MovementMotor Motor;
        public CharacterController Controller;
        public MeleeCombat Melee;
        public LockOnTargeting LockOn;
        public PlayerAbilities Abilities;
        public SwordAbilities SwordAbilities;
        public PlayerInteractor Interactor;
        public DialogueDirector Director;
        public NewtonBoss Boss;
        public Health PlayerHealth;
        public Mana Mana;
        public SpellCaster Caster;
        public SandboxOrbitCamera CameraRig;
        public Camera Camera;
        public Talkable[] Talkables;
        public TrainingStage Training;

        /// <summary>Resolves everything. The player is <see cref="ChibiLocomotion"/>'s object.</summary>
        public static Actors Find()
        {
            var a = new Actors
            {
                Player = Object.FindFirstObjectByType<ChibiLocomotion>(),
                Boss = Object.FindFirstObjectByType<NewtonBoss>(),
                Director = Object.FindFirstObjectByType<DialogueDirector>(),
                Interactor = Object.FindFirstObjectByType<PlayerInteractor>(),
                CameraRig = Object.FindFirstObjectByType<SandboxOrbitCamera>(),
                Talkables = Object.FindObjectsByType<Talkable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None),
                Training = Object.FindFirstObjectByType<TrainingStage>(),
                Camera = Camera.main,
            };
            if (a.Camera == null && a.CameraRig != null) a.Camera = a.CameraRig.GetComponent<Camera>();
            if (a.Player != null)
            {
                var p = a.Player.gameObject;
                a.Motor = p.GetComponent<MovementMotor>();
                a.Controller = p.GetComponent<CharacterController>();
                a.Melee = p.GetComponent<MeleeCombat>();
                a.LockOn = p.GetComponent<LockOnTargeting>();
                a.Abilities = p.GetComponent<PlayerAbilities>();
                a.SwordAbilities = p.GetComponent<SwordAbilities>();
                a.PlayerHealth = p.GetComponent<Health>();
                a.Mana = p.GetComponent<Mana>();
                a.Caster = p.GetComponentInChildren<SpellCaster>();
            }
            return a;
        }
    }
}
