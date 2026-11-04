using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia;
using Pickleball.Sandbox.Boss.Hypatia.Presentation;
using Action = Pickleball.Sandbox.Boss.Hypatia.Presentation.HypatiaAnimationDirector.Action;
using ClipPlan = Pickleball.Sandbox.Boss.Hypatia.Presentation.HypatiaAnimationDirector.ClipPlan;
using ClipTiming = Pickleball.Sandbox.Boss.Hypatia.Presentation.HypatiaAnimationDirector.ClipTiming;

namespace Pickleball.Tests.Hypatia.Animation
{
    /// <summary>
    /// Which clip she shows when attacks overlap (a ring forecast during a conic is
    /// guaranteed), and that each clip's release pose lands on the gameplay release.
    /// </summary>
    public class HypatiaAnimationDirectorTests
    {
        HypatiaAnimationDirector _director;
        List<Action> _started, _finished;

        static readonly ClipPlan Now = new(0f, 0f, 1f, 1f, 0f);

        [SetUp]
        public void SetUp()
        {
            _director = new HypatiaAnimationDirector();
            _started = new List<Action>();
            _finished = new List<Action>();
            _director.Started += (action, _) => _started.Add(action);
            _director.Finished += action => _finished.Add(action);
        }

        static ClipPlan Later(float delay) => new(delay, 0f, 1f, 1f, delay + 0.2f);

        // ─── Priority ───────────────────────────────────────────────────────

        [Test]
        public void Priority_IsDeathToppleTwoBurningPulseConicRings()
        {
            Assert.That(HypatiaAnimationDirector.Priority(Action.Death), Is.GreaterThan(HypatiaAnimationDirector.Priority(Action.Topple)));
            Assert.That(HypatiaAnimationDirector.Priority(Action.Topple), Is.GreaterThan(HypatiaAnimationDirector.Priority(Action.PhaseTwo)));
            Assert.That(HypatiaAnimationDirector.Priority(Action.PhaseTwo), Is.GreaterThan(HypatiaAnimationDirector.Priority(Action.Burning)));
            Assert.That(HypatiaAnimationDirector.Priority(Action.Burning), Is.GreaterThan(HypatiaAnimationDirector.Priority(Action.Pulse)));
            Assert.That(HypatiaAnimationDirector.Priority(Action.Pulse), Is.GreaterThan(HypatiaAnimationDirector.Priority(Action.ConicEllipse)));
            Assert.That(HypatiaAnimationDirector.Priority(Action.ConicEllipse), Is.EqualTo(HypatiaAnimationDirector.Priority(Action.ConicHyperbola)));
            Assert.That(HypatiaAnimationDirector.Priority(Action.ConicParabola), Is.GreaterThan(HypatiaAnimationDirector.Priority(Action.Rings)));
            Assert.That(HypatiaAnimationDirector.Priority(Action.Rings), Is.GreaterThan(HypatiaAnimationDirector.Priority(Action.None)));
        }

        [Test]
        public void ARing_DuringAConic_IsSkipped_NotQueued()
        {
            Assert.IsTrue(_director.Request(Action.ConicEllipse, Now));
            Assert.IsFalse(_director.Request(Action.Rings, Now));
            _director.Tick(5f);
            Assert.That(_started, Is.EqualTo(new[] { Action.ConicEllipse }), "the ring never plays later");
            Assert.That(_director.Active, Is.EqualTo(Action.None));
        }

        [Test]
        public void AHigherPriorityClip_ReplacesALowerOne_WithoutARestBetween()
        {
            _director.Request(Action.Rings, Now);
            Assert.IsTrue(_director.Request(Action.Pulse, Now));
            Assert.That(_director.Active, Is.EqualTo(Action.Pulse));
            Assert.That(_finished, Is.Empty);
        }

        [Test]
        public void AHigherPriorityClip_IsNeverRestarted_OrReplacedByALowerOne()
        {
            _director.Request(Action.PhaseTwo, Now);
            Assert.IsFalse(_director.Request(Action.PhaseTwo, Now), "not restarted");
            Assert.IsFalse(_director.Request(Action.Pulse, Now));
            Assert.IsFalse(_director.Request(Action.Rings, Now));
            Assert.That(_started, Is.EqualTo(new[] { Action.PhaseTwo }));
        }

        [Test]
        public void TheConics_ShareARank_SoOneNeverReplacesAnother()
        {
            _director.Request(Action.ConicParabola, Now);
            Assert.IsFalse(_director.Request(Action.ConicHyperbola, Now));
        }

        [Test]
        public void AFinishedClip_ReturnsToRest_AndAnythingCanThenPlay()
        {
            _director.Request(Action.Pulse, Now);
            _director.Tick(1.1f);
            Assert.That(_finished, Is.EqualTo(new[] { Action.Pulse }));
            Assert.IsTrue(_director.Request(Action.Rings, Now));
        }

        // ─── A clip waiting for its start time ──────────────────────────────

        [Test]
        public void ADelayedClip_StartsAtItsTime_AndReplacesALowerClip_OnlyThen()
        {
            _director.Request(Action.Rings, new ClipPlan(0f, 0f, 1f, 5f, 0f));
            Assert.IsTrue(_director.Request(Action.ConicHyperbola, Later(1f)));
            Assert.That(_director.Active, Is.EqualTo(Action.Rings), "the ring plays on until the conic is due");
            _director.Tick(0.9f);
            Assert.That(_director.Active, Is.EqualTo(Action.Rings));
            _director.Tick(0.2f);
            Assert.That(_director.Active, Is.EqualTo(Action.ConicHyperbola));
            Assert.That(_finished, Is.Empty);
        }

        [Test]
        public void ARing_UnderAPendingConic_PlaysOnlyIfItsReleaseComesFirst()
        {
            _director.Request(Action.ConicHyperbola, Later(1.75f));
            Assert.IsTrue(_director.Request(Action.Rings, new ClipPlan(0f, 0f, 0.87f, 1.8f, 1f)), "released at 1 s, before the conic");

            var other = new HypatiaAnimationDirector();
            other.Request(Action.ConicHyperbola, Later(0.5f));
            Assert.IsFalse(other.Request(Action.Rings, new ClipPlan(0f, 0f, 0.87f, 1.8f, 1f)), "it would be cut off before its release");
        }

        [Test]
        public void APendingClip_WhoseMomentWasTaken_IsDropped()
        {
            _director.Request(Action.ConicHyperbola, Later(0.5f));
            _director.Request(Action.Pulse, new ClipPlan(0f, 0f, 1f, 2f, 0.8f));
            _director.Tick(1f);
            Assert.That(_director.Active, Is.EqualTo(Action.Pulse));
            _director.Tick(5f);
            Assert.That(_started, Is.EqualTo(new[] { Action.Pulse }), "the conic did not play late");
        }

        [Test]
        public void ACancelledPulse_StopsPlaying_AndAPendingOneIsForgotten()
        {
            _director.Request(Action.Pulse, Now);
            _director.Cancel(Action.Pulse);
            Assert.That(_finished, Is.EqualTo(new[] { Action.Pulse }));
            Assert.That(_director.Active, Is.EqualTo(Action.None));

            _director.Request(Action.Pulse, Later(0.5f));
            _director.Cancel(Action.Pulse);
            _director.Tick(5f);
            Assert.That(_started, Is.EqualTo(new[] { Action.Pulse }), "only the first one ever played");
        }

        [Test]
        public void CancellingWhatIsNotPlaying_DoesNothing()
        {
            _director.Request(Action.Rings, Now);
            _director.Cancel(Action.Pulse);
            Assert.That(_director.Active, Is.EqualTo(Action.Rings));
            Assert.That(_finished, Is.Empty);
        }

        // ─── Topple and death ───────────────────────────────────────────────

        [Test]
        public void ATopple_InterruptsEverything_IncludingWhatWasWaiting()
        {
            _director.Request(Action.PhaseTwo, new ClipPlan(0f, 0f, 1f, 5f, 0f));
            _director.Request(Action.ConicEllipse, Later(9f));
            _director.BeginTopple();
            Assert.That(_director.Active, Is.EqualTo(Action.Topple));
            Assert.That(_director.Pending, Is.EqualTo(Action.None));
            _director.Tick(20f);
            Assert.That(_director.Active, Is.EqualTo(Action.Topple), "held until she recovers, whatever the clock says");
        }

        [Test]
        public void WhileToppled_NothingElsePlays_AndRecoveringFreesHer()
        {
            _director.BeginTopple();
            Assert.IsFalse(_director.Request(Action.Pulse, Now));
            Assert.IsFalse(_director.Request(Action.Rings, Now));
            _director.EndTopple();
            Assert.That(_finished, Is.EqualTo(new[] { Action.Topple }));
            Assert.That(_started, Is.EqualTo(new[] { Action.Topple }), "a refused attack does not play on recovery");
            Assert.IsTrue(_director.Request(Action.Rings, Now));
        }

        [Test]
        public void APhaseTwo_RefusedByATopple_PlaysOnRecovery()
        {
            _director.BeginTopple();
            Assert.IsFalse(_director.Request(Action.PhaseTwo, Now));
            _director.Tick(5f);
            Assert.That(_started, Is.EqualTo(new[] { Action.Topple }));
            _director.EndTopple();
            Assert.That(_started, Is.EqualTo(new[] { Action.Topple, Action.PhaseTwo }));
            Assert.That(_director.Active, Is.EqualTo(Action.PhaseTwo));
        }

        [Test]
        public void Death_InterruptsEverything_IsTerminal_AndHolds()
        {
            _director.Request(Action.Pulse, Now);
            _director.BeginDeath(Now);
            Assert.That(_director.Active, Is.EqualTo(Action.Death));
            Assert.IsFalse(_director.Request(Action.PhaseTwo, Now));
            _director.BeginTopple();
            Assert.That(_director.Active, Is.EqualTo(Action.Death), "a topple cannot interrupt it");
            _director.BeginDeath(Now);
            _director.Tick(100f);
            Assert.That(_director.Active, Is.EqualTo(Action.Death), "the last pose holds");
            Assert.That(_started, Is.EqualTo(new[] { Action.Pulse, Action.Death }), "started once");
            Assert.That(_finished, Is.Empty);
        }

        [Test]
        public void HerDeath_DuringATopple_GoesStraightToDeath_NotAHeldBackPhaseTwo()
        {
            _director.BeginTopple();
            _director.Request(Action.PhaseTwo, Now);
            // The brain's order: StopFighting ends the topple, then Defeated.
            _director.EndTopple();
            _director.BeginDeath(Now);
            Assert.That(_director.Active, Is.EqualTo(Action.Death));
            _director.Tick(100f);
            Assert.That(_director.Active, Is.EqualTo(Action.Death));
        }

        // ─── Her clock ──────────────────────────────────────────────────────

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        public void AFrozenClock_HoldsBothCountdowns(float dt)
        {
            _director.Request(Action.Rings, Now);
            _director.Request(Action.ConicEllipse, Later(0.5f));
            for (int i = 0; i < 100; i++) _director.Tick(dt);
            Assert.That(_director.Active, Is.EqualTo(Action.Rings));
            Assert.That(_director.Pending, Is.EqualTo(Action.ConicEllipse));
            Assert.That(_finished, Is.Empty);
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void ADelayedClip_StartsOnTime_AtAnyFrameRate(float dt)
        {
            _director.Request(Action.ConicHyperbola, new ClipPlan(1.75f, 0f, 1f, 0.73f, 2f));
            float clock = 0f;
            while (_director.Active == Action.None && clock < 5f)
            {
                _director.Tick(dt);
                clock += dt;
            }
            Assert.That(clock, Is.EqualTo(1.75f).Within(dt + 1e-3f));
        }

        // ─── Fitting a clip to a lead ───────────────────────────────────────

        /// <summary>Where the plan puts the release pose, from the moment of asking.</summary>
        static float ReleaseFromNow(in ClipTiming clip, in ClipPlan plan) =>
            plan.Delay + (clip.Release - plan.Offset) / plan.Speed;

        [TestCase(1f, 0.87f, 1f)]      // a ring: within limits
        [TestCase(2.67f, 1.2f, 2f)]    // the burn
        [TestCase(1.33f, 0.6f, 1.2f)]  // the ellipse
        public void ALeadWithinTheSpeedLimits_IsMetByChangingSpeed(float length, float release, float lead)
        {
            var clip = new ClipTiming(length, release, 0.5f, 1.5f);
            ClipPlan plan = HypatiaAnimationDirector.Plan(clip, lead);
            Assert.That(plan.Delay, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(plan.Offset, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(ReleaseFromNow(clip, plan), Is.EqualTo(lead).Within(1e-4f));
            Assert.That(plan.ReleaseAt, Is.EqualTo(lead).Within(1e-4f));
        }

        [Test]
        public void ATooLongLead_WaitsFirst_RatherThanSlowingASharpMotion()
        {
            var clip = new ClipTiming(0.73f, 0.25f, 1f, 1.3f);
            ClipPlan plan = HypatiaAnimationDirector.Plan(clip, 2f);
            Assert.That(plan.Speed, Is.EqualTo(1f));
            Assert.That(plan.Delay, Is.EqualTo(1.75f).Within(1e-4f));
            Assert.That(ReleaseFromNow(clip, plan), Is.EqualTo(2f).Within(1e-4f));
            Assert.That(plan.Duration, Is.EqualTo(0.73f).Within(1e-4f));
        }

        [Test]
        public void ATooShortLead_SkipsTheAnticipation_AtFullSpeed()
        {
            var clip = new ClipTiming(1.83f, 1f, 0.9f, 1.5f);
            ClipPlan plan = HypatiaAnimationDirector.Plan(clip, 0.4f);
            Assert.That(plan.Speed, Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(plan.Delay, Is.EqualTo(0f));
            Assert.That(plan.Offset, Is.EqualTo(0.4f).Within(1e-4f));
            Assert.That(ReleaseFromNow(clip, plan), Is.EqualTo(0.4f).Within(1e-4f));
            Assert.That(plan.Duration, Is.EqualTo((1.83f - 0.4f) / 1.5f).Within(1e-4f));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        public void NoLeadAtAll_StartsOnTheReleasePose(float lead)
        {
            ClipPlan plan = HypatiaAnimationDirector.Plan(new ClipTiming(1.5f, 0.5f), lead);
            Assert.That(plan.Offset, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(plan.Delay, Is.EqualTo(0f));
            Assert.That(plan.Duration, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void AClipWithNoRelease_JustPlays_WhateverTheLead()
        {
            ClipPlan plan = HypatiaAnimationDirector.Plan(new ClipTiming(2.3f, 0f), 5f);
            Assert.That(plan.Delay, Is.EqualTo(0f));
            Assert.That(plan.Offset, Is.EqualTo(0f));
            Assert.That(plan.Speed, Is.EqualTo(1f));
            Assert.That(plan.Duration, Is.EqualTo(2.3f).Within(1e-4f));
        }

        [Test]
        public void AReleasePastTheEnd_IsClampedToTheClip()
        {
            ClipPlan plan = HypatiaAnimationDirector.Plan(new ClipTiming(1f, 3f), 1f);
            Assert.That(plan.Speed, Is.EqualTo(1f));
            Assert.That(plan.Offset, Is.EqualTo(0f));
            Assert.That(plan.Duration, Is.EqualTo(1f).Within(1e-4f));
        }

        // ─── When a conic actually leaves her hand ──────────────────────────

        [Test]
        public void AConicsRelease_FollowsTheCastersOwnWaits()
        {
            Assert.That(Lead(ConicKind.Ellipse, 1.2f, 0.8f, 1.4f, 1f), Is.EqualTo(1.2f).Within(1e-4f), "first orbiter at the volley's start");
            Assert.That(Lead(ConicKind.Parabola, 1.2f, 0.8f, 1.4f, 1f), Is.EqualTo(1.2f).Within(1e-4f), "flight covers the marker lead");
            Assert.That(Lead(ConicKind.Parabola, 1.2f, 0.8f, 0.5f, 1f), Is.EqualTo(1.7f).Within(1e-4f), "the launch waits so the marker leads");
            Assert.That(Lead(ConicKind.Hyperbola, 1.2f, 0.8f, 1.4f, 1f), Is.EqualTo(2f).Within(1e-4f), "the asymptotes come after the needle");
        }

        static float Lead(ConicKind kind, float needle, float asymptote, float flight, float marker) =>
            HypatiaAnimationDirector.ConicReleaseLead(kind, needle, asymptote, flight, marker);
    }
}
