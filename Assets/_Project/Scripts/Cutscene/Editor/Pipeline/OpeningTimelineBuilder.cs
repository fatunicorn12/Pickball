using System;
using System.Collections.Generic;
using Pickleball.Cutscene.Script;
using Pickleball.Cutscene.Timeline;
using Pickleball.Cutscene.Timeline.Plan;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace Pickleball.Cutscene.Editor.Pipeline
{
    /// <summary>
    /// Builds <c>Timeline/Cutscene/OC_S1..6.playable</c> from <see cref="OpeningTimingPlan"/>, the
    /// script asset and the open scene, and binds every track on the scene's director.
    /// <para>Tracks per scene (all names stable, so other builders can find them):</para>
    /// <list type="bullet">
    /// <item><b>Shots</b> (<see cref="ShotTrack"/>, bound to the main Camera): one clip per shot.</item>
    /// <item><b>Subtitles</b> (<see cref="SubtitleTrack"/>, bound to the SubtitleRelay): one clip per card.</item>
    /// <item><b>Voice</b> group: one AudioTrack per line, named by id, bound to the speaker's
    /// <see cref="CutsceneActor.Voice"/>; a clip only when the line has audio, otherwise the track is
    /// empty (silent) and the line keeps its placeholder slot.</item>
    /// <item><b>Actions</b> group: "MC Actions", "BIRDY Actions" (AnimationTracks on each actor's
    /// Animator), clips looked up by verb in the actor's <see cref="CutsceneActionLibrary"/>; a missing
    /// verb plays the library's idle and is reported.</item>
    /// <item><b>Blocking</b> group: "MC Marks", "BIRDY Marks" (<see cref="ActorMarkTrack"/>) and
    /// "PROPELLER Marks" (<see cref="PropMarkTrack"/>).</item>
    /// <item><b>Look</b> group: one AnimationTrack per look channel, on an Animator added to the sets
    /// workstream's component, animating its float field through an infinite clip. Skipped with a
    /// warning when the component isn't in the scene.</item>
    /// </list>
    /// Tracks other workstreams add (cameras) are left alone on a rebuild. Idempotent: the same
    /// inputs give the same tracks, clips and bindings.
    /// </summary>
    public static class OpeningTimelineBuilder
    {
        /// <summary>
        /// The minimum card time for voiced lines, from <c>CutscenePlaybackConfig</c> (or
        /// <see cref="LineTiming.DefaultMinCardSeconds"/> before the config exists). The timeline and
        /// camera builders both lay scenes out with it, so their beat times agree.
        /// </summary>
        public static float MinCardSeconds()
        {
            var config = AssetDatabase.LoadAssetAtPath<Pickleball.Cutscene.Playback.CutscenePlaybackConfig>(PipelinePaths.PlaybackConfig);
            return config != null ? config.MinCardSeconds : LineTiming.DefaultMinCardSeconds;
        }

        /// <summary>Shot track name.</summary>
        public const string ShotTrackName = "Shots";
        /// <summary>Subtitle track name.</summary>
        public const string SubtitleTrackName = "Subtitles";
        /// <summary>Voice group name.</summary>
        public const string VoiceGroup = "Voice";
        /// <summary>Action group name.</summary>
        public const string ActionGroup = "Actions";
        /// <summary>Blocking group name.</summary>
        public const string BlockingGroup = "Blocking";
        /// <summary>Look group name.</summary>
        public const string LookGroup = "Look";
        /// <summary>Prefix of the director's exposed-reference names this builder sets.</summary>
        public const string RefPrefix = "oc:";

        /// <summary>Builds all six timelines. Returns them in scene order (null where a scene failed).</summary>
        public static TimelineAsset[] BuildAll(OpeningSceneContext ctx, CutsceneScript script, BuildReport report)
        {
            var lines = new Dictionary<string, LineSpec>();
            int withAudio = 0;
            for (int i = 0; i < script.LineCount; i++)
            {
                var line = script.GetLine(i);
                lines[line.Id] = LineSpec.FromScript(line);
                if (line.Audio != null) withAudio++;
            }
            report.Info($"Script: {script.LineCount} lines, {withAudio} with audio, {script.LineCount - withAudio} on placeholder timing.");

            EditorFolders.Ensure(PipelinePaths.TimelineDir);
            var result = new TimelineAsset[OpeningTimingPlan.SceneCount];
            for (int s = 1; s <= OpeningTimingPlan.SceneCount; s++)
            {
                try
                {
                    var layout = TimelineLayout.Layout(s, OpeningTimingPlan.Scene(s), lines, MinCardSeconds());
                    result[s - 1] = BuildScene(ctx, script, layout, report);
                }
                catch (ArgumentException e)
                {
                    report.Warn($"Scene {s} not built: {e.Message}");
                }
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        static TimelineAsset BuildScene(OpeningSceneContext ctx, CutsceneScript script, SceneLayout layout, BuildReport report)
        {
            string path = PipelinePaths.Timeline(layout.Scene);
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(path);
            if (timeline == null)
            {
                timeline = ScriptableObject.CreateInstance<TimelineAsset>();
                timeline.name = OpeningTimingPlan.TimelineName(layout.Scene);
                AssetDatabase.CreateAsset(timeline, path);
            }
            RemoveOwnTracks(timeline, ctx.Director);
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = layout.Duration;

            BuildShots(timeline, ctx, layout);
            BuildSubtitles(timeline, ctx, layout);
            BuildVoice(timeline, ctx, script, layout, report);
            BuildActions(timeline, ctx, layout, report);
            BuildBlocking(timeline, ctx, layout, report);
            BuildLook(timeline, ctx, layout, report);
            BuildStates(timeline, ctx, layout, report);

            EditorUtility.SetDirty(timeline);
            report.Info($"OC_S{layout.Scene}: {layout.Shots.Count} shots, {layout.Lines.Count} lines, {layout.Duration:0.00} s.");
            return timeline;
        }

        // ── Housekeeping ────────────────────────────────────────────────────────

        static bool IsOwn(TrackAsset track) =>
            track is ShotTrack || track is SubtitleTrack ||
            (track is GroupTrack && (track.name == VoiceGroup || track.name == ActionGroup ||
                                     track.name == BlockingGroup || track.name == LookGroup || track.name == StateGroup));

        /// <summary>Group holding the actor state tracks (rotor, loose propeller).</summary>
        const string StateGroup = "State";

        /// <summary>
        /// One <see cref="ActorStateTrack"/> per actor with state keys (Birdy's rotor, the loose
        /// propeller): each distinct key time starts a clip that holds the accumulated state to
        /// the next key time (or the scene end). Fields with no key yet stay "leave as it is".
        /// </summary>
        static void BuildStates(TimelineAsset timeline, OpeningSceneContext ctx, SceneLayout layout, BuildReport report)
        {
            GroupTrack group = null;
            foreach (string actorId in new[] { PlanActors.MC, PlanActors.Birdy, PlanActors.Propeller })
            {
                var keys = new List<CurveKeySpan>();
                foreach (var k in layout.Keys)
                    if (StateChannels.ActorOf(k.Channel) == actorId) keys.Add(k);
                if (keys.Count == 0) continue;
                keys.Sort((a, b) => a.Time.CompareTo(b.Time));

                GameObject target = actorId == PlanActors.Propeller
                    ? (ctx.Propeller != null ? ctx.Propeller.gameObject : null)
                    : (ctx.Actor(actorId) != null ? ctx.Actor(actorId).gameObject : null);
                if (target == null)
                {
                    report.Warn($"OC_S{layout.Scene}: no {actorId} in the scene; its state track is skipped.");
                    continue;
                }
                group ??= timeline.CreateTrack<GroupTrack>(null, StateGroup);
                var track = timeline.CreateTrack<ActorStateTrack>(group, actorId + " State");
                Bind(ctx, track, target);

                int attached = -1, visible = -1, eyes = -1;
                float spin = -1f, propSpin = -1f;
                int i = 0;
                while (i < keys.Count)
                {
                    double t = keys[i].Time;
                    for (; i < keys.Count && keys[i].Time <= t + 1e-6; i++)
                    {
                        var k = keys[i];
                        if (k.Channel == StateChannels.BirdyRotor) attached = k.Value > 0.5f ? 1 : 0;
                        else if (k.Channel == StateChannels.BirdyRotorSpin) spin = k.Value;
                        else if (k.Channel == StateChannels.PropVisible) visible = k.Value > 0.5f ? 1 : 0;
                        else if (k.Channel == StateChannels.PropSpin) propSpin = k.Value;
                        else if (k.Channel == StateChannels.McEyes || k.Channel == StateChannels.BirdyEyes) eyes = Mathf.RoundToInt(k.Value);
                    }
                    double end = i < keys.Count ? keys[i].Time : layout.Duration;
                    if (end - t < 1e-4) continue;
                    var clip = track.CreateClip<ActorStateClip>();
                    clip.start = t;
                    clip.duration = end - t;
                    clip.displayName = $"rotor {attached} {spin:0} / prop {visible} {propSpin:0} / eyes {eyes}";
                    ((ActorStateClip)clip.asset).Configure(attached, spin, visible, propSpin, eyes);
                }
            }
        }

        /// <summary>Deletes the tracks this builder owns (and their bindings), keeping everyone else's.</summary>
        static void RemoveOwnTracks(TimelineAsset timeline, PlayableDirector director)
        {
            var doomed = new List<TrackAsset>();
            foreach (var root in timeline.GetRootTracks())
                if (IsOwn(root)) doomed.Add(root);
            foreach (var root in doomed)
            {
                foreach (var child in root.GetChildTracks())
                {
                    if (director != null) director.ClearGenericBinding(child);
                    timeline.DeleteTrack(child);
                }
                if (director != null) director.ClearGenericBinding(root);
                timeline.DeleteTrack(root);
            }
        }

        static ExposedReference<Transform> Ref(OpeningSceneContext ctx, string key, Transform target)
        {
            if (target == null || ctx.Director == null) return default;
            var name = new PropertyName(RefPrefix + key);
            ctx.Director.SetReferenceValue(name, target);
            return new ExposedReference<Transform> { exposedName = name };
        }

        static void Bind(OpeningSceneContext ctx, TrackAsset track, Object target)
        {
            if (ctx.Director != null && target != null) ctx.Director.SetGenericBinding(track, target);
        }

        // ── Shots ───────────────────────────────────────────────────────────────

        static void BuildShots(TimelineAsset timeline, OpeningSceneContext ctx, SceneLayout layout)
        {
            var track = timeline.CreateTrack<ShotTrack>(null, ShotTrackName);
            foreach (var shot in layout.Shots)
            {
                var clip = track.CreateClip<ShotClip>();
                clip.start = shot.Start;
                clip.duration = shot.Duration;
                clip.displayName = shot.Id;
                var plan = shot.Plan;
                var subject = ctx.Resolve(plan.Subject);
                var root = ctx.RootOf(plan.Subject);
                ((ShotClip)clip.asset).Configure(plan.Id, plan.Framing, plan.LensStart, plan.LensEnd,
                    Ref(ctx, plan.Subject, subject), Ref(ctx, RootKey(plan.Subject), root), plan.Subject,
                    plan.PlaceholderDistanceH * ctx.H);
            }
            Bind(ctx, track, ctx.Camera);
        }

        static string RootKey(string subject)
        {
            return PlanActors.TryParseActorRef(subject, out string actor, out _) ? PlanActors.ActorRefPrefix + actor : subject;
        }

        // ── Subtitles ───────────────────────────────────────────────────────────

        static void BuildSubtitles(TimelineAsset timeline, OpeningSceneContext ctx, SceneLayout layout)
        {
            var track = timeline.CreateTrack<SubtitleTrack>(null, SubtitleTrackName);
            foreach (var line in layout.Lines)
            {
                int count = line.Cards.Length;
                foreach (var card in line.Cards)
                {
                    if (card.End - card.Start <= 1e-4) continue;
                    var clip = track.CreateClip<SubtitleClip>();
                    clip.start = card.Start;
                    clip.duration = card.End - card.Start;
                    clip.displayName = count > 1 ? $"{line.Id} {(char)('a' + card.Index)}" : line.Id;
                    ((SubtitleClip)clip.asset).Configure(line.Id, line.Line.Speaker, card.Text, card.Index, count);
                }
            }
            Bind(ctx, track, ctx.Relay);
        }

        // ── Voice ───────────────────────────────────────────────────────────────

        static void BuildVoice(TimelineAsset timeline, OpeningSceneContext ctx, CutsceneScript script, SceneLayout layout,
            BuildReport report)
        {
            var group = timeline.CreateTrack<GroupTrack>(null, VoiceGroup);
            foreach (var line in layout.Lines)
            {
                var track = timeline.CreateTrack<AudioTrack>(group, line.Id);
                if (script.TryGetLine(line.Id, out var scriptLine) && scriptLine.Audio != null)
                {
                    var clip = track.CreateClip(scriptLine.Audio);
                    clip.start = line.Start;
                    clip.duration = scriptLine.Audio.length;
                    clip.displayName = line.Id;
                }
                var speaker = ctx.Actor(line.Line.Speaker);
                if (speaker == null) report.Warn($"{line.Id}: no actor with id {line.Line.Speaker}; its voice track is unbound.");
                else if (speaker.Voice == null) report.Warn($"{line.Id}: {line.Line.Speaker} has no Voice AudioSource.");
                else Bind(ctx, track, speaker.Voice);
            }
        }

        // ── Actions ─────────────────────────────────────────────────────────────

        static void BuildActions(TimelineAsset timeline, OpeningSceneContext ctx, SceneLayout layout, BuildReport report)
        {
            var group = timeline.CreateTrack<GroupTrack>(null, ActionGroup);
            foreach (string actorId in new[] { PlanActors.MC, PlanActors.Birdy })
            {
                var track = timeline.CreateTrack<AnimationTrack>(group, actorId + " Actions");
                // Transform offsets at zero: the model stays exactly on its actor root. With scene
                // offsets, Play mode moved the model itself to the baked clips' root height (the
                // body's, "based upon original") and MC rode 0.64 m above his marks.
                track.trackOffset = TrackOffset.ApplyTransformOffsets;
                track.position = Vector3.zero;
                track.rotation = Quaternion.identity;
                var actor = ctx.Actor(actorId);
                var animator = actor != null ? actor.GetComponentInChildren<Animator>(true) : null;
                if (animator == null) report.Warn($"OC_S{layout.Scene}: {actorId} has no Animator; its action track is unbound.");
                else Bind(ctx, track, animator);

                var library = FindLibrary(actorId);
                var missing = new List<string>();
                TimelineClip prev = null;
                foreach (var span in layout.Actions)
                {
                    if (span.Actor != actorId) continue;
                    string verb = span.Verb;
                    CutsceneActionLibrary.Entry entry = default;
                    bool found = library != null && library.TryGet(verb, out entry);
                    if (!found)
                    {
                        if (!missing.Contains(verb)) missing.Add(verb);
                        if (library == null || !library.TryGet(library.IdleVerb, out entry)) continue;
                        verb = library.IdleVerb + " (for " + span.Verb + ")";
                    }
                    var clip = track.CreateClip(entry.clip);
                    clip.start = span.Start;
                    clip.duration = span.End - span.Start;
                    clip.displayName = verb;
                    // Blend from the previous action instead of snapping (the review saw MC's pose
                    // pop at every action change): the previous clip runs on for the library's
                    // blend-in time after this one starts, so the blend happens after a cut.
                    // A change that lands on a cut snaps there instead: the cut hides it, and a blend
                    // after the cut read as MC turning on the spot at the head of S1_SH04 (review round 2).
                    if (prev != null && entry.blendIn > 0f && prev.end >= span.Start - 1e-4 && !OnCut(layout, span.Start))
                    {
                        // At least 0.4 s: a 0.25 s blend still read as a snap of MC's sword arm (S6_SH13).
                        double overlap = Math.Min(Math.Max(entry.blendIn, MinActionBlend), clip.duration * 0.5);
                        prev.duration += overlap;
                        prev.blendOutDuration = overlap;
                        clip.blendInDuration = overlap;
                    }
                    prev = clip;
                    if (clip.asset is AnimationPlayableAsset asset)
                    {
                        asset.loop = entry.loop ? AnimationPlayableAsset.LoopMode.On : AnimationPlayableAsset.LoopMode.UseSourceAsset;
                        // The baked clips carry no IK goals: with Timeline's default foot IK the
                        // feet were pulled to empty goals and MC's body rode ~0.6 m off the ground.
                        asset.applyFootIK = false;
                    }
                    // A block that hands back to gameplay (S2, S6) lets go of MC's Animator before
                    // its end: the last clip eases out into the locomotion controller (restarted on
                    // its idle when the block began), so the final frames already show exactly the
                    // pose gameplay resumes in and the cut has nothing to pop.
                    if (actorId == PlanActors.MC && HandsBack(layout.Scene) && span.End >= layout.Duration - 1e-4 &&
                        clip.duration > HandBackRelease + HandBackEase)
                    {
                        clip.duration -= HandBackRelease;
                        clip.easeOutDuration = HandBackEase;
                    }
                }
                if (library == null)
                    report.Warn($"OC_S{layout.Scene}: no action library for {actorId}; verbs unplayed: {string.Join(", ", missing)}.");
                else if (missing.Count > 0)
                    report.Warn($"OC_S{layout.Scene}: {actorId} verbs missing from {library.name}, played as \"{library.IdleVerb}\": {string.Join(", ", missing)}.");
            }
        }

        /// <summary>True when <paramref name="time"/> is a cut (the start of any shot but the first).</summary>
        static bool OnCut(SceneLayout layout, double time)
        {
            foreach (var shot in layout.Shots)
                if (shot.Start > 1e-4 && Math.Abs(shot.Start - time) < 1e-3) return true;
            return false;
        }

        /// <summary>Shortest blend between consecutive action clips, seconds.</summary>
        public const double MinActionBlend = 0.4;

        /// <summary>Seconds before a hand-back scene's end that MC's last clip has fully let go.</summary>
        public const double HandBackRelease = 1.0;

        /// <summary>Seconds MC's last clip takes to ease out into the locomotion controller.</summary>
        public const double HandBackEase = 0.6;

        /// <summary>True for the last scene of each block: the scenes that hand back to gameplay.</summary>
        public static bool HandsBack(int scene)
        {
            var one = OpeningTimingPlan.BlockOne;
            var two = OpeningTimingPlan.BlockTwo;
            return scene == one[one.Length - 1] || scene == two[two.Length - 1];
        }

        static CutsceneActionLibrary FindLibrary(string actorId)
        {
            string file = actorId == PlanActors.Birdy ? "Birdy" : actorId;
            var lib = AssetDatabase.LoadAssetAtPath<CutsceneActionLibrary>(PipelinePaths.ActionLibrary(file));
            if (lib != null) return lib;
            foreach (string guid in AssetDatabase.FindAssets("t:CutsceneActionLibrary"))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<CutsceneActionLibrary>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate != null && string.Equals(candidate.ActorId, actorId, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            return null;
        }

        // ── Blocking ────────────────────────────────────────────────────────────

        struct ResolvedMark
        {
            public MarkPoseRef Pose;
            public Vector3 World;
        }

        static void BuildBlocking(TimelineAsset timeline, OpeningSceneContext ctx, SceneLayout layout, BuildReport report)
        {
            var group = timeline.CreateTrack<GroupTrack>(null, BlockingGroup);
            float hover = ctx.HoverRootHeight();
            foreach (string actorId in new[] { PlanActors.MC, PlanActors.Birdy, PlanActors.Propeller })
            {
                TrackAsset track = actorId == PlanActors.Propeller
                    ? timeline.CreateTrack<PropMarkTrack>(group, actorId + " Marks")
                    : timeline.CreateTrack<ActorMarkTrack>(group, actorId + " Marks");
                if (actorId == PlanActors.Propeller)
                {
                    if (ctx.Propeller == null) report.Warn($"OC_S{layout.Scene}: no propeller in the scene; its blocking is unbound.");
                    else Bind(ctx, track, ctx.Propeller);
                }
                else
                {
                    var actor = ctx.Actor(actorId);
                    if (actor == null) report.Warn($"OC_S{layout.Scene}: no CutsceneActor {actorId}; its blocking is unbound.");
                    else Bind(ctx, track, actor);
                }

                // The Player's CharacterController rests skinWidth above the ground. Blocking it on
                // the ground made it pop up ~8 cm on the first gameplay frame after a hand-off (and
                // the camera drift with it), so its marks sit at the resting height.
                float lift = actorId == PlanActors.MC ? RestLift(ctx.Actor(actorId)) : 0f;
                if (track is ActorMarkTrack markTrack) markTrack.ConfigureGround(actorId == PlanActors.MC, lift);
                bool has = false;
                ResolvedMark prev = default;
                double cursor = 0;
                foreach (var span in layout.Marks)
                {
                    var cue = span.Cue;
                    if (cue.Actor != actorId) continue;
                    if (!TryResolve(ctx, cue.Target, hover, lift, out var target))
                    {
                        report.Warn($"OC_S{layout.Scene}: {actorId} mark on \"{cue.Target.Frame}\" not found; cue skipped.");
                        continue;
                    }
                    double start = span.Start;
                    if (start < cursor - 1e-6)
                    {
                        report.Warn($"OC_S{layout.Scene}: {actorId} cue at {start:0.00} s starts before the last move ends ({cursor:0.00} s); delayed.");
                        start = cursor;
                    }
                    if (!has) prev = target;   // before the first cue the actor holds its first mark
                    if (start > cursor + 1e-6) AddMark(track, cursor, start, prev, prev, MarkEase.Linear, "hold");

                    double move = cue.Speed > 0f ? Vector3.Distance(prev.World, target.World) / cue.Speed : cue.MoveSeconds;
                    move = Math.Min(move, layout.Duration - start);
                    if (has && move > 1e-4)
                    {
                        AddMark(track, start, start + move, prev, target, cue.Ease, "→ " + cue.Target.Frame);
                        cursor = start + move;
                    }
                    else cursor = start;
                    prev = target;
                    has = true;
                }
                if (has && cursor < layout.Duration - 1e-6)
                    AddMark(track, cursor, layout.Duration, prev, prev, MarkEase.Linear, "hold");
            }
        }

        /// <summary>
        /// How far above the ground an actor's root rests when it carries a CharacterController
        /// (the gameplay Player): the controller keeps its capsule skinWidth clear of the ground,
        /// less however far the capsule's bottom sits below the root. 0 for stand-in actors.
        /// </summary>
        public static float RestLift(Component actor)
        {
            var cc = actor != null ? actor.GetComponent<CharacterController>() : null;
            if (cc == null) return 0f;
            float bottom = cc.center.y - Mathf.Max(cc.height * 0.5f, cc.radius);
            return (cc.skinWidth - bottom) * actor.transform.lossyScale.y;
        }

        static bool TryResolve(OpeningSceneContext ctx, PlanMark mark, float hover, float lift, out ResolvedMark result)
        {
            result = default;
            var frame = ctx.Resolve(mark.Frame);
            if (frame == null) return false;
            Transform face = mark.Face != null ? ctx.Resolve(mark.Face) : null;
            Vector3 offset = mark.OffsetH * ctx.H + (mark.Hover ? Vector3.up * hover : Vector3.zero) + Vector3.up * lift;
            result.Pose = new MarkPoseRef
            {
                frame = Ref(ctx, mark.Frame, frame),
                offset = offset,
                face = face != null ? Ref(ctx, mark.Face, face) : default,
                yaw = mark.Yaw,
                pitch = mark.Pitch,
            };
            result.World = ActorMarkMath.Resolve(frame.position, frame.rotation, offset, mark.Yaw,
                face != null, face != null ? face.position : default).position;
            return true;
        }

        static void AddMark(TrackAsset track, double start, double end, ResolvedMark from, ResolvedMark to, MarkEase ease, string label)
        {
            var clip = track.CreateClip<ActorMarkClip>();
            clip.start = start;
            clip.duration = end - start;
            clip.displayName = label;
            ((ActorMarkClip)clip.asset).Configure(from.Pose, to.Pose, ease);
        }

        // ── Look ────────────────────────────────────────────────────────────────

        static void BuildLook(TimelineAsset timeline, OpeningSceneContext ctx, SceneLayout layout, BuildReport report)
        {
            GroupTrack group = null;
            foreach (string channel in LookChannels.All)
            {
                var keys = new List<CurveKeySpan>();
                foreach (var k in layout.Keys)
                    if (k.Channel == channel) keys.Add(k);
                if (keys.Count == 0) continue;

                string typeName = LookChannels.ComponentType(channel);
                if (!LookTargets.TryFind(typeName, LookChannels.FieldName(channel), out var component, out string propertyPath))
                {
                    report.Warn($"OC_S{layout.Scene}: no {typeName} with a float \"{LookChannels.FieldName(channel)}\" in the scene; {channel} track skipped.");
                    continue;
                }
                group ??= timeline.CreateTrack<GroupTrack>(null, LookGroup);
                var track = timeline.CreateTrack<AnimationTrack>(group, channel);
                var animator = component.GetComponent<Animator>();
                if (animator == null) animator = component.gameObject.AddComponent<Animator>();
                Bind(ctx, track, animator);

                track.CreateInfiniteClip(channel);
                var curve = new AnimationCurve();
                foreach (var k in keys) curve.AddKey(new Keyframe((float)k.Time, k.Value));
                for (int i = 0; i < curve.length; i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                }
                var binding = EditorCurveBinding.FloatCurve(string.Empty, component.GetType(), propertyPath);
                AnimationUtility.SetEditorCurve(track.infiniteClip, binding, curve);
            }
        }
    }
}
