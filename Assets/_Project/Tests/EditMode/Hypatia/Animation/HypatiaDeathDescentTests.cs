using System.Reflection;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Boss.Hypatia;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Animation
{
    /// <summary>
    /// She must not play her death 2.6 m in the air: once defeated the brain carries
    /// her body down to the plate, stops her bob and stops her turning, on her clock.
    /// </summary>
    public class HypatiaDeathDescentTests
    {
        GameObject _root, _player;
        HypatiaConfig _config;
        HypatiaBoss _boss;
        Health _health;
        Transform _body;
        CapsuleCollider _hurtbox;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<HypatiaConfig>();
            _config.hideFlags = HideFlags.DontSave;
            var config = new SerializedObject(_config);
            config.FindProperty("hoverHeight").floatValue = 2.6f;
            config.FindProperty("hoverSettleRate").floatValue = 4f;
            config.FindProperty("toppleHoverHeight").floatValue = 0f;
            config.FindProperty("defeatedHoverHeight").floatValue = 0.05f;
            config.FindProperty("firstConicDelay").floatValue = 1000f;
            config.ApplyModifiedPropertiesWithoutUndo();

            _root = new GameObject("Boss") { hideFlags = HideFlags.DontSave };
            _body = new GameObject("Body").transform;
            _body.SetParent(_root.transform, false);
            _body.localPosition = new Vector3(0f, 2.6f, 0f);
            _hurtbox = _body.gameObject.AddComponent<CapsuleCollider>();
            _player = new GameObject("Player") { hideFlags = HideFlags.DontSave };
            _player.transform.position = new Vector3(6f, 0f, 0f);

            _boss = _root.AddComponent<HypatiaBoss>();
            _health = _root.GetComponent<Health>();
            var health = new SerializedObject(_health);
            health.FindProperty("maxHealth").floatValue = 100f;
            health.ApplyModifiedPropertiesWithoutUndo();
            _health.ResetToFull();

            var boss = new SerializedObject(_boss);
            boss.FindProperty("config").objectReferenceValue = _config;
            boss.FindProperty("arenaCentre").objectReferenceValue = _root.transform;
            boss.FindProperty("player").objectReferenceValue = _player.transform;
            boss.FindProperty("body").objectReferenceValue = _body;
            boss.ApplyModifiedPropertiesWithoutUndo();

            Call("OnEnable");
            Call("Start");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_player);
            Object.DestroyImmediate(_config);
        }

        void Call(string method)
        {
            MethodInfo m = typeof(HypatiaBoss).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(m, Is.Not.Null, $"HypatiaBoss.{method}() not found; it was probably renamed.");
            m.Invoke(_boss, null);
        }

        void Step(float seconds, float dt)
        {
            for (float t = 0f; t < seconds; t += dt) _boss.Tick(dt);
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void Defeated_SheComesDownToThePlate(float dt)
        {
            Step(1f, dt);
            Assert.That(_body.localPosition.y, Is.GreaterThan(2f), "hovering before the killing blow");

            _health.Damage(1000f, Vector3.zero);
            Assert.IsTrue(_boss.IsDefeated);
            Step(3f, dt);

            Assert.That(_body.localPosition.y, Is.EqualTo(0.05f).Within(0.01f));
        }

        [Test]
        public void Defeated_SheNeverGoesBelowThePlate_AndDoesNotBob()
        {
            _health.Damage(1000f, Vector3.zero);
            Step(3f, 1f / 60f);
            float settled = _body.localPosition.y;
            for (int i = 0; i < 240; i++)
            {
                _boss.Tick(1f / 60f);
                Assert.That(_body.localPosition.y, Is.GreaterThanOrEqualTo(0.04f));
                Assert.That(_body.localPosition.y, Is.EqualTo(settled).Within(0.01f));
            }
        }

        [Test]
        public void ItsHurtbox_IsOnWhileSheFights_AndOffOnceDefeated()
        {
            Step(1f, 1f / 60f);
            Assert.IsTrue(_hurtbox.enabled, "still there to hit while she lives");

            _health.Damage(1000f, Vector3.zero);

            Assert.IsFalse(_hurtbox.enabled, "else it stands as an invisible pillar where she was");
        }

        [Test]
        public void ItsHurtbox_StaysOff_AfterAScriptReload()
        {
            _health.Damage(1000f, Vector3.zero);
            _hurtbox.enabled = true;
            Call("OnDisable");
            Call("OnEnable");
            Assert.IsFalse(_hurtbox.enabled);
        }

        [Test]
        public void TheBrainStillFights_WithNoHurtboxWired()
        {
            Object.DestroyImmediate(_hurtbox);
            Call("OnDisable");
            Call("OnEnable");
            Assert.DoesNotThrow(() => _health.Damage(1000f, Vector3.zero));
            Assert.IsTrue(_boss.IsDefeated);
        }

        [Test]
        public void Defeated_SheStopsTurningToThePlayer()
        {
            _health.Damage(1000f, Vector3.zero);
            Quaternion facing = _root.transform.rotation;
            _player.transform.position = new Vector3(-6f, 0f, -6f);
            Step(2f, 1f / 60f);
            Assert.That(Quaternion.Angle(_root.transform.rotation, facing), Is.EqualTo(0f).Within(0.01f));
        }
    }
}
