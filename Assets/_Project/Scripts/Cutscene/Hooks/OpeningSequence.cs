using Pickleball.Cutscene.Playback;
using UnityEngine;
using UnityEngine.Playables;

namespace Pickleball.Cutscene.Hooks
{
    /// <summary>
    /// The opening's gameplay glue. On Start it plays block 1 (scenes 1–2, waking in the void to the
    /// lost propeller), after which the player is free on the meadow with the propeller resting on
    /// Propeller_Ledge. When <see cref="PropellerPickup"/> fires it plays block 2 (scenes 3–6) back
    /// to back and gameplay resumes on the crest. Testers can turn off <c>playOnStart</c> to start
    /// straight on Play_Spawn.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OpeningSequence : MonoBehaviour
    {
        /// <summary>Block id raised through <see cref="ICutscenePlayback"/> for scenes 1–2.</summary>
        public const string BlockOneId = "OC_Block1";

        /// <summary>Block id for scenes 3–6.</summary>
        public const string BlockTwoId = "OC_Block2";

        [Header("Dev")]
        [Tooltip("Play scenes 1–2 when the scene starts. Off: start straight in the platforming on " +
                 "Play_Spawn with the propeller on its ledge (for testing the level or the pickup).")]
        [SerializeField] private bool playOnStart = true;

        [Header("Wiring")]
        [SerializeField] private CutscenePlayer player;
        [SerializeField] private PropellerPickup pickup;

        [Tooltip("Scenes 1 and 2, in order.")]
        [SerializeField] private PlayableAsset[] blockOne = new PlayableAsset[0];

        [Tooltip("Scenes 3 to 6, in order.")]
        [SerializeField] private PlayableAsset[] blockTwo = new PlayableAsset[0];

        [Header("Skipping the intro (playOnStart off)")]
        [Tooltip("The player's CharacterController, moved to Play Spawn.")]
        [SerializeField] private CharacterController playerController;

        [Tooltip("The Play_Spawn marker.")]
        [SerializeField] private Transform playSpawn;

        [Tooltip("The propeller prop, put on Propeller Ledge.")]
        [SerializeField] private Transform propeller;

        [Tooltip("The Propeller_Ledge marker.")]
        [SerializeField] private Transform propellerLedge;

        /// <summary>Whether block 1 plays on Start.</summary>
        public bool PlayOnStart => playOnStart;

        void OnEnable()
        {
            if (pickup != null) pickup.PickedUp += OnPickedUp;
        }

        void OnDisable()
        {
            if (pickup != null) pickup.PickedUp -= OnPickedUp;
        }

        void Start()
        {
            if (playOnStart && player != null && player.Play(BlockOneId, blockOne)) return;
            PlaceForPlatforming();
        }

        void OnPickedUp()
        {
            if (player != null) player.Play(BlockTwoId, blockTwo);
        }

        /// <summary>The state block 1 ends in, for starting without it.</summary>
        void PlaceForPlatforming()
        {
            if (playerController != null && playSpawn != null)
            {
                // A CharacterController overrides a teleport made while it is enabled.
                bool was = playerController.enabled;
                playerController.enabled = false;
                playerController.transform.SetPositionAndRotation(playSpawn.position, playSpawn.rotation);
                playerController.enabled = was;
            }
            if (propeller != null && propellerLedge != null)
                propeller.SetPositionAndRotation(propellerLedge.position, propellerLedge.rotation);
        }
    }
}
