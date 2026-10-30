using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Pickleball.Sandbox.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Measures sword clips on the chibi's own rig, so a moveset is chosen by numbers
/// rather than by clip name or a glance at the Mixamo thumbnail. Each clip is played
/// through a PlayableGraph on an instance of the Chibi prefab (the way SwingTwistSetup
/// samples), and a straight blade of <see cref="BladeReach"/> metres is traced from the
/// hand along the prefab's own blade direction.
///
/// Per clip it reports, at 1x speed: where the strike sits (wind-up end, peak tip
/// speed, strike end, as fractions of the clip), how many separate strikes it has, the
/// arc the tip sweeps during the strike and its direction, how close the blade passes
/// to the head, and how far the hips drop and the feet leave the floor (a full-body
/// move). It writes nothing into Assets; the CSV goes wherever the caller says.
///
/// Used to pick and window the ruler's clips (Docs/Systems/SwordAbilities.md, Ruler).
/// Command line: -executeMethod RulerMovesetAudit.Batch -auditOut &lt;csv&gt; [-auditClips a.fbx;b.fbx]
/// </summary>
public static class RulerMovesetAudit
{
    const string PackDir = "Assets/_Project/Animation/Chibi/SwordShield";
    const string ChibiPrefab = "Assets/_Project/Prefabs/Chibi.prefab";

    /// <summary>Hand-to-tip distance of the traced blade, in metres: the ruler is
    /// 0.98 m long and gripped about 8 cm from its pommel.</summary>
    public const float BladeReach = 0.9f;

    const int Samples = 121;

    /// <summary>One clip's measurements; see the class summary.</summary>
    public sealed class Report
    {
        public string Clip;
        public float Length;
        public float WindupEnd, Peak, StrikeEnd;   // fractions of the clip
        public int Strikes;
        public float PeakTipSpeed;                 // m/s at 1x
        public float ArcDegrees;                   // tip sweep during the strike, around the chest
        public float LateralSpan, VerticalSpan;    // m, tip, during the strike
        public string Direction;                   // e.g. "R>L down"
        public float MinBladeToHead;               // m, hand→tip segment vs head bone, whole clip
        public float MinBladeToHeadInStrike;
        public float HeadHitFraction;              // share of samples with the blade inside the head sphere
        public float HeadDepth;                    // m, deepest the blade goes into it
        public float HipDrop, HipRise, FootLift;   // m vs the first frame
        public float HipYaw;                       // °, largest hip turn away from the first frame's
        public float[] TipSpeed;                   // per sample
        public Vector3[] Tip, Grip, Head;          // per sample, character space
        public float[] HipY, FootY;                // per sample, character space
    }

    [MenuItem("Tools/Pickleball/Ruler Moveset/Audit Sword Clips")]
    public static void Menu()
    {
        string csv = Path.Combine(Path.GetTempPath(), "pickball-clip-audit.csv");
        Write(AuditAll(null), csv);
        EditorUtility.RevealInFinder(csv);
    }

    public static void Batch()
    {
        var args = System.Environment.GetCommandLineArgs();
        string Arg(string key)
        {
            int i = System.Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        string outPath = Arg("-auditOut") ?? Path.Combine(Path.GetTempPath(), "pickball-clip-audit.csv");
        string list = Arg("-auditClips");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Write(AuditAll(list?.Split(';')), outPath);
    }

    /// <summary>
    /// A markdown table of every step of the given movesets (ground chain, finisher, air
    /// chain, then each ability's step for that weapon) and the mashed chain totals, from
    /// <see cref="ComboTiming"/>. What the doc's compass-vs-ruler table is generated from.
    /// </summary>
    public static string TimingTable(params SwordMoveset[] movesets)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("| Moveset | Slot | Step | Clip | Speed | Duration s | Active | Wind-up s | Chain at s | Damage | Knockback | Launch | Hitstop s | Shake | FOV | Lunge m/s | Burst |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        var abilities = AssetDatabase.FindAssets("t:SwordAbilityDefinition", new[] { "Assets/_Project/ScriptableObjects/Combat" })
            .Select(g => AssetDatabase.LoadAssetAtPath<SwordAbilityDefinition>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(a => a != null).OrderBy(a => a.name).ToList();
        var resolver = new ComboResolver();
        foreach (var m in movesets)
        {
            if (m == null) continue;
            void Row(string slot, ComboStepDefinition st)
            {
                if (st == null) { sb.AppendLine($"| {m.name} | {slot} | (none) |||||||||||||||"); return; }
                var w = st.Windows; var h = st.Hit; var b = st.Burst; var look = st.Presentation;
                string burst = b.Enabled
                    ? $"{b.Radius.ToString("0.0", ci)} m @{b.TriggerAt.ToString("0.00", ci)}, {b.Hit.Damage.ToString("0", ci)} dmg, kb {b.Hit.Knockback.ToString("0", ci)}, stop {b.Hit.Hitstop.ToString("0.00", ci)}"
                    : "";
                sb.AppendLine($"| {m.name} | {slot} | {st.name} | {(st.Clip != null ? st.Clip.name : "-")} | {st.PlaybackSpeed.ToString("0.00", ci)} | {st.Duration.ToString("0.000", ci)} | " +
                              $"{w.ActiveStart.ToString("0.00", ci)}-{w.ActiveEnd.ToString("0.00", ci)} | {ComboTiming.WindupSeconds(st).ToString("0.000", ci)} | " +
                              $"{(st.Duration * ComboTiming.ChainFraction(st)).ToString("0.000", ci)} | {h.Damage.ToString("0", ci)} | {h.Knockback.ToString("0.0", ci)} | {h.LaunchSpeed.ToString("0.0", ci)} | " +
                              $"{h.Hitstop.ToString("0.00", ci)} | {look.ShakeAmount.ToString("0.00", ci)} | {look.FovPunch.ToString("0", ci)} | {st.LungeSpeed.ToString("0.0", ci)} | {burst} |");
            }
            var ground = m.ChainFor(ComboTrack.Ground);
            for (int i = 0; i < ground.Count; i++) Row($"ground {i + 1}", ground[i]);
            Row("ground finisher", m.DefaultFinisherFor(ComboTrack.Ground));
            var air = m.ChainFor(ComboTrack.Air);
            for (int i = 0; i < air.Count; i++) Row($"air {i + 1}", air[i]);
            if (m.DefaultFinisherFor(ComboTrack.Air) != null) Row("air finisher", m.DefaultFinisherFor(ComboTrack.Air));
            foreach (var a in abilities) Row($"{a.DisplayName} ({a.Role})", m.StepFor(a));
        }

        sb.AppendLine();
        sb.AppendLine("| Moveset | Chain | Hits | Mashed s | + hitstop s | Damage (blade) | Damage (+rings) | DPS blade | DPS +rings |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var m in movesets)
        {
            if (m == null) continue;
            void Chain(string label, ComboTrack track, IReadOnlyList<SwordAbilityDefinition> learned)
            {
                var seq = resolver.BuildSequence(new ComboContext(track, 0), m, learned);
                float t = ComboTiming.ChainSeconds(seq, false), th = ComboTiming.ChainSeconds(seq, true);
                float d = ComboTiming.ChainDamage(seq, false), dr = ComboTiming.ChainDamage(seq, true);
                sb.AppendLine($"| {m.name} | {label} | {seq.Count} | {t.ToString("0.000", ci)} | {th.ToString("0.000", ci)} | {d.ToString("0", ci)} | {dr.ToString("0", ci)} | " +
                              $"{(d / th).ToString("0.0", ci)} | {(dr / th).ToString("0.0", ci)} |");
            }
            Chain("ground, Lv1", ComboTrack.Ground, null);
            Chain("air, Lv1", ComboTrack.Air, null);
            Chain("ground, all learned", ComboTrack.Ground, abilities);
            Chain("air, all learned", ComboTrack.Air, abilities);
        }
        return sb.ToString();
    }

    /// <summary>Command line: writes <see cref="TimingTable"/> for the compass and the
    /// ruler to -timingOut.</summary>
    public static void BatchTiming()
    {
        var args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-timingOut");
        string outPath = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Path.GetTempPath(), "pickball-timing.md");
        var compass = AssetDatabase.LoadAssetAtPath<SwordMoveset>("Assets/_Project/ScriptableObjects/Combat/Moveset_Compass.asset");
        var ruler = AssetDatabase.LoadAssetAtPath<SwordMoveset>(RulerMovesetSetup.MovesetPath);
        File.WriteAllText(outPath, TimingTable(compass, ruler));
        Debug.Log($"Timing table written to {outPath}");
    }

    /// <summary>Audit the named FBXs in the sword pack (all attack-like ones if null).
    /// Clips that are not Humanoid are skipped with a warning: import them first.</summary>
    public static List<Report> AuditAll(IEnumerable<string> files)
    {
        files ??= Directory.GetFiles(PackDir, "*.fbx").Select(Path.GetFileName)
            .Where(f => f.Contains("attack") || f.Contains("slash") || f.Contains("power up")
                        || f.Contains("casting") || f.Contains("kick"));
        var reports = new List<Report>();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChibiPrefab);
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            foreach (var file in files)
            {
                string path = $"{PackDir}/{file}";
                if (AssetImporter.GetAtPath(path) is ModelImporter imp && imp.animationType != ModelImporterAnimationType.Human)
                {
                    Debug.LogWarning($"Audit: {file} is {imp.animationType}; skipped.");
                    continue;
                }
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                             .Where(c => !c.name.StartsWith("__preview__")))
                {
                    var r = Measure(rig, clip);
                    r.Clip = $"{file}:{clip.name}";
                    reports.Add(r);
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(rig);
        }
        return reports;
    }

    /// <summary>Samples <paramref name="clip"/> on <paramref name="rig"/> (a Chibi prefab
    /// instance) and measures it. Leaves the rig rebound to its bind pose.</summary>
    public static Report Measure(GameObject rig, AnimationClip clip)
    {
        var animator = rig.GetComponentInChildren<Animator>();
        var root = animator.transform;
        var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
        var lFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        FindBlade(rig, out var bladeBase, out var bladeTip);

        // The blade's direction in the hand's frame, from the prefab's markers.
        Vector3 bladeDirInHand = bladeBase != null && bladeTip != null
            ? hand.InverseTransformDirection((bladeTip.position - bladeBase.position).normalized)
            : Vector3.forward;
        Vector3 gripInHand = bladeBase != null ? hand.InverseTransformPoint(bladeBase.position) : Vector3.zero;

        var tip = new Vector3[Samples];
        var grip = new Vector3[Samples];
        var headPos = new Vector3[Samples];
        MeasureHead(animator, head, out var headCentre, out float headRadius);
        var chestPos = new Vector3[Samples];
        var hipY = new float[Samples];
        var footY = new float[Samples];
        float hipYaw = 0f;
        Vector3 hip0 = Vector3.forward;
        var graph = PlayableGraph.Create("RulerAudit");
        try
        {
            var playable = AnimationClipPlayable.Create(graph, clip);
            var output = AnimationPlayableOutput.Create(graph, "pose", animator);
            output.SetSourcePlayable(playable);
            for (int i = 0; i < Samples; i++)
            {
                playable.SetTime(clip.length * i / (Samples - 1));
                graph.Evaluate(0f);
                Vector3 g = hand.TransformPoint(gripInHand);
                Vector3 d = hand.TransformDirection(bladeDirInHand);
                grip[i] = root.InverseTransformPoint(g);
                tip[i] = root.InverseTransformPoint(g + d * BladeReach);
                headPos[i] = root.InverseTransformPoint(head.TransformPoint(headCentre));
                chestPos[i] = root.InverseTransformPoint(chest.position);
                hipY[i] = root.InverseTransformPoint(hips.position).y;
                var hf = Vector3.ProjectOnPlane(root.InverseTransformDirection(hips.forward), Vector3.up);
                if (i == 0) hip0 = hf;    // the bone's own axes are arbitrary: measure the turn from frame 0
                hipYaw = Mathf.Max(hipYaw, Mathf.Abs(Vector3.SignedAngle(hip0, hf, Vector3.up)));
                footY[i] = Mathf.Min(root.InverseTransformPoint(lFoot.position).y,
                                     root.InverseTransformPoint(rFoot.position).y);
            }
        }
        finally
        {
            graph.Destroy();
            animator.Rebind();
        }

        float dt = clip.length / (Samples - 1);
        var speed = new float[Samples];
        for (int i = 1; i < Samples - 1; i++)
            speed[i] = (tip[i + 1] - tip[i - 1]).magnitude / (2f * dt);
        speed[0] = speed[1];
        speed[^1] = speed[^2];
        // Light smoothing: a single noisy key must not read as the impact.
        var smooth = new float[Samples];
        for (int i = 0; i < Samples; i++)
        {
            float s = 0f; int n = 0;
            for (int k = -2; k <= 2; k++)
                if (i + k >= 0 && i + k < Samples) { s += speed[i + k]; n++; }
            smooth[i] = s / n;
        }

        int peak = 0;
        for (int i = 1; i < Samples; i++) if (smooth[i] > smooth[peak]) peak = i;
        float hi = smooth[peak];
        int start = peak, end = peak;
        while (start > 0 && smooth[start - 1] > 0.35f * hi) start--;
        while (end < Samples - 1 && smooth[end + 1] > 0.35f * hi) end++;

        // Separate strikes: maxima over 60% of the peak divided by a dip under 30%.
        int strikes = 0; bool inStrike = false;
        for (int i = 0; i < Samples; i++)
        {
            if (!inStrike && smooth[i] > 0.6f * hi) { strikes++; inStrike = true; }
            else if (inStrike && smooth[i] < 0.3f * hi) inStrike = false;
        }

        float arc = 0f;
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int i = start; i <= end; i++)
        {
            minX = Mathf.Min(minX, tip[i].x); maxX = Mathf.Max(maxX, tip[i].x);
            minY = Mathf.Min(minY, tip[i].y); maxY = Mathf.Max(maxY, tip[i].y);
            if (i > start)
                arc += Vector3.Angle(tip[i - 1] - chestPos[i - 1], tip[i] - chestPos[i]);
        }

        Vector3 v = tip[Mathf.Min(end, peak + 2)] - tip[Mathf.Max(start, peak - 2)];
        string dir = (Mathf.Abs(v.x) > 0.05f ? (v.x < 0f ? "R>L" : "L>R") : "--") + " "
                     + (Mathf.Abs(v.y) > 0.05f ? (v.y < 0f ? "down" : "up") : "flat");

        float minHead = float.MaxValue, minHeadStrike = float.MaxValue, depth = 0f;
        int inHead = 0;
        for (int i = 0; i < Samples; i++)
        {
            float dHead = DistanceToSegment(headPos[i], grip[i], tip[i]);
            minHead = Mathf.Min(minHead, dHead);
            if (dHead < headRadius) { inHead++; depth = Mathf.Max(depth, headRadius - dHead); }
            if (i >= start && i <= end) minHeadStrike = Mathf.Min(minHeadStrike, dHead);
        }

        return new Report
        {
            Length = clip.length,
            WindupEnd = start / (float)(Samples - 1),
            Peak = peak / (float)(Samples - 1),
            StrikeEnd = end / (float)(Samples - 1),
            Strikes = strikes,
            PeakTipSpeed = hi,
            ArcDegrees = arc,
            LateralSpan = maxX - minX,
            VerticalSpan = maxY - minY,
            Direction = dir,
            MinBladeToHead = minHead,
            MinBladeToHeadInStrike = minHeadStrike,
            HeadHitFraction = inHead / (float)Samples,
            HeadDepth = depth,
            HipDrop = hipY[0] - hipY.Min(),
            HipRise = hipY.Max() - hipY[0],
            FootLift = footY.Max() - footY[0],
            HipYaw = hipYaw,
            TipSpeed = smooth,
            Tip = tip, Grip = grip, Head = headPos, HipY = hipY, FootY = footY,
        };
    }

    /// <summary>The head as a sphere in the head bone's space: the centroid of the
    /// skinned vertices the head bone mostly drives, and the radius that holds 90% of
    /// them (the hair spikes would otherwise make it huge). The chibi's head is far
    /// larger than the Mixamo rig's the swings were authored on, which is why the
    /// distance to the head bone alone says little.</summary>
    static void MeasureHead(Animator animator, Transform head, out Vector3 centre, out float radius)
    {
        centre = new Vector3(0f, 0.12f, 0f);
        radius = 0.15f;
        var pts = new List<Vector3>();
        foreach (var smr in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            int headIndex = System.Array.IndexOf(smr.bones, head);
            if (headIndex < 0) continue;
            var baked = new Mesh();
            smr.BakeMesh(baked, false);
            var verts = baked.vertices;
            var weights = smr.sharedMesh.boneWeights;
            for (int i = 0; i < verts.Length && i < weights.Length; i++)
                if (weights[i].boneIndex0 == headIndex && weights[i].weight0 > 0.5f)
                    pts.Add(head.InverseTransformPoint(smr.transform.TransformPoint(verts[i])));
            Object.DestroyImmediate(baked);
        }
        if (pts.Count < 20) return;
        var c = Vector3.zero;
        foreach (var p in pts) c += p;
        c /= pts.Count;
        var d = pts.Select(p => (p - c).magnitude).OrderBy(x => x).ToList();
        centre = c;
        radius = d[(int)(d.Count * 0.9f)] * head.lossyScale.x;
    }

    static void FindBlade(GameObject rig, out Transform bladeBase, out Transform bladeTip)
    {
        bladeBase = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "BladeBase");
        bladeTip = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "BladeTip");
    }

    static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
        return Vector3.Distance(p, a + ab * t);
    }

    static void Write(List<Report> reports, string path)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("clip,length,windupEnd,peak,strikeEnd,strikes,peakTipSpeed,arcDeg,lateral,vertical,direction,minBladeHead,minBladeHeadStrike,headHitShare,headDepth,hipDrop,hipRise,footLift,hipYaw,speedProfile");
        foreach (var r in reports)
        {
            // A 20-bucket speed profile, 0-9 relative to the peak, to eyeball the shape.
            var prof = new StringBuilder();
            for (int b = 0; b < 20; b++)
            {
                int i = Mathf.RoundToInt(b / 19f * (r.TipSpeed.Length - 1));
                prof.Append(Mathf.Clamp(Mathf.RoundToInt(9f * r.TipSpeed[i] / Mathf.Max(1e-4f, r.PeakTipSpeed)), 0, 9));
            }
            sb.AppendLine(string.Join(",", new[]
            {
                $"\"{r.Clip}\"", r.Length.ToString("0.00", ci), r.WindupEnd.ToString("0.00", ci),
                r.Peak.ToString("0.00", ci), r.StrikeEnd.ToString("0.00", ci), r.Strikes.ToString(ci),
                r.PeakTipSpeed.ToString("0.0", ci), r.ArcDegrees.ToString("0", ci),
                r.LateralSpan.ToString("0.00", ci), r.VerticalSpan.ToString("0.00", ci), r.Direction,
                r.MinBladeToHead.ToString("0.00", ci), r.MinBladeToHeadInStrike.ToString("0.00", ci),
                r.HeadHitFraction.ToString("0.00", ci), r.HeadDepth.ToString("0.00", ci),
                r.HipDrop.ToString("0.00", ci), r.HipRise.ToString("0.00", ci), r.FootLift.ToString("0.00", ci),
                r.HipYaw.ToString("0", ci), prof.ToString(),
            }));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, sb.ToString());

        // Per-sample traces next to the summary, for plotting.
        var tr = new StringBuilder().AppendLine("clip,i,t,tipX,tipY,tipZ,gripX,gripY,gripZ,headX,headY,headZ,speed,hipY,footY");
        foreach (var r in reports)
            for (int i = 0; i < r.TipSpeed.Length; i++)
                tr.AppendLine(string.Join(",", $"\"{r.Clip}\"", i.ToString(ci),
                    (i / (float)(r.TipSpeed.Length - 1)).ToString("0.000", ci),
                    r.Tip[i].x.ToString("0.000", ci), r.Tip[i].y.ToString("0.000", ci), r.Tip[i].z.ToString("0.000", ci),
                    r.Grip[i].x.ToString("0.000", ci), r.Grip[i].y.ToString("0.000", ci), r.Grip[i].z.ToString("0.000", ci),
                    r.Head[i].x.ToString("0.000", ci), r.Head[i].y.ToString("0.000", ci), r.Head[i].z.ToString("0.000", ci),
                    r.TipSpeed[i].ToString("0.00", ci), r.HipY[i].ToString("0.000", ci), r.FootY[i].ToString("0.000", ci)));
        File.WriteAllText(Path.ChangeExtension(path, ".traces.csv"), tr.ToString());
        Debug.Log($"Clip audit: {reports.Count} clip(s) written to {path}");
    }
}
