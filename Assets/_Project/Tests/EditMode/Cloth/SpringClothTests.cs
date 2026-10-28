using NUnit.Framework;
using Pickleball.Sandbox.Cloth;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Cloth
{
    /// <summary>
    /// The spring-cloth solver's promises, on a bare four-joint chain with no
    /// Animator: segments never stretch, a joint never swings further than
    /// <c>maxAngle</c> off its animated direction, the cloth settles back to
    /// its pose, and hit-stop (dt = 0), long frames and teleports never produce
    /// a NaN or a whip.
    ///
    /// <see cref="SpringCloth.Simulate"/> is public, so the tests drive it with
    /// explicit frame times instead of relying on the Editor's clock.
    /// </summary>
    public class SpringClothTests
    {
        const int Joints = 4;
        const float Spacing = 0.2f;

        GameObject _root;
        SpringCloth _cloth;
        SpringClothConfig _config;
        Transform[] _joints;

        [SetUp]
        public void MakeChain()
        {
            _root = new GameObject("Test_ClothRoot") { hideFlags = HideFlags.DontSave };
            _config = ScriptableObject.CreateInstance<SpringClothConfig>();
            _config.hideFlags = HideFlags.DontSave;

            // A skirt hanging straight down from the root.
            _joints = new Transform[Joints];
            Transform parent = _root.transform;
            for (int i = 0; i < Joints; i++)
            {
                var joint = new GameObject($"Joint{i}").transform;
                joint.SetParent(parent, false);
                joint.localPosition = i == 0 ? Vector3.zero : Vector3.down * Spacing;
                _joints[i] = joint;
                parent = joint;
            }

            _cloth = _root.AddComponent<SpringCloth>();
            var so = new SerializedObject(_cloth);
            so.FindProperty("config").objectReferenceValue = _config;
            so.FindProperty("characterHeight").floatValue = 1f;
            var roots = so.FindProperty("chainRoots");
            roots.arraySize = 1;
            roots.GetArrayElementAtIndex(0).objectReferenceValue = _joints[0];
            so.ApplyModifiedPropertiesWithoutUndo();

            _cloth.Rebind();
            _cloth.Simulate(1f / 60f);      // the first step snaps to the pose
        }

        [TearDown]
        public void DestroyChain()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            if (_config != null) Object.DestroyImmediate(_config);
        }

        /// <summary>Shake the root side to side for <paramref name="frames"/>
        /// frames, staying well under the teleport distance.</summary>
        void Shake(int frames, float dt = 1f / 60f, float stride = 0.08f)
        {
            for (int f = 0; f < frames; f++)
            {
                _root.transform.position += (f % 2 == 0 ? Vector3.right : Vector3.left) * stride;
                _cloth.Simulate(dt);
            }
        }

        void AssertFinite()
        {
            foreach (var joint in _joints)
            {
                var q = joint.localRotation;
                Assert.That(float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w),
                            Is.False, $"{joint.name} has a NaN rotation.");
                Assert.That(joint.position.x, Is.Not.NaN);
            }
        }

        [Test]
        public void Shaking_NeverStretchesASegment()
        {
            Shake(40);

            for (int i = 1; i < Joints; i++)
                Assert.That(Vector3.Distance(_joints[i - 1].position, _joints[i].position),
                            Is.EqualTo(Spacing).Within(1e-4f), $"Segment {i} stretched.");
        }

        [TestCase(30f)]
        [TestCase(10f)]
        public void Shaking_NeverSwingsASegmentPastMaxAngle(float maxAngle)
        {
            _config.maxAngle = maxAngle;
            _config.gravity = new Vector3(0f, -30f, 0f);
            _config.stiffnessRoot = _config.stiffnessTip = 0f;   // nothing pulling it back: worst case

            for (int f = 0; f < 60; f++)
            {
                _root.transform.position += Vector3.right * 0.15f;
                _cloth.Simulate(1f / 60f);

                for (int i = 1; i < Joints; i++)
                {
                    Vector3 segment = _joints[i].position - _joints[i - 1].position;
                    Assert.That(Vector3.Angle(Vector3.down, segment), Is.LessThanOrEqualTo(maxAngle + 0.05f),
                                $"Segment {i} swung past {maxAngle}° on frame {f}.");
                }
            }
        }

        [Test]
        public void WithoutGravity_SettlesBackToItsPose()
        {
            _config.gravity = Vector3.zero;
            Shake(30);

            for (int f = 0; f < 600; f++) _cloth.Simulate(1f / 60f);

            for (int i = 0; i < Joints; i++)
                Assert.That(Quaternion.Angle(_joints[i].localRotation, Quaternion.identity),
                            Is.LessThan(0.5f), $"{_joints[i].name} did not settle.");
        }

        [Test]
        public void ZeroDeltaTime_HoldsThePose()
        {
            Shake(15);
            var before = new Quaternion[Joints];
            for (int i = 0; i < Joints; i++) before[i] = _joints[i].localRotation;

            _root.transform.position += Vector3.right * 0.1f;
            _cloth.Simulate(0f);

            for (int i = 0; i < Joints; i++)
                Assert.That(Quaternion.Angle(_joints[i].localRotation, before[i]), Is.LessThan(1e-3f),
                            "Hit-stop should freeze the cloth where it is.");
        }

        [TestCase(0.5f)]
        [TestCase(5f)]
        public void LongFrame_StaysFinite(float dt)
        {
            Shake(10);
            _cloth.Simulate(dt);
            Shake(10);

            AssertFinite();
        }

        [Test]
        public void Teleport_SnapsToThePoseInsteadOfWhipping()
        {
            Shake(15);

            _root.transform.position += Vector3.forward * (_config.teleportDistance * 2f);
            _cloth.Simulate(1f / 60f);

            for (int i = 0; i < Joints; i++)
                Assert.That(Quaternion.Angle(_joints[i].localRotation, Quaternion.identity), Is.LessThan(1e-3f),
                            $"{_joints[i].name} swung after a teleport.");
        }
    }
}
