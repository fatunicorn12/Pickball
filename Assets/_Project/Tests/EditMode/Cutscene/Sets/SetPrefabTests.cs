using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Pickleball.Cutscene;
using Pickleball.Cutscene.Editor.Sets;
using Pickleball.Cutscene.Sets;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Sets
{
    /// <summary>
    /// The set prefabs built by Tools ▸ Pickleball ▸ Cutscene ▸ 2 Build Sets exist, sit at the
    /// origin, and list every marker the shot list needs, exactly once.
    /// </summary>
    public class SetPrefabTests
    {
        private static GameObject Load(string setId)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SetLayout.PrefabPath(setId));
            Assert.IsNotNull(prefab, $"{SetLayout.PrefabPath(setId)} is missing; run 2 Build Sets.");
            return prefab;
        }

        [Test]
        public void EverySetPrefabHasACutsceneSetRootAtTheOrigin()
        {
            foreach (string id in SetLayout.SetIds())
            {
                GameObject prefab = Load(id);
                var set = prefab.GetComponent<CutsceneSet>();
                Assert.IsNotNull(set, $"{id} has no CutsceneSet on its root.");
                Assert.AreEqual(id, set.SetId);
                Assert.AreEqual(Vector3.zero, prefab.transform.position, $"{id}'s root must be at the origin.");
                Assert.AreEqual(Quaternion.identity, prefab.transform.rotation, $"{id}'s root must be unrotated.");
                Assert.AreEqual(Vector3.one, prefab.transform.localScale, $"{id}'s root must be unscaled.");
            }
        }

        [Test]
        public void EverySetListsItsRequiredMarkersWithUniqueNonNullIds()
        {
            foreach (string id in SetLayout.SetIds())
            {
                var set = Load(id).GetComponent<CutsceneSet>();
                var seen = new HashSet<string>();
                for (int i = 0; i < set.MarkerCount; i++)
                {
                    CutsceneMarker marker = set.GetMarker(i);
                    Assert.IsNotNull(marker, $"{id} marker {i} is null.");
                    Assert.IsFalse(string.IsNullOrEmpty(marker.Id), $"{id} marker {i} has no id.");
                    Assert.IsTrue(seen.Add(marker.Id), $"{id} lists {marker.Id} twice.");
                    Assert.AreEqual(marker.Id, marker.gameObject.name, "Marker objects are named after their id.");
                }
                foreach (string required in SetLayout.RequiredMarkers(id))
                    Assert.IsTrue(set.TryGetMarker(required, out _), $"{id} is missing marker {required}.");
                Assert.AreEqual(SetLayout.RequiredMarkers(id).Length, set.MarkerCount, $"{id} lists unexpected markers.");
            }
        }

        [Test]
        public void EveryMarkerIdIsProvidedByExactlyOneSet()
        {
            var owner = new Dictionary<string, string>();
            foreach (string id in SetLayout.SetIds())
            {
                var set = Load(id).GetComponent<CutsceneSet>();
                for (int i = 0; i < set.MarkerCount; i++)
                {
                    string marker = set.GetMarker(i).Id;
                    Assert.IsFalse(owner.ContainsKey(marker), $"{marker} is in both {owner.GetValueOrDefault(marker)} and {id}.");
                    owner[marker] = id;
                }
            }
            foreach (FieldInfo field in typeof(CutsceneMarkerIds).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!field.IsLiteral) continue;
                string marker = (string)field.GetRawConstantValue();
                Assert.IsTrue(owner.ContainsKey(marker), $"No set provides {marker}.");
            }
        }

        [Test]
        public void MarkersSitWhereTheLayoutSays()
        {
            var shrine = Load(SetLayout.OuterShrineId).GetComponent<CutsceneSet>();
            AssertMarker(shrine, CutsceneMarkerIds.MCStart, SetLayout.ShrineCentre);
            AssertMarker(shrine, CutsceneMarkerIds.PropellerLedge, SetLayout.PropellerLedge);
            AssertMarker(shrine, CutsceneMarkerIds.MCHandover, SetLayout.MCHandover);
            Assert.IsTrue(shrine.TryGetMarker(CutsceneMarkerIds.MCStart, out Transform mcStart));
            Assert.That(Vector3.Angle(mcStart.forward, Vector3.forward), Is.LessThan(0.01f), "MC_Start's head points +Z.");
            Assert.IsTrue(shrine.TryGetMarker(CutsceneMarkerIds.MCHandover, out Transform handover));
            Assert.That(Vector3.Angle(handover.forward, Vector3.right), Is.LessThan(0.01f), "MC_Handover faces +X.");

            var crest = Load(SetLayout.SouthCrestId).GetComponent<CutsceneSet>();
            Assert.IsTrue(crest.TryGetMarker(CutsceneMarkerIds.CrestView, out Transform view));
            Assert.That(Vector3.Angle(view.forward, Vector3.forward), Is.LessThan(0.01f), "Crest_View faces +Z.");

            var vista = Load(SetLayout.AcademyVistaId).GetComponent<CutsceneSet>();
            AssertMarker(vista, CutsceneMarkerIds.BlackSun, GreyboxReference.BlackSunCentre);
        }

        [Test]
        public void TheCameraTrackRunsEastOfTheWalkAtTheShotListOffsets()
        {
            var path = Load(SetLayout.OlivePathId).GetComponent<CutsceneSet>();
            Assert.IsTrue(path.TryGetMarker(CutsceneMarkerIds.PathTrackA, out Transform a));
            Assert.IsTrue(path.TryGetMarker(CutsceneMarkerIds.CamTrackS4Start, out Transform cam));
            Vector3 offset = cam.position - a.position;
            Assert.AreEqual(0.75f * SetLayout.H, offset.y, 1e-3f, "0.75·H above the path.");
            Assert.AreEqual(3.2f * SetLayout.H, new Vector2(offset.x, offset.z).magnitude, 1e-3f, "3.2·H to the side.");
            Assert.Greater(offset.x, 0f, "On the walkers' east side.");
            Assert.Greater(Vector3.Dot(cam.forward, -offset), 0f, "Looking back at the walkers.");
        }

        [Test]
        public void TheVoidPrefabCarriesTheLookAndTheShrineCarriesTheLines()
        {
            var look = Load(SetLayout.VoidId).GetComponent<CutsceneLook>();
            Assert.IsNotNull(look, "Void has no CutsceneLook.");
            var lines = Load(SetLayout.OuterShrineId).GetComponentInChildren<ConstructionLineResolve>(true);
            Assert.IsNotNull(lines, "OuterShrine has no ConstructionLineResolve.");
            Assert.Greater(lines.StrokeCount, 30);
            Assert.LessOrEqual(lines.StrokeCount, ConstructionLineResolve.MaxStrokes);
        }

        [Test]
        public void NoSetGeometryIsMarkedForBakedLighting()
        {
            foreach (string id in SetLayout.SetIds())
            {
                foreach (Transform t in Load(id).GetComponentsInChildren<Transform>(true))
                {
                    StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
                    Assert.IsFalse((flags & StaticEditorFlags.ContributeGI) != 0, $"{id}/{t.name} contributes to GI (baked lighting).");
                }
            }
        }

        private static void AssertMarker(CutsceneSet set, string id, Vector3 expected)
        {
            Assert.IsTrue(set.TryGetMarker(id, out Transform t), $"{id} missing.");
            Assert.That(Vector3.Distance(t.position, expected), Is.LessThan(1e-3f), $"{id} at {t.position}, expected {expected}.");
        }
    }
}
