using System;
using UnityEngine;

namespace Pickleball.Art
{
    /// <summary>
    /// The measuring and fitting maths behind the Tripo import, kept out of the
    /// editor script so the EditMode tests can check it and the validation test
    /// measures prefabs exactly the way the import fitted them.
    ///
    /// Everything works from mesh bounds and index counts, which Unity keeps for
    /// meshes imported with Read/Write off, so nothing here needs readable meshes.
    /// </summary>
    public static class TripoFit
    {
        /// <summary>Significant digits kept in a computed import scale, so
        /// re-running the import on the same model reproduces the same number
        /// instead of churning the .meta in its last float bits.</summary>
        public const int ScaleDigits = 5;

        /// <summary>The size <paramref name="axis"/> refers to, read off a bounds size.</summary>
        public static float Measure(Vector3 size, TripoSizeAxis axis) => axis switch
        {
            TripoSizeAxis.Height => size.y,
            TripoSizeAxis.LargestHorizontal => Mathf.Max(size.x, size.z),
            _ => Mathf.Max(size.x, Mathf.Max(size.y, size.z)),
        };

        /// <summary>
        /// The import scale factor that makes a model measured at
        /// <paramref name="measured"/> metres (while imported at
        /// <paramref name="currentScale"/>) measure <paramref name="target"/>.
        /// Scale is linear, so one measurement is enough. Rounded to
        /// <see cref="ScaleDigits"/> significant digits.
        /// </summary>
        public static float ImportScale(float currentScale, float measured, float target)
        {
            if (measured <= 1e-6f || currentScale <= 0f || target <= 0f)
                throw new ArgumentOutOfRangeException(nameof(measured),
                    $"Cannot fit: measured {measured} m at scale {currentScale} to {target} m.");
            return RoundSignificant(currentScale * target / measured, ScaleDigits);
        }

        /// <summary>Rounds to a number of significant digits.</summary>
        public static float RoundSignificant(double value, int digits)
        {
            if (value == 0d) return 0f;
            double magnitude = Math.Pow(10d, digits - 1 - Math.Floor(Math.Log10(Math.Abs(value))));
            return (float)(Math.Round(value * magnitude) / magnitude);
        }

        /// <summary>
        /// The local position for the model's holder that puts the prefab origin at
        /// <paramref name="pivot"/>, given the model's bounds in the holder's
        /// space: bottom-centre moves the footprint centre to (0,0,0), centre moves
        /// the bounds centre there.
        /// </summary>
        public static Vector3 PivotOffset(Bounds bounds, TripoPivot pivot)
        {
            var anchor = bounds.center;
            if (pivot == TripoPivot.BottomCentre) anchor.y = bounds.min.y;
            return -anchor;
        }

        /// <summary>Whether <paramref name="measured"/> is within a fraction
        /// <paramref name="tolerance"/> of <paramref name="target"/>.</summary>
        public static bool WithinSize(float measured, float target, float tolerance) =>
            Mathf.Abs(measured - target) <= target * tolerance;

        /// <summary>
        /// Bounds of every mesh under <paramref name="root"/>, mapped into the space
        /// given by <paramref name="worldToTarget"/> (pass the prefab root's
        /// worldToLocalMatrix to measure in prefab space). Works on prefab and model
        /// assets that are not in a scene, unlike Renderer.bounds. Returns a zero
        /// bounds when there is no mesh.
        /// </summary>
        public static Bounds MeshBounds(GameObject root, Matrix4x4 worldToTarget)
        {
            bool any = false;
            var result = new Bounds();
            foreach (var (mesh, transform) in Meshes(root))
            {
                var m = worldToTarget * transform.localToWorldMatrix;
                var b = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                    else result.Encapsulate(p);
                }
            }
            return result;
        }

        /// <summary>Triangles drawn for every mesh under <paramref name="root"/>
        /// (triangle-topology submeshes only). Readable or not.</summary>
        public static int CountTriangles(GameObject root)
        {
            long indices = 0;
            foreach (var (mesh, _) in Meshes(root))
                for (int s = 0; s < mesh.subMeshCount; s++)
                    if (mesh.GetTopology(s) == MeshTopology.Triangles)
                        indices += mesh.GetIndexCount(s);
            return (int)(indices / 3);
        }

        static System.Collections.Generic.IEnumerable<(Mesh, Transform)> Meshes(GameObject root)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) yield return (mf.sharedMesh, mf.transform);
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh != null) yield return (smr.sharedMesh, smr.transform);
        }
    }
}
