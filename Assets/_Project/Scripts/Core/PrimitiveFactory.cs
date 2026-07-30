using UnityEngine;

namespace Pickleball
{
    /// <summary>
    /// Helpers for building flat-shaded URP primitives and materials at runtime.
    /// Keeps the Wii-style "low-poly placeholder" look consistent across systems and
    /// means the whole scene can be constructed from code with no imported assets.
    /// </summary>
    public static class PrimitiveFactory
    {
        private static Shader _litShader;
        private static Shader LitShader => _litShader != null
            ? _litShader
            : (_litShader = Shader.Find("Universal Render Pipeline/Lit"));

        /// <summary>Flat pastel URP material (low smoothness for a matte, toy-like surface).</summary>
        public static Material CreateFlatMaterial(Color color, float smoothness = 0.05f)
        {
            var mat = new Material(LitShader);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", 0f);
            return mat;
        }

        /// <summary>A physics material with explicit friction/bounce (the ball uses 0 bounce; see BallController).</summary>
        public static PhysicsMaterial CreatePhysicsMaterial(float bounciness, float friction)
        {
            return new PhysicsMaterial("BallPhysics")
            {
                bounciness = bounciness,
                dynamicFriction = friction,
                staticFriction = friction,
                bounceCombine = PhysicsMaterialCombine.Maximum,
                frictionCombine = PhysicsMaterialCombine.Average
            };
        }

        /// <summary>
        /// Creates a box primitive (scaled cube). When <paramref name="withCollider"/> is false
        /// the BoxCollider is removed (useful for purely decorative geometry like court lines).
        /// </summary>
        public static GameObject CreateBox(string name, Vector3 size, Color color, Transform parent = null,
            bool withCollider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.localScale = size;
            if (parent != null) go.transform.SetParent(parent, false);

            go.GetComponent<MeshRenderer>().sharedMaterial = CreateFlatMaterial(color);

            if (!withCollider)
            {
                SafeDestroy(go.GetComponent<Collider>());
            }
            return go;
        }

        /// <summary>Creates a sphere primitive with a SphereCollider.</summary>
        public static GameObject CreateSphere(string name, float diameter, Color color, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.localScale = Vector3.one * diameter;
            if (parent != null) go.transform.SetParent(parent, false);
            go.GetComponent<MeshRenderer>().sharedMaterial = CreateFlatMaterial(color);
            return go;
        }

        /// <summary>Creates a capsule primitive used as a placeholder character.</summary>
        public static GameObject CreateCapsule(string name, float height, float radius, Color color, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            // Unity capsule default height is 2, radius 0.5.
            go.transform.localScale = new Vector3(radius / 0.5f, height / 2f, radius / 0.5f);
            if (parent != null) go.transform.SetParent(parent, false);
            go.GetComponent<MeshRenderer>().sharedMaterial = CreateFlatMaterial(color);
            return go;
        }

        /// <summary>Destroys an object correctly whether we're in Play mode or building in the Editor.</summary>
        public static void SafeDestroy(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }

    /// <summary>Shared Wii-style pastel palette so colours stay consistent across builders.</summary>
    public static class Palette
    {
        public static readonly Color CourtSurface = new Color(0.42f, 0.62f, 0.78f);   // pastel blue
        public static readonly Color Kitchen = new Color(0.85f, 0.55f, 0.45f);        // pastel terracotta
        public static readonly Color Line = new Color(0.96f, 0.96f, 0.96f);           // off-white
        public static readonly Color Net = new Color(0.9f, 0.9f, 0.92f);              // light grey
        public static readonly Color NetPost = new Color(0.35f, 0.35f, 0.4f);         // dark grey
        public static readonly Color Ground = new Color(0.55f, 0.74f, 0.5f);          // pastel grass
        public static readonly Color Sky = new Color(0.62f, 0.8f, 0.92f);             // pastel sky
        public static readonly Color Player = new Color(0.95f, 0.78f, 0.3f);          // warm yellow
        public static readonly Color Paddle = new Color(0.8f, 0.3f, 0.3f);            // red paddle
        public static readonly Color Ai = new Color(0.55f, 0.45f, 0.85f);             // purple
        public static readonly Color Ball = new Color(0.95f, 0.85f, 0.25f);           // pickleball yellow
    }
}
