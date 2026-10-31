using NUnit.Framework;
using Pickleball.Cutscene.Actors;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Actors
{
    /// <summary>The runtime presentation components through their Tick / Apply seams.</summary>
    public class ActorComponentTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void MouthFlap_NodsWithLevel_AndNeverAccumulates()
        {
            _root = new GameObject("Actor");
            var head = new GameObject("Head").transform;
            head.SetParent(_root.transform, false);
            head.localRotation = Quaternion.Euler(0f, 10f, 0f);
            Quaternion rest = head.localRotation;
            var config = ScriptableObject.CreateInstance<MouthFlapConfig>();
            var flap = _root.AddComponent<MouthFlap>();
            flap.Configure(config, null, head, _root.transform);

            flap.Apply(0f, 1f / 60f);
            Assert.AreEqual(0f, flap.Openness);
            Assert.That(Quaternion.Angle(rest, head.localRotation), Is.LessThan(1e-3f), "silence leaves the head alone");

            for (int i = 0; i < 10; i++) flap.Apply(0.5f, 1f / 60f); // nobody re-poses the bone: must not accumulate
            float angle = Quaternion.Angle(rest, head.localRotation);
            Assert.That(angle, Is.InRange(config.MaxAngle * 0.9f, config.MaxAngle + 1e-3f));

            for (int i = 0; i < 120; i++) flap.Apply(0f, 1f / 60f);
            Assert.That(Quaternion.Angle(rest, head.localRotation), Is.LessThan(0.05f), "back at rest after the line");
            Object.DestroyImmediate(config);
        }

        [Test]
        public void CutsceneRotor_DetachHidesAndAttachSpins()
        {
            _root = new GameObject("Birdy");
            var rotor = new GameObject("Rotor").transform;
            rotor.SetParent(_root.transform, false);
            var r = _root.AddComponent<CutsceneRotor>();
            r.Configure(null, rotor);
            r.SetSpin(900f);
            r.Tick(0.1f);
            Assert.AreEqual(Vector3.one, rotor.localScale);
            Assert.Greater(Quaternion.Angle(Quaternion.identity, rotor.localRotation), 1f, "the rotor turned");
            r.SetAttached(false);
            r.Tick(0.1f);
            Assert.AreEqual(Vector3.zero, rotor.localScale, "detached collapses the skinned propeller");
            r.SetAttached(true);
            r.Tick(0.1f);
            Assert.AreEqual(Vector3.one, rotor.localScale);
        }

        [Test]
        public void PropellerProp_SeatsOnASocketAndWindsDown()
        {
            _root = new GameObject("Propeller");
            var spin = new GameObject("Spin").transform;
            spin.SetParent(_root.transform, false);
            var prop = _root.AddComponent<PropellerProp>();
            prop.Configure(null, spin);
            var socket = new GameObject("PropellerSocket").transform;
            socket.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(10f, 20f, 30f));
            prop.AttachTo(socket);
            Assert.AreEqual(socket, _root.transform.parent);
            Assert.That(Vector3.Distance(socket.position, _root.transform.position), Is.LessThan(1e-5f));

            prop.SpinTo(1000f);
            for (int i = 0; i < 120; i++) prop.Tick(1f / 60f);
            Assert.Greater(prop.CurrentSpin, 900f);
            prop.SpinDown();
            for (int i = 0; i < 200; i++) prop.Tick(1f / 60f);
            Assert.AreEqual(0f, prop.CurrentSpin);
            prop.Detach();
            Assert.IsNull(_root.transform.parent);
            Object.DestroyImmediate(socket.gameObject);
        }
    }
}
