using NUnit.Framework;
using Pickleball.Sandbox.Movement.Logic;
using Pickleball.Sandbox.Movement.Presentation;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Movement
{
    /// <summary>Which foot a skid's sparks come from, and that the player actually
    /// carries the effect.</summary>
    public class SideFlipSparksTests
    {
        const float Outset = 0.1f;
        static readonly Vector3 Ahead = Vector3.forward;

        static Vector3 Edge(Vector3 left, Vector3 right, Vector3 travel, float outset = Outset)
            => FurtherFootEdge.Find(left, left, right, right, travel, outset);

        [Test]
        public void PicksTheFootFurtherAlongTravel_WhicheverSideItIsOn()
        {
            Vector3 left = new(-0.1f, 0f, 0.4f), right = new(0.1f, 0f, -0.3f);
            Assert.That(Edge(left, right, Ahead).x, Is.EqualTo(left.x), "left foot leading");
            Assert.That(Edge(right, left, Ahead).x, Is.EqualTo(left.x), "right foot leading");
        }

        [Test]
        public void ReversingTravel_PicksTheOtherFoot()
        {
            Vector3 left = new(-0.1f, 0f, 0.4f), right = new(0.1f, 0f, -0.3f);
            Assert.That(Edge(left, right, Ahead).x, Is.EqualTo(left.x));
            Assert.That(Edge(left, right, -Ahead).x, Is.EqualTo(right.x));
        }

        [Test]
        public void TheEdgeIsPastTheFoot_ByTheOutset_AlongTravel()
        {
            Vector3 edge = Edge(new Vector3(0f, 0f, 0.4f), new Vector3(0f, 0f, -0.3f), Ahead);
            Assert.That(edge.z, Is.EqualTo(0.4f + Outset).Within(1e-5f));
            Assert.That(edge.x, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void ToesCountTowardReach_AndSetTheEdge()
        {
            // The right ankle is behind the left, but its toes reach past it.
            Vector3 leftFoot = new(-0.1f, 0f, 0.2f), rightFoot = new(0.1f, 0f, 0.1f);
            Vector3 leftToes = leftFoot + Ahead * 0.05f, rightToes = rightFoot + Ahead * 0.3f;
            Vector3 edge = FurtherFootEdge.Find(leftFoot, leftToes, rightFoot, rightToes, Ahead, 0f);
            Assert.That(edge, Is.EqualTo(rightToes));
        }

        [Test]
        public void TiltedOrUnnormalisedTravel_IsTreatedAsPlanar()
        {
            Vector3 left = new(0f, 0f, 0.4f), right = new(0f, 0f, -0.3f);
            Vector3 edge = Edge(left, right, new Vector3(0f, 5f, 3f));
            Assert.That(edge.z, Is.EqualTo(0.4f + Outset).Within(1e-5f), "no outset gained from scale");
            Assert.That(edge.y, Is.EqualTo(0f).Within(1e-5f), "no height gained from the tilt");
        }

        [Test]
        public void HeightDoesNotDecideTheFoot()
        {
            // A raised foot behind him must not win over the planted one ahead.
            Vector3 planted = new(-0.1f, 0f, 0.4f), raised = new(0.1f, 0.6f, -0.3f);
            Assert.That(Edge(planted, raised, Ahead).x, Is.EqualTo(planted.x));
        }

        [Test]
        public void NoTravel_FallsBackToARealFoot_WithNoOutset()
        {
            Vector3 right = new(0.1f, 0f, -0.3f);
            Assert.That(Edge(new Vector3(-0.1f, 0f, 0.4f), right, Vector3.zero), Is.EqualTo(right));
        }

        [Test, Category("Assets")]
        public void ChibiPrefab_CarriesWiredSparks()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Chibi.prefab");
            Assert.That(prefab, Is.Not.Null);
            var sparks = prefab.GetComponentInChildren<SideFlipSparks>(true);
            Assert.That(sparks, Is.Not.Null, "Run Tools > Pickleball > Setup Side Flip Sparks.");

            var so = new SerializedObject(sparks);
            foreach (string field in new[] { "motor", "config", "animator" })
                Assert.That(so.FindProperty(field).objectReferenceValue, Is.Not.Null, field);

            var system = sparks.GetComponent<ParticleSystem>();
            Assert.That(system.emission.rateOverTime.constant, Is.EqualTo(0f),
                        "emitted from code, so the window alone decides when sparks appear");
            Assert.That(system.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World),
                        "sparks must stay where they were thrown, not follow him");
            Assert.That(sparks.GetComponent<ParticleSystemRenderer>().sharedMaterial, Is.Not.Null);
        }
    }
}
