using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Pickleball.Art;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pickleball.Tests.Validation
{
    /// <summary>
    /// Every Tripo prefab listed in the <see cref="TripoAssetCatalog"/> is
    /// game-ready: under its triangle cap, drawn with URP Lit, the size the plan
    /// asked for, pivoted where the row says, collider and shadows as the row
    /// says, and with no missing references. Mobile import settings are checked
    /// too (Read/Write off, texture size cap, mip maps).
    ///
    /// Read-only (AssetDatabase loads only), so it is safe in the shared Editor.
    /// One test case per catalog row, each reporting every problem it finds at
    /// once rather than stopping at the first.
    /// </summary>
    [Category("Assets")]
    public class TripoAssetTests
    {
        const string UrpLit = "Universal Render Pipeline/Lit";
        const string UrpParticlesLit = "Universal Render Pipeline/Particles/Lit";

        static TripoAssetCatalog LoadCatalog() =>
            AssetDatabase.LoadAssetAtPath<TripoAssetCatalog>(TripoAssetCatalog.AssetPath);

        static IEnumerable<TestCaseData> Rows()
        {
            var catalog = LoadCatalog();
            if (catalog == null) yield break;
            foreach (var spec in catalog.Assets)
                if (spec != null)
                    yield return new TestCaseData(spec.Id).SetName($"TripoPrefab_IsGameReady({spec.Id} {spec.AssetName})");
        }

        [Test]
        public void Catalog_LoadsWithUniqueRows()
        {
            var catalog = LoadCatalog();
            Assert.That(catalog, Is.Not.Null,
                        $"{TripoAssetCatalog.AssetPath} does not load. Run Tools ▸ Pickleball ▸ Tripo ▸ Import Tripo Assets.");
            Assert.That(catalog.Assets, Is.Not.Empty);
            Assert.That(catalog.Assets.Select(s => s.Id), Is.Unique, "duplicate plan ids");
            Assert.That(catalog.Assets.Select(s => s.AssetName), Is.Unique, "duplicate asset names would share a prefab");
        }

        [TestCaseSource(nameof(Rows))]
        public void TripoPrefab_IsGameReady(string id)
        {
            var spec = LoadCatalog().Find(id);
            var problems = new List<string>();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
            Assert.That(prefab, Is.Not.Null, $"{spec.PrefabPath} does not load");

            CheckBudget(spec, prefab, problems);
            CheckMaterials(spec, prefab, problems);
            CheckSizeAndPivot(spec, prefab, problems);
            CheckColliderAndShadows(spec, prefab, problems);
            CheckReferences(prefab, problems);
            CheckImportSettings(spec, problems);

            Assert.That(problems, Is.Empty, $"{spec}:\n  " + string.Join("\n  ", problems));
        }

        static void CheckBudget(TripoAssetSpec spec, GameObject prefab, List<string> problems)
        {
            int tris = TripoFit.CountTriangles(prefab);
            if (tris == 0) problems.Add("no mesh");
            if (tris > spec.TriangleCap) problems.Add($"{tris} triangles, over the cap of {spec.TriangleCap}");

            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                if (spec.GenerateMeshLods && mesh.lodCount < 2)
                    problems.Add($"{mesh.name}: Mesh LODs requested but the mesh has {mesh.lodCount} level");
                if (!spec.VertexGradient.IsNeutral && !mesh.HasVertexAttribute(VertexAttribute.Color))
                    problems.Add($"{mesh.name}: vertex gradient requested but the mesh has no vertex colours");
            }
        }

        static void CheckMaterials(TripoAssetSpec spec, GameObject prefab, List<string> problems)
        {
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) problems.Add("no renderer");
            foreach (var r in renderers)
            {
                if (r.sharedMaterials.Length == 0) problems.Add($"{r.name}: no material slot");
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) { problems.Add($"{r.name}: empty material slot"); continue; }
                    // URP Lit, or URP Particles/Lit for a vertex gradient (it multiplies vertex colour).
                    string wanted = spec.VertexGradient.IsNeutral ? UrpLit : UrpParticlesLit;
                    if (m.shader == null || m.shader.name != wanted)
                        problems.Add($"{r.name}: material {m.name} uses {(m.shader != null ? m.shader.name : "no shader")}, not {wanted}");
                    if (spec.HasTexture && m.GetTexture("_BaseMap") == null)
                        problems.Add($"{r.name}: material {m.name} has no base map");
                }
            }
            var distinct = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().Count();
            if (distinct > 1) problems.Add($"{distinct} materials; the plan allows one per asset");
        }

        static void CheckSizeAndPivot(TripoAssetSpec spec, GameObject prefab, List<string> problems)
        {
            var b = TripoFit.MeshBounds(prefab, prefab.transform.worldToLocalMatrix);
            float measured = TripoFit.Measure(b.size, spec.SizeAxis);
            if (!TripoFit.WithinSize(measured, spec.TargetSize, spec.SizeTolerance))
                problems.Add($"{spec.SizeAxis} is {measured:0.###} m, expected {spec.TargetSize} m ±{spec.SizeTolerance:P0}");

            // Pivot error allowed: 2 % of the target size, at least 5 mm.
            float slack = Mathf.Max(0.005f, spec.TargetSize * 0.02f);
            var anchor = b.center;
            if (spec.Pivot == TripoPivot.BottomCentre) anchor.y = b.min.y;
            if (anchor.magnitude > slack)
                problems.Add($"{spec.Pivot} pivot is off by {anchor} (bounds {b.min}..{b.max})");
        }

        static void CheckColliderAndShadows(TripoAssetSpec spec, GameObject prefab, List<string> problems)
        {
            var colliders = prefab.GetComponentsInChildren<Collider>(true);
            if (spec.ColliderShape == TripoColliderShape.None && colliders.Length > 0)
                problems.Add($"{colliders.Length} collider(s); code moves and hit-tests this asset, so it must have none");
            if (spec.ColliderShape == TripoColliderShape.Box)
            {
                if (colliders.Length != 1 || !(colliders[0] is BoxCollider box) || box.gameObject != prefab)
                    problems.Add("expected exactly one BoxCollider, on the root");
                else
                {
                    var b = TripoFit.MeshBounds(prefab, prefab.transform.worldToLocalMatrix);
                    var cb = new Bounds(box.center, box.size);
                    if (Vector3.Distance(cb.center, b.center) > 0.01f || Vector3.Distance(cb.size, b.size) > 0.01f)
                        problems.Add($"box collider {cb} does not fit the mesh bounds {b}");
                }
            }

            var wanted = spec.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                if (r.shadowCastingMode != wanted)
                    problems.Add($"{r.name}: shadows {r.shadowCastingMode}, expected {wanted}");
        }

        static void CheckReferences(GameObject prefab, List<string> problems)
        {
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
            {
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c == null) { problems.Add($"{t.name}: missing script"); continue; }
                    if (c is MeshFilter mf && mf.sharedMesh == null) problems.Add($"{t.name}: MeshFilter has no mesh");

                    var so = new SerializedObject(c);
                    var p = so.GetIterator();
                    while (p.NextVisible(true))
                    {
                        if (p.propertyType == SerializedPropertyType.ObjectReference &&
                            p.objectReferenceValue == null && p.objectReferenceInstanceIDValue != 0)
                            problems.Add($"{t.name}.{c.GetType().Name}.{p.propertyPath}: missing reference");
                    }
                }
            }
        }

        static void CheckImportSettings(TripoAssetSpec spec, List<string> problems)
        {
            if (AssetImporter.GetAtPath(spec.ModelPath) is not ModelImporter model)
            {
                problems.Add($"no model at {spec.ModelPath}");
                return;
            }
            if (model.isReadable) problems.Add("model Read/Write is on (doubles mesh memory)");
            if (spec.Rig == TripoRig.Humanoid)
            {
                var avatar = AssetDatabase.LoadAllAssetsAtPath(spec.ModelPath).OfType<Avatar>().FirstOrDefault();
                if (avatar == null || !avatar.isValid || !avatar.isHuman)
                    problems.Add("rigged as Humanoid but the model has no valid human avatar");
            }

            if (!spec.HasTexture) return;
            var textures = AssetDatabase.FindAssets("t:Texture2D", new[] { spec.Folder })
                                        .Select(AssetDatabase.GUIDToAssetPath)
                                        .Select(AssetImporter.GetAtPath).OfType<TextureImporter>().ToArray();
            if (textures.Length == 0) problems.Add($"no texture in {spec.Folder}");
            foreach (var ti in textures)
            {
                if (ti.maxTextureSize > spec.TextureMaxSize)
                    problems.Add($"{ti.assetPath}: max size {ti.maxTextureSize}, cap {spec.TextureMaxSize}");
                if (!ti.mipmapEnabled) problems.Add($"{ti.assetPath}: no mip maps");
                if (ti.isReadable) problems.Add($"{ti.assetPath}: Read/Write is on");
                foreach (string platform in new[] { "Android", "iPhone" })
                {
                    var s = ti.GetPlatformTextureSettings(platform);
                    if (!s.overridden || s.maxTextureSize > spec.TextureMaxSize)
                        problems.Add($"{ti.assetPath}: no {platform} override within the size cap");
                }
            }
        }
    }
}
