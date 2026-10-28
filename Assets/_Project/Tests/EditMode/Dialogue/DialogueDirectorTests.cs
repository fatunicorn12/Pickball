using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Dialogue;
using UnityEngine;
using UnityEngine.TestTools;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// Drives a whole conversation through <see cref="DialogueDirector.Tick"/>
    /// with explicit time: the start order (block, settle, pause, notify), the
    /// guards on the opening press, the end order (box, camera, notify, time,
    /// event, input a frame later, cooldown) and every abort path. Times below
    /// come from the config defaults set in <see cref="SetUpConversation"/>.
    /// </summary>
    public class DialogueDirectorTests : DialogueTestFixture
    {
        const float Settle = 0.25f;
        const float TextDelay = 0.2f;
        const float Close = 0.35f;
        const float Cooldown = 0.35f;

        DialogueConfig _config;
        PlayerInputGate _gate;
        DialogueDirector _director;
        Talkable _npc;
        Transform _player;
        List<string> _log;

        [SetUp]
        public void SetUpConversation()
        {
            _config = NewConfig();
            SetFloat(_config, "settleSeconds", Settle);
            SetFloat(_config, "textStartDelaySeconds", TextDelay);
            SetFloat(_config, "closeSeconds", Close);
            SetFloat(_config, "restartCooldownSeconds", Cooldown);
            SetFloat(_config, "charactersPerSecond", 40f);
            SetFloat(_config, "advanceLockoutSeconds", 0f);
            SetBool(_config, "pauseAudio", false);

            var playerHost = NewHost("Player");
            _player = playerHost.transform;
            _gate = playerHost.AddComponent<PlayerInputGate>();
            _director = NewDirector(_config, _gate);
            _npc = NewTalkable(Definition("Hello there.", "Second line.", "Bye."), new Vector3(0f, 0f, 2f));

            _log = new List<string>();
            _director.ConversationStarting += _ => _log.Add($"starting ts={Ts()}");
            _director.ConversationStarted += _ => _log.Add($"started ts={Ts()}");
            _director.ConversationEnding += _ => _log.Add($"ending ts={Ts()}");
            _director.ConversationEnded += t => _log.Add($"ended ts={Ts()} npc={t.IsInConversation}");
            _npc.ConversationStarted += _ => _log.Add("npc started");
            _npc.ConversationEnded += t => _log.Add($"npc ended ts={Ts()} {t.LastEndReason}");
        }

        static string Ts() => Time.timeScale.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Start, settle and open: line 0 has just started typing.</summary>
        void StartAndOpen()
        {
            Assert.That(_director.TryStart(_npc, _player), Is.True);
            _director.Tick(Settle + 0.01f, false);
            _director.Tick(TextDelay + 0.01f, false);
            Assert.That(_director.State, Is.EqualTo(DialogueDirector.Phase.Talking));
        }

        [Test]
        public void Cooldown_IsADebounce_MashingKeepsItShut_AQuietPressOpens()
        {
            StartAndOpen();
            ReadToTheEnd();
            _director.Tick(Close + 0.01f, false);        // ended
            _director.Tick(0.01f, false);                // input released: cooling down
            Assert.That(_director.IsCoolingDown, Is.True);

            // E mashed every 0.15 s for 3 s, far longer than the 0.35 s cooldown.
            for (int i = 0; i < 20; i++)
            {
                _director.Tick(0.15f, true);
                Assert.That(_director.CanStartConversation, Is.False, $"press {i}: still shut while mashing");
            }

            _director.Tick(Cooldown - 0.05f, false);
            Assert.That(_director.CanStartConversation, Is.False, "not quiet long enough yet");
            _director.Tick(0.1f, false);
            Assert.That(_director.CanStartConversation, Is.True, "a quiet spell reopens it");
        }

        [Test]
        public void FirstLine_WaitsWhileTheNpcIsStillTurningRound()
        {
            // The NPC at z 2 faces +z, away from the player at the origin (bearing 180).
            Assert.That(_director.TryStart(_npc, _player), Is.True);
            _director.Tick(Settle + 0.01f, false);
            Assert.That(_director.State, Is.EqualTo(DialogueDirector.Phase.Opening));

            float yaw = 0f;
            for (int i = 0; i < 10; i++)              // 0.5 s, turning 12° a tick, as its driver would
            {
                yaw += 12f;
                _npc.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                _director.Tick(0.05f, false);
            }
            Assert.That(_director.State, Is.EqualTo(DialogueDirector.Phase.Opening),
                        "past the text delay, but still 60° off and turning");

            while (yaw < 168f)
            {
                yaw += 12f;
                _npc.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                _director.Tick(0.05f, false);
            }
            Assert.That(_director.State, Is.EqualTo(DialogueDirector.Phase.Talking), "12° off: he speaks");
        }

        [Test]
        public void FirstLine_AnNpcThatIsNotTurning_NeverHoldsItUp()
        {
            Assert.That(_director.TryStart(_npc, _player), Is.True);
            _director.Tick(Settle + 0.01f, false);
            _director.Tick(TextDelay + 0.01f, false);       // facing fully away, but still

            Assert.That(_director.State, Is.EqualTo(DialogueDirector.Phase.Talking));
        }

        [Test]
        public void FirstLine_WaitForTheNpc_IsCapped()
        {
            Assert.That(_director.TryStart(_npc, _player), Is.True);
            _director.Tick(Settle + 0.01f, false);
            float yaw = 0f, waited = 0f;
            while (waited < TextDelay + _config.WaitForNpcFacingSeconds - 0.1f)
            {
                yaw -= 0.5f;                                   // creeping the wrong way
                _npc.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                _director.Tick(0.05f, false);
                waited += 0.05f;
            }
            Assert.That(_director.State, Is.EqualTo(DialogueDirector.Phase.Opening));

            for (int i = 0; i < 4; i++)
            {
                yaw -= 0.5f;
                _npc.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                _director.Tick(0.05f, false);
            }
            Assert.That(_director.State, Is.EqualTo(DialogueDirector.Phase.Talking), "never longer than the cap");
        }

        /// <summary>Reveal and dismiss every line.</summary>
        void ReadToTheEnd()
        {
            for (int i = 0; i < 3; i++)
            {
                _director.Tick(0.01f, true);     // reveal
                _director.Tick(0.01f, true);     // next, or finish
            }
            Assert.That(_director.State, Is.EqualTo(DialogueDirector.Phase.Closing));
        }

        // ------------------------------------------------------------------ start

        [Test]
        public void Start_BlocksInput_ThenSettlesInRunningTime_ThenPauses_ThenNotifies()
        {
            Time.timeScale = 0.8f;
            Assert.That(_director.TryStart(_npc, _player), Is.True);

            Assert.That(_gate.IsBlocked(InputBlock.All), Is.True, "everything blocked from the press");
            Assert.That(Time.timeScale, Is.EqualTo(0.8f), "the world still runs while he settles");
            Assert.That(_npc.IsInConversation, Is.False);

            _director.Tick(Settle * 0.5f, false);
            Assert.That(_director.State, Is.EqualTo(DialogueDirector.Phase.Settling));

            _director.Tick(Settle, false);
            Assert.That(Time.timeScale, Is.EqualTo(0f));
            Assert.That(_npc.IsInConversation, Is.True);
            Assert.That(_npc.Partner, Is.SameAs(_player));
            Assert.That(_log, Is.EqualTo(new[] { "starting ts=0.8", "npc started", "started ts=0" }));
        }

        [Test]
        public void Start_WithNoSettle_PausesAtOnce()
        {
            SetFloat(_config, "settleSeconds", 0f);
            _director.TryStart(_npc, _player);
            Assert.That(Time.timeScale, Is.EqualTo(0f));
            Assert.That(_director.State, Is.EqualTo(DialogueDirector.Phase.Opening));
        }

        [Test]
        public void Start_PressesDuringSettleAndOpening_DoNotAdvance()
        {
            _director.TryStart(_npc, _player);
            _director.Tick(Settle + 0.01f, true);
            _director.Tick(TextDelay + 0.01f, true);

            Assert.That(_director.LineIndex, Is.EqualTo(0));
            Assert.That(_director.IsLineRevealed, Is.False, "the opening press must not skip line 0");
        }

        [Test]
        public void Start_IsRefused_WhenBusy_CoolingDown_TimeStopped_OrTheNpcCannotTalk()
        {
            var other = NewTalkable(Definition("Me too."));
            StartAndOpen();
            Assert.That(_director.TryStart(other, _player), Is.False, "busy");

            ReadToTheEnd();
            _director.Tick(Close + 0.01f, false);
            _director.Tick(0.01f, false);
            Assert.That(_director.State, Is.EqualTo(DialogueDirector.Phase.Idle));
            Assert.That(_director.TryStart(other, _player), Is.False, "cooling down");

            _director.Tick(Cooldown + 0.01f, false);
            Time.timeScale = 0f;
            Assert.That(_director.TryStart(other, _player), Is.False, "time already stopped");
            Time.timeScale = 1f;

            var mute = NewTalkable(Definition());
            Assert.That(_director.TryStart(mute, _player), Is.False, "no lines");
            Assert.That(_director.TryStart(other, _player), Is.True);
        }

        // ------------------------------------------------------------------- talk

        [Test]
        public void Talk_TextTypesInUnscaledTime_WhileTimeIsStopped()
        {
            StartAndOpen();
            int lastCount = -1;
            _director.CharactersRevealed += c => lastCount = c;

            _director.Tick(0.1f, false);     // 40 chars/s

            Assert.That(Time.timeScale, Is.EqualTo(0f));
            Assert.That(_director.VisibleCharacters, Is.InRange(3, 4));
            Assert.That(lastCount, Is.EqualTo(_director.VisibleCharacters));
        }

        [Test]
        public void Talk_PressRevealsThenAdvances_AndTheNpcHearsEveryStep()
        {
            var started = new List<int>();
            var revealed = new List<int>();
            _npc.LineStarted += (_, i) => started.Add(i);
            _npc.LineRevealed += (_, i) => revealed.Add(i);
            StartAndOpen();

            _director.Tick(0.01f, true);
            Assert.That(_director.IsLineRevealed, Is.True);
            Assert.That(_director.LineIndex, Is.EqualTo(0));

            _director.Tick(0.01f, true);
            Assert.That(_director.LineIndex, Is.EqualTo(1));
            Assert.That(started, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(revealed, Is.EqualTo(new[] { 0 }));
        }

        // -------------------------------------------------------------------- end

        [Test]
        public void End_FollowsTheFixedOrder_AndRestoresThePreviousTimeScale()
        {
            Time.timeScale = 0.6f;
            StartAndOpen();
            ReadToTheEnd();
            _log.Clear();

            _director.Tick(Close * 0.5f, false);
            Assert.That(Time.timeScale, Is.EqualTo(0f), "still stopped while the camera blends back");
            Assert.That(_npc.IsInConversation, Is.True);

            _director.Tick(Close, false);
            Assert.That(_log, Is.EqualTo(new[] { "npc ended ts=0 Completed", "ended ts=0.6 npc=False" }));
            Assert.That(Time.timeScale, Is.EqualTo(0.6f), "the previous value, not 1");
            Assert.That(_director.State, Is.EqualTo(DialogueDirector.Phase.Releasing));
            Assert.That(_gate.IsBlocked(InputBlock.Movement), Is.True, "input waits one more frame");

            _director.Tick(0.01f, false);
            Assert.That(_director.State, Is.EqualTo(DialogueDirector.Phase.Idle));
            Assert.That(_gate.Blocked, Is.EqualTo(InputBlock.None));
            Assert.That(_director.IsCoolingDown, Is.True);
            Assert.That(_director.CanStartConversation, Is.False);

            _director.Tick(Cooldown + 0.01f, false);
            Assert.That(_director.CanStartConversation, Is.True);
        }

        [Test]
        public void End_EventsFireOncePerConversation()
        {
            int starting = 0, started = 0, ending = 0, ended = 0;
            _director.ConversationStarting += _ => starting++;
            _director.ConversationStarted += _ => started++;
            _director.ConversationEnding += _ => ending++;
            _director.ConversationEnded += _ => ended++;

            StartAndOpen();
            ReadToTheEnd();
            for (int i = 0; i < 10; i++) _director.Tick(0.1f, true);

            Assert.That((starting, started, ending, ended), Is.EqualTo((1, 1, 1, 1)));
        }

        // ----------------------------------------------------------------- aborts

        [Test]
        public void Abort_NpcDisabledMidTalk_UnwindsNextTick()
        {
            Time.timeScale = 0.9f;
            StartAndOpen();

            _npc.gameObject.SetActive(false);
            _director.Tick(0.01f, false);

            Assert.That(Time.timeScale, Is.EqualTo(0.9f));
            Assert.That(_npc.IsInConversation, Is.False);
            Assert.That(_npc.LastEndReason, Is.EqualTo(ConversationEndReason.Aborted));
            Assert.That(_director.State, Is.EqualTo(DialogueDirector.Phase.Releasing));
            _director.Tick(0.01f, false);
            Assert.That(_gate.Blocked, Is.EqualTo(InputBlock.None));
        }

        [Test]
        public void Abort_NpcKilledMidTalk_EndsAsAborted()
        {
            var health = _npc.gameObject.AddComponent<Health>();
            health.ResetToFull();
            StartAndOpen();

            health.Damage(health.Max * 10f, Vector3.zero);
            _director.Tick(0.01f, false);

            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(_npc.LastEndReason, Is.EqualTo(ConversationEndReason.Aborted));
            Assert.That(_director.Current, Is.Null);
        }

        [Test]
        public void Abort_NpcDestroyedMidTalk_UnwindsWithoutErrors()
        {
            StartAndOpen();
            Object.DestroyImmediate(_npc.gameObject);

            _director.Tick(0.01f, false);
            _director.Tick(0.01f, false);

            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(_director.State, Is.EqualTo(DialogueDirector.Phase.Idle));
            Assert.That(_gate.Blocked, Is.EqualTo(InputBlock.None));
        }

        [Test]
        public void Abort_DuringSettle_NeverPauses_AndNeverNotifiesTheNpc()
        {
            int npcEnded = 0;
            _npc.ConversationEnded += _ => npcEnded++;
            _director.TryStart(_npc, _player);

            _npc.enabled = false;
            _director.Tick(0.01f, false);

            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(npcEnded, Is.EqualTo(0));
            Assert.That(_log, Does.Contain("ended ts=1 npc=False"));
        }

        [Test]
        public void Abort_TheDisablePath_RestoresTimeAndInputInTheSameCall()
        {
            Time.timeScale = 0.7f;
            StartAndOpen();

            _director.Abort();

            Assert.That(Time.timeScale, Is.EqualTo(0.7f));
            Assert.That(_gate.Blocked, Is.EqualTo(InputBlock.None));
            Assert.That(_director.State, Is.EqualTo(DialogueDirector.Phase.Idle));
            Assert.That(_npc.LastEndReason, Is.EqualTo(ConversationEndReason.Aborted));

            _director.Abort();   // idle: harmless
            Assert.That(Time.timeScale, Is.EqualTo(0.7f));
        }

        [Test]
        public void Abort_WhileClosing_StillRestoresEverything()
        {
            StartAndOpen();
            ReadToTheEnd();
            _director.Abort();

            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(_npc.LastEndReason, Is.EqualTo(ConversationEndReason.Aborted));
            Assert.That(_gate.Blocked, Is.EqualTo(InputBlock.None));
        }

        // ------------------------------------------------- exception safety

        [Test]
        public void End_ThrowingListeners_CannotLeaveTheGameFrozenOrBlocked()
        {
            Time.timeScale = 0.8f;
            bool laterListenerRan = false;
            _director.ConversationEnding += _ => throw new System.InvalidOperationException("boom ending");
            _director.ConversationEnded += _ => throw new System.InvalidOperationException("boom ended");
            _director.ConversationEnded += _ => laterListenerRan = true;
            LogAssert.Expect(LogType.Exception, new Regex("boom ending"));
            LogAssert.Expect(LogType.Exception, new Regex("boom ended"));

            StartAndOpen();
            ReadToTheEnd();
            _director.Tick(Close + 0.01f, false);

            Assert.That(Time.timeScale, Is.EqualTo(0.8f), "time restored");
            Assert.That(laterListenerRan, Is.True, "one broken listener does not stop the others");
            Assert.That(_npc.IsInConversation, Is.False);
            Assert.That(_director.Current, Is.Null);

            _director.Tick(0.01f, false);
            Assert.That(_director.State, Is.EqualTo(DialogueDirector.Phase.Idle));
            Assert.That(_gate.Blocked, Is.EqualTo(InputBlock.None), "input released");
        }

        [Test]
        public void OnDisable_ThrowingListeners_StillUnwindEverything()
        {
            Time.timeScale = 0.9f;
            _director.ConversationEnding += _ => throw new System.InvalidOperationException("boom ending");
            _director.ConversationEnded += _ => throw new System.InvalidOperationException("boom ended");
            _npc.ConversationEnded += _ => throw new System.InvalidOperationException("boom npc");
            LogAssert.Expect(LogType.Exception, new Regex("boom ending"));
            LogAssert.Expect(LogType.Exception, new Regex("boom npc"));
            LogAssert.Expect(LogType.Exception, new Regex("boom ended"));
            StartAndOpen();

            Lifecycle(_director, "OnDisable");   // what a script reload runs first

            Assert.That(Time.timeScale, Is.EqualTo(0.9f));
            Assert.That(_director.State, Is.EqualTo(DialogueDirector.Phase.Idle));
            Assert.That(_gate.Blocked, Is.EqualTo(InputBlock.None));
            Assert.That(_npc.IsInConversation, Is.False);
            Assert.That(_npc.LastEndReason, Is.EqualTo(ConversationEndReason.Aborted));
        }

        [Test]
        public void Start_AThrowingStartListener_DoesNotStopTheConversation()
        {
            _director.ConversationStarting += _ => throw new System.InvalidOperationException("boom starting");
            _director.ConversationStarted += _ => throw new System.InvalidOperationException("boom started");
            LogAssert.Expect(LogType.Exception, new Regex("boom starting"));
            LogAssert.Expect(LogType.Exception, new Regex("boom started"));

            StartAndOpen();

            Assert.That(_npc.IsInConversation, Is.True);
            Assert.That(_director.LineIndex, Is.EqualTo(0));
        }

        // ----------------------------------------------------- two directors

        [Test]
        public void Settle_TimeStoppedBySomeoneElse_AbortsInsteadOfRecordingZero()
        {
            int npcStarted = 0;
            _npc.ConversationStarted += _ => npcStarted++;
            _director.TryStart(_npc, _player);

            Time.timeScale = 0f;                       // a second director, a menu
            _director.Tick(Settle + 0.01f, false);

            Assert.That(_director.State, Is.EqualTo(DialogueDirector.Phase.Releasing));
            Assert.That(npcStarted, Is.EqualTo(0));
            Assert.That(Time.timeScale, Is.EqualTo(0f), "their pause is left alone");
            _director.Tick(0.01f, false);
            Assert.That(_gate.Blocked, Is.EqualTo(InputBlock.None));

            Time.timeScale = 1f;                       // they let go
            _director.Tick(0.5f, false);
            _director.Abort();
            Assert.That(Time.timeScale, Is.EqualTo(1f), "nothing of ours was waiting to restore 0");
        }
    }
}
