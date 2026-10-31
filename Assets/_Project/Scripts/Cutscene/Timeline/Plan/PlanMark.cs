using UnityEngine;

namespace Pickleball.Cutscene.Timeline.Plan
{
    /// <summary>
    /// A blocking pose written relative to a frame (a marker id, or <c>@ACTOR[.Anchor]</c>): an
    /// offset in the frame's yawed axes (x right, y up, z forward) in units of MC's height H, an
    /// optional face target and an extra yaw. <see cref="Hover"/> adds Birdy's hover height so his
    /// head sits at MC.Feet + 0.80·H (shot list §1). Resolved to metres by the timeline builder,
    /// which reads H from the actors, so the plan holds at any model size.
    /// </summary>
    public readonly struct PlanMark
    {
        /// <summary>Marker id or actor reference.</summary>
        public readonly string Frame;

        /// <summary>Offset in the frame's yawed axes, in units of H.</summary>
        public readonly Vector3 OffsetH;

        /// <summary>Marker id or actor reference to face (yaw only), or null to use the frame's yaw.</summary>
        public readonly string Face;

        /// <summary>Extra yaw in degrees on top of the frame's (ignored when <see cref="Face"/> is set).</summary>
        public readonly float Yaw;

        /// <summary>Raise the root so Birdy hovers with his head at MC.Feet + 0.80·H.</summary>
        public readonly bool Hover;

        /// <summary>Tilt about the mark's right axis after the yaw, degrees (props only).</summary>
        public readonly float Pitch;

        /// <summary>Creates a mark.</summary>
        public PlanMark(string frame, Vector3 offsetH, string face, float yaw, bool hover)
        {
            Frame = frame;
            OffsetH = offsetH;
            Face = face;
            Yaw = yaw;
            Hover = hover;
            Pitch = 0f;
        }

        /// <summary>Creates a mark with a tilt.</summary>
        public PlanMark(string frame, Vector3 offsetH, string face, float yaw, bool hover, float pitch)
            : this(frame, offsetH, face, yaw, hover)
        {
            Pitch = pitch;
        }

        /// <summary>This mark tilted <paramref name="degrees"/> about its right axis.</summary>
        public PlanMark Tilted(float degrees) => new PlanMark(Frame, OffsetH, Face, Yaw, Hover, degrees);

        /// <summary>Standing on <paramref name="frame"/>, facing its forward or <paramref name="face"/>.</summary>
        public static PlanMark On(string frame, string face = null) =>
            new PlanMark(frame, Vector3.zero, face, 0f, false);

        /// <summary>Standing on <paramref name="frame"/>, turned <paramref name="yaw"/> degrees from its forward.</summary>
        public static PlanMark Turned(string frame, float yaw) =>
            new PlanMark(frame, Vector3.zero, null, yaw, false);

        /// <summary>Offset from <paramref name="frame"/> (in H), optionally hovering.</summary>
        public static PlanMark At(string frame, float x, float y, float z, string face = null, bool hover = false) =>
            new PlanMark(frame, new Vector3(x, y, z), face, 0f, hover);
    }
}
