using UnityEngine;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// One actor's anchors and measured size at an instant, copied out of its
    /// <see cref="CutsceneActor"/> so the rig maths runs on plain values (and tests can build one
    /// without a scene). Missing anchors fall back the way <see cref="CutsceneActor"/> documents:
    /// the propeller socket and the blade to the head.
    /// </summary>
    public struct ActorSample
    {
        /// <summary>False when the actor isn't in the scene; every point then reads as the origin.</summary>
        public bool Valid;

        /// <summary>Root position.</summary>
        public Vector3 Root;

        /// <summary>Root yaw in degrees (the frame offsets are written in).</summary>
        public float Yaw;

        /// <summary>Head anchor.</summary>
        public Vector3 Head;

        /// <summary>Eyes anchor.</summary>
        public Vector3 Eyes;

        /// <summary>Right hand / wing tip.</summary>
        public Vector3 HandR;

        /// <summary>Feet (ground point).</summary>
        public Vector3 Feet;

        /// <summary>Propeller socket (head when missing).</summary>
        public Vector3 Socket;

        /// <summary>Blade guard (head when missing).</summary>
        public Vector3 Blade;

        /// <summary>Measured height, metres (H for MC).</summary>
        public float Height;

        /// <summary>Measured head box, metres (HB for Birdy).</summary>
        public float HeadBox;

        /// <summary>Mid = lerp(Feet, Head, 0.5).</summary>
        public Vector3 Mid => Vector3.Lerp(Feet, Head, 0.5f);

        /// <summary>The yaw rotation.</summary>
        public Quaternion YawRotation => Quaternion.Euler(0f, Yaw, 0f);

        /// <summary>Reads an actor. Allocation-free.</summary>
        public static ActorSample From(CutsceneActor actor)
        {
            var s = new ActorSample();
            if (actor == null) return s;
            s.Valid = true;
            var t = actor.transform;
            s.Root = t.position;
            s.Yaw = CameraMath.YawOf(t.forward);
            s.Head = actor.AnchorPosition(CutsceneAnchor.Head);
            s.Eyes = Or(actor, CutsceneAnchor.Eyes, s.Head);
            s.HandR = Or(actor, CutsceneAnchor.HandR, s.Head);
            s.Feet = Or(actor, CutsceneAnchor.Feet, s.Root);
            s.Socket = Or(actor, CutsceneAnchor.PropellerSocket, s.Head);
            s.Blade = Or(actor, CutsceneAnchor.Blade, s.Head);
            s.Height = actor.Height;
            s.HeadBox = actor.HeadBox;
            return s;
        }

        static Vector3 Or(CutsceneActor actor, CutsceneAnchor anchor, Vector3 fallback)
        {
            var t = actor.GetAnchor(anchor);
            return t != null ? t.position : fallback;
        }

        /// <summary>
        /// A standing test actor: feet at <paramref name="feet"/>, facing <paramref name="yaw"/>, head
        /// at <paramref name="headHeight"/>, eyes a little forward of it.
        /// </summary>
        public static ActorSample Standing(Vector3 feet, float yaw, float height, float headBox, float headHeight)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var s = new ActorSample
            {
                Valid = true,
                Root = feet,
                Yaw = yaw,
                Feet = feet,
                Head = feet + Vector3.up * headHeight,
                Height = height,
                HeadBox = headBox,
            };
            s.Eyes = s.Head + rot * new Vector3(0f, 0.05f * height, 0.25f * headBox);
            s.HandR = feet + rot * new Vector3(0.25f * height, 0.45f * height, 0f);
            s.Socket = feet + Vector3.up * height;
            s.Blade = feet + rot * new Vector3(0f, 0.45f * height, -0.15f * height);
            return s;
        }
    }
}
