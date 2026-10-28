using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Dialogue;
using UnityEngine;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// The dialogue camera as a component, driven through the director's
    /// events and its own <see cref="DialogueCamera.Tick"/> with explicit time:
    /// it takes the orbit camera off at once, blends to the shot, keeps out of
    /// walls, puts the pose and field of view back exactly, and hands the orbit
    /// camera back only on a frame whose scaled delta time is above zero.
    /// </summary>
    public class DialogueCameraTests : DialogueTestFixture
    {
        const float Fov = 60f;
        static readonly Vector3 StartPosition = new(0.5f, 2.5f, -4f);

        DialogueConfig _config;
        DialogueDirector _director;
        Talkable _npc;
        Transform _player;
        Camera _camera;
        SandboxOrbitCamera _orbit;
        DialogueCamera _dialogueCamera;
        Quaternion _startRotation;

        [SetUp]
        public void SetUpRig()
        {
            _config = NewConfig();
            SetFloat(_config, "settleSeconds", 0f);
            SetFloat(_config, "advanceLockoutSeconds", 0f);
            SetBool(_config, "pauseAudio", false);

            var playerHost = NewHost("Player");
            _player = playerHost.transform;
            var gate = playerHost.AddComponent<PlayerInputGate>();
            _director = NewDirector(_config, gate);
            _npc = NewTalkable(Definition("Hello."), new Vector3(0f, 0f, 2f));

            var cameraHost = NewHost("Camera", StartPosition);
            cameraHost.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1.35f, 0f) - StartPosition);
            _startRotation = cameraHost.transform.rotation;   // as stored, so "exact" means exact
            _camera = cameraHost.AddComponent<Camera>();
            _camera.fieldOfView = Fov;
            _orbit = cameraHost.AddComponent<SandboxOrbitCamera>();
            _dialogueCamera = cameraHost.AddComponent<DialogueCamera>();
            SetRef(_dialogueCamera, "director", _director);
            SetRef(_dialogueCamera, "config", _config);
            SetRef(_dialogueCamera, "targetCamera", _camera);
            SetRef(_dialogueCamera, "orbitCamera", _orbit);
            Lifecycle(_dialogueCamera, "OnEnable");      // subscribe, as Play mode would
        }

        [TearDown]
        public void Unsubscribe() => Lifecycle(_dialogueCamera, "OnDisable");

        /// <summary>Runs camera frames until any blend has finished. A frame's
        /// unscaled step is capped (0.1 s), so a blend needs several.</summary>
        void Frames(float scaledDeltaTime = 0f)
        {
            for (int i = 0; i < 30; i++) _dialogueCamera.Tick(scaledDeltaTime, 0.05f);
        }

        /// <summary>Plays the conversation to the moment time is restored.</summary>
        void TalkToTheEnd()
        {
            _director.Tick(_config.TextStartDelaySeconds + 0.01f, false);
            _director.Tick(0.01f, true);       // reveal
            _director.Tick(0.01f, true);       // finish: closing
            Frames();                          // blend back
            _director.Tick(_config.CloseSeconds + 0.01f, false);   // ended
        }

        [Test]
        public void Start_TakesTheOrbitCameraOffAtOnce()
        {
            Assert.That(_orbit.enabled, Is.True);
            _director.TryStart(_npc, _player);
            Assert.That(_orbit.enabled, Is.False, "off before any LateUpdate of the talk frame");
        }

        [Test]
        public void BlendIn_EndsOnTheShot_BehindThePlayer_CloserAndTighter()
        {
            _director.TryStart(_npc, _player);
            _dialogueCamera.Tick(0f, 0.1f);
            Vector3 midway = _camera.transform.position;
            Frames();

            Vector3 end = _camera.transform.position;
            Assert.That(midway, Is.Not.EqualTo(StartPosition), "it moves in unscaled time");
            Assert.That(end.z, Is.LessThan(0f), "behind the player");
            Assert.That(Vector3.Distance(end, _player.position), Is.LessThan(Vector3.Distance(StartPosition, _player.position)));
            Assert.That(_camera.fieldOfView, Is.InRange(30f, 50f).And.LessThan(Fov), "pushed in");
            Assert.That(Vector3.Angle(_camera.transform.forward, _npc.FocusPosition - end), Is.LessThan(15f),
                        "the NPC is near the middle of the frame");
        }

        [Test]
        public void End_PutsPoseAndFovBackExactly_AndHandsBackOnlyWhenTimeMoves()
        {
            _camera.fieldOfView = 57f;                       // e.g. mid FOV punch
            _director.TryStart(_npc, _player);
            Frames();
            TalkToTheEnd();

            Assert.That(_camera.transform.position, Is.EqualTo(StartPosition));
            Assert.That(_camera.transform.rotation, Is.EqualTo(_startRotation));
            Assert.That(_camera.fieldOfView, Is.EqualTo(57f));

            _dialogueCamera.Tick(0f, 0.016f);
            Assert.That(_orbit.enabled, Is.False, "never handed back on a dt = 0 frame");
            _dialogueCamera.Tick(0.016f, 0.016f);
            Assert.That(_orbit.enabled, Is.True);
        }

        [Test]
        public void ClipSweep_AWallBehindThePlayer_PullsTheCameraIn()
        {
            var wall = NewHost("Wall", new Vector3(0f, 1.5f, -1f));
            wall.AddComponent<BoxCollider>().size = new Vector3(6f, 4f, 0.2f);

            _director.TryStart(_npc, _player);
            Frames();

            Vector3 end = _camera.transform.position;
            Assert.That(end.z, Is.GreaterThan(-0.9f), "stays on the player's side of the wall");
            float fromPivot = Vector3.Distance(end, _player.position + Vector3.up * _config.PlayerFocusHeight);
            Assert.That(fromPivot, Is.GreaterThanOrEqualTo(_config.MinCameraDistance - 1e-3f));
        }

        [Test]
        public void MissingConfig_StillTakesTheOrbitCameraOff_AndGivesItBack()
        {
            SetRef(_dialogueCamera, "config", null);
            _director.TryStart(_npc, _player);
            Assert.That(_orbit.enabled, Is.False, "no orbit camera ticking at dt 0");
            Assert.That(_camera.transform.position, Is.EqualTo(StartPosition), "nothing else moves");

            TalkToTheEnd();
            _dialogueCamera.Tick(0.016f, 0.016f);
            Assert.That(_orbit.enabled, Is.True);
        }

        [Test]
        public void DisabledMidTalk_RestoresAtOnce()
        {
            _director.TryStart(_npc, _player);
            Frames();

            Lifecycle(_dialogueCamera, "OnDisable");

            Assert.That(_camera.transform.position, Is.EqualTo(StartPosition));
            Assert.That(_camera.fieldOfView, Is.EqualTo(Fov));
            Assert.That(_orbit.enabled, Is.True);
        }
    }
}
