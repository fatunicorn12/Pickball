using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Tools ▸ Pickleball ▸ Clean World. Repairs the World Labs academy export,
/// which is choppy in three separate ways, and splits it up to render cheaply:
///
///  * Black specks. Thousands of triangles map onto texels the bake never
///    painted (left pure black). Each one is re-pointed at the colour of its
///    nearest painted neighbour across the mesh, which is spatially right; the
///    nearest chart in the atlas is an unrelated surface. The black gutter
///    between charts is then filled outwards from the charts, so bilinear
///    filtering and mipmaps stop pulling black into chart edges.
///  * Floating shards. Pieces under <see cref="MinIslandTris"/> triangles that
///    touch nothing else are dropped. The bigger detached pieces are chunks of
///    tree canopy and are kept.
///  * Snagging collision. The collider was the visual mesh, shards and all; it
///    now uses the cleaned mesh, so it still matches exactly what is drawn.
///    (The export's own _collider.glb is not used: near the spawn it sits a
///    median 23 cm above the visible grass, off by over 50 cm in 17% of spots,
///    and is no smoother.)
///  * One giant renderer. The export is a single mesh, drawn whole every frame
///    wherever the camera looks. It is split into a <see cref="ChunkGrid"/>²
///    grid of renderers, each with its own collider, so frustum culling skips
///    what is off screen. The export also gives every triangle its own three
///    vertices; corners sharing a position and UV are merged, with normals
///    smoothed by position, which more than halves the vertex count.
///
/// Decimating was measured and rejected: 93% of the triangles span over 16 px
/// at 1080p from the nearest walkable spot, and 92.6% are directly visible
/// from somewhere walkable, so there is little to remove without it showing.
///
/// The originals are never touched: the cleaned chunks and texture are written
/// next to them and swapped into the open scene. The chunks are written as one
/// .glb rather than Mesh .assets so they land in Git LFS instead of ~100 MB of
/// YAML. Safe to re-run; after rebuilding the sandbox, run Setup World Look,
/// then this.
/// </summary>
public static class CleanWorldSetup
{
    const string WorldDir = "Assets/_Project/Art/World";
    const string WorldName = "AcademyWorld";
    const string SourcePath = WorldDir + "/Ancient Greek Scholarly Academy_texture_mesh.glb";
    const string CleanMeshPath = WorldDir + "/AcademyWorld_Clean.glb";
    const string FilledTexturePath = WorldDir + "/AcademyWorld_Filled.png";
    const string ChunksName = "Chunks";

    /// <summary>Detached pieces smaller than this are floaters. The real detached
    /// pieces in the academy (canopy chunks) are 60–250 tris and 2–5 m across;
    /// the floaters are mostly 1–20 tris.</summary>
    const int MinIslandTris = 50;

    /// <summary>The export gives every triangle its own three vertices, so
    /// positions within 1/WeldScale mesh units are merged to find which
    /// triangles touch.</summary>
    const float WeldScale = 1000f;

    /// <summary>Chunks per side of the ground-plane grid. From typical orbit-camera
    /// views, 8×8 draws about half the map per frame (4×4: two thirds; 16×16:
    /// 42% for three times the renderers).</summary>
    const int ChunkGrid = 8;

    [MenuItem("Tools/Pickleball/Clean World")]
    public static void Run()
    {
        var world = GameObject.Find(WorldName);
        if (world == null)
        {
            Debug.LogError($"No '{WorldName}' in the open scene — run Setup Chibi Sandbox first.");
            return;
        }
        // The glTF mesh node itself, not one of the chunks from an earlier run.
        var visual = world.GetComponentsInChildren<MeshRenderer>(true)
            .FirstOrDefault(r => r.transform.parent == null || r.transform.parent.name != ChunksName);
        var material = visual != null ? visual.sharedMaterial : null;
        if (material == null || !material.HasProperty("_BaseMap")
            || AssetDatabase.GetAssetPath(material).EndsWith(".glb"))
        {
            Debug.LogError("The world still has glTFast's material — run Setup World Look first, then this.");
            return;
        }

        try
        {
            Step("Reading the World Labs export", 0f);
            var source = LoadSource(SourcePath);
            int triCount = source.Indices.Length / 3;

            Step("Finding floating pieces", 0.15f);
            var keep = FindKeptTriangles(source, out var weld, out int weldCount, out int droppedPieces);
            int droppedTris = keep.Count(k => !k);

            Step("Re-colouring unpainted triangles", 0.25f);
            var colourFrom = ColourSources(source, keep, weld, weldCount, out int unpainted, out int recoloured);

            Step("Splitting into chunks", 0.35f);
            var chunks = BuildChunks(source, keep, colourFrom, weld, weldCount);
            WriteCleanGlb(CleanMeshPath, source, chunks);

            Step("Filling the texture gutter", 0.5f);
            int filled = FillGutter(source.Pixels, source.Width, source.Height);
            var tex = new Texture2D(source.Width, source.Height, TextureFormat.RGBA32, false);
            tex.SetPixels32(source.Pixels);
            tex.Apply(false);
            Step("Writing the filled texture", 0.6f);
            File.WriteAllBytes(FilledTexturePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            Step("Importing (the 8K texture compresses slowly)", 0.7f);
            var cleanTexture = ImportFilledTexture(source.Width);
            AssetDatabase.ImportAsset(CleanMeshPath, ImportAssetOptions.ForceUpdate);
            var chunkMeshes = AssetDatabase.LoadAllAssetsAtPath(CleanMeshPath).OfType<Mesh>()
                .OrderBy(m => m.name, StringComparer.Ordinal).ToArray();
            if (chunkMeshes.Length != chunks.Count || cleanTexture == null)
            {
                Debug.LogError($"Expected {chunks.Count} meshes in {CleanMeshPath} and a texture at " +
                               $"{FilledTexturePath}; found {chunkMeshes.Length} meshes.");
                return;
            }

            Step("Swapping into the scene", 0.95f);
            material.SetTexture("_BaseMap", cleanTexture);
            EditorUtility.SetDirty(material);
            PlaceChunks(visual, chunkMeshes);
            EditorSceneManager.MarkSceneDirty(world.scene);

            Debug.Log($"World cleaned: dropped {droppedPieces:n0} floating pieces ({droppedTris:n0} of {triCount:n0} tris); " +
                      $"re-coloured {recoloured:n0} of {unpainted:n0} unpainted triangles from their neighbours; " +
                      $"filled {filled:n0} gutter texels. Split into {chunks.Count} chunks with " +
                      $"{chunks.Sum(c => c.VertexCount):n0} vertices (the export had {source.Positions.Length / 3:n0}).");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    static void Step(string step, float t) => EditorUtility.DisplayProgressBar("Clean World", step, t);

    // ── Reading the export ──────────────────────────────────────────────────

    const uint GlbMagic = 0x46546C67;   // "glTF"
    const uint JsonChunk = 0x4E4F534A;  // "JSON"
    const uint BinChunk = 0x004E4942;   // "BIN\0"
    const int FloatType = 5126, UIntType = 5125, UShortType = 5123, UByteType = 5121;
    const int VertexTarget = 34962, IndexTarget = 34963;

    /// <summary>The export in glTF space (not Unity's): the cleaned copy is
    /// written back the same way, so glTFast converts both identically.</summary>
    class Source
    {
        public float[] Positions, Normals, Uvs;   // 3, 3 and 2 floats per vertex
        public int[] Indices;
        public Color32[] Pixels;                  // the baked texture, row 0 = bottom
        public int Width, Height;
        public List<object> Nodes;                // the scene hierarchy, copied over
        public object Scenes, Scene;
        public int MeshNode;                      // the node that holds the mesh
    }

    static Source LoadSource(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (BitConverter.ToUInt32(bytes, 0) != GlbMagic || BitConverter.ToUInt32(bytes, 16) != JsonChunk)
            throw new InvalidDataException($"{path} is not a binary glTF.");
        int jsonLength = BitConverter.ToInt32(bytes, 12);
        int i = 0;
        var gltf = ParseObject(Encoding.UTF8.GetString(bytes, 20, jsonLength), ref i);
        int bin = 20 + jsonLength + 8;   // past the BIN chunk's header

        var meshes = Arr(gltf["meshes"]);
        var primitives = Arr(Obj(meshes[0])["primitives"]);
        var images = gltf.TryGetValue("images", out var im) ? Arr(im) : null;
        var nodes = Arr(gltf["nodes"]);
        var meshNodes = Enumerable.Range(0, nodes.Count).Where(n => Obj(nodes[n]).ContainsKey("mesh")).ToList();
        if (meshes.Count != 1 || primitives.Count != 1 || images is not { Count: 1 } || meshNodes.Count != 1)
            throw new InvalidDataException($"{path}: expected one mesh, on one node, with one primitive and one baked texture.");
        var prim = Obj(primitives[0]);
        var attributes = Obj(prim["attributes"]);

        var image = View(gltf, Int(Obj(images[0]), "bufferView"));
        var imageBytes = new byte[Int(image, "byteLength")];
        Buffer.BlockCopy(bytes, bin + Int(image, "byteOffset", 0), imageBytes, 0, imageBytes.Length);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(imageBytes))
            throw new InvalidDataException($"{path}: could not decode the baked texture.");

        var source = new Source
        {
            Positions = ReadFloats(gltf, bytes, bin, Int(attributes, "POSITION"), "VEC3"),
            Normals = ReadFloats(gltf, bytes, bin, Int(attributes, "NORMAL"), "VEC3"),
            Uvs = ReadFloats(gltf, bytes, bin, Int(attributes, "TEXCOORD_0"), "VEC2"),
            Indices = ReadIndices(gltf, bytes, bin, Int(prim, "indices")),
            Pixels = tex.GetPixels32(),
            Width = tex.width,
            Height = tex.height,
            Nodes = nodes,
            Scenes = gltf["scenes"],
            Scene = gltf.TryGetValue("scene", out var scene) ? scene : null,
            MeshNode = meshNodes[0],
        };
        Object.DestroyImmediate(tex);
        if (source.Normals.Length != source.Positions.Length || source.Uvs.Length / 2 != source.Positions.Length / 3)
            throw new InvalidDataException($"{path}: positions, normals and UVs don't line up.");
        return source;
    }

    static float[] ReadFloats(Dictionary<string, object> gltf, byte[] bytes, int bin, int accessor, string type)
    {
        var acc = Obj(Arr(gltf["accessors"])[accessor]);
        if (Int(acc, "componentType") != FloatType || (string)acc["type"] != type)
            throw new InvalidDataException($"Accessor {accessor} is {acc["type"]}/{acc["componentType"]}, expected float {type}.");
        var view = View(gltf, Int(acc, "bufferView"));
        int count = Int(acc, "count"), components = type == "VEC2" ? 2 : 3, size = components * 4;
        int stride = Int(view, "byteStride", size);
        int start = bin + Int(view, "byteOffset", 0) + Int(acc, "byteOffset", 0);
        var data = new float[count * components];
        if (stride == size)
            Buffer.BlockCopy(bytes, start, data, 0, data.Length * 4);
        else
            for (int i = 0; i < count; i++)
                Buffer.BlockCopy(bytes, start + i * stride, data, i * size, size);
        return data;
    }

    static int[] ReadIndices(Dictionary<string, object> gltf, byte[] bytes, int bin, int accessor)
    {
        var acc = Obj(Arr(gltf["accessors"])[accessor]);
        var view = View(gltf, Int(acc, "bufferView"));
        int count = Int(acc, "count");
        int start = bin + Int(view, "byteOffset", 0) + Int(acc, "byteOffset", 0);
        var data = new int[count];
        switch (Int(acc, "componentType"))
        {
            case UIntType: Buffer.BlockCopy(bytes, start, data, 0, count * 4); break;
            case UShortType: for (int i = 0; i < count; i++) data[i] = BitConverter.ToUInt16(bytes, start + i * 2); break;
            case UByteType: for (int i = 0; i < count; i++) data[i] = bytes[start + i]; break;
            default: throw new InvalidDataException($"Accessor {accessor} is not an index buffer.");
        }
        return data;
    }

    static Dictionary<string, object> View(Dictionary<string, object> gltf, int index) =>
        Obj(Arr(gltf["bufferViews"])[index]);

    // ── Minimal JSON ────────────────────────────────────────────────────────
    // Objects become dictionaries, arrays lists, numbers doubles: enough for a
    // glTF header, without depending on a JSON package.

    static Dictionary<string, object> Obj(object o) => (Dictionary<string, object>)o;
    static List<object> Arr(object o) => (List<object>)o;

    static int Int(Dictionary<string, object> o, string key, int? fallback = null)
    {
        if (o.TryGetValue(key, out var v)) return Convert.ToInt32(v);
        return fallback ?? throw new InvalidDataException($"glTF is missing '{key}'.");
    }

    static Dictionary<string, object> ParseObject(string json, ref int i)
    {
        var obj = new Dictionary<string, object>();
        Expect(json, ref i, '{');
        while (Peek(json, ref i) != '}')
        {
            string key = ParseString(json, ref i);
            Expect(json, ref i, ':');
            obj[key] = ParseValue(json, ref i);
            if (Peek(json, ref i) == ',') i++;
        }
        i++;
        return obj;
    }

    static object ParseValue(string json, ref int i)
    {
        switch (Peek(json, ref i))
        {
            case '{':
                return ParseObject(json, ref i);
            case '[':
                var list = new List<object>();
                i++;
                while (Peek(json, ref i) != ']')
                {
                    list.Add(ParseValue(json, ref i));
                    if (Peek(json, ref i) == ',') i++;
                }
                i++;
                return list;
            case '"':
                return ParseString(json, ref i);
            default:
                int start = i;
                while (i < json.Length && ",:]} \t\r\n".IndexOf(json[i]) < 0) i++;
                string word = json.Substring(start, i - start);
                return word switch
                {
                    "true" => true,
                    "false" => false,
                    "null" => null,
                    _ => double.Parse(word, NumberStyles.Float, CultureInfo.InvariantCulture),
                };
        }
    }

    static string ParseString(string json, ref int i)
    {
        Expect(json, ref i, '"');
        var sb = new StringBuilder();
        while (json[i] != '"')
        {
            char c = json[i++];
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }
            switch (c = json[i++])
            {
                case 'u': sb.Append((char)Convert.ToInt32(json.Substring(i, 4), 16)); i += 4; break;
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                default: sb.Append(c); break;   // \" \\ \/
            }
        }
        i++;
        return sb.ToString();
    }

    static int SkipSpace(string json, ref int i)
    {
        while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        return i;
    }

    static char Peek(string json, ref int i) =>
        SkipSpace(json, ref i) < json.Length ? json[i] : throw new InvalidDataException("glTF JSON ends early.");

    static void Expect(string json, ref int i, char c)
    {
        if (Peek(json, ref i) != c)
            throw new InvalidDataException($"glTF JSON: expected '{c}' at {i}.");
        i++;
    }

    static void WriteJson(StringBuilder sb, object value)
    {
        switch (value)
        {
            case null: sb.Append("null"); break;
            case bool b: sb.Append(b ? "true" : "false"); break;
            case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
            case float f: sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); break;
            case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
            case string s:
                sb.Append('"');
                foreach (char c in s)
                {
                    if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                    else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                }
                sb.Append('"');
                break;
            case Dictionary<string, object> obj:
                sb.Append('{');
                bool firstKey = true;
                foreach (var kv in obj)
                {
                    if (!firstKey) sb.Append(',');
                    firstKey = false;
                    WriteJson(sb, kv.Key);
                    sb.Append(':');
                    WriteJson(sb, kv.Value);
                }
                sb.Append('}');
                break;
            case List<object> list:
                sb.Append('[');
                for (int k = 0; k < list.Count; k++)
                {
                    if (k > 0) sb.Append(',');
                    WriteJson(sb, list[k]);
                }
                sb.Append(']');
                break;
            default:
                throw new ArgumentException($"Can't write {value.GetType()} as JSON.");
        }
    }

    // ── Floating pieces ─────────────────────────────────────────────────────

    /// <summary>Per triangle: is it part of a piece of at least MinIslandTris
    /// triangles? Also gives the welded vertex id behind every index.</summary>
    static bool[] FindKeptTriangles(Source s, out int[] weld, out int weldCount, out int droppedPieces)
    {
        int vertexCount = s.Positions.Length / 3;
        var ids = new Dictionary<Vector3Int, int>(vertexCount);
        var vertexWeld = new int[vertexCount];
        for (int v = 0; v < vertexCount; v++)
        {
            var p = new Vector3(s.Positions[3 * v], s.Positions[3 * v + 1], s.Positions[3 * v + 2]);
            var key = Vector3Int.RoundToInt(p * WeldScale);
            if (!ids.TryGetValue(key, out int id)) ids[key] = id = ids.Count;
            vertexWeld[v] = id;
        }
        weldCount = ids.Count;
        weld = new int[s.Indices.Length];
        for (int i = 0; i < weld.Length; i++) weld[i] = vertexWeld[s.Indices[i]];

        // Union-find over welded vertices: a triangle joins its three corners.
        var parent = new int[weldCount];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int x)
        {
            while (parent[x] != x) x = parent[x] = parent[parent[x]];
            return x;
        }
        int triCount = weld.Length / 3;
        for (int t = 0; t < triCount; t++)
        {
            int a = Find(weld[3 * t]);
            parent[Find(weld[3 * t + 1])] = a;
            parent[Find(weld[3 * t + 2])] = a;
        }

        var root = new int[triCount];
        var size = new int[weldCount];
        for (int t = 0; t < triCount; t++)
        {
            root[t] = Find(weld[3 * t]);
            size[root[t]]++;
        }
        var keep = new bool[triCount];
        var dropped = new HashSet<int>();
        for (int t = 0; t < triCount; t++)
        {
            keep[t] = size[root[t]] >= MinIslandTris;
            if (!keep[t]) dropped.Add(root[t]);
        }
        droppedPieces = dropped.Count;
        return keep;
    }

    // ── Unpainted triangles ─────────────────────────────────────────────────

    static Vector2 CentroidUv(Source s, int t)
    {
        Vector2 sum = default;
        for (int c = 0; c < 3; c++)
        {
            int v = s.Indices[3 * t + c];
            sum += new Vector2(s.Uvs[2 * v], s.Uvs[2 * v + 1]);
        }
        return sum / 3f;
    }

    /// <summary>The bake leaves texels it never painted pure black.</summary>
    static bool Painted(Source s, Vector2 uv)
    {
        float u = uv.x - Mathf.Floor(uv.x), v = uv.y - Mathf.Floor(uv.y);
        int x = Mathf.Min((int)(u * s.Width), s.Width - 1);
        int y = Mathf.Min((int)((1f - v) * s.Height), s.Height - 1);   // glTF v runs down the image
        var c = s.Pixels[y * s.Width + x];
        return (c.r | c.g | c.b) != 0;
    }

    /// <summary>Per kept triangle, the triangle whose colour it shows: itself if
    /// its texels were painted, otherwise the nearest painted triangle reached
    /// through shared vertices, or -1 if its whole piece is unpainted.</summary>
    static int[] ColourSources(Source s, bool[] keep, int[] weld, int weldCount, out int unpainted, out int recoloured)
    {
        // Triangles around each welded vertex, packed (CSR).
        var first = new int[weldCount + 1];
        foreach (int w in weld) first[w + 1]++;
        for (int i = 0; i < weldCount; i++) first[i + 1] += first[i];
        var around = new int[weld.Length];
        var next = (int[])first.Clone();
        for (int i = 0; i < weld.Length; i++) around[next[weld[i]]++] = i / 3;

        // Breadth-first from every painted triangle at once.
        int triCount = keep.Length;
        var from = new int[triCount];
        var queue = new int[triCount];
        int head = 0, tail = 0, kept = 0;
        for (int t = 0; t < triCount; t++)
        {
            from[t] = -1;
            if (!keep[t]) continue;
            kept++;
            if (!Painted(s, CentroidUv(s, t))) continue;
            from[t] = t;
            queue[tail++] = t;
        }
        unpainted = kept - tail;
        int seeds = tail;
        while (head < tail)
        {
            int t = queue[head++];
            for (int c = 0; c < 3; c++)
            {
                int w = weld[3 * t + c];
                for (int k = first[w]; k < first[w + 1]; k++)
                {
                    int n = around[k];
                    if (!keep[n] || from[n] >= 0) continue;
                    from[n] = from[t];
                    queue[tail++] = n;
                }
            }
        }
        recoloured = tail - seeds;
        return from;
    }

    // ── Chunks ──────────────────────────────────────────────────────────────

    class Chunk
    {
        public string Name;
        public float[] Positions, Normals, Uvs;
        public int[] Indices;
        public int VertexCount => Positions.Length / 3;
    }

    /// <summary>The kept triangles, grouped by where their centroid falls on a
    /// ChunkGrid² grid over the ground plane. Within a chunk, corners that share
    /// a position and UV become one vertex; normals are averaged over every
    /// triangle meeting at a position, chunk borders included.</summary>
    static List<Chunk> BuildChunks(Source s, bool[] keep, int[] colourFrom, int[] weld, int weldCount)
    {
        var smooth = new Vector3[weldCount];
        for (int i = 0; i < weld.Length; i++)
            if (keep[i / 3]) smooth[weld[i]] += NormalAt(s, s.Indices[i]);

        // Ground-plane cell of every kept triangle, from its centroid in the
        // glTF scene (Y up).
        var toScene = MeshNodeMatrix(s);
        int triCount = keep.Length;
        var x = new float[triCount];
        var z = new float[triCount];
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        for (int t = 0; t < triCount; t++)
        {
            if (!keep[t]) continue;
            var c = toScene.MultiplyPoint3x4((PositionAt(s, s.Indices[3 * t]) + PositionAt(s, s.Indices[3 * t + 1])
                                              + PositionAt(s, s.Indices[3 * t + 2])) / 3f);
            x[t] = c.x;
            z[t] = c.z;
            minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
            minZ = Mathf.Min(minZ, c.z); maxZ = Mathf.Max(maxZ, c.z);
        }
        var cells = new List<int>[ChunkGrid * ChunkGrid];
        for (int t = 0; t < triCount; t++)
        {
            if (!keep[t]) continue;
            int cx = Mathf.Clamp((int)((x[t] - minX) / (maxX - minX) * ChunkGrid), 0, ChunkGrid - 1);
            int cz = Mathf.Clamp((int)((z[t] - minZ) / (maxZ - minZ) * ChunkGrid), 0, ChunkGrid - 1);
            (cells[cz * ChunkGrid + cx] ??= new List<int>()).Add(t);
        }

        var chunks = new List<Chunk>();
        var merged = new Dictionary<(int, int, int, int, int), int>();
        var pos = new List<float>();
        var nrm = new List<float>();
        var uv = new List<float>();
        var idx = new List<int>();
        for (int cell = 0; cell < cells.Length; cell++)
        {
            if (cells[cell] == null) continue;
            merged.Clear(); pos.Clear(); nrm.Clear(); uv.Clear(); idx.Clear();
            foreach (int t in cells[cell])
            {
                // An unpainted triangle samples a single point of its painted neighbour.
                bool borrowed = colourFrom[t] >= 0 && colourFrom[t] != t;
                var borrowedUv = borrowed ? CentroidUv(s, colourFrom[t]) : default;
                for (int c = 0; c < 3; c++)
                {
                    int v = s.Indices[3 * t + c];
                    var p = PositionAt(s, v);
                    var tc = borrowed ? borrowedUv : new Vector2(s.Uvs[2 * v], s.Uvs[2 * v + 1]);
                    var key = (Bits(p.x), Bits(p.y), Bits(p.z), Bits(tc.x), Bits(tc.y));
                    if (!merged.TryGetValue(key, out int o))
                    {
                        merged[key] = o = merged.Count;
                        // Opposite faces at one spot can cancel out; keep the face's own normal then.
                        var n = smooth[weld[3 * t + c]];
                        n = n.sqrMagnitude > 1e-8f ? n.normalized : NormalAt(s, v);
                        pos.Add(p.x); pos.Add(p.y); pos.Add(p.z);
                        nrm.Add(n.x); nrm.Add(n.y); nrm.Add(n.z);
                        uv.Add(tc.x); uv.Add(tc.y);
                    }
                    idx.Add(o);
                }
            }
            chunks.Add(new Chunk
            {
                Name = $"chunk_{cell % ChunkGrid}_{cell / ChunkGrid}",
                Positions = pos.ToArray(),
                Normals = nrm.ToArray(),
                Uvs = uv.ToArray(),
                Indices = idx.ToArray(),
            });
        }
        return chunks;
    }

    static Vector3 PositionAt(Source s, int v) => new(s.Positions[3 * v], s.Positions[3 * v + 1], s.Positions[3 * v + 2]);
    static Vector3 NormalAt(Source s, int v) => new(s.Normals[3 * v], s.Normals[3 * v + 1], s.Normals[3 * v + 2]);
    static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

    /// <summary>The mesh node's placement in the glTF scene (glTF space, Y up).</summary>
    static Matrix4x4 MeshNodeMatrix(Source s)
    {
        var parent = new Dictionary<int, int>();
        for (int n = 0; n < s.Nodes.Count; n++)
            if (Obj(s.Nodes[n]).TryGetValue("children", out var children))
                foreach (var child in Arr(children))
                    parent[Convert.ToInt32(child)] = n;

        var m = Matrix4x4.identity;
        for (int n = s.MeshNode; ; n = parent[n])
        {
            m = LocalMatrix(Obj(s.Nodes[n])) * m;
            if (!parent.ContainsKey(n)) return m;
        }
    }

    static Matrix4x4 LocalMatrix(Dictionary<string, object> node)
    {
        float[] Floats(string key, params float[] fallback) =>
            node.TryGetValue(key, out var v) ? Arr(v).Select(f => Convert.ToSingle(f)).ToArray() : fallback;
        if (node.ContainsKey("matrix"))
        {
            var a = Floats("matrix");
            var m = new Matrix4x4();
            for (int i = 0; i < 16; i++) m[i] = a[i];   // both column-major
            return m;
        }
        var t = Floats("translation", 0, 0, 0);
        var r = Floats("rotation", 0, 0, 0, 1);
        var sc = Floats("scale", 1, 1, 1);
        return Matrix4x4.TRS(new Vector3(t[0], t[1], t[2]), new Quaternion(r[0], r[1], r[2], r[3]), new Vector3(sc[0], sc[1], sc[2]));
    }

    // ── Writing the cleaned mesh ────────────────────────────────────────────

    /// <summary>One mesh per chunk, hung as child nodes under the export's mesh
    /// node so the hierarchy, and with it the placement, is unchanged. No
    /// material: the scene uses its own.</summary>
    static void WriteCleanGlb(string path, Source s, List<Chunk> chunks)
    {
        var bin = new byte[chunks.Sum(c => (c.Positions.Length + c.Normals.Length + c.Uvs.Length + c.Indices.Length) * 4)];
        var views = new List<object>();
        var accessors = new List<object>();
        var meshes = new List<object>();
        int offset = 0;

        int AddAccessor(Array data, int floatsOrInts, int componentType, int count, string type, int target)
        {
            int bytes = floatsOrInts * 4;
            Buffer.BlockCopy(data, 0, bin, offset, bytes);
            views.Add(new Dictionary<string, object>
                { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = bytes, ["target"] = target });
            offset += bytes;
            accessors.Add(new Dictionary<string, object>
                { ["bufferView"] = views.Count - 1, ["componentType"] = componentType, ["count"] = count, ["type"] = type });
            return accessors.Count - 1;
        }

        foreach (var c in chunks)
        {
            int n = c.VertexCount;
            int position = AddAccessor(c.Positions, n * 3, FloatType, n, "VEC3", VertexTarget);
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            for (int v = 0; v < n; v++)
            {
                var p = new Vector3(c.Positions[3 * v], c.Positions[3 * v + 1], c.Positions[3 * v + 2]);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            var bounds = Obj(accessors[position]);
            bounds["min"] = new List<object> { min.x, min.y, min.z };
            bounds["max"] = new List<object> { max.x, max.y, max.z };

            int normal = AddAccessor(c.Normals, n * 3, FloatType, n, "VEC3", VertexTarget);
            int uv = AddAccessor(c.Uvs, n * 2, FloatType, n, "VEC2", VertexTarget);
            int indices = AddAccessor(c.Indices, c.Indices.Length, UIntType, c.Indices.Length, "SCALAR", IndexTarget);
            meshes.Add(new Dictionary<string, object>
            {
                ["name"] = c.Name,
                ["primitives"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["attributes"] = new Dictionary<string, object>
                            { ["POSITION"] = position, ["NORMAL"] = normal, ["TEXCOORD_0"] = uv },
                        ["indices"] = indices,
                    },
                },
            });
        }

        var nodes = s.Nodes.Select(node => (object)new Dictionary<string, object>(Obj(node))).ToList();
        var meshNode = Obj(nodes[s.MeshNode]);
        meshNode.Remove("mesh");
        var children = meshNode.TryGetValue("children", out var existing) ? new List<object>(Arr(existing)) : new List<object>();
        for (int i = 0; i < chunks.Count; i++)
        {
            children.Add(nodes.Count);
            nodes.Add(new Dictionary<string, object> { ["name"] = chunks[i].Name, ["mesh"] = i });
        }
        meshNode["children"] = children;

        var root = new Dictionary<string, object>
        {
            ["asset"] = new Dictionary<string, object> { ["version"] = "2.0", ["generator"] = "Pickball Clean World" },
            ["scenes"] = s.Scenes,
            ["nodes"] = nodes,
            ["meshes"] = meshes,
            ["accessors"] = accessors,
            ["bufferViews"] = views,
            ["buffers"] = new List<object> { new Dictionary<string, object> { ["byteLength"] = bin.Length } },
        };
        if (s.Scene != null) root["scene"] = s.Scene;
        var json = new StringBuilder();
        WriteJson(json, root);
        WriteGlb(path, json.ToString(), bin);
    }

    static void WriteGlb(string path, string json, byte[] bin)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        int jsonPad = -jsonBytes.Length & 3, binPad = -bin.Length & 3;   // chunks are 4-byte aligned
        using var w = new BinaryWriter(File.Create(path));
        w.Write(GlbMagic);
        w.Write(2u);
        w.Write((uint)(12 + 8 + jsonBytes.Length + jsonPad + 8 + bin.Length + binPad));
        w.Write((uint)(jsonBytes.Length + jsonPad));
        w.Write(JsonChunk);
        w.Write(jsonBytes);
        w.Write(Encoding.ASCII.GetBytes(new string(' ', jsonPad)));
        w.Write((uint)(bin.Length + binPad));
        w.Write(BinChunk);
        w.Write(bin);
        w.Write(new byte[binPad]);
    }

    // ── Texture gutter ──────────────────────────────────────────────────────

    static readonly (int dx, int dy)[] Around =
        { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1) };

    /// <summary>Grows the painted charts out into the pure-black gutter one ring
    /// at a time, so every gutter texel takes the colour of the nearest chart.
    /// Returns the number of texels filled.</summary>
    static int FillGutter(Color32[] px, int w, int h)
    {
        const byte Coloured = 0, Empty = 1, Queued = 2;
        var state = new byte[px.Length];
        for (int i = 0; i < px.Length; i++)
            if ((px[i].r | px[i].g | px[i].b) == 0) state[i] = Empty;

        var ring = new List<int>();
        var nextRing = new List<int>();
        var colours = new List<Color32>();
        for (int i = 0; i < px.Length; i++)
        {
            if (state[i] != Empty) continue;
            int x = i % w, y = i / w;
            foreach (var (dx, dy) in Around)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h || state[ny * w + nx] != Coloured) continue;
                state[i] = Queued;
                ring.Add(i);
                break;
            }
        }

        int filled = 0;
        while (ring.Count > 0)
        {
            // Colour the whole ring from the texels painted before it, then commit.
            colours.Clear();
            foreach (int i in ring)
            {
                int x = i % w, y = i / w, r = 0, g = 0, b = 0, count = 0;
                foreach (var (dx, dy) in Around)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = ny * w + nx;
                    if (state[j] != Coloured) continue;
                    r += px[j].r; g += px[j].g; b += px[j].b;
                    count++;
                }
                colours.Add(new Color32((byte)(r / count), (byte)(g / count), (byte)(b / count), 255));
            }
            for (int k = 0; k < ring.Count; k++)
            {
                px[ring[k]] = colours[k];
                state[ring[k]] = Coloured;
            }
            filled += ring.Count;

            nextRing.Clear();
            foreach (int i in ring)
            {
                int x = i % w, y = i / w;
                foreach (var (dx, dy) in Around)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = ny * w + nx;
                    if (state[j] != Empty) continue;
                    state[j] = Queued;
                    nextRing.Add(j);
                }
            }
            (ring, nextRing) = (nextRing, ring);
        }
        return filled;
    }

    /// <summary>Imported like glTFast imported the original (sRGB, mipmapped,
    /// bilinear, repeat) but block-compressed: the original sits in memory
    /// uncompressed, 256 MB at 8K.</summary>
    static Texture2D ImportFilledTexture(int size)
    {
        AssetDatabase.ImportAsset(FilledTexturePath, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(FilledTexturePath);
        if (importer.maxTextureSize != size || importer.textureCompression != TextureImporterCompression.CompressedHQ)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = size;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(FilledTexturePath);
    }

    // ── Scene ───────────────────────────────────────────────────────────────

    /// <summary>Hangs a renderer and collider per chunk under the glTF mesh
    /// node. The node keeps its transform (the chunks are in its space) but no
    /// longer draws or collides itself.</summary>
    static void PlaceChunks(MeshRenderer visual, Mesh[] meshes)
    {
        var node = visual.transform;
        var old = node.Find(ChunksName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        if (visual.TryGetComponent<MeshCollider>(out var oldCollider)) Object.DestroyImmediate(oldCollider);
        visual.GetComponent<MeshFilter>().sharedMesh = null;
        visual.enabled = false;

        var root = new GameObject(ChunksName);
        root.transform.SetParent(node, false);
        foreach (var mesh in meshes)
        {
            var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            go.transform.SetParent(root.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = visual.sharedMaterial;
            r.shadowCastingMode = visual.shadowCastingMode;
            r.receiveShadows = visual.receiveShadows;
            go.GetComponent<MeshCollider>().sharedMesh = mesh;
        }
        foreach (var t in root.GetComponentsInChildren<Transform>())
        {
            t.gameObject.isStatic = visual.gameObject.isStatic;
            t.gameObject.layer = visual.gameObject.layer;
        }
        Physics.SyncTransforms();
    }
}
