using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Dialogue;
using Pickleball.Sandbox.Npc;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Pickleball.Tests.Npc
{
    /// <summary>
    /// Drives the shipped Birdy.controller on the shipped prefab with
    /// <see cref="Animator.Update"/>, the runtime path, headless:
    /// every state is reachable from the parameters NpcAnimatorDriver sets,
    /// Death is reachable from every state (mid-transition too) and never left,
    /// and the controller's parameters are exactly the
    /// <see cref="NpcAnimatorParameters"/> contract. The last test runs the
    /// whole prefab (driver, Talkable, Health, NpcBody) through a kill mid-talk.
    ///
    /// Read-only use of shipped assets, so it is in the Assets category.
    /// </summary>
    [Category("Assets")]
    public class BirdyControllerTests
    {
        const string PrefabPath = "Assets/_Project/Prefabs/Npc/Birdy.prefab";
        const string ControllerPath = "Assets/_Project/Animation/Controllers/Birdy.controller";
        const float Dt = 1f / 60f;

        readonly List<Object> _spawned = new();

        [TearDown]
        public void DestroySpawned()
        {
            foreach (var o in _spawned) if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
        }

        GameObject SpawnBirdy(out Animator animator)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, $"{PrefabPath} is missing; run Setup Birdy NPC.");
            var go = Object.Instantiate(prefab);
            go.hideFlags = HideFlags.DontSave;
            _spawned.Add(go);
            animator = go.GetComponentInChildren<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.Update(0f);
            return go;
        }

        static void Step(Animator a, float seconds)
        {
            for (int i = 0, n = Mathf.CeilToInt(seconds / Dt); i < n; i++) a.Update(Dt);
        }

        static bool In(Animator a, string state)
            => a.GetCurrentAnimatorStateInfo(0).IsName(state) && !a.IsInTransition(0);

        static string Describe(Animator a)
        {
            var names = new[] { "Idle", NpcAnimatorParameters.TalkIdleState, "Talk_Talk", "Talk_Excited",
                                "Talk_Thinking", NpcAnimatorParameters.DeathState };
            var info = a.GetCurrentAnimatorStateInfo(0);
            string n = names.FirstOrDefault(s => info.IsName(s)) ?? info.fullPathHash.ToString();
            return a.IsInTransition(0) ? n + " (in transition)" : n;
        }

        [Test]
        public void Parameters_MatchTheDriverContract()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.That(controller, Is.Not.Null, $"{ControllerPath} is missing.");
            var actual = controller.parameters.ToDictionary(p => p.name, p => p.type);
            var expected = new Dictionary<string, AnimatorControllerParameterType>
            {
                { NpcAnimatorParameters.Talking, AnimatorControllerParameterType.Bool },
                { NpcAnimatorParameters.Speaking, AnimatorControllerParameterType.Bool },
                { NpcAnimatorParameters.Emote, AnimatorControllerParameterType.Int },
                { NpcAnimatorParameters.Dead, AnimatorControllerParameterType.Bool },
                { NpcAnimatorParameters.IdleVariant, AnimatorControllerParameterType.Int },
            };
            Assert.That(actual, Is.EquivalentTo(expected));
        }

        [Test]
        public void EveryStateIsReachable()
        {
            SpawnBirdy(out var a);
            Assert.That(In(a, "Idle"), Is.True, "default state: " + Describe(a));

            a.SetBool(NpcAnimatorParameters.TalkingHash, true);
            Step(a, 0.6f);
            Assert.That(In(a, NpcAnimatorParameters.TalkIdleState), Is.True, "Talking: " + Describe(a));

            a.SetBool(NpcAnimatorParameters.SpeakingHash, true);
            foreach (DialogueEmote emote in System.Enum.GetValues(typeof(DialogueEmote)))
            {
                a.SetInteger(NpcAnimatorParameters.EmoteHash, (int)emote);
                Step(a, 0.6f);
                string state = NpcAnimatorParameters.TalkStateName(emote);
                Assert.That(In(a, state), Is.True, $"Speaking {emote}: {Describe(a)}");
            }

            a.SetBool(NpcAnimatorParameters.SpeakingHash, false);
            Step(a, 0.6f);
            Assert.That(In(a, NpcAnimatorParameters.TalkIdleState), Is.True, "line shown: " + Describe(a));

            a.SetBool(NpcAnimatorParameters.TalkingHash, false);
            Step(a, 0.6f);
            Assert.That(In(a, "Idle"), Is.True, "conversation over: " + Describe(a));

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var idleVariants = controller.layers[0].stateMachine.states
                .Count(s => s.state.name.StartsWith("Idle"));
            for (int v = 1; v < idleVariants; v++)
            {
                a.SetInteger(NpcAnimatorParameters.IdleVariantHash, v);
                Step(a, a.GetCurrentAnimatorStateInfo(0).length + 0.5f);
                Assert.That(In(a, NpcAnimatorParameters.IdleStateName(v)), Is.True, $"IdleVariant {v}: {Describe(a)}");
                a.SetInteger(NpcAnimatorParameters.IdleVariantHash, 0);
                Step(a, a.GetCurrentAnimatorStateInfo(0).length + 0.5f);
            }
        }

        [Test]
        public void QuickNextLine_CutsIntoTheSettle()
        {
            SpawnBirdy(out var a);
            a.SetBool(NpcAnimatorParameters.TalkingHash, true);
            a.SetBool(NpcAnimatorParameters.SpeakingHash, true);
            a.SetInteger(NpcAnimatorParameters.EmoteHash, (int)DialogueEmote.Excited);
            Step(a, 0.8f);
            Assert.That(In(a, "Talk_Excited"), Is.True, "setup: " + Describe(a));

            a.SetBool(NpcAnimatorParameters.SpeakingHash, false);      // line shown: settle starts
            Step(a, 0.1f);
            Assert.That(a.IsInTransition(0), Is.True, "setup: should be mid-settle");

            a.SetBool(NpcAnimatorParameters.SpeakingHash, true);       // next line, skipped to quickly
            a.SetInteger(NpcAnimatorParameters.EmoteHash, (int)DialogueEmote.Talk);
            Step(a, 0.3f);
            Assert.That(In(a, "Talk_Talk"), Is.True,
                        "the next gesture should cut into the settle, not wait for it: " + Describe(a));
        }

        [TestCase("Idle")]
        [TestCase("TalkIdle")]
        [TestCase("Talk_Talk")]
        [TestCase("Talk_Excited")]
        [TestCase("Talk_Thinking")]
        [TestCase("mid-transition")]
        public void Death_IsReachableFromEveryStateAndNeverLeft(string from)
        {
            SpawnBirdy(out var a);
            if (from != "Idle")
            {
                a.SetBool(NpcAnimatorParameters.TalkingHash, true);
                Step(a, 0.6f);
            }
            if (from.StartsWith("Talk_") || from == "mid-transition")
            {
                int emote = from == "Talk_Excited" ? 1 : from == "Talk_Thinking" ? 2 : 0;
                a.SetInteger(NpcAnimatorParameters.EmoteHash, emote);
                a.SetBool(NpcAnimatorParameters.SpeakingHash, true);
                Step(a, from == "mid-transition" ? 0.05f : 0.6f);
            }
            if (from == "mid-transition")
                Assert.That(a.IsInTransition(0), Is.True, "setup: should be mid-blend into a talk state");
            else
                Assert.That(In(a, from), Is.True, $"setup: expected {from}, got {Describe(a)}");

            a.SetBool(NpcAnimatorParameters.DeadHash, true);
            Step(a, 0.5f);
            Assert.That(In(a, NpcAnimatorParameters.DeathState), Is.True, $"from {from}: {Describe(a)}");

            // Everything that could pull it out at once: nothing does.
            a.SetBool(NpcAnimatorParameters.TalkingHash, false);
            a.SetBool(NpcAnimatorParameters.SpeakingHash, true);
            a.SetInteger(NpcAnimatorParameters.EmoteHash, 1);
            a.SetInteger(NpcAnimatorParameters.IdleVariantHash, 1);
            Step(a, 6f);
            Assert.That(In(a, NpcAnimatorParameters.DeathState), Is.True, "Death was left: " + Describe(a));
        }

        static void Call(Object target, string method)
        {
            var m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(m, Is.Not.Null, $"{target.GetType().Name}.{method} not found");
            m.Invoke(target, null);
        }

        [Test]
        public void Prefab_KilledMidTalk_DiesHoldsAndUnwinds()
        {
            var go = SpawnBirdy(out var a);
            var health = go.GetComponent<Health>();
            var talkable = go.GetComponent<Talkable>();
            var driver = go.GetComponent<NpcAnimatorDriver>();
            var body = go.GetComponent<NpcBody>();
            Assert.That(talkable.gameObject, Is.SameAs(health.gameObject), "Talkable and Health share the root");
            health.ResetToFull();                      // stands in for Awake
            Call(driver, "OnEnable");
            Call(body, "OnEnable");
            var restMode = a.updateMode;

            var partner = new GameObject("Partner") { hideFlags = HideFlags.DontSave };
            _spawned.Add(partner);
            partner.transform.position = go.transform.position + new Vector3(2f, 0f, 2f);

            float now = 100f;
            void Frames(int n)
            {
                for (int i = 0; i < n; i++)
                {
                    now += Dt;
                    driver.Tick(now, Dt, 0f);         // game time paused
                    a.Update(Dt);
                }
            }

            talkable.NotifyConversationStarted(partner.transform);
            Frames(30);
            Assert.That(a.updateMode, Is.EqualTo(AnimatorUpdateMode.UnscaledTime));
            talkable.NotifyLineStarted(0);
            Frames(30);
            Assert.That(a.GetBool(NpcAnimatorParameters.SpeakingHash), Is.True);

            health.Damage(1000f, go.transform.position);
            Frames(40);
            Assert.That(In(a, NpcAnimatorParameters.DeathState), Is.True, "killed mid-talk: " + Describe(a));
            Assert.That(body.Blocker.enabled, Is.False, "corpse collider off");
            Assert.That(talkable.CanTalk, Is.False);

            talkable.NotifyConversationEnded(ConversationEndReason.Aborted);
            Frames(30);
            Assert.That(a.updateMode, Is.EqualTo(restMode), "update mode restored after the abort");
            Assert.That(In(a, NpcAnimatorParameters.DeathState), Is.True, "still dead: " + Describe(a));
            Call(driver, "OnDisable");
        }
    }
}
