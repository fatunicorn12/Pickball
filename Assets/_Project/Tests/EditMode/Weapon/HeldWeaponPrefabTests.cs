using System.IO;
using NUnit.Framework;
using Pickleball.Sandbox.Hands;
using Pickleball.Sandbox.Weapon;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Validation
{
    /// <summary>
    /// The shipped held-weapon prefabs are what an equipper can rely on: a
    /// <see cref="HeldItem"/> at the grip frame with both blade markers, the
    /// compass exactly where it has always sat in the hand, and the ruler's
    /// steel edge on the anchor's +Z side (checked against the texture, not
    /// against the builder's own answer).
    ///
    /// Read-only (AssetDatabase loads, a texture decoded from its file).
    /// </summary>
    [Category("Assets")]
    public class HeldWeaponPrefabTests
    {
        const string CompassPath = "Assets/_Project/Prefabs/Weapons/Held_Compass.prefab";
        const string RulerPath = "Assets/_Project/Prefabs/Weapons/Held_Ruler.prefab";
        const string RulerTexture = "Assets/_Project/Art/Props/ruler-sword/ruler-sword_basecolor.jpg";
        const string RulerClearance = "Assets/_Project/ScriptableObjects/Weapon/RulerWeaponClearance.asset";

        static HeldItem Load(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(go, $"{path} is missing: run Tools ▸ Pickleball ▸ Build Held Weapons.");
            var item = go.GetComponent<HeldItem>();
            Assert.IsNotNull(item, $"{path} has no HeldItem on its root.");
            return item;
        }

        [TestCase(CompassPath)]
        [TestCase(RulerPath)]
        public void HeldPrefab_IsAGripFrameWithABlade(string path)
        {
            var item = Load(path);

            Assert.IsTrue(item.HasBlade, "both blade markers set");
            Assert.AreNotSame(item.transform, item.GripAnchor, "has its own GripAnchor child");
            Assert.Less(item.GripAnchor.localPosition.magnitude, 1e-5f, "the root is the grip frame");
            Assert.Less(Quaternion.Angle(Quaternion.identity, item.GripAnchor.localRotation), 0.01f);
            Assert.Less(item.BladeBase.localPosition.magnitude, 1e-5f, "BladeBase is the centre of the fist");
            Vector3 tip = item.BladeTip.localPosition;
            Assert.Greater(tip.y, 0.5f, "BladeTip runs up +Y");
            Assert.Less(new Vector2(tip.x, tip.z).magnitude, 1e-4f, "BladeTip is on the handle's axis");
        }

        [Test]
        public void HeldCompass_SitsWhereTheCompassAlwaysHas()
        {
            // The compass's pose under the grab point in every saved scene since
            // the measured grip (Setup Weapon Grip), and its tip marker.
            var item = Load(CompassPath);
            var model = item.transform.Find("Model");
            Assert.IsNotNull(model);

            Assert.Less((model.localPosition - new Vector3(0f, 0.782f, 0f)).magnitude, 1e-4f);
            Assert.Less(Quaternion.Angle(model.localRotation, new Quaternion(-0.7071068f, 0f, -0.7071068f, 0f)), 0.01f);
            Assert.AreEqual(0.85166f, model.localScale.x, 1e-4f);
            Assert.AreEqual(0.782f, item.BladeTip.localPosition.y, 1e-3f);
            Assert.AreEqual(0f, item.GripRadius, "the hand's own grip size");
        }

        [Test]
        public void HeldRuler_IsBiggerThanTheCompass_WithAMeasuredHandle()
        {
            var ruler = Load(RulerPath);
            var compass = Load(CompassPath);

            Assert.Greater(ruler.BladeTip.localPosition.y, compass.BladeTip.localPosition.y * 1.1f);
            Assert.That(ruler.GripRadius, Is.InRange(0.015f, 0.025f),
                        "slimmed handle, a little fatter than the hand's 0.015");
        }

        [Test]
        public void HeldRuler_LeadsWithItsSteelEdge()
        {
            var item = Load(RulerPath);
            var filter = item.GetComponentInChildren<MeshFilter>();
            Assert.IsNotNull(filter);
            var mesh = filter.sharedMesh;
            var tex = new Texture2D(2, 2);
            try
            {
                Assert.IsTrue(tex.LoadImage(File.ReadAllBytes(RulerTexture)));
                // Mesh into the prefab root (the grip frame): +Z is the leading edge.
                Matrix4x4 toRoot = item.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var verts = mesh.vertices;
                var uvs = mesh.uv;
                float lead = 0f, trail = 0f;
                int leadN = 0, trailN = 0;
                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 p = toRoot.MultiplyPoint3x4(verts[i]);
                    if (p.y < 0.3f || p.y > 0.8f) continue;            // the blade's middle only
                    float s = BladeEdge.Steeliness(tex.GetPixelBilinear(uvs[i].x, uvs[i].y));
                    if (s < 0f) continue;
                    if (p.z > 0.08f) { lead += s; leadN++; }
                    else if (p.z < -0.08f) { trail += s; trailN++; }
                }
                Assert.Greater(leadN, 0);
                Assert.Greater(trailN, 0);
                Assert.Greater(lead / leadN, trail / trailN + 0.2f,
                               "the +Z (knuckle) side of the blade is the grey steel one");
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void RulerClearance_PadsForAWideBlade()
        {
            var ruler = AssetDatabase.LoadAssetAtPath<WeaponClearanceProfile>(RulerClearance);
            var chibi = AssetDatabase.LoadAssetAtPath<WeaponClearanceProfile>(
                "Assets/_Project/ScriptableObjects/Weapon/ChibiWeaponClearance.asset");
            Assert.IsNotNull(ruler);
            Assert.IsNotNull(chibi);

            Assert.Greater(ruler.BladeRadius, chibi.BladeRadius);
            Assert.AreEqual(chibi.Volumes[0].bone, ruler.Volumes[0].bone, "same head sphere first");
            Assert.AreEqual(chibi.Volumes[0].radius, ruler.Volumes[0].radius, 1e-6f);
        }
    }
}
