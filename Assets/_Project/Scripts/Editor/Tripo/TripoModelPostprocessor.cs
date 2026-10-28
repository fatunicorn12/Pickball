using Pickleball.Art;
using UnityEditor;
using UnityEngine;

namespace Pickleball.EditorTools.Tripo
{
    /// <summary>
    /// Bakes a catalog row's <see cref="TripoVertexGradient"/> into its model's
    /// vertex colours at import time. <see cref="TripoAssetImporter"/> writes the
    /// gradient into the model importer's userData, so changing it reimports the
    /// model, and the FBX in the repo stays exactly as Tripo exported it.
    ///
    /// Height is measured in the model's own space across every mesh of the
    /// model, so a multi-part model shades as one object. Applies only under
    /// <see cref="TripoAssetCatalog.ArtRoot"/> and only to models carrying a
    /// gradient. Deterministic. Bump <see cref="GetVersion"/> when the maths changes.
    /// </summary>
    public sealed class TripoModelPostprocessor : AssetPostprocessor
    {
        /// <inheritdoc/>
        public override uint GetVersion() => 1;

        void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith(TripoAssetCatalog.ArtRoot + "/")) return;
            if (!TripoVertexGradient.TryDecode(assetImporter.userData, out var gradient)) return;

            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            float min = float.MaxValue, max = float.MinValue;
            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                var m = mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    float y = m.MultiplyPoint3x4(v).y;
                    if (y < min) min = y;
                    if (y > max) max = y;
                }
            }
            float span = Mathf.Max(max - min, 1e-6f);

            foreach (var mf in filters)
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                var m = mf.transform.localToWorldMatrix;
                var vertices = mesh.vertices;
                var colours = new Color32[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                    colours[i] = gradient.Evaluate((m.MultiplyPoint3x4(vertices[i]).y - min) / span);
                mesh.colors32 = colours;
            }
        }
    }
}
