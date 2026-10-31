using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pickleball.Cutscene.Editor.Sets
{
    /// <summary>
    /// The cutscene sets' materials and the Greek-key texture. Most set geometry shares one
    /// painterly material whose colour comes from vertex colours, so the palette lives in the
    /// mesh builders and stays consistent. Every property is rewritten on each run (the menu is
    /// the source of truth); the assets themselves are kept, so their GUIDs never change.
    /// </summary>
    public static class SetMaterials
    {
        /// <summary>Shader names.</summary>
        public const string PainterlyShader = "Pickball/Cutscene/Painterly",
                            VoidShader = "Pickball/Cutscene/Void Surface",
                            LineShader = "Pickball/Cutscene/Construction Line";

        /// <summary>Render queue of the void dome (the disc draws one after it).</summary>
        public const int VoidDomeQueue = 3500;
        /// <summary>Render queue of the construction lines: after the void.</summary>
        public const int LineQueue = 3600;

        /// <summary>The materials the builder uses.</summary>
        public sealed class Set
        {
            /// <summary>Vertex-coloured painterly surface: ground, stone, marble, trees, pots.</summary>
            public Material Painterly;
            /// <summary>The cream Greek-key trim on standable ledges.</summary>
            public Material GreekKey;
            /// <summary>Inside of the white void dome.</summary>
            public Material VoidDome;
            /// <summary>The white void's ground disc.</summary>
            public Material VoidDisc;
            /// <summary>Gold construction lines.</summary>
            public Material Line;
        }

        /// <summary>Creates or refreshes every material.</summary>
        public static Set Ensure()
        {
            var set = new Set
            {
                Painterly = LoadMaterial("SetPainterly", PainterlyShader),
                GreekKey = LoadMaterial("SetGreekKey", PainterlyShader),
                VoidDome = LoadMaterial("VoidDome", VoidShader),
                VoidDisc = LoadMaterial("VoidDisc", VoidShader),
                Line = LoadMaterial("ConstructionLines", LineShader),
            };

            Painterly(set.Painterly, null);
            Painterly(set.GreekKey, GreekKeyTexture());

            set.VoidDome.SetColor("_Color", Color.white);
            set.VoidDome.SetFloat("_RiseWeight", SetLayout.DomeRise);
            set.VoidDome.SetFloat("_Cull", (float)CullMode.Front);
            set.VoidDome.SetFloat("_SkyFade", 1f);
            // Queue order: every ordinary transparent (the greybox's floating signs, glass, veils)
            // first, then the void dome and disc over them, then the gold lines over the void.
            set.VoidDome.renderQueue = VoidDomeQueue;
            set.VoidDisc.SetColor("_Color", Color.white);
            set.VoidDisc.SetFloat("_RiseWeight", 0f);
            set.VoidDisc.SetFloat("_SkyFade", 0f);
            set.VoidDisc.SetFloat("_Cull", (float)CullMode.Off);
            set.VoidDisc.renderQueue = VoidDomeQueue + 1;

            set.Line.SetColor("_LineColor", new Color(1f, 0.78f, 0.3f, 1f));
            set.Line.SetFloat("_LineAlpha", 1f);
            set.Line.SetFloat("_MinPixelHalfWidth", 0.8f);
            set.Line.renderQueue = LineQueue;

            foreach (var m in new[] { set.Painterly, set.GreekKey, set.VoidDome, set.VoidDisc, set.Line })
                EditorUtility.SetDirty(m);
            return set;
        }

        private static void Painterly(Material m, Texture2D map)
        {
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseMap", map);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetColor("_ShadeColor", new Color(0.8f, 0.85f, 1f, 1f));
            m.SetFloat("_Wrap", 0.35f);
            m.SetFloat("_Softness", 0.55f);
            m.SetFloat("_ShadowStrength", 0.85f);
            m.SetFloat("_AOStrength", 0.5f);
            m.enableInstancing = true;
        }

        private static Material LoadMaterial(string name, string shaderName)
        {
            string path = $"{SetLayout.MaterialDir}/{name}.mat";
            var shader = Shader.Find(shaderName);
            if (shader == null) Debug.LogError($"Shader '{shaderName}' not found; is Shaders/Cutscene imported?");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (shader != null && mat.shader != shader)
            {
                mat.shader = shader;
            }
            return mat;
        }

        /// <summary>
        /// The Greek key (a meander: a square spiral hook on a running baseline, between two border
        /// rules), terracotta-brown on cream, four repeats across. Written as a PNG and imported to
        /// repeat; regenerated identically each run.
        /// </summary>
        public static Texture2D GreekKeyTexture()
        {
            string path = $"{SetLayout.TextureDir}/GreekKey.png";
            const int width = 128, height = 48, unit = 4, period = 32;
            var cream = new Color32(246, 233, 201, 255);
            var key = new Color32(150, 86, 52, 255);
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = cream;

            void Fill(int x0, int y0, int x1, int y1)
            {
                for (int y = Mathf.Max(y0, 0); y < Mathf.Min(y1, height); y++)
                for (int x = x0; x < x1; x++)
                    pixels[y * width + ((x % width) + width) % width] = key;
            }
            // Border rules.
            Fill(0, 0, width, 3);
            Fill(0, height - 3, width, height);
            // One meander per period, in units of `unit` px above a baseline at y = 8 px.
            (float, float, float, float)[] segments =
            {
                (0f, 0f, 8f, 0f), (0f, 0f, 0f, 7f), (0f, 7f, 6f, 7f), (6f, 7f, 6f, 2f),
                (6f, 2f, 2.5f, 2f), (2.5f, 2f, 2.5f, 4.5f), (2.5f, 4.5f, 4f, 4.5f),
            };
            for (int p = 0; p < width / period; p++)
            {
                foreach (var (ax, ay, bx, by) in segments)
                {
                    int x0 = p * period + Mathf.RoundToInt(Mathf.Min(ax, bx) * unit) - 2;
                    int x1 = p * period + Mathf.RoundToInt(Mathf.Max(ax, bx) * unit) + 2;
                    int y0 = 8 + Mathf.RoundToInt(Mathf.Min(ay, by) * unit) - 2;
                    int y1 = 8 + Mathf.RoundToInt(Mathf.Max(ay, by) * unit) + 2;
                    Fill(x0, y0, x1, y1);
                }
            }

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            string full = Path.GetFullPath(path);
            bool changed = !File.Exists(full) || !FileEquals(full, png);
            if (changed)
            {
                File.WriteAllBytes(full, png);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null && (importer.wrapMode != TextureWrapMode.Repeat || !importer.mipmapEnabled
                                     || importer.textureCompression != TextureImporterCompression.Uncompressed))
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static bool FileEquals(string path, byte[] bytes)
        {
            byte[] existing = File.ReadAllBytes(path);
            if (existing.Length != bytes.Length) return false;
            for (int i = 0; i < bytes.Length; i++)
                if (existing[i] != bytes[i]) return false;
            return true;
        }
    }
}
