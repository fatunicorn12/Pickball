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
/// which is choppy in three separate ways:
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
///
/// The originals are never touched: the cleaned mesh and texture are written
/// next to them and swapped into the open scene. The mesh is written as a .glb
/// rather than a Mesh .asset so it lands in Git LFS instead of ~150 MB of YAML.
/// Safe to re-run; after rebuilding the sandbox, run Setup World Look, then this.
/// </summary>
public static class CleanWorldSetup
{
    const string WorldDir = "Assets/_Project/Art/World";
    const string WorldName = "AcademyWorld";
    const string SourcePath = WorldDir + "/Ancient Greek Scholarly Academy_texture_mesh.glb";
    const string CleanMeshPath = WorldDir + "/AcademyWorld_Clean.glb";
    const string FilledTexturePath = WorldDir + "/AcademyWorld_Filled.png";

    /// <summary>Detached pieces smaller than this are floaters. The real detached
    /// pieces in the academy (canopy chunks) are 60–250 tris and 2–5 m across;
    /// the floaters are mostly 1–20 tris.</summary>
    const int MinIslandTris = 50;

    /// <summary>The export gives every triangle its own three vertices, so
    /// positions within 1/WeldScale mesh units are merged to find which
    /// triangles touch.</summary>
    const float WeldScale = 1000f;

    [MenuItem("Tools/Pickleball/Clean World")]
    public static void Run()
    {
        var world = GameObject.Find(WorldName);
        if (world == null)
        {
            Debug.LogError($"No '{WorldName}' in the open scene — run Setup Chibi Sandbox first.");
            return;
        }
        var visual = world.GetComponentInChildren<MeshFilter>();
        var material = visual != null ? visual.GetComponent<MeshRenderer>().sharedMaterial : null;
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

            Step("Re-colouring unpainted triangles", 0.3f);
            var colourFrom = ColourSources(source, keep, weld, weldCount, out int unpainted, out int recoloured);

            Step("Writing the cleaned mesh", 0.4f);
            WriteCleanGlb(source, keep, colourFrom);

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
            var cleanMesh = AssetDatabase.LoadAllAssetsAtPath(CleanMeshPath).OfType<Mesh>().FirstOrDefault();
            if (cleanMesh == null || cleanTexture == null)
            {
                Debug.LogError($"Could not load {CleanMeshPath} or {FilledTexturePath} after import.");
                return;
            }

            Step("Swapping into the scene", 0.95f);
            visual.sharedMesh = cleanMesh;
            if (!visual.TryGetComponent<MeshCollider>(out var collider))
                collider = visual.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = cleanMesh;
            Physics.SyncTransforms();
            material.SetTexture("_BaseMap", cleanTexture);
            EditorUtility.SetDirty(material);
            EditorSceneManager.MarkSceneDirty(world.scene);

            Debug.Log($"World cleaned: dropped {droppedPieces:n0} floating pieces ({droppedTris:n0} of {triCount:n0} tris); " +
                      $"re-coloured {recoloured:n0} of {unpainted:n0} unpainted triangles from their neighbours; " +
                      $"filled {filled:n0} gutter texels. Render and collision now use {CleanMeshPath}.");
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

    /// <summary>The export in glTF space (not Unity's): the cleaned copy is
    /// written back the same way, so glTFast converts both identically.</summary>
    class Source
    {
        public float[] Positions, Normals, Uvs;   // 3, 3 and 2 floats per vertex
        public int[] Indices;
        public Color32[] Pixels;                  // the baked texture, row 0 = bottom
        public int Width, Height;
        public string Scene, Scenes, Nodes;       // raw JSON, copied over verbatim
    }

    static Source LoadSource(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (BitConverter.ToUInt32(bytes, 0) != GlbMagic || BitConverter.ToUInt32(bytes, 16) != JsonChunk)
            throw new InvalidDataException($"{path} is not a binary glTF.");
        int jsonLength = BitConverter.ToInt32(bytes, 12);
        var gltf = ParseRoot(Encoding.UTF8.GetString(bytes, 20, jsonLength), out var raw);
        int bin = 20 + jsonLength + 8;   // past the BIN chunk's header

        var meshes = Arr(gltf["meshes"]);
        var primitives = Arr(Obj(meshes[0])["primitives"]);
        var images = gltf.TryGetValue("images", out var im) ? Arr(im) : null;
        if (meshes.Count != 1 || primitives.Count != 1 || images is not { Count: 1 })
            throw new InvalidDataException($"{path}: expected one mesh with one primitive and one baked texture.");
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
            Scene = raw.TryGetValue("scene", out var scene) ? scene : null,
            Scenes = raw["scenes"],
            Nodes = raw["nodes"],
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

    /// <summary>Parses the root object, and keeps each top-level value's raw
    /// text so parts of it can be copied over untouched.</summary>
    static Dictionary<string, object> ParseRoot(string json, out Dictionary<string, string> raw)
    {
        var values = new Dictionary<string, string>();
        int i = 0;
        var root = ParseObject(json, ref i, (key, from, to) => values[key] = json.Substring(from, to - from));
        raw = values;
        return root;
    }

    static Dictionary<string, object> ParseObject(string json, ref int i, Action<string, int, int> onValue = null)
    {
        var obj = new Dictionary<string, object>();
        Expect(json, ref i, '{');
        while (Peek(json, ref i) != '}')
        {
            string key = ParseString(json, ref i);
            Expect(json, ref i, ':');
            int from = SkipSpace(json, ref i);
            obj[key] = ParseValue(json, ref i);
            onValue?.Invoke(key, from, i);
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

    // ── Writing the cleaned mesh ────────────────────────────────────────────

    static void WriteCleanGlb(Source s, bool[] keep, int[] colourFrom)
    {
        int n = keep.Count(k => k) * 3;   // every triangle keeps its own three vertices
        var pos = new float[n * 3];
        var nrm = new float[n * 3];
        var uv = new float[n * 2];
        var min = Vector3.positiveInfinity;
        var max = Vector3.negativeInfinity;
        int o = 0;
        for (int t = 0; t < keep.Length; t++)
        {
            if (!keep[t]) continue;
            // An unpainted triangle samples a single point of its painted neighbour.
            bool borrowed = colourFrom[t] >= 0 && colourFrom[t] != t;
            var borrowedUv = borrowed ? CentroidUv(s, colourFrom[t]) : default;
            for (int c = 0; c < 3; c++, o++)
            {
                int v = s.Indices[3 * t + c];
                Array.Copy(s.Positions, 3 * v, pos, 3 * o, 3);
                Array.Copy(s.Normals, 3 * v, nrm, 3 * o, 3);
                if (borrowed) { uv[2 * o] = borrowedUv.x; uv[2 * o + 1] = borrowedUv.y; }
                else Array.Copy(s.Uvs, 2 * v, uv, 2 * o, 2);
                var p = new Vector3(pos[3 * o], pos[3 * o + 1], pos[3 * o + 2]);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }
        var indices = new int[n];
        for (int i = 0; i < n; i++) indices[i] = i;

        int posBytes = n * 12, uvBytes = n * 8, indexBytes = n * 4;
        var bin = new byte[posBytes * 2 + uvBytes + indexBytes];
        Buffer.BlockCopy(pos, 0, bin, 0, posBytes);
        Buffer.BlockCopy(nrm, 0, bin, posBytes, posBytes);
        Buffer.BlockCopy(uv, 0, bin, posBytes * 2, uvBytes);
        Buffer.BlockCopy(indices, 0, bin, posBytes * 2 + uvBytes, indexBytes);

        // Same scene and node hierarchy as the export, so it imports at the same
        // place; no material, since the scene uses its own.
        var json = new StringBuilder("{\"asset\":{\"version\":\"2.0\",\"generator\":\"Pickball Clean World\"}");
        if (s.Scene != null) json.Append(",\"scene\":").Append(s.Scene);
        json.Append(",\"scenes\":").Append(s.Scenes);
        json.Append(",\"nodes\":").Append(s.Nodes);
        json.Append(",\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"NORMAL\":1,\"TEXCOORD_0\":2},\"indices\":3}]}]");
        string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
        json.Append($",\"accessors\":[{{\"bufferView\":0,\"componentType\":{FloatType},\"count\":{n},\"type\":\"VEC3\",")
            .Append($"\"min\":[{F(min.x)},{F(min.y)},{F(min.z)}],\"max\":[{F(max.x)},{F(max.y)},{F(max.z)}]}},")
            .Append($"{{\"bufferView\":1,\"componentType\":{FloatType},\"count\":{n},\"type\":\"VEC3\"}},")
            .Append($"{{\"bufferView\":2,\"componentType\":{FloatType},\"count\":{n},\"type\":\"VEC2\"}},")
            .Append($"{{\"bufferView\":3,\"componentType\":{UIntType},\"count\":{n},\"type\":\"SCALAR\"}}]");
        json.Append($",\"bufferViews\":[{{\"buffer\":0,\"byteOffset\":0,\"byteLength\":{posBytes},\"target\":34962}},")
            .Append($"{{\"buffer\":0,\"byteOffset\":{posBytes},\"byteLength\":{posBytes},\"target\":34962}},")
            .Append($"{{\"buffer\":0,\"byteOffset\":{posBytes * 2},\"byteLength\":{uvBytes},\"target\":34962}},")
            .Append($"{{\"buffer\":0,\"byteOffset\":{posBytes * 2 + uvBytes},\"byteLength\":{indexBytes},\"target\":34963}}]");
        json.Append($",\"buffers\":[{{\"byteLength\":{bin.Length}}}]}}");

        WriteGlb(CleanMeshPath, json.ToString(), bin);
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
}
