using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Animation
{
    /// <summary>
    /// Read-only checks of what she ships with: every clip the view plays exists on
    /// the right layer, is Humanoid, does not loop and is baked in place (the brain
    /// owns her position), and the upper-body mask leaves root and legs alone.
    /// </summary>
    [Category("Assets")]
    public class HypatiaClipAssetsTests
    {
        const string ControllerPath = "Assets/_Project/Animation/Hypatia/Hypatia.controller";
        const string ClipDir = "Assets/_Project/Animation/Hypatia/Clips";

        static readonly string[] UpperStates = { "Rings", "ConicEllipse", "ConicParabola", "ConicHyperbola", "Pulse", "Burning", "PhaseTwo" };
        static readonly string[] BaseStates = { "Death" };

        static string[] AllStates => UpperStates.Concat(BaseStates).ToArray();

        static AnimatorController Controller()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.That(controller, Is.Not.Null, $"{ControllerPath} is missing. Run Setup Hypatia Fight.");
            return controller;
        }

        [TestCaseSource(nameof(AllStates))]
        public void EachClip_IsHumanoid_NotLooping_AndBakedInPlace(string state)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipDir}/Hypatia_{state}.anim");
            Assert.That(clip, Is.Not.Null);
            Assert.IsTrue(clip.isHumanMotion, "Humanoid");
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            Assert.IsFalse(settings.loopTime, "non-looping: the view ends it, and Death holds its last pose");
            Assert.IsTrue(settings.loopBlendOrientation, "root rotation baked");
            Assert.IsTrue(settings.loopBlendPositionY, "root height baked");
            Assert.IsTrue(settings.loopBlendPositionXZ, "root position baked");
        }

        [TestCaseSource(nameof(UpperStates))]
        public void AttacksAndPhaseTwo_PlayOnTheUpperBodyLayer(string state)
        {
            AnimatorControllerLayer upper = Controller().layers.Single(l => l.name == "UpperBody");
            AnimatorState found = upper.stateMachine.states.Select(s => s.state).SingleOrDefault(s => s.name == state);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.motion.name, Is.EqualTo($"Hypatia_{state}"));
            Assert.IsTrue(found.speedParameterActive, "the view fits each clip to its lead");
        }

        [TestCaseSource(nameof(BaseStates))]
        public void Death_IsFullBody_OnTheBaseLayer(string state)
        {
            AnimatorState found = Controller().layers[0].stateMachine.states.Select(s => s.state).SingleOrDefault(s => s.name == state);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.motion.name, Is.EqualTo($"Hypatia_{state}"));
        }

        [Test]
        public void TheToppleStates_StayIntact()
        {
            AnimatorController controller = Controller();
            var names = controller.layers[0].stateMachine.states.Select(s => s.state.name).ToArray();
            Assert.That(names, Does.Contain("Idle").And.Contain("ToppleHit").And.Contain("ToppledHold"));
            Assert.That(controller.parameters.Any(p => p.name == "Toppled" && p.type == AnimatorControllerParameterType.Bool));
            Assert.That(controller.layers[0].stateMachine.defaultState.name, Is.EqualTo("Idle"));
        }

        [Test]
        public void TheUpperLayer_RestsOnAnEmptyState()
        {
            AnimatorStateMachine machine = Controller().layers.Single(l => l.name == "UpperBody").stateMachine;
            Assert.That(machine.defaultState.name, Is.EqualTo("Empty"));
            Assert.That(machine.defaultState.motion, Is.Null, "no motion, so the base layer shows through");
        }

        [Test]
        public void TheMask_LeavesRootAndLegsToTheBaseLayer()
        {
            AnimatorControllerLayer upper = Controller().layers.Single(l => l.name == "UpperBody");
            Assert.That(upper.avatarMask, Is.Not.Null);
            Assert.That(upper.defaultWeight, Is.EqualTo(1f));
            AvatarMask mask = upper.avatarMask;
            Assert.IsFalse(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Root));
            Assert.IsFalse(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg));
            Assert.IsFalse(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg));
            Assert.IsTrue(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body));
            Assert.IsTrue(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm));
            Assert.IsTrue(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm));
        }
    }
}
