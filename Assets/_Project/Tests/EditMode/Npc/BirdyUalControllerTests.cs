using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;
using Pickleball.Sandbox.Npc;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Pickleball.Tests.Npc
{
    /// <summary>
    /// The Quaternius (UAL) trial controller built by Setup Birdy UAL Trial
    /// must be a drop-in for the live Birdy.controller, so adopting the trial is
    /// one field on Birdy.prefab. These tests swap it onto the shipped prefab's
    /// Animator and drive it with <see cref="Animator.Update"/>, the runtime
    /// path: same parameters as the live controller, every state reachable from
    /// the parameters NpcAnimatorDriver sets, Death reached and never left, and
    /// every state playing a real humanoid UAL clip at its own speed.
    ///
    /// Read-only use of shipped assets, so it is in the Assets category.
    /// </summary>
    [Category("Assets")]
    public class BirdyUalControllerTests
    {
        const string PrefabPath = "Assets/_Project/Prefabs/Npc/Birdy.prefab";
        const string LiveControllerPath = "Assets/_Project/Animation/Controllers/Birdy.controller";
        const string TrialControllerPath = "Assets/_Project/Animation/Quaternius/Birdy_UAL.controller";
        const string TrialClipDir = "Assets/_Project/Animation/Quaternius/Birdy/";
        const float Dt = 1f / 60f;

        readonly List<Object> _spawned = new();

        [TearDown]
        public void DestroySpawned()
        {
            foreach (var o in _spawned) if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
        }

        static AnimatorController Trial()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(TrialControllerPath);
            if (controller == null) Assert.Ignore($"{TrialControllerPath} not built; run Setup Birdy UAL Trial.");
            return controller;
        }

        Animator SpawnBirdyWithTrial()
        {
            var trial = Trial();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, $"{PrefabPath} is missing; run Setup Birdy NPC.");
            var go = Object.Instantiate(prefab);
            go.hideFlags = HideFlags.DontSave;
            _spawned.Add(go);
            var a = go.GetComponentInChildren<Animator>();
            a.runtimeAnimatorController = trial;
            a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            a.Rebind();
            a.Update(0f);
            return a;
        }

        static void Step(Animator a, float seconds)
        {
            for (int i = 0, n = Mathf.CeilToInt(seconds / Dt); i < n; i++) a.Update(Dt);
        }

        static bool In(Animator a, string state)
            => a.GetCurrentAnimatorStateInfo(0).IsName(state) && !a.IsInTransition(0);

        [Test]
        public void Parameters_MatchTheLiveController()
        {
            var trial = Trial();
            var live = AssetDatabase.LoadAssetAtPath<AnimatorController>(LiveControllerPath);
            Assert.That(live, Is.Not.Null, $"{LiveControllerPath} is missing.");
            Assert.That(trial.parameters.ToDictionary(p => p.name, p => p.type),
                        Is.EquivalentTo(live.parameters.ToDictionary(p => p.name, p => p.type)));
        }

        [Test]
        public void EveryState_PlaysAHumanoidUalClipAtItsOwnSpeed()
        {
            foreach (var cs in Trial().layers[0].stateMachine.states)
            {
                var clip = cs.state.motion as AnimationClip;
                Assert.That(clip, Is.Not.Null, $"{cs.state.name} has no clip");
                Assert.That(clip.humanMotion, Is.True, $"{cs.state.name}: {clip.name} is not a humanoid clip");
                Assert.That(AssetDatabase.GetAssetPath(clip), Does.StartWith(TrialClipDir), cs.state.name);
                Assert.That(cs.state.speed, Is.EqualTo(1f), $"{cs.state.name} plays a fallback speed");
                Assert.That(clip.isLooping, Is.EqualTo(cs.state.name != NpcAnimatorParameters.DeathState), cs.state.name);
            }
        }

        [Test]
        public void EveryStateIsReachable_OnTheBirdyPrefab()
        {
            var a = SpawnBirdyWithTrial();
            Assert.That(In(a, "Idle"), Is.True, "default state");

            a.SetBool(NpcAnimatorParameters.TalkingHash, true);
            Step(a, 0.6f);
            Assert.That(In(a, NpcAnimatorParameters.TalkIdleState), Is.True, "Talking");

            a.SetBool(NpcAnimatorParameters.SpeakingHash, true);
            foreach (DialogueEmote emote in System.Enum.GetValues(typeof(DialogueEmote)))
            {
                a.SetInteger(NpcAnimatorParameters.EmoteHash, (int)emote);
                Step(a, 0.6f);
                Assert.That(In(a, NpcAnimatorParameters.TalkStateName(emote)), Is.True, $"Speaking {emote}");
            }

            a.SetBool(NpcAnimatorParameters.SpeakingHash, false);
            Step(a, 0.6f);
            Assert.That(In(a, NpcAnimatorParameters.TalkIdleState), Is.True, "line shown");

            a.SetBool(NpcAnimatorParameters.TalkingHash, false);
            Step(a, 0.6f);
            Assert.That(In(a, "Idle"), Is.True, "conversation over");

            int variants = Trial().layers[0].stateMachine.states.Count(s => s.state.name.StartsWith("Idle"));
            for (int v = 1; v < variants; v++)
            {
                a.SetInteger(NpcAnimatorParameters.IdleVariantHash, v);
                Step(a, a.GetCurrentAnimatorStateInfo(0).length + 0.5f);
                Assert.That(In(a, NpcAnimatorParameters.IdleStateName(v)), Is.True, $"IdleVariant {v}");
                a.SetInteger(NpcAnimatorParameters.IdleVariantHash, 0);
                Step(a, a.GetCurrentAnimatorStateInfo(0).length + 0.5f);
            }
        }

        [Test]
        public void Death_FromMidTalk_IsReachedAndNeverLeft()
        {
            var a = SpawnBirdyWithTrial();
            a.SetBool(NpcAnimatorParameters.TalkingHash, true);
            a.SetBool(NpcAnimatorParameters.SpeakingHash, true);
            a.SetInteger(NpcAnimatorParameters.EmoteHash, (int)DialogueEmote.Excited);
            Step(a, 0.05f);

            a.SetBool(NpcAnimatorParameters.DeadHash, true);
            Step(a, 0.5f);
            Assert.That(In(a, NpcAnimatorParameters.DeathState), Is.True, "Dead");

            a.SetBool(NpcAnimatorParameters.TalkingHash, false);
            a.SetInteger(NpcAnimatorParameters.IdleVariantHash, 1);
            Step(a, 6f);
            Assert.That(In(a, NpcAnimatorParameters.DeathState), Is.True, "Death was left");
        }
    }
}
