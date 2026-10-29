using System;
using System.Reflection;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Boss.Hypatia;
using Pickleball.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Pickleball.Tests.UI
{
    /// <summary>
    /// BarkView reads any <see cref="IBarkSource"/> through a MonoBehaviour field
    /// (still named <c>source</c>, so Newton's serialized wiring survives).
    ///
    /// The bark source here is a real runtime one (HypatiaBarks): a fake
    /// MonoBehaviour declared in this Editor-only test assembly cannot be added
    /// to a GameObject. The test raises its <see cref="IBarkSource.Spoke"/> event
    /// directly with an arbitrary line, which is what a fake would do. EditMode
    /// never runs OnEnable/OnDisable, so they are called by reflection.
    /// </summary>
    public class BarkViewTests
    {
        GameObject _host, _sourceGo;

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            if (_sourceGo != null) Object.DestroyImmediate(_sourceGo);
        }

        (BarkView view, TMP_Text text) MakeView(MonoBehaviour source)
        {
            _host = new GameObject("BarkViewTest") { hideFlags = HideFlags.DontSave };
            var text = _host.AddComponent<TextMeshPro>();
            var view = _host.AddComponent<BarkView>();
            var so = new SerializedObject(view);
            so.FindProperty("source").objectReferenceValue = source;
            so.FindProperty("text").objectReferenceValue = text;
            so.ApplyModifiedPropertiesWithoutUndo();
            return (view, text);
        }

        T AddSource<T>() where T : MonoBehaviour
        {
            _sourceGo = new GameObject("Source") { hideFlags = HideFlags.DontSave };
            return _sourceGo.AddComponent<T>();
        }

        static void Call(Object target, string method) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
                  .Invoke(target, null);

        /// <summary>The compiler-generated delegate behind a field-like event.</summary>
        static Action<string, float> SpokeDelegate(HypatiaBarks barks) =>
            (Action<string, float>)typeof(HypatiaBarks)
                .GetField(nameof(HypatiaBarks.Spoke), BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(barks);

        [Test]
        public void RaisingSpoke_ShowsTheLine_AndDisableUnsubscribes()
        {
            var barks = AddSource<HypatiaBarks>();
            var (view, text) = MakeView(barks);

            Call(view, "OnEnable");
            var spoke = SpokeDelegate(barks);
            Assert.That(spoke, Is.Not.Null, "BarkView subscribed to Spoke through IBarkSource");
            Assert.That(spoke.GetInvocationList().Length, Is.EqualTo(1));

            spoke("e is the shape of the sky.", 2f);
            Assert.That(text.text, Is.EqualTo("e is the shape of the sky."));

            Call(view, "OnDisable");
            Assert.That(SpokeDelegate(barks), Is.Null, "BarkView must unsubscribe in OnDisable");
        }

        [Test]
        public void HypatiaBarks_Intro_ReachesTheView()
        {
            var barks = AddSource<HypatiaBarks>();
            var (view, text) = MakeView(barks);
            Call(view, "OnEnable");
            barks.Say(HypatiaBarks.Occasion.Intro);   // Intro always speaks
            Assert.That(text.text, Is.Not.Empty);
        }

        [Test]
        public void NonBarkSource_WarnsAndDoesNotThrow()
        {
            var notASource = AddSource<FaceCamera>();   // a runtime MonoBehaviour that is no IBarkSource
            var (view, _) = MakeView(notASource);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not an IBarkSource"));
            Assert.DoesNotThrow(() => Call(view, "OnEnable"));
            Assert.DoesNotThrow(() => Call(view, "OnDisable"));
        }
    }
}
