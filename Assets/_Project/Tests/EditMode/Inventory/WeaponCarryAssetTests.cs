using System.Linq;
using NUnit.Framework;
using Pickleball.Sandbox.Inventory;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Pickleball.Tests.Inventory
{
    /// <summary>
    /// The carry pose contract between the weapon assets, the shipped controller and
    /// WeaponCarryPose: the ruler carries, the compass doesn't, and every carry pose has
    /// its state on a right-arm-only Carry layer that sits under Gesture and Attack.
    /// Built by Tools ▸ Pickleball ▸ Setup Weapon Carry.
    /// </summary>
    [Category("Assets")]
    public class WeaponCarryAssetTests
    {
        const string ControllerPath = "Assets/_Project/Animation/Controllers/ChibiLocomotion.controller";
        const string WeaponsDir = "Assets/_Project/ScriptableObjects/Inventory/Weapons/";
        const string Fix = "Run Tools ▸ Pickleball ▸ Setup Weapon Carry.";

        static WeaponDefinition Weapon(string name)
        {
            var w = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponsDir + name + ".asset");
            Assert.That(w, Is.Not.Null, $"{name} is missing.");
            return w;
        }

        static AnimatorControllerLayer CarryLayer(out AnimatorController controller, out int index)
        {
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.That(controller, Is.Not.Null, $"{ControllerPath} is missing.");
            var layers = controller.layers;
            index = System.Array.FindIndex(layers, l => l.name == "Carry");
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "No Carry layer. " + Fix);
            return layers[index];
        }

        [Test]
        public void Ruler_HasAStandingAndAMovingCarry()
        {
            var ruler = Weapon("Weapon_Ruler");
            Assert.That(ruler.HasCarryPose, Is.True, "The ruler has no carry pose. " + Fix);
            Assert.That(ruler.CarryPoseMoving, Is.Not.Null, "The ruler has no moving carry pose. " + Fix);
            Assert.That(ruler.CarryPose.humanMotion && ruler.CarryPoseMoving.humanMotion, Is.True,
                        "Carry poses must be Humanoid clips to play on the masked layer.");
            Assert.That(ruler.CarryWeight, Is.GreaterThan(0f));
        }

        [Test]
        public void Compass_KeepsTheClipsArm()
        {
            Assert.That(Weapon("Weapon_Compass").HasCarryPose, Is.False,
                        "The compass must look exactly as before: no carry pose.");
        }

        [Test]
        public void CarryLayer_IsRightArmOnly_Override_StartsHidden()
        {
            var layer = CarryLayer(out _, out _);
            Assert.That(layer.blendingMode, Is.EqualTo(AnimatorLayerBlendingMode.Override));
            Assert.That(layer.defaultWeight, Is.EqualTo(0f), "WeaponCarryPose fades it in.");
            Assert.That(layer.iKPass, Is.False);
            var mask = layer.avatarMask;
            Assert.That(mask, Is.Not.Null, "The Carry layer has no mask: it would pose the whole body.");
            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                Assert.That(mask.GetHumanoidBodyPartActive(part), Is.EqualTo(part == AvatarMaskBodyPart.RightArm),
                            $"Mask part {part}.");
        }

        [Test]
        public void CarryLayer_SitsUnderGestureAndTheAttackLayers()
        {
            CarryLayer(out var controller, out int carry);
            var names = controller.layers.Select(l => l.name).ToList();
            foreach (var above in new[] { "Gesture", "Attack" })
                if (names.Contains(above))
                    Assert.That(carry, Is.LessThan(names.IndexOf(above)), $"Carry must be under {above}, which overrides it.");
            if (names.Contains("AttackBody"))
                Assert.That(carry, Is.GreaterThan(names.IndexOf("AttackBody")),
                            "Carry must be above the full-body AttackBody layer, or AttackBody's arm would replace it.");
        }

        [Test]
        public void EveryCarryPose_HasItsState()
        {
            var layer = CarryLayer(out _, out _);
            var states = layer.stateMachine.states.Select(s => s.state).ToDictionary(s => s.name);
            foreach (var guid in AssetDatabase.FindAssets("t:WeaponDefinition", new[] { "Assets/_Project/ScriptableObjects/Inventory" }))
            {
                var w = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (w == null || w.CarryPose == null) continue;
                Assert.That(states.TryGetValue(w.CarryPose.name, out var state), Is.True,
                            $"{w.name}: no Carry state '{w.CarryPose.name}'. " + Fix);
                if (w.CarryPoseMoving == null) Assert.That(state.motion, Is.EqualTo(w.CarryPose));
                else
                {
                    Assert.That(state.motion, Is.InstanceOf<BlendTree>(), $"{w.name}: the moving pose needs a blend tree.");
                    var tree = (BlendTree)state.motion;
                    Assert.That(tree.blendParameter, Is.EqualTo("Speed"));
                    Assert.That(tree.children.Select(c => c.motion), Is.EqualTo(new Motion[] { w.CarryPose, w.CarryPoseMoving }));
                    Assert.That(tree.children[0].threshold, Is.LessThan(tree.children[1].threshold));
                }
            }
        }

        [Test]
        public void ConfigureCarry_ClampsAndReports()
        {
            var w = ScriptableObject.CreateInstance<WeaponDefinition>();
            var clip = new AnimationClip { name = "Carry_Test" };
            try
            {
                Assert.That(w.HasCarryPose, Is.False, "a fresh weapon has no carry");
                w.ConfigureCarry(clip, null, 1.5f, 5f);
                Assert.That(w.HasCarryPose, Is.True);
                Assert.That(w.CarryWeight, Is.EqualTo(1f));
                Assert.That(w.CarryBlendSeconds, Is.EqualTo(1f));
                w.ConfigureCarry(clip, null, 0f, 0.25f);
                Assert.That(w.HasCarryPose, Is.False, "a zero weight shows nothing");
            }
            finally
            {
                Object.DestroyImmediate(w);
                Object.DestroyImmediate(clip);
            }
        }
    }
}
