using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pickleball.Sandbox.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Contact sheets of the ruler's swings: each step's clip posed on the Chibi prefab at
/// several fractions of the swing, from two cameras, with a ~1 m stand-in blade (or
/// Held_Ruler.prefab once it exists) on the hand. A look check for big readable arcs,
/// the blade clear of the head and planted feet, made without Play mode or any scene
/// on disk: everything is built in a fresh empty scene and destroyed again.
///
/// The pose is the clip on the whole body, which is what a full-body step plays when
/// standing. A masked step plays the clip's upper body over the idle legs at runtime,
/// with SwingTwist turning the chest back to the clip's facing, so its upper body reads
/// the same as here and only the legs differ.
///
/// Command line: -executeMethod RulerMovesetPreview.Batch -previewOut &lt;folder&gt;.
/// Writes PNGs only to that folder, never into Assets.
/// </summary>
public static class RulerMovesetPreview
{
    const string ChibiPrefab = "Assets/_Project/Prefabs/Chibi.prefab";
    const string HeldRulerPrefab = "Assets/_Project/Prefabs/Weapons/Held_Ruler.prefab";
    const int Cell = 360;

    /// <summary>Fractions of the swing each sheet shows: wind-up, strike, follow-through.</summary>
    static readonly float[] Times = { 0f, 0.2f, 0.35f, 0.45f, 0.55f, 0.65f, 0.8f, 1f };

    [MenuItem("Tools/Pickleball/Ruler Moveset/Render Swing Contact Sheets")]
    public static void Menu()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pickball-ruler-sheets");
        RenderAll(dir);
        EditorUtility.RevealInFinder(dir);
    }

    public static void Batch()
    {
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-previewOut");
        string dir = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Path.GetTempPath(), "pickball-ruler-sheets");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        int c = System.Array.IndexOf(args, "-previewClips");
        if (c >= 0 && c + 1 < args.Length)
        {
            // Raw clips from the pack, before any step exists: default windows.
            Directory.CreateDirectory(dir);
            foreach (var file in args[c + 1].Split(';'))
            {
                var clip = AssetDatabase.LoadAllAssetsAtPath($"Assets/_Project/Animation/Chibi/SwordShield/{file}")
                    .OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__preview__"));
                if (clip != null)
                    RenderSheet(clip, new SwingWindows(2f, 2f, 1f, 1f), Path.Combine(dir, $"clip_{Path.GetFileNameWithoutExtension(file)}.png"));
            }
            return;
        }
        RenderAll(dir);
    }

    /// <summary>
    /// Command line: -executeMethod RulerMovesetPreview.BatchBakeScene -scene &lt;path&gt;.
    /// Re-bakes the player's swing twist in that scene (so the Ruler_ states get their hip
    /// turn) and saves it, for a local play test of the ruler. Scenes are not committed by
    /// this branch: revert the scene afterwards; the integrator's Setup Sword Abilities
    /// run does the real bake.
    /// </summary>
    public static void BatchBakeScene()
    {
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-scene");
        string path = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Assets/_Project/Scenes/TrainingStage.unity";
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        var player = Object.FindFirstObjectByType<Pickleball.Sandbox.ChibiLocomotion>();
        if (player == null) { Debug.LogError($"No ChibiLocomotion in {path}."); return; }
        SwingTwistSetup.Bake(player.gameObject);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>One sheet per ruler step asset, plus the compass steps for comparison.</summary>
    public static void RenderAll(string dir)
    {
        Directory.CreateDirectory(dir);
        var steps = AssetDatabase.FindAssets("t:ComboStepDefinition", new[] { "Assets/_Project/ScriptableObjects/Combat/Steps" })
            .Select(g => AssetDatabase.LoadAssetAtPath<ComboStepDefinition>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(s => s != null && s.Clip != null)
            .ToList();
        foreach (var step in steps)
            RenderSheet(step.Clip, step.Windows, Path.Combine(dir, $"{step.name}.png"));
    }

    /// <summary>Renders <paramref name="clip"/> at <see cref="Times"/> into a 2-row sheet:
    /// a front-right three-quarter view on top, a side view below. Frames inside the
    /// step's active window get a red border.</summary>
    public static void RenderSheet(AnimationClip clip, SwingWindows windows, string png)
    {
        var made = new List<Object>();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChibiPrefab);
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        made.Add(rig);
        rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var animator = rig.GetComponentInChildren<Animator>();
        AttachStandIn(rig, animator, made);
        // Props that only clutter a pose check.
        foreach (var t in rig.GetComponentsInChildren<Transform>(true))
            if (t.name is "Skateboard" or "BladeTrail" or "DashArc" or "Rope") t.gameObject.SetActive(false);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        made.Add(floor);
        floor.transform.localScale = Vector3.one * 0.6f;
        var floorMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.82f, 0.85f, 0.8f) };
        made.Add(floorMat);
        floor.GetComponent<Renderer>().sharedMaterial = floorMat;
        // Grid lines every 0.5 m so a floating foot or a lunge reads against the floor.
        for (int g = -3; g <= 3; g++)
        {
            made.Add(Line(new Vector3(g * 0.5f, 0.002f, 0f), new Vector3(0.01f, 0.001f, 3f)));
            made.Add(Line(new Vector3(0f, 0.002f, g * 0.5f), new Vector3(3f, 0.001f, 0.01f)));
        }

        var lightGo = new GameObject("PreviewLight");
        made.Add(lightGo);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        lightGo.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

        var camGo = new GameObject("PreviewCam");
        made.Add(camGo);
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.65f, 0.78f);
        cam.fieldOfView = 38f;
        var rt = new RenderTexture(Cell, Cell, 24);
        made.Add(rt);
        cam.targetTexture = rt;

        // Cameras placed from the rig's own facing: the model child may be turned.
        Vector3 f = animator.transform.forward, rgt = animator.transform.right;
        var views = new[]
        {
            (pos: f * 3.2f + rgt * 2.2f + Vector3.up * 1.6f, look: new Vector3(0f, 0.7f, 0f)),   // front-right
            (pos: rgt * 3.8f + f * 0.4f + Vector3.up * 1.0f, look: new Vector3(0f, 0.65f, 0f)),  // his right side
            (pos: Vector3.up * 4.6f + f * 0.4f, look: f * 0.4f),                                    // top-down
        };
        var sheet = new Texture2D(Cell * Times.Length, Cell * views.Length, TextureFormat.RGB24, false);
        made.Add(sheet);

        var graph = PlayableGraph.Create("RulerPreview");
        try
        {
            var playable = AnimationClipPlayable.Create(graph, clip);
            var output = AnimationPlayableOutput.Create(graph, "pose", animator);
            output.SetSourcePlayable(playable);
            for (int c = 0; c < Times.Length; c++)
            {
                playable.SetTime(clip.length * Times[c]);
                graph.Evaluate(0f);
                bool live = Times[c] >= windows.ActiveStart && Times[c] <= windows.ActiveEnd;
                for (int r = 0; r < views.Length; r++)
                {
                    cam.transform.position = views[r].pos;
                    cam.transform.LookAt(views[r].look, r == 2 ? f : Vector3.up);
                    cam.Render();
                    RenderTexture.active = rt;
                    // Row 0 at the top of the image: Texture2D rows run bottom-up.
                    sheet.ReadPixels(new Rect(0, 0, Cell, Cell), c * Cell, (views.Length - 1 - r) * Cell);
                    RenderTexture.active = null;
                    if (live) Border(sheet, c * Cell, (views.Length - 1 - r) * Cell, Color.red);
                }
            }
        }
        finally
        {
            graph.Destroy();
            animator.Rebind();
        }
        sheet.Apply();
        File.WriteAllBytes(png, sheet.EncodeToPNG());
        Debug.Log($"Contact sheet: {png}");
        cam.targetTexture = null;
        foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
    }

    /// <summary>Held_Ruler on the socket if that prefab exists, else a yellow 0.78 m
    /// slab with a 0.2 m crossguard disc and a handle, laid along the prefab's
    /// BladeBase→BladeTip direction from the grip.</summary>
    static void AttachStandIn(GameObject rig, Animator animator, List<Object> made)
    {
        var all = rig.GetComponentsInChildren<Transform>(true);
        var socket = all.FirstOrDefault(t => t.name == "WeaponSocket");
        var bladeBase = all.FirstOrDefault(t => t.name == "BladeBase");
        var bladeTip = all.FirstOrDefault(t => t.name == "BladeTip");
        var visual = all.FirstOrDefault(t => t.name == "WeaponVisual");
        if (socket == null || bladeBase == null || bladeTip == null) return;

        var held = AssetDatabase.LoadAssetAtPath<GameObject>(HeldRulerPrefab);
        if (held != null)
        {
            if (visual != null) visual.gameObject.SetActive(false);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(held, socket);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            return;
        }

        if (visual != null) visual.gameObject.SetActive(false);
        var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Vector3 dir = (bladeTip.position - bladeBase.position).normalized;
        var root = new GameObject("RulerStandIn").transform;
        root.SetParent(hand, false);
        root.position = bladeBase.position;
        root.rotation = Quaternion.LookRotation(dir, hand.up);

        var yellow = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(1f, 0.82f, 0.2f) };
        var dark = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.25f, 0.2f, 0.18f) };
        var steel = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.8f, 0.82f, 0.86f) };
        made.Add(yellow); made.Add(dark); made.Add(steel);
        Box(root, "Handle", dark, new Vector3(0f, 0f, -0.02f), new Vector3(0.045f, 0.045f, 0.16f));
        Box(root, "Guard", dark, new Vector3(0f, 0f, 0.08f), new Vector3(0.21f, 0.21f, 0.04f));
        Box(root, "Blade", yellow, new Vector3(0f, 0f, 0.49f), new Vector3(0.07f, 0.2f, 0.78f));
        Box(root, "Edge", steel, new Vector3(0f, -0.105f, 0.49f), new Vector3(0.03f, 0.02f, 0.78f));
    }

    static void Box(Transform parent, string name, Material mat, Vector3 pos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static GameObject Line(Vector3 pos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.position = pos;
        go.transform.localScale = size;
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.5f, 0.55f, 0.5f) };
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go;
    }

    static void Border(Texture2D tex, int x0, int y0, Color c)
    {
        for (int i = 0; i < Cell; i++)
            for (int w = 0; w < 4; w++)
            {
                tex.SetPixel(x0 + i, y0 + w, c);
                tex.SetPixel(x0 + i, y0 + Cell - 1 - w, c);
                tex.SetPixel(x0 + w, y0 + i, c);
                tex.SetPixel(x0 + Cell - 1 - w, y0 + i, c);
            }
    }
}
