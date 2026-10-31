using System;
using System.Collections.Generic;
using Pickleball.Cutscene.Hooks;
using Pickleball.Cutscene.Playback;
using Pickleball.Cutscene.Timeline.Plan;
using Pickleball.Sandbox;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Pickleball.Cutscene.Editor.Pipeline
{
    /// <summary>
    /// Everything the timeline builder needs from the open OpeningPlayable scene, found by component
    /// (editor time only): markers by id, actors by id, the propeller, the camera, the relay and the
    /// director. Also resolves the plan's frame strings (<c>Crest_View</c>, <c>@MC.HandR</c>) to
    /// transforms.
    /// </summary>
    public sealed class OpeningSceneContext
    {
        readonly Dictionary<string, Transform> _markers = new Dictionary<string, Transform>();
        readonly Dictionary<string, CutsceneActor> _actors = new Dictionary<string, CutsceneActor>();

        /// <summary>The cutscene root's director.</summary>
        public PlayableDirector Director { get; private set; }

        /// <summary>The cutscene player.</summary>
        public CutscenePlayer Player { get; private set; }

        /// <summary>The opening's gameplay glue.</summary>
        public OpeningSequence Sequence { get; private set; }

        /// <summary>The subtitle relay.</summary>
        public SubtitleRelay Relay { get; private set; }

        /// <summary>The main camera.</summary>
        public Camera Camera { get; private set; }

        /// <summary>The propeller prop's root, or null.</summary>
        public Transform Propeller { get; private set; }

        /// <summary>MC's height H in metres (1.6 when MC is missing).</summary>
        public float H => _actors.TryGetValue(PlanActors.MC, out var mc) && mc != null ? mc.Height : 1.6f;

        /// <summary>Marker ids found.</summary>
        public IEnumerable<string> MarkerIds => _markers.Keys;

        /// <summary>
        /// Gathers the open scene. <paramref name="propeller"/> overrides the propeller lookup (the
        /// scene builder knows the instance it just placed).
        /// </summary>
        public static OpeningSceneContext Gather(BuildReport report, Transform propeller = null)
        {
            var ctx = new OpeningSceneContext();
            foreach (var marker in Object.FindObjectsByType<CutsceneMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (string.IsNullOrEmpty(marker.Id)) continue;
                if (ctx._markers.TryGetValue(marker.Id, out var existing) && existing != null)
                {
                    report?.Warn($"Marker {marker.Id} appears twice ({PathOf(existing)} and {PathOf(marker.transform)}); using the first.");
                    continue;
                }
                ctx._markers[marker.Id] = marker.transform;
            }
            foreach (var actor in Object.FindObjectsByType<CutsceneActor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (string.IsNullOrEmpty(actor.ActorId)) continue;
                if (ctx._actors.ContainsKey(actor.ActorId))
                {
                    report?.Warn($"Two CutsceneActors with id {actor.ActorId}; using {PathOf(ctx._actors[actor.ActorId].transform)}.");
                    continue;
                }
                ctx._actors[actor.ActorId] = actor;
            }
            ctx.Player = Object.FindFirstObjectByType<CutscenePlayer>(FindObjectsInactive.Include);
            ctx.Director = ctx.Player != null ? ctx.Player.GetComponent<PlayableDirector>() : null;
            ctx.Sequence = Object.FindFirstObjectByType<OpeningSequence>(FindObjectsInactive.Include);
            ctx.Relay = Object.FindFirstObjectByType<SubtitleRelay>(FindObjectsInactive.Include);
            ctx.Camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            ctx.Propeller = propeller;
            if (ctx.Propeller == null && ctx.Sequence != null)
                ctx.Propeller = new SerializedObject(ctx.Sequence).FindProperty("propeller").objectReferenceValue as Transform;
            return ctx;
        }

        /// <summary>The actor with this id, or null.</summary>
        public CutsceneActor Actor(string id) => id != null && _actors.TryGetValue(id, out var a) ? a : null;

        /// <summary>The marker with this id, or null.</summary>
        public Transform Marker(string id) => id != null && _markers.TryGetValue(id, out var m) ? m : null;

        /// <summary>
        /// Resolves a plan frame: a marker id, <c>@ACTOR</c> (its root), <c>@ACTOR.Anchor</c> (the
        /// anchor, falling back to Head and then the root), or <c>@PROPELLER</c>.
        /// </summary>
        public Transform Resolve(string frame)
        {
            if (!PlanActors.TryParseActorRef(frame, out string actorId, out string anchorName)) return Marker(frame);
            if (actorId == PlanActors.Propeller) return Propeller;
            var actor = Actor(actorId);
            if (actor == null) return null;
            if (anchorName == null) return actor.transform;
            if (Enum.TryParse(anchorName, out CutsceneAnchor anchor))
            {
                var t = actor.GetAnchor(anchor);
                if (t != null) return t;
                t = actor.GetAnchor(CutsceneAnchor.Head);
                if (t != null) return t;
            }
            return actor.transform;
        }

        /// <summary>The actor root behind a frame or subject (the actor for <c>@MC.Head</c>; itself otherwise).</summary>
        public Transform RootOf(string frame)
        {
            if (!PlanActors.TryParseActorRef(frame, out string actorId, out _)) return Marker(frame);
            if (actorId == PlanActors.Propeller) return Propeller;
            var actor = Actor(actorId);
            return actor != null ? actor.transform : null;
        }

        /// <summary>
        /// The root height that puts Birdy's head at MC.Feet + 0.80·H, in metres above the frame:
        /// 0.8·H minus Birdy's head height above his own root (his Head anchor at rest, or
        /// h − HB/2 when the anchor is missing).
        /// </summary>
        public float HoverRootHeight()
        {
            var birdy = Actor(PlanActors.Birdy);
            float headAbove = 0.75f;
            if (birdy != null)
            {
                var head = birdy.GetAnchor(CutsceneAnchor.Head);
                headAbove = head != null ? head.position.y - birdy.transform.position.y : birdy.Height - birdy.HeadBox * 0.5f;
            }
            return Mathf.Max(0f, OpeningTimingPlan.HoverHeadH * H - headAbove);
        }

        /// <summary>The player root (the gameplay character).</summary>
        public static Transform FindPlayerRoot()
        {
            var loco = Object.FindFirstObjectByType<ChibiLocomotion>(FindObjectsInactive.Include);
            if (loco != null) return loco.transform;
            var gate = Object.FindFirstObjectByType<PlayerInputGate>(FindObjectsInactive.Include);
            return gate != null ? gate.transform : null;
        }

        /// <summary>Hierarchy path, for logs.</summary>
        public static string PathOf(Transform t)
        {
            if (t == null) return "(none)";
            string path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
