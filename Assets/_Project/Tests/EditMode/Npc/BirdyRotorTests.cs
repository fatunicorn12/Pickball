using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Dialogue;
using Pickleball.Sandbox.Npc;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Npc
{
    /// <summary>
    /// Birdy's rotor on the shipped prefab, through the runtime components
    /// (Health, NpcAnimatorDriver, NpcRotor) on explicit time, headless: the
    /// rotor is its own bone, it spins about its axis on the head (faster on an
    /// Excited line, while game time is paused), and a death knocks it off to
    /// rest upright on the ground, clear of the body, never below the ground.
    ///
    /// The copy stands far below any scene so the landing's ground probe finds
    /// nothing and uses the height of the feet. Read-only use of shipped
    /// assets, so it is in the Assets category.
    /// </summary>
    [Category("Assets")]
    public class BirdyRotorTests
    {
        const string PrefabPath = "Assets/_Project/Prefabs/Npc/Birdy.prefab";
        const float Dt = 1f / 60f;
        static readonly Vector3 Feet = new(0f, -1000f, 0f);

        readonly List<Object> _spawned = new();
        Mesh _baked;

        [TearDown]
        public void DestroySpawned()
        {
            foreach (var o in _spawned) if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
            _baked = null;
        }

        sealed class Rig
        {
            public GameObject Go;
            public Animator Animator;
            public Health Health;
            public Talkable Talkable;
            public NpcAnimatorDriver Driver;
            public NpcRotor Rotor;
            public SkinnedMeshRenderer RotorMesh;
            public float Now = 100f;

            public void Frames(int n, float scaledDt = Dt)
            {
                for (int i = 0; i < n; i++)
                {
                    Now += Dt;
                    Driver.Tick(Now, Dt, scaledDt);
                    Animator.Update(Dt);
                    Rotor.Tick(scaledDt, Dt, Now);
                }
            }
        }

        Rig Spawn()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, $"{PrefabPath} is missing; run Setup Birdy NPC.");
            var go = Object.Instantiate(prefab);
            go.hideFlags = HideFlags.DontSave;
            _spawned.Add(go);
            go.transform.SetPositionAndRotation(Feet, Quaternion.identity);
            var rig = new Rig
            {
                Go = go,
                Animator = go.GetComponentInChildren<Animator>(),
                Health = go.GetComponent<Health>(),
                Talkable = go.GetComponent<Talkable>(),
                Driver = go.GetComponent<NpcAnimatorDriver>(),
                Rotor = go.GetComponent<NpcRotor>(),
                RotorMesh = go.GetComponentsInChildren<SkinnedMeshRenderer>()
                              .FirstOrDefault(s => s.name == "tripo_part_1"),
            };
            Assert.That(rig.Rotor, Is.Not.Null, "NpcRotor on the prefab root");
            Assert.That(rig.Rotor.Rotor, Is.Not.Null, "NpcRotor has its rotor bone");
            rig.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            rig.Animator.Rebind();
            rig.Animator.Update(0f);
            rig.Health.ResetToFull();                  // stands in for Awake
            return rig;
        }

        Vector3[] World(SkinnedMeshRenderer smr)
        {
            if (_baked == null)
            {
                _baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                _spawned.Add(_baked);
            }
            smr.BakeMesh(_baked, true);
            var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            return _baked.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray();
        }

        [Test]
        public void TheRotorIsItsOwnBoneUnderTheHead_AndTheRotorMeshFollowsOnlyIt()
        {
            var rig = Spawn();
            var bone = rig.Rotor.Rotor;
            Assert.That(bone.name, Is.EqualTo("Rotor"));
            Assert.That(bone.parent, Is.SameAs(rig.Animator.GetBoneTransform(HumanBodyBones.Head)));
            Assert.That(rig.RotorMesh, Is.Not.Null);
            int index = System.Array.IndexOf(rig.RotorMesh.bones, bone);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "the rotor mesh lists the bone");
            foreach (var w in rig.RotorMesh.sharedMesh.boneWeights)
                Assert.That(w.boneIndex0 == index && w.weight0 > 0.9999f, Is.True, "every rotor vertex 100% on the bone");
        }

        [Test]
        public void Alive_SpinsAboutItsOwnAxis_WithoutLeavingTheHead()
        {
            var rig = Spawn();
            var bone = rig.Rotor.Rotor;
            var head = rig.Animator.GetBoneTransform(HumanBodyBones.Head);
            Vector3 restLocal = bone.localPosition;
            rig.Frames(1);
            Vector3 axisInHead = head.InverseTransformDirection(bone.up);
            Quaternion before = bone.localRotation;
            rig.Frames(30);
            Assert.That(Vector3.Distance(bone.localPosition, restLocal), Is.LessThan(1e-5f), "stays on the head");
            Assert.That(Vector3.Angle(head.InverseTransformDirection(bone.up), axisInHead), Is.LessThan(0.01f),
                        "turns about its own axis only");
            Assert.That(Quaternion.Angle(bone.localRotation, before), Is.GreaterThan(1f), "it turned");
            Assert.That(rig.Rotor.SpinSpeed, Is.GreaterThan(0f));
        }

        [Test]
        public void AnExcitedLine_SpinsItFaster_WhileGameTimeIsPaused()
        {
            var rig = Spawn();
            rig.Frames(120);
            float idle = rig.Rotor.SpinSpeed;

            var partner = new GameObject("Partner") { hideFlags = HideFlags.DontSave };
            _spawned.Add(partner);
            partner.transform.position = rig.Go.transform.position + new Vector3(0f, 0f, 2f);
            typeof(NpcAnimatorDriver).GetMethod("OnEnable", System.Reflection.BindingFlags.Instance |
                                                System.Reflection.BindingFlags.NonPublic).Invoke(rig.Driver, null);
            rig.Talkable.NotifyConversationStarted(partner.transform);
            int excited = -1;
            var dialogue = rig.Talkable.Dialogue;
            for (int i = 0; i < dialogue.LineCount; i++)
                if (dialogue.GetLine(i).Emote == DialogueEmote.Excited) { excited = i; break; }
            if (excited < 0) Assert.Ignore("Birdy's dialogue has no Excited line.");
            rig.Talkable.NotifyLineStarted(excited);
            rig.Frames(45, scaledDt: 0f);               // the game is paused in a conversation
            Assert.That(rig.Rotor.SpinSpeed, Is.GreaterThan(idle * 2f), $"idle {idle:0} °/s");
            rig.Talkable.NotifyConversationEnded(ConversationEndReason.Completed);
        }

        [Test]
        public void Death_KnocksItOff_ToRestUprightOnTheGround_ClearOfTheBody()
        {
            var rig = Spawn();
            rig.Frames(30);
            rig.Health.Damage(rig.Health.Max * 10f, rig.Go.transform.position, recoverable: false);

            float lowestInFlight = float.PositiveInfinity;
            for (int i = 0; i < 60 * 10 && (!rig.Rotor.IsSettled || rig.Driver.DeathProgress < 1f); i++)
            {
                rig.Frames(1);
                if (rig.Rotor.HasFlown && !rig.Rotor.IsSettled)
                    lowestInFlight = Mathf.Min(lowestInFlight, World(rig.RotorMesh).Min(v => v.y) - Feet.y);
            }
            Assert.That(rig.Rotor.HasFlown, Is.True, "knocked off");
            Assert.That(rig.Rotor.IsSettled, Is.True, "came to rest");
            Assert.That(rig.Rotor.SpinSpeed, Is.EqualTo(0f).Within(1e-3f), "wound down");

            var bone = rig.Rotor.Rotor;
            Assert.That(Vector3.Angle(bone.up, Vector3.up), Is.LessThan(0.5f), "rests right way up");
            var rotorVerts = World(rig.RotorMesh);
            Assert.That(rotorVerts.Min(v => v.y) - Feet.y, Is.GreaterThanOrEqualTo(-0.001f), "no part below the ground at rest");
            Assert.That(lowestInFlight, Is.GreaterThanOrEqualTo(-0.001f), "never below the ground in flight");

            var body = new List<Vector3>();
            foreach (var smr in rig.Go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr == rig.RotorMesh) continue;
                var verts = World(smr);
                for (int v = 0; v < verts.Length; v += 4) body.Add(verts[v]);
            }
            float nearest = float.PositiveInfinity;
            foreach (var r in rotorVerts)
                foreach (var b in body)
                    nearest = Mathf.Min(nearest, (r - b).sqrMagnitude);
            Assert.That(Mathf.Sqrt(nearest), Is.GreaterThan(0.03f), "rests clear of the body");
        }
    }
}
