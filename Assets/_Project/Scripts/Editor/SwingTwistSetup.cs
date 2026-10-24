using System.Collections.Generic;
using System.Linq;
using Pickleball.Sandbox;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Bakes the hip turn of every Attack-layer swing onto the player's
/// SwingTwist, which puts it back at runtime after the upper-body mask has
/// dropped it. See SwingTwist for why that matters.
///
/// Tools ▸ Pickleball ▸ Bake Swing Twist. Setup Weapon also runs it, so the
/// tracks follow whichever clips the combo is wired to.
///
/// Measured on the player's own Animator rather than read from the clip's
/// RootQ curves: what SwingTwist compares against at runtime is the Hips bone
/// of this rig, so sampling that same bone keeps the two in the same space.
/// </summary>
public static class SwingTwistSetup
{
    /// <summary>Samples per clip. The swings are fast but smooth; 48 keys
    /// over ~1.5s of authored motion is well under a degree of error.</summary>
    const int Samples = 48;

    [MenuItem("Tools/Pickleball/Bake Swing Twist")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("Bake Swing Twist samples the rig in edit mode — exit Play first.");
            return;
        }
        var player = GameObject.Find("Chibi");
        if (player == null)
        {
            Debug.LogError("No 'Chibi' in the scene — run Setup Chibi Sandbox first.");
            return;
        }
        Bake(player);
    }

    public static void Bake(GameObject player)
    {
        var animator = player.GetComponentInChildren<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
        {
            Debug.LogError("Player has no Humanoid avatar; cannot bake swing twist.");
            return;
        }
        var controller = animator.runtimeAnimatorController as AnimatorController;
        var layer = controller == null ? null
            : controller.layers.FirstOrDefault(l => l.name == WeaponSetup.AttackLayer);
        if (layer == null)
        {
            Debug.LogError($"No '{WeaponSetup.AttackLayer}' layer on the controller — run Setup Weapon first.");
            return;
        }

        var tracks = new List<SwingTwist.Track>();
        var tr = animator.transform;
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);

        // Sampled through a PlayableGraph on the player's own Animator rather
        // than AnimationMode.SampleAnimationClip: the graph evaluates the clip
        // exactly as the runtime Animator does (root motion off, so the clip's
        // body turn stays in the pose), whereas AnimationMode's humanoid
        // sampling disagreed with it by up to 90° mid-swing.
        foreach (var child in layer.stateMachine.states)
        {
            if (child.state.motion is not AnimationClip clip) continue;   // "None"

            var rots = new Quaternion[Samples];
            float maxYaw = 0f;
            var graph = PlayableGraph.Create("SwingTwistBake");
            try
            {
                var playable = AnimationClipPlayable.Create(graph, clip);
                var output = AnimationPlayableOutput.Create(graph, "pose", animator);
                output.SetSourcePlayable(playable);
                for (int i = 0; i < Samples; i++)
                {
                    playable.SetTime(clip.length * i / (Samples - 1));
                    graph.Evaluate(0f);
                    rots[i] = Quaternion.Inverse(tr.rotation) * hips.rotation;

                    var fwd = Vector3.ProjectOnPlane(rots[i] * Vector3.forward, Vector3.up);
                    maxYaw = Mathf.Max(maxYaw, Mathf.Abs(Vector3.SignedAngle(Vector3.forward, fwd, Vector3.up)));
                }
            }
            finally
            {
                graph.Destroy();
            }
            tracks.Add(new SwingTwist.Track { state = child.state.name, hips = rots });
            Debug.Log($"  swing twist {child.state.name,-10} <- {clip.name} (hips turn up to {maxYaw:0}°)");
        }
        // Put the rig back in its bind pose; the graph left it on the last sample.
        animator.Rebind();

        var twist = player.GetComponent<SwingTwist>() ?? Undo.AddComponent<SwingTwist>(player);
        Undo.RecordObject(twist, "Bake Swing Twist");
        twist.SetTracks(animator, tracks);
        EditorUtility.SetDirty(twist);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(player.scene);
        Debug.Log($"Swing twist baked for {tracks.Count} swing(s) onto {player.name}.");
    }
}
