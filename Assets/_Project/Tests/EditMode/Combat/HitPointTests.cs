using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Combat;
using UnityEngine;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// The finisher ring used to call Physics.ClosestPoint on every collider it
    /// overlapped, level meshes included, and Unity warns (and returns a junk
    /// point) for anything but a box, sphere, capsule or convex mesh — about
    /// 1,300 warnings in one TrainingStage session. HitPoint keeps the exact
    /// answer where Unity supports it and falls back to bounds elsewhere.
    /// </summary>
    public class HitPointTests
    {
        readonly List<Object> _made = new();

        [TearDown]
        public void DestroyMade()
        {
            foreach (var o in _made) if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
        }

        GameObject Make(string name, Vector3 at)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.position = at;
            _made.Add(go);
            return go;
        }

        static Mesh CubeMesh()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(tmp);
            return mesh;
        }

        MeshCollider MeshColliderAt(Vector3 at, bool convex)
        {
            var mc = Make("Mesh", at).AddComponent<MeshCollider>();
            mc.sharedMesh = CubeMesh();
            mc.convex = convex;
            return mc;
        }

        [Test]
        public void SupportsClosestPoint_OnlyForTheShapesUnityHandles()
        {
            Assert.That(HitPoint.SupportsClosestPoint(Make("Box", Vector3.zero).AddComponent<BoxCollider>()), Is.True);
            Assert.That(HitPoint.SupportsClosestPoint(Make("Sphere", Vector3.zero).AddComponent<SphereCollider>()), Is.True);
            Assert.That(HitPoint.SupportsClosestPoint(Make("Capsule", Vector3.zero).AddComponent<CapsuleCollider>()), Is.True);
            Assert.That(HitPoint.SupportsClosestPoint(MeshColliderAt(Vector3.zero, convex: true)), Is.True);
            Assert.That(HitPoint.SupportsClosestPoint(MeshColliderAt(Vector3.zero, convex: false)), Is.False,
                        "a level's non-convex mesh is exactly what flooded the console");
            Assert.That(HitPoint.SupportsClosestPoint(null), Is.False);
        }

        [Test]
        public void Nearest_OnABox_IsExact()
        {
            var box = Make("Box", Vector3.zero).AddComponent<BoxCollider>();
            Physics.SyncTransforms();
            Vector3 p = HitPoint.Nearest(box, new Vector3(5f, 0.2f, 0f));
            Assert.That(p.x, Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(p.y, Is.EqualTo(0.2f).Within(1e-3f));
            Assert.That(p.z, Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void Nearest_OnANonConvexMesh_UsesBoundsAndLogsNothing()
        {
            var mesh = MeshColliderAt(new Vector3(2f, 0f, 0f), convex: false);
            Physics.SyncTransforms();
            var from = new Vector3(10f, 0f, 0f);

            int warnings = 0;
            void Count(string message, string stack, LogType type)
            {
                if (message.Contains("ClosestPoint")) warnings++;
            }
            Application.logMessageReceived += Count;
            Vector3 p;
            try { p = HitPoint.Nearest(mesh, from); }
            finally { Application.logMessageReceived -= Count; }

            Assert.That(warnings, Is.EqualTo(0));
            Assert.That(p, Is.EqualTo(mesh.bounds.ClosestPoint(from)));
        }
    }
}
