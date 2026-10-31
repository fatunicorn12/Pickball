using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Pickleball.Tests.Cutscene.Actors
{
    /// <summary>
    /// The cutscene walks really walk (review round 2 saw MC's boot frozen behind him at hip
    /// height): sampled on MC's model over one cycle, the feet swap front and back, and neither rises
    /// above knee height. And every baked MC clip keeps its feet under foot IK: the gameplay
    /// controller's idle has Foot IK on and runs under the timeline, so a clip without IK goals
    /// had both feet pulled up to the hips.
    /// </summary>
    public class WalkCycleTests
    {
        [TestCase("Assets/_Project/Animation/Cutscene/MC/MC_Walk.anim")]
        [TestCase("Assets/_Project/Animation/Cutscene/MC/MC_WalkRelaxed.anim")]
        [Category("Assets")]
        public void BothFeetAlternateAndStayBelowTheKnees(string clipPath)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Cutscene/Actors/MC_Actor.prefab");
            Assert.That(clip, Is.Not.Null, clipPath);
            Assert.That(prefab, Is.Not.Null);
            var actor = Object.Instantiate(prefab);
            try
            {
                var animator = actor.GetComponentInChildren<Animator>();
                Transform root = actor.transform;
                Transform lf = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform rf = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                Transform lk = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                Transform rk = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                float minDz = float.MaxValue, maxDz = float.MinValue;
                const int samples = 24;
                for (int i = 0; i < samples; i++)
                {
                    clip.SampleAnimation(animator.gameObject, clip.length * i / samples);
                    Vector3 l = root.InverseTransformPoint(lf.position), r = root.InverseTransformPoint(rf.position);
                    float dz = l.z - r.z;
                    minDz = Mathf.Min(minDz, dz);
                    maxDz = Mathf.Max(maxDz, dz);
                    // Each foot stays below its own knee (the review's frozen boot sat at hip height).
                    Assert.That(l.y, Is.LessThan(root.InverseTransformPoint(lk.position).y), $"{clip.name} @ {i}/{samples}: the left foot is above its knee");
                    Assert.That(r.y, Is.LessThan(root.InverseTransformPoint(rk.position).y), $"{clip.name} @ {i}/{samples}: the right foot is above its knee");
                }
                Assert.That(minDz, Is.LessThan(-0.05f), $"{clip.name}: the left foot never leads");
                Assert.That(maxDz, Is.GreaterThan(0.05f), $"{clip.name}: the right foot never leads");
            }
            finally
            {
                Object.DestroyImmediate(actor);
            }
        }

        [Test]
        [Category("Assets")]
        public void EveryMcClipKeepsItsFeetUnderFootIK()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Cutscene/Actors/MC_Actor.prefab");
            Assert.That(prefab, Is.Not.Null);
            var actor = Object.Instantiate(prefab);
            try
            {
                var animator = actor.GetComponentInChildren<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                int checkedClips = 0;
                foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/_Project/Animation/Cutscene/MC" }))
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                    for (int i = 0; i <= 4; i++)
                    {
                        float t = clip.length * i / 4f;
                        Vector3[] fk = Feet(animator, clip, t, false), ik = Feet(animator, clip, t, true);
                        for (int s = 0; s < 2; s++)
                            Assert.That(Vector3.Distance(fk[s], ik[s]), Is.LessThan(0.02f),
                                        $"{clip.name} @ {t:0.00}s: foot IK moves the {(s == 0 ? "left" : "right")} foot {fk[s]} -> {ik[s]}");
                    }
                    checkedClips++;
                }
                Assert.That(checkedClips, Is.GreaterThan(10));
            }
            finally
            {
                Object.DestroyImmediate(actor);
            }
        }

        static Vector3[] Feet(Animator animator, AnimationClip clip, float time, bool footIK)
        {
            var graph = PlayableGraph.Create("WalkCycleTests");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(footIK);
                AnimationPlayableOutput.Create(graph, "Feet", animator).SetSourcePlayable(playable);
                playable.SetTime(time);
                graph.Evaluate();
                Transform root = animator.transform;
                return new[]
                {
                    root.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.LeftFoot).position),
                    root.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.RightFoot).position),
                };
            }
            finally
            {
                graph.Destroy();
            }
        }
    }
}
