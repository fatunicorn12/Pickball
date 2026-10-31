using System.Collections.Generic;

namespace Pickleball.Cutscene.Editor.Actors
{
    /// <summary>
    /// Where every shot-list verb's clip comes from, per character: a hand-keyed spec
    /// (<see cref="McClips"/>, <see cref="BirdyClips"/>), or an existing project clip or inbox
    /// Mixamo clip baked into a cutscene-owned in-place copy (<see cref="Reuse.CopyName"/>); the
    /// libraries never point at a clip that can carry root motion. Verbs with no clip go on the explicit
    /// missing lists, which the docs and the coverage test read; the timeline plays the idle for
    /// them.
    /// </summary>
    public static class CutsceneClipCatalog
    {
        /// <summary>An existing or copied clip file mapped to a verb.</summary>
        public struct Reuse
        {
            /// <summary>Verb (shot list spelling, see <see cref="CutsceneVerbs"/>).</summary>
            public string Verb;

            /// <summary>Clip asset path (FBX first take or .anim).</summary>
            public string ClipPath;

            /// <summary>Loop the clip to fill its time.</summary>
            public bool Loop;

            /// <summary>Blend-in, seconds.</summary>
            public float BlendIn;

            /// <summary>Where it came from, for the docs.</summary>
            public string Source;

            /// <summary>
            /// Name of the cutscene-owned in-place copy the library actually plays
            /// (<c>Animation/Cutscene/{MC,Birdy}/&lt;CopyName&gt;.anim</c>). The source keeps its own
            /// import settings (gameplay may want its root motion); the copy has its net XZ travel
            /// removed and root rotation, height and XZ baked into the pose, so it never moves the
            /// actor the timeline is blocking.
            /// </summary>
            public string CopyName;

            /// <summary>Rebuild the right leg from the left, half a cycle later (<see cref="PoseClipSpec.SymmetricLegs"/>).</summary>
            public bool SymmetricLegs;

            /// <summary>An optional additive key layered on the whole copy (muscle deltas).</summary>
            public PoseKey Layer;

            /// <summary>What <see cref="Layer"/> does, for the docs.</summary>
            public string LayerNote;
        }

        /// <summary>
        /// The bake spec for a reused clip's in-place copy: the source's own pose curves, played
        /// through with no keys, not grounded (its height is kept as authored), net XZ travel removed.
        /// </summary>
        public static PoseClipSpec InPlaceCopy(Reuse r, string modelPath, string folder)
        {
            AnimationClipLength(r.ClipPath, out float length);
            var spec = new PoseClipSpec
            {
                Name = r.CopyName,
                ModelPath = modelPath,
                Folder = folder,
                Verbs = new[] { r.Verb },
                BaseClip = r.ClipPath,
                BaseTime = -1f,
                Duration = length,
                Loop = r.Loop,
                BlendIn = r.BlendIn,
                Ground = 0f,
                SymmetricLegs = r.SymmetricLegs,
                Note = "In-place copy of " + r.Source + (r.SymmetricLegs ? "; right leg = left leg half a cycle later" : "")
                       + (r.Layer != null ? "; " + r.LayerNote : ""),
            };
            if (r.Layer != null) spec.K(r.Layer);
            return spec;
        }

        private static void AnimationClipLength(string path, out float length)
        {
            var clip = PoseClipBaker.LoadClip(path);
            length = clip != null ? (float)System.Math.Round(clip.length, 4) : 1f;
        }

        /// <summary>The library's fallback verb for MC.</summary>
        public const string McIdleVerb = "idle stand";

        /// <summary>The library's fallback verb for Birdy.</summary>
        public const string BirdyIdleVerb = "hover idle";

        /// <summary>Mixamo files copied from C:\Users\willr\Pickball-mixamo-inbox (import: Humanoid, own avatar, root baked).</summary>
        public static readonly string[] MixamoCopies =
        {
            CutsceneActorPaths.BirdyCutsceneClipDir + "/Hard Landing.fbx",
        };

        /// <summary>MC verbs played by clips that already existed.</summary>
        public static List<Reuse> McReuse() => new List<Reuse>
        {
            new Reuse { Verb = "walk", ClipPath = CutsceneActorPaths.ChibiClipDir + "/Walking.fbx", Loop = true, BlendIn = 0.25f, SymmetricLegs = true,
                // The Mixamo walk lifts the left arm out with the forearm bent: on the chibi the
                // gloved fist stuck out behind his hip in every rear three-quarter shot and read as
                // a boot frozen at hip height (review round 2, N1). Lower it and straighten it.
                Layer = new PoseKey(0f).A("Left Arm Down-Up", -0.2f).A("Left Forearm Stretch", 0.3f),
                LayerNote = "left arm lowered 0.2 and forearm straightened 0.3 (muscle units)",
                CopyName = "MC_Walk", Source = "Animation/Chibi/Walking.fbx (Mixamo, the gameplay walk)" },
            new Reuse { Verb = McIdleVerb, ClipPath = CutsceneActorPaths.ChibiClipDir + "/Idle.fbx", Loop = true, BlendIn = 0.3f,
                CopyName = "MC_Idle", Source = "Animation/Chibi/Idle.fbx (Mixamo idle, the gameplay idle)" },
            new Reuse { Verb = "look around", ClipPath = CutsceneActorPaths.BirdyClipDir + "/Idle_LookAround.fbx", Loop = true, BlendIn = 0.3f,
                CopyName = "MC_LookAround", Source = "Animation/Birdy/Idle_LookAround.fbx (Mixamo 'Unarmed Idle Looking Ver. 2'; standing)" },
        };

        /// <summary>Birdy verbs played by clips that already existed or were copied in.</summary>
        public static List<Reuse> BirdyReuse() => new List<Reuse>
        {
            new Reuse { Verb = "crash tumble and bounce, scramble up", ClipPath = MixamoCopies[0], Loop = false, BlendIn = 0.1f,
                CopyName = "B_HardLanding", Source = "Mixamo 'Hard Landing' (Mid-Air Falling To A Hard Landing), from the inbox" },
            new Reuse { Verb = "crash tumble and bounce", ClipPath = MixamoCopies[0], Loop = false, BlendIn = 0.1f,
                CopyName = "B_HardLanding", Source = "alias of the row above (the brief's spelling)" },
        };

        /// <summary>MC verbs with no clip (the timeline falls back to the idle). Empty means full coverage.</summary>
        public static readonly string[] McMissing = new string[0];

        /// <summary>Birdy verbs with no clip. Empty means full coverage.</summary>
        public static readonly string[] BirdyMissing = new string[0];
    }
}
