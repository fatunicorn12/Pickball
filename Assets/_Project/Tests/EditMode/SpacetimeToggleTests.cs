using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Pickleball.Sandbox;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests
{
    /// <summary>
    /// Pins down the on/off switch. Spacetime ships switched off because it has
    /// not been play-tested, and the promise is that off means <i>inert</i>: no
    /// input binding, no recording, no flip, no beams. These tests are what lets
    /// the feature sit in the main branch without anyone having to trust that.
    /// They also check the other direction, that switching on gives a working
    /// flip and switching off mid-4D drops cleanly back to 3D.
    ///
    /// Components are driven through their public per-frame bodies
    /// (<see cref="SpacetimeView.Tick"/>, <see cref="WorldlineRecorder.Sample"/>),
    /// because EditMode runs no lifecycle callbacks.
    /// </summary>
    public class SpacetimeToggleTests
    {
        readonly List<Object> _spawned = new();

        [TearDown]
        public void DestroySpawned()
        {
            foreach (var o in _spawned)
                if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
        }

        SpacetimeSettings NewSettings(bool on)
        {
            var settings = ScriptableObject.CreateInstance<SpacetimeSettings>();
            settings.hideFlags = HideFlags.DontSave;
            settings.SetEnabled(on);
            _spawned.Add(settings);
            return settings;
        }

        /// <summary>A component on a throwaway object that is never saved into the
        /// open scene, wired to <paramref name="settings"/> the way the setup menus
        /// wire it.</summary>
        T NewWired<T>(SpacetimeSettings settings) where T : Component
        {
            var host = new GameObject($"Test_{typeof(T).Name}") { hideFlags = HideFlags.DontSave };
            _spawned.Add(host);
            var component = host.AddComponent<T>();
            var so = new SerializedObject(component);
            so.FindProperty("settings").objectReferenceValue = settings;
            so.ApplyModifiedPropertiesWithoutUndo();
            return component;
        }

        /// <summary>A recorder with a two-second straight line in it, registered on
        /// the channel as OnEnable would register it.</summary>
        WorldlineRecorder RecorderWithHistory(SpacetimeSettings settings)
        {
            var recorder = NewWired<WorldlineRecorder>(settings);
            for (int i = 0; i <= 20; i++) recorder.Sample(new Vector3(0f, 0f, i * 0.2f), i * 0.1f);
            settings.Live.Add(recorder);
            return recorder;
        }

        [Test]
        public void NewSettings_AreOffByDefault()
        {
            var settings = ScriptableObject.CreateInstance<SpacetimeSettings>();
            _spawned.Add(settings);
            Assert.That(settings.Enabled, Is.False);
        }

        [Test]
        public void Recorder_Off_RecordsNothing()
        {
            var recorder = NewWired<WorldlineRecorder>(NewSettings(on: false));

            for (int i = 0; i < 10; i++)
                Assert.That(recorder.Sample(new Vector3(i, 0f, 0f), i * 0.5f), Is.False);

            Assert.That(recorder.History, Is.Null);
        }

        [Test]
        public void Recorder_WithNoSettings_RecordsNothing()
        {
            var recorder = NewWired<WorldlineRecorder>(null);

            Assert.That(recorder.Sample(Vector3.one, 1f), Is.False);
            Assert.That(recorder.History, Is.Null);
        }

        [Test]
        public void Recorder_On_Records()
        {
            var recorder = NewWired<WorldlineRecorder>(NewSettings(on: true));

            Assert.That(recorder.Sample(Vector3.zero, 0f), Is.True);
            Assert.That(recorder.Sample(Vector3.right, 0.5f), Is.True);
            Assert.That(recorder.History.Count, Is.EqualTo(2));
        }

        [Test]
        public void Recorder_SwitchedOff_DropsItsHistory()
        {
            // Turning the feature back on later must not draw a stale line.
            var settings = NewSettings(on: true);
            var recorder = RecorderWithHistory(settings);

            settings.SetEnabled(false);
            recorder.Sample(Vector3.zero, 3f);

            Assert.That(recorder.History, Is.Null);
        }

        [Test]
        public void View_Off_RegistersNoInputAndNeverFlips()
        {
            var settings = NewSettings(on: false);
            RecorderWithHistory(settings);
            var view = NewWired<SpacetimeView>(settings);

            for (int i = 0; i < 5; i++) view.Tick(togglePressed: true, now: 3f + i, deltaTime: 0.1f);

            Assert.That(view.InputRegistered, Is.False);
            Assert.That(view.In4D, Is.False);
            Assert.That(settings.In4D, Is.False);
            Assert.That(view.BeamCount, Is.Zero);
        }

        [Test]
        public void View_WithNoSettings_IsInert()
        {
            var view = NewWired<SpacetimeView>(null);

            view.Tick(togglePressed: true, now: 1f, deltaTime: 0.1f);
            view.Enter();

            Assert.That(view.InputRegistered, Is.False);
            Assert.That(view.In4D, Is.False);
            Assert.That(view.BeamCount, Is.Zero);
        }

        [Test]
        public void View_On_FlipsAndBuildsBeams()
        {
            var settings = NewSettings(on: true);
            RecorderWithHistory(settings);
            var view = NewWired<SpacetimeView>(settings);

            view.Tick(togglePressed: true, now: 2f, deltaTime: 0.1f);

            Assert.That(view.InputRegistered, Is.True);
            Assert.That(settings.In4D, Is.True);
            Assert.That(view.BeamCount, Is.EqualTo(1));

            // Switch off through the view, so the input action it enabled is
            // disposed. EditMode runs no OnDisable to do it on teardown.
            settings.SetEnabled(false);
            view.Tick(togglePressed: false, now: 2.1f, deltaTime: 0.1f);
        }

        [Test]
        public void Charge01_InRange_WhenOffAndBeforeFirstTick()
        {
            // ST1: before the first tick the raw ratio was −1/maxDuration, and a
            // HUD that read it drew a negative meter.
            var off = NewWired<SpacetimeView>(NewSettings(on: false));
            var on = NewWired<SpacetimeView>(NewSettings(on: true));
            var unwired = NewWired<SpacetimeView>(null);

            foreach (var view in new[] { off, on, unwired })
            {
                Assert.That(view.Charge01, Is.InRange(0f, 1f));
                Assert.That(view.Charge01, Is.EqualTo(1f), "the meter starts full");
            }
        }

        [Test]
        public void Charge01_StaysInRange_WhenMaxDurationIsLoweredMidPlay()
        {
            var settings = NewSettings(on: true);
            var view = NewWired<SpacetimeView>(settings);
            view.Tick(togglePressed: false, now: 1f, deltaTime: 0.1f);   // fills the meter

            var so = new SerializedObject(settings);
            so.FindProperty("maxDuration").floatValue = settings.MaxDuration * 0.25f;
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(view.Charge01, Is.InRange(0f, 1f));
        }

        [Test]
        public void Update_FlipsOnALatchedPress_ExactlyOnce()
        {
            // ST6: the press is latched from InputAction.performed and consumed by
            // the next Update, rather than polled with WasPressedThisFrame.
            var settings = NewSettings(on: true);
            RecorderWithHistory(settings);
            var view = NewWired<SpacetimeView>(settings);
            view.Tick(togglePressed: false, now: 2f, deltaTime: 0.1f);   // registers input
            Assert.That(view.InputRegistered, Is.True, "precondition: binding registered");

            SetLatch(view, true);
            RunUpdate(view);
            Assert.That(settings.In4D, Is.True, "the latched press flipped into 4D");
            Assert.That(GetLatch(view), Is.False, "the press was consumed");

            RunUpdate(view);
            Assert.That(settings.In4D, Is.True, "no second flip without a second press");

            settings.SetEnabled(false);
            view.Tick(togglePressed: false, now: 3f, deltaTime: 0.1f);
        }

        [Test]
        public void SwitchingOff_DropsALatchedPress()
        {
            // A press latched just before the switch went off must not flip the
            // world the moment it comes back on.
            var settings = NewSettings(on: true);
            var view = NewWired<SpacetimeView>(settings);
            view.Tick(togglePressed: false, now: 1f, deltaTime: 0.1f);
            SetLatch(view, true);

            settings.SetEnabled(false);
            view.Tick(togglePressed: false, now: 1.1f, deltaTime: 0.1f);

            Assert.That(GetLatch(view), Is.False);
        }

        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        static void SetLatch(SpacetimeView view, bool value) =>
            LatchField().SetValue(view, value);

        static bool GetLatch(SpacetimeView view) => (bool)LatchField().GetValue(view);

        static FieldInfo LatchField()
        {
            var field = typeof(SpacetimeView).GetField("_togglePressed", Private);
            Assert.That(field, Is.Not.Null, "SpacetimeView._togglePressed was renamed; update the test.");
            return field;
        }

        /// <summary>One frame of the view's private Update, which EditMode never
        /// calls on its own.</summary>
        static void RunUpdate(SpacetimeView view)
        {
            var update = typeof(SpacetimeView).GetMethod("Update", Private);
            Assert.That(update, Is.Not.Null, "SpacetimeView.Update was renamed; update the test.");
            update.Invoke(view, null);
        }

        [Test]
        public void View_SwitchedOffIn4D_DropsBackAndReleasesInput()
        {
            var settings = NewSettings(on: true);
            RecorderWithHistory(settings);
            var view = NewWired<SpacetimeView>(settings);
            view.Tick(togglePressed: true, now: 2f, deltaTime: 0.1f);
            Assert.That(settings.In4D, Is.True, "precondition: flipped into 4D");

            settings.SetEnabled(false);
            view.Tick(togglePressed: false, now: 2.1f, deltaTime: 0.1f);

            Assert.That(settings.In4D, Is.False);
            Assert.That(view.BeamCount, Is.Zero);
            Assert.That(view.InputRegistered, Is.False);
        }
    }
}
