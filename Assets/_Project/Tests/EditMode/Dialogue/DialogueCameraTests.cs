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
            Assert.That(_camera.fieldOfView, Is.InRange(40f, 55f).And.LessThan(Fov), "pushed in");
            Assert.That(Vector3.Angle(_camera.transform.forward, _npc.FocusPosition - end), Is.LessThan(25f),
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
        public void Shoulder_NothingInTheWay_IsTheConfiguredLeft()
        {
            _director.TryStart(_npc, _player);
            Frames();

            Assert.That(_camera.transform.position.x, Is.LessThan(-0.5f), "over the left shoulder");
            Assert.That(_camera.WorldToViewportPoint(_npc.FocusPosition).x, Is.LessThan(0.5f), "NPC on the open left");
        }

        [Test]
        public void Shoulder_SceneryHidingTheNpcFromTheLeft_TakesTheRight()
        {
            // A post across the left shot's sightlines to the NPC's head and feet.
            var post = NewHost("Post", new Vector3(-0.7f, 1f, 0.5f));
            post.AddComponent<BoxCollider>().size = new Vector3(0.5f, 2.5f, 0.5f);

            _director.TryStart(_npc, _player);
            Frames();

            Vector3 end = _camera.transform.position;
            Assert.That(end.x, Is.GreaterThan(0.5f), "over the right shoulder");
            Assert.That(_camera.WorldToViewportPoint(_npc.FocusPosition).x, Is.GreaterThan(0.5f), "NPC on the open right");
            Assert.That(Physics.Linecast(end, _npc.FocusPosition), Is.False, "nothing between the camera and the NPC's head");
        }

        [Test]
        public void Shoulder_AWallCrowdingTheLeftShot_TakesTheRight()
        {
            // A wall just left of the player: the left shot would be pulled in against his back.
            var wall = NewHost("Wall", new Vector3(-1f, 1.5f, -1.25f));
            wall.AddComponent<BoxCollider>().size = new Vector3(0.2f, 4f, 3.5f);

            _director.TryStart(_npc, _player);
            Frames();

            Assert.That(_camera.transform.position.x, Is.GreaterThan(0.5f), "over the right shoulder, away from the wall");
        }

        [Test]
        public void Shoulder_AWallBehindBoth_KeepsTheConfiguredSide()
        {
            var wall = NewHost("Wall", new Vector3(0f, 1.5f, -1f));
            wall.AddComponent<BoxCollider>().size = new Vector3(6f, 4f, 0.2f);

            _director.TryStart(_npc, _player);
            Frames();

            Assert.That(_camera.transform.position.x, Is.LessThan(0f), "both sides are crowded alike: stay left");
        }

        /// <summary>Looks around with time stopped (scaled dt 0) until the orbit settles.</summary>
        void LookAround(Vector2 degrees)
        {
            _dialogueCamera.Look(degrees);
            for (int i = 0; i < 120; i++) _dialogueCamera.Tick(0f, 0.016f);
        }

        Pose CameraPose => new(_camera.transform.position, _camera.transform.rotation);

        [Test]
        public void Look_BeforeAnyInput_TheShotIsUntouched()
        {
            _director.TryStart(_npc, _player);
            Frames();
            Pose shot = CameraPose;

            for (int i = 0; i < 10; i++) _dialogueCamera.Tick(0f, 0.016f);

            Assert.That(CameraPose.position, Is.EqualTo(shot.position));
            Assert.That(CameraPose.rotation, Is.EqualTo(shot.rotation));
        }

        [Test]
        public void Look_OrbitsTheNpcsHead_WithTimeStopped_KeepingItWhereTheShotPutIt()
        {
            _director.TryStart(_npc, _player);
            Frames();
            Vector3 head = _npc.FocusPosition;
            Pose shot = CameraPose;
            float radius = Vector3.Distance(shot.position, head);
            Vector3 onScreen = _camera.WorldToViewportPoint(head);
            Vector3 npcAt = _npc.transform.position, playerAt = _player.position;

            LookAround(new Vector2(70f, 15f));

            Assert.That(Vector3.Distance(CameraPose.position, shot.position), Is.GreaterThan(1f), "it moved");
            Assert.That(Vector3.Distance(CameraPose.position, head), Is.EqualTo(radius).Within(1e-3f),
                        "same distance from the NPC's head");
            Vector3 now = _camera.WorldToViewportPoint(head);
            Assert.That(now.x, Is.EqualTo(onScreen.x).Within(2e-3f), "the NPC stays where the shot put it");
            Assert.That(now.y, Is.EqualTo(onScreen.y).Within(2e-3f));
            Assert.That(Mathf.DeltaAngle(0f, _camera.transform.eulerAngles.z), Is.EqualTo(0f).Within(1e-3f), "no roll");
            Assert.That(_npc.transform.position, Is.EqualTo(npcAt));
            Assert.That(_player.position, Is.EqualTo(playerAt));
        }

        [Test]
        public void Look_DuringTheBlendIn_IsDropped()
        {
            _director.TryStart(_npc, _player);
            _dialogueCamera.Tick(0f, 0.1f);
            _dialogueCamera.Look(new Vector2(180f, 0f));    // would put the camera beyond the NPC
            Frames();

            Assert.That(_camera.transform.position.z, Is.LessThan(0f), "still the shot behind the player");
        }

        [Test]
        public void Look_AdvancingALine_KeepsTheView_AndLookNeverAdvances()
        {
            var npc = NewTalkable(Definition("One.", "Two."), new Vector3(0f, 0f, 2f));
            _director.TryStart(npc, _player);
            _director.Tick(_config.TextStartDelaySeconds + 0.01f, false);
            Frames();
            _director.Tick(0.01f, true);                    // reveal line 0
            LookAround(new Vector2(-50f, 10f));
            Assert.That(_director.LineIndex, Is.EqualTo(0), "looking is not advancing");
            Pose looked = CameraPose;

            _director.Tick(0.01f, true);                    // line 1
            for (int i = 0; i < 10; i++) _dialogueCamera.Tick(0f, 0.016f);

            Assert.That(_director.LineIndex, Is.EqualTo(1));
            Assert.That(CameraPose.position, Is.EqualTo(looked.position), "no snap back to the shot");
            Assert.That(CameraPose.rotation, Is.EqualTo(looked.rotation));
        }

        [Test]
        public void Look_IntoAWall_PullsTheCameraIn_NeverThrough()
        {
            var wall = NewHost("Wall", new Vector3(0f, 1.5f, 3.5f));   // beyond the NPC
            wall.AddComponent<BoxCollider>().size = new Vector3(12f, 6f, 0.2f);
            _director.TryStart(_npc, _player);
            Frames();
            Vector3 onScreen = _camera.WorldToViewportPoint(_npc.FocusPosition);

            LookAround(new Vector2(180f, 0f));

            Vector3 end = _camera.transform.position;
            Assert.That(end.z, Is.LessThan(3.4f - _config.CameraCollisionRadius + 1e-3f), "short of the wall");
            Assert.That(end.z, Is.GreaterThan(_npc.transform.position.z), "but round on the far side of the NPC");
            Assert.That(Vector3.Distance(end, _npc.FocusPosition), Is.GreaterThanOrEqualTo(_config.MinCameraDistance - 1e-3f));
            Vector3 now = _camera.WorldToViewportPoint(_npc.FocusPosition);
            Assert.That(now.x, Is.EqualTo(onScreen.x).Within(2e-3f));
            Assert.That(now.y, Is.EqualTo(onScreen.y).Within(2e-3f));
        }

        [Test]
        public void Look_AWallRightBehindTheNpc_StopsTheTurn_NeverEntersIt()
        {
            // The wall's face is 0.35 m behind the NPC: no room to swing round behind it.
            var wall = NewHost("TightWall", new Vector3(0f, 1.5f, 2.6f));
            wall.AddComponent<BoxCollider>().size = new Vector3(8f, 6f, 0.5f);
            _director.TryStart(_npc, _player);
            Frames();
            float minDistance = Mathf.Max(_config.MinCameraDistance, _config.NpcHalfWidth + _config.CameraCollisionRadius);

            _dialogueCamera.Look(new Vector2(180f, 0f));
            for (int i = 0; i < 120; i++)
            {
                _dialogueCamera.Tick(0f, 0.016f);
                Vector3 p = _camera.transform.position;
                Assert.That(p.z, Is.LessThan(2.35f - _config.CameraCollisionRadius + 0.01f), $"frame {i}: never in the wall");
                Assert.That(Vector3.Distance(p, _npc.FocusPosition), Is.GreaterThanOrEqualTo(minDistance - 1e-3f),
                            $"frame {i}: never in the NPC");
            }

            // The blocked turn left no backlog: turning back responds at once.
            Vector3 stopped = _camera.transform.position;
            _dialogueCamera.Look(new Vector2(-20f, 0f));
            for (int i = 0; i < 5; i++) _dialogueCamera.Tick(0f, 0.016f);
            Assert.That(Vector3.Distance(_camera.transform.position, stopped), Is.GreaterThan(0.05f));
        }

        [Test]
        public void End_BlendsBackFromWhereThePlayerLeftTheCamera()
        {
            _director.TryStart(_npc, _player);
            Frames();
            LookAround(new Vector2(120f, 0f));
            Vector3 looked = _camera.transform.position;

            _director.Tick(_config.TextStartDelaySeconds + 0.01f, false);
            _director.Tick(0.01f, true);       // reveal
            _director.Tick(0.01f, true);       // finish: closing starts
            _dialogueCamera.Look(new Vector2(90f, 0f));     // too late: closing
            _dialogueCamera.Tick(0f, 0.01f);

            Assert.That(Vector3.Distance(_camera.transform.position, looked), Is.LessThan(0.2f),
                        "the blend back starts from the player's view, no jump");
            Frames();
            _director.Tick(_config.CloseSeconds + 0.01f, false);   // ended
            Assert.That(_camera.transform.position, Is.EqualTo(StartPosition));
            Assert.That(_camera.transform.rotation, Is.EqualTo(_startRotation));
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
