using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Hands
{
    /// <summary>
    /// Works out where a hand's grab point goes (see <see cref="GripFrame"/>
    /// for the axis convention) from measurements of the rig, rather than from
    /// a hand-tuned offset.
    ///
    /// The old weapon socket was a "first guess" offset and rotation typed in
    /// by hand. It put the sword's grip at the wrist and had to be re-guessed
    /// for every rig. Here the inputs come from the rig itself, all in the hand
    /// bone's local space at bind pose: the wrist (the bone's origin), the arm's
    /// axis, the finger joints if the rig has them, and the hand's skin.
    ///
    /// Two cases:
    ///  - <b>Hands with finger joints</b> (<see cref="TrySolveClosedFist"/>):
    ///    the grab point is where curled fingers would wrap a handle. The
    ///    proximal knuckles give the hand's length and width directly; the
    ///    handle sits in front of the palm and just behind the curled first
    ///    finger segments.
    ///  - <b>Hands without finger joints</b> (<see cref="TrySolveOpenHand"/>),
    ///    which includes every Tripo rig: nothing can curl, so a handle placed
    ///    in front of the palm would be held by nothing. The grab point is the
    ///    centre of the hand's volume instead, so the hand mesh itself encloses
    ///    the handle and only the two ends show, like a fist.
    ///
    /// Pure functions of positions and directions with no Transforms, so
    /// EditMode tests pin the geometry down without a rig.
    /// </summary>
    public static class GripFrameSolver
    {
        /// <summary>
        /// Builds orthonormal hand axes from two rough directions.
        /// <paramref name="knuckleDirection"/> is kept exactly;
        /// <paramref name="thumbSide"/> only needs to lean the right way and is
        /// made perpendicular to it.
        ///
        /// The palm is found by handedness, not measured: for a right hand in a
        /// palms-down T-pose (knuckles along +X, thumb along +Z),
        /// Cross(knuckle, thumb) is −Y, out of the palm, and a left hand is the
        /// mirror image of that.
        /// </summary>
        /// <returns>False if a direction is zero or the two are parallel.</returns>
        public static bool TryHandAxes(Vector3 knuckleDirection, Vector3 thumbSide, bool rightHand,
                                       out HandAxes axes)
        {
            axes = default;
            float knuckleLength = knuckleDirection.magnitude;
            if (knuckleLength < 1e-6f) return false;
            Vector3 knuckle = knuckleDirection / knuckleLength;

            Vector3 thumb = thumbSide - Vector3.Dot(thumbSide, knuckle) * knuckle;
            // Relative test: reject a thumb direction within ~0.5° of the knuckles.
            if (thumb.sqrMagnitude < 1e-4f * thumbSide.sqrMagnitude || thumb.sqrMagnitude < 1e-12f)
                return false;
            thumb.Normalize();

            Vector3 palm = rightHand ? Vector3.Cross(knuckle, thumb) : Vector3.Cross(thumb, knuckle);
            axes = new HandAxes(knuckle, thumb, palm, rightHand);
            return true;
        }

        /// <summary>
        /// Centre of the volume enclosed by a triangle surface, given as a flat
        /// list of corners (three per triangle).
        ///
        /// Each triangle and <paramref name="reference"/> form a tetrahedron;
        /// signed tetrahedron volumes sum to the enclosed volume and their
        /// weighted centres to its centroid. A hand cut out of a body mesh is
        /// open at the wrist, but with the reference on the wrist the missing
        /// cap's tetrahedra are flat and would add nothing, so the open surface
        /// gives the same answer as a closed one.
        ///
        /// Volume, not vertex average: low-poly meshes put their vertices where
        /// the shape bends, so a vertex average leans toward detail (finger
        /// tips) rather than toward bulk (the palm).
        /// </summary>
        /// <returns>False if there are no triangles or they enclose no volume.</returns>
        public static bool TryVolumeCentroid(IReadOnlyList<Vector3> triangleCorners, Vector3 reference,
                                             out Vector3 centroid, out float volume)
        {
            centroid = reference;
            volume = 0f;
            if (triangleCorners == null) return false;

            double total = 0, sx = 0, sy = 0, sz = 0;
            for (int i = 0; i + 2 < triangleCorners.Count; i += 3)
            {
                Vector3 a = triangleCorners[i] - reference;
                Vector3 b = triangleCorners[i + 1] - reference;
                Vector3 c = triangleCorners[i + 2] - reference;
                double tet = Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
                Vector3 sum = a + b + c;   // the tetrahedron's centre is sum / 4
                total += tet;
                sx += tet * sum.x;
                sy += tet * sum.y;
                sz += tet * sum.z;
            }
            // Winding only flips the sign of every term, so the ratio is the same.
            if (System.Math.Abs(total) < 1e-12) return false;
            double k = 1.0 / (4.0 * total);
            centroid = reference + new Vector3((float)(sx * k), (float)(sy * k), (float)(sz * k));
            volume = (float)System.Math.Abs(total);
            return true;
        }

        /// <summary>Plain average of the points; the origin for an empty list.</summary>
        public static Vector3 Centroid(IReadOnlyList<Vector3> points)
        {
            if (points == null || points.Count == 0) return Vector3.zero;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < points.Count; i++) sum += points[i];
            return sum / points.Count;
        }

        /// <summary>
        /// Lowest and highest of (point − origin) · axis over the points; both
        /// zero for an empty list.
        /// </summary>
        public static void Extent(IReadOnlyList<Vector3> points, Vector3 origin, Vector3 axis,
                                  out float min, out float max)
        {
            min = max = 0f;
            if (points == null || points.Count == 0) return;
            min = float.MaxValue;
            max = float.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                float d = Vector3.Dot(points[i] - origin, axis);
                if (d < min) min = d;
                if (d > max) max = d;
            }
        }

        /// <summary>
        /// Centre of a handle held in a closed fist.
        ///
        /// The knuckle joints sit in the middle of the hand's thickness. Curled,
        /// the first finger segments hang from them toward the palm, so the
        /// handle rests against the palm surface on one side and against those
        /// segments on the other: it is one handle radius past each.
        /// </summary>
        /// <param name="knuckleCentre">Mean of the proximal finger joints.</param>
        /// <param name="palmHalfThickness">Knuckle centre to the palm surface, m.</param>
        /// <param name="fingerHalfThickness">Half a finger's thickness, m.</param>
        /// <param name="gripRadius">Radius of the handle the fist closes on, m.</param>
        public static Vector3 FistCentre(Vector3 knuckleCentre, in HandAxes axes,
                                         float palmHalfThickness, float fingerHalfThickness,
                                         float gripRadius)
        {
            return knuckleCentre
                   + axes.Palm * (palmHalfThickness + gripRadius)
                   - axes.Knuckle * (fingerHalfThickness + gripRadius);
        }

        /// <summary>
        /// The grab point's pose from its centre and the hand's axes: +Z along
        /// the knuckles, +Y toward the thumb (see <see cref="GripFrame"/>).
        /// </summary>
        /// <param name="tiltDegrees">Turns the handle about +X so its thumb-side
        /// end leans toward the knuckles (positive) or the wrist (negative), °.
        /// A real power grip crosses the palm diagonally, roughly 15-20°.</param>
        public static GripFrame Compose(Vector3 position, in HandAxes axes, float tiltDegrees)
        {
            Quaternion rotation = Quaternion.LookRotation(axes.Knuckle, axes.Thumb)
                                  * Quaternion.AngleAxis(tiltDegrees, Vector3.right);
            return new GripFrame(position, rotation);
        }

        /// <summary>
        /// Grab point for a hand with no finger joints: the centre of the hand's
        /// volume, so the hand mesh itself encloses the handle.
        /// </summary>
        /// <param name="wrist">The hand bone's origin.</param>
        /// <param name="armAxis">Forearm toward the wrist at bind pose; the hand
        /// carries on along it.</param>
        /// <param name="thumbSide">A direction on the thumb side. Without finger
        /// joints the rig can't say where the thumb is, so the caller passes the
        /// humanoid T-pose convention: thumbs point the way the character faces.</param>
        /// <param name="handTriangleCorners">The hand's skin triangles, three
        /// corners each. If they enclose no volume their average is used.</param>
        public static bool TrySolveOpenHand(Vector3 wrist, Vector3 armAxis, Vector3 thumbSide, bool rightHand,
                                            IReadOnlyList<Vector3> handTriangleCorners, float tiltDegrees,
                                            out GripFrame frame)
        {
            frame = default;
            if (handTriangleCorners == null || handTriangleCorners.Count < 3) return false;
            if (!TryHandAxes(armAxis, thumbSide, rightHand, out var axes)) return false;
            if (!TryVolumeCentroid(handTriangleCorners, wrist, out var core, out _))
                core = Centroid(handTriangleCorners);
            frame = Compose(core, axes, tiltDegrees);
            return true;
        }

        /// <summary>
        /// Grab point for a hand with finger joints: where curled fingers wrap a
        /// handle of <paramref name="gripRadius"/>.
        /// </summary>
        /// <param name="wrist">The hand bone's origin.</param>
        /// <param name="knuckles">Proximal finger joints, index first and little
        /// finger last. Their mean is the knuckle line; index − little is the
        /// width of the hand, which says where the thumb is.</param>
        /// <param name="thumbSide">Used only when one knuckle is given, or the
        /// knuckles can't give a width.</param>
        /// <param name="palmPoints">Skin on the palm (not the fingers). The
        /// palm surface is its furthest point toward the palm side within the
        /// strip the handle lies across (see <see cref="PalmUnderHandle"/>).
        /// May be empty.</param>
        /// <param name="fingerPoints">Skin on the first finger segments; half its
        /// spread across the palm direction is the finger thickness. May be empty.</param>
        public static bool TrySolveClosedFist(Vector3 wrist, IReadOnlyList<Vector3> knuckles, Vector3 thumbSide,
                                              bool rightHand, IReadOnlyList<Vector3> palmPoints,
                                              IReadOnlyList<Vector3> fingerPoints, float gripRadius,
                                              float tiltDegrees, out GripFrame frame)
        {
            frame = default;
            if (knuckles == null || knuckles.Count == 0) return false;

            Vector3 knuckleCentre = Centroid(knuckles);
            Vector3 across = knuckles.Count >= 2 ? knuckles[0] - knuckles[knuckles.Count - 1] : thumbSide;
            if (!TryHandAxes(knuckleCentre - wrist, across, rightHand, out var axes)
                && !TryHandAxes(knuckleCentre - wrist, thumbSide, rightHand, out axes))
                return false;

            gripRadius = Mathf.Max(0f, gripRadius);
            Extent(fingerPoints, knuckleCentre, axes.Palm, out float fingerMin, out float fingerMax);
            float fingerHalf = Mathf.Max(0f, (fingerMax - fingerMin) * 0.5f);
            float palmHalf = PalmUnderHandle(palmPoints, knuckleCentre, axes, fingerHalf, gripRadius);

            Vector3 centre = FistCentre(knuckleCentre, axes, palmHalf, fingerHalf, gripRadius);
            frame = Compose(centre, axes, tiltDegrees);
            return true;
        }

        /// <summary>
        /// How far the palm surface lies past the knuckle centre, toward the
        /// palm side, measured only on the strip of palm the handle rests
        /// against: from the knuckle line back by one finger half-thickness and
        /// the handle's width.
        ///
        /// The deepest point of the whole palm is usually the heel of the hand
        /// by the wrist, nowhere near the handle. On the chibi it is 4.2 cm past
        /// the knuckles where the palm under them is 2.6 cm, and measuring it
        /// held the handle 1.6 cm too far out, beyond the reach of the curled
        /// fingers. Falls back to the whole palm if no point lies in the strip.
        /// </summary>
        public static float PalmUnderHandle(IReadOnlyList<Vector3> palmPoints, Vector3 knuckleCentre,
                                            in HandAxes axes, float fingerHalfThickness, float gripRadius)
        {
            if (palmPoints == null || palmPoints.Count == 0) return 0f;
            float back = fingerHalfThickness + 2f * gripRadius;
            float deepest = float.MinValue;
            for (int i = 0; i < palmPoints.Count; i++)
            {
                Vector3 d = palmPoints[i] - knuckleCentre;
                float along = Vector3.Dot(d, axes.Knuckle);
                if (along > 0f || along < -back) continue;
                deepest = Mathf.Max(deepest, Vector3.Dot(d, axes.Palm));
            }
            if (deepest == float.MinValue)
                Extent(palmPoints, knuckleCentre, axes.Palm, out _, out deepest);
            return Mathf.Max(0f, deepest);
        }
    }
}
