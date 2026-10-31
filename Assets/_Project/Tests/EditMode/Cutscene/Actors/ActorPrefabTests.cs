using NUnit.Framework;
using Pickleball.Cutscene;
using Pickleball.Cutscene.Actors;
using Pickleball.Cutscene.Editor.Actors;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Actors
{
    /// <summary>The built actor prefabs carry a complete, measured slot (category Assets: needs menu 1's output).</summary>
    [Category("Assets")]
    public class ActorPrefabTests
    {
        private static CutsceneActor Load(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.NotNull(go, $"{path} missing: run 1 Build Actors");
            var actor = go.GetComponent<CutsceneActor>();
            Assert.NotNull(actor, $"{path} has no CutsceneActor");
            return actor;
        }

        private static void CommonChecks(CutsceneActor a, string id)
        {
            Assert.AreEqual(id, a.ActorId);
            foreach (var anchor in new[] { CutsceneAnchor.Head, CutsceneAnchor.Eyes, CutsceneAnchor.HandR, CutsceneAnchor.Feet })
            {
                Transform t = a.GetAnchor(anchor);
                Assert.NotNull(t, $"{a.name}: {anchor} not assigned");
                Assert.AreEqual(CutsceneAnchors.TransformName(anchor), t.name);
                Assert.IsTrue(t.IsChildOf(a.transform), $"{anchor} is outside the actor");
            }
            Assert.Greater(a.HeadBox, 0.05f);
            Assert.Greater(a.Height, a.HeadBox * 0.5f);
            Assert.NotNull(a.Voice, "no voice source");
            Assert.AreEqual(0f, a.Voice.spatialBlend, "cutscene voices are 2D");
            Assert.NotNull(a.GetComponent<MouthFlap>(), "no mouth flap");
            Assert.NotNull(a.GetComponentInChildren<Animator>().avatar, "no avatar");
            // Head above eyes' ground and feet on the root's ground.
            Assert.AreEqual(a.transform.position.y, a.GetAnchor(CutsceneAnchor.Feet).position.y, 1e-4f);
        }

        [Test]
        public void McActor_HasAFullSlot()
        {
            CutsceneActor a = Load(CutsceneActorPaths.McActorPrefab);
            CommonChecks(a, CutsceneSpeakers.MC);
            Assert.NotNull(a.GetAnchor(CutsceneAnchor.Blade), "MC needs a Blade anchor (at the hand on the standalone actor)");
            Assert.IsNull(a.GetAnchor(CutsceneAnchor.PropellerSocket));
            var swap = a.GetComponentInChildren<EyeSwap>();
            Assert.NotNull(swap);
            Assert.AreEqual("Closed", swap.VariantName(1));
            Assert.That(a.Height, Is.InRange(1.4f, 1.9f), "MC is about 1.6–1.7 m");
        }

        [Test]
        public void BirdyActor_HasAFullSlotWithPropeller()
        {
            CutsceneActor a = Load(CutsceneActorPaths.BirdyActorPrefab);
            CommonChecks(a, CutsceneSpeakers.Birdy);
            Transform socket = a.GetAnchor(CutsceneAnchor.PropellerSocket);
            Assert.NotNull(socket, "Birdy needs a PropellerSocket");
            Assert.Greater(socket.position.y, a.GetAnchor(CutsceneAnchor.Head).position.y, "the socket is on top of the helmet");
            var rotor = a.GetComponentInChildren<CutsceneRotor>();
            Assert.NotNull(rotor);
            Assert.AreEqual(ActorRigger.RotorBoneName, rotor.Rotor.name);
            Assert.AreEqual("Drop", a.GetComponentInChildren<EyeSwap>().VariantName(1));
            Assert.That(a.Height, Is.InRange(0.9f, 1.3f), "Birdy is about chin height on MC");
        }

        [Test]
        public void Propeller_HasASpinner()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(CutsceneActorPaths.PropellerPrefab);
            Assert.NotNull(go);
            var prop = go.GetComponent<PropellerProp>();
            Assert.NotNull(prop);
            Assert.NotNull(prop.Spinner);
            Assert.NotNull(prop.Config);
            Assert.NotNull(go.GetComponentInChildren<MeshRenderer>(), "no propeller mesh");
        }
    }
}
