using UnityEngine;

namespace Pickleball.Cutscene.Editor.Pipeline
{
    /// <summary>
    /// Stand-in actors for when the characters workstream's prefabs and its rig step (menu 4b)
    /// aren't present: a capsule Birdy with a CutsceneActor (id BIRDY) and anchors, a flat-disc
    /// propeller, and a CutsceneActor slot on the gameplay Player with anchors on its humanoid
    /// bones and its height measured from its renderers. Every anchor child is named exactly as
    /// <see cref="CutsceneAnchors.TransformName"/> says, the swap contract.
    /// </summary>
    public static class PlaceholderActors
    {
        /// <summary>Nominal Birdy height h, metres (his rotor at MC's chin).</summary>
        public const float BirdyHeight = 1.0f;

        /// <summary>Nominal Birdy head box, metres (0.55·h).</summary>
        public const float BirdyHeadBox = 0.55f;

        /// <summary>Builds the capsule Birdy under <paramref name="parent"/>.</summary>
        public static CutsceneActor BuildBirdy(Transform parent)
        {
            var root = new GameObject("Birdy_Actor (placeholder)");
            root.transform.SetParent(parent, false);
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, BirdyHeight * 0.5f, 0f);
            body.transform.localScale = new Vector3(0.6f, BirdyHeight * 0.5f, 0.6f);
            var beak = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beak.name = "Beak";
            Object.DestroyImmediate(beak.GetComponent<Collider>());
            beak.transform.SetParent(root.transform, false);
            beak.transform.localPosition = new Vector3(0f, 0.72f, 0.3f);
            beak.transform.localScale = new Vector3(0.12f, 0.08f, 0.2f);
            root.AddComponent<Animator>();

            var anchors = new Transform[CutsceneAnchors.Count];
            anchors[(int)CutsceneAnchor.Head] = Anchor(root.transform, CutsceneAnchor.Head, new Vector3(0f, BirdyHeight - BirdyHeadBox * 0.5f, 0f));
            anchors[(int)CutsceneAnchor.Eyes] = Anchor(root.transform, CutsceneAnchor.Eyes, new Vector3(0f, 0.76f, 0.28f));
            anchors[(int)CutsceneAnchor.HandR] = Anchor(root.transform, CutsceneAnchor.HandR, new Vector3(0.38f, 0.5f, 0f));
            anchors[(int)CutsceneAnchor.Feet] = Anchor(root.transform, CutsceneAnchor.Feet, Vector3.zero);
            anchors[(int)CutsceneAnchor.PropellerSocket] = Anchor(root.transform, CutsceneAnchor.PropellerSocket, new Vector3(0f, BirdyHeight, 0f));
            var actor = root.AddComponent<CutsceneActor>();
            actor.Configure(CutsceneSpeakers.Birdy, anchors, Voice(root.transform), BirdyHeight, BirdyHeadBox);
            return actor;
        }

        /// <summary>Builds the disc propeller under <paramref name="parent"/>.</summary>
        public static Transform BuildPropeller(Transform parent)
        {
            var root = new GameObject("Propeller (placeholder)");
            root.transform.SetParent(parent, false);
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Blades";
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.transform.SetParent(root.transform, false);
            disc.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            disc.transform.localScale = new Vector3(0.5f, 0.02f, 0.5f);
            return root.transform;
        }

        /// <summary>
        /// Gives the Player a CutsceneActor (id MC) if it has none: anchors parented to its humanoid
        /// bones, a voice source, and the rest-pose height measured from its renderers. Reuses
        /// existing anchor children, so running twice changes nothing.
        /// </summary>
        public static CutsceneActor RigPlayer(Transform player, BuildReport report)
        {
            var existing = player.GetComponent<CutsceneActor>();
            if (existing != null) return existing;

            var animator = player.GetComponentInChildren<Animator>(true);
            bool humanoid = animator != null && animator.isHuman;
            if (!humanoid) report?.Warn("Player has no humanoid Animator; MC anchors sit at nominal heights.");

            float top = MeasureTop(player);
            float height = Mathf.Max(0.5f, top - player.position.y);

            Transform headBone = humanoid ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            Transform handBone = humanoid ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            Transform leftEye = humanoid ? animator.GetBoneTransform(HumanBodyBones.LeftEye) : null;
            Transform rightEye = humanoid ? animator.GetBoneTransform(HumanBodyBones.RightEye) : null;

            var anchors = new Transform[CutsceneAnchors.Count];
            Vector3 headPos = headBone != null ? headBone.position : player.position + Vector3.up * height * 0.8f;
            anchors[(int)CutsceneAnchor.Head] = WorldAnchor(headBone != null ? headBone : player, CutsceneAnchor.Head, headPos);
            Vector3 eyes = leftEye != null && rightEye != null
                ? (leftEye.position + rightEye.position) * 0.5f
                : headPos + player.forward * height * 0.08f;
            anchors[(int)CutsceneAnchor.Eyes] = WorldAnchor(headBone != null ? headBone : player, CutsceneAnchor.Eyes, eyes);
            anchors[(int)CutsceneAnchor.HandR] = WorldAnchor(handBone != null ? handBone : player, CutsceneAnchor.HandR,
                handBone != null ? handBone.position : player.position + player.right * 0.3f + Vector3.up * height * 0.45f);
            anchors[(int)CutsceneAnchor.Feet] = WorldAnchor(player, CutsceneAnchor.Feet, player.position);
            var socket = FindDeep(player, "WeaponSocket");
            if (socket != null) anchors[(int)CutsceneAnchor.Blade] = WorldAnchor(socket, CutsceneAnchor.Blade, socket.position);
            else report?.Warn("Player has no WeaponSocket; MC.Blade falls back to MC.Head.");

            float headBox = Mathf.Max(0.05f, 2f * (top - headPos.y));
            var actor = player.gameObject.AddComponent<CutsceneActor>();
            actor.Configure(CutsceneSpeakers.MC, anchors, Voice(player), height, headBox);
            report?.Info($"Placeholder MC slot on {player.name}: H = {height:0.00} m, HB = {headBox:0.00} m.");
            return actor;
        }

        /// <summary>
        /// Top of the body in its current (rest) pose. Skinned meshes are baked and their vertices
        /// measured, because a skinned renderer's bounds are a loose box and the worn sword and
        /// effect meshes (MeshRenderers) stick out above the head. Falls back to MeshRenderer
        /// bounds only when there is no skinned mesh.
        /// </summary>
        static float MeasureTop(Transform root)
        {
            bool any = false;
            float top = float.MinValue;
            var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh == null) continue;
                    smr.BakeMesh(baked, true);
                    var t = smr.transform;
                    foreach (var v in baked.vertices)
                    {
                        float y = (t.position + t.rotation * v).y;
                        if (y > top) top = y;
                    }
                    any = true;
                }
            }
            finally
            {
                Object.DestroyImmediate(baked);
            }
            if (any) return top;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                top = any ? Mathf.Max(top, r.bounds.max.y) : r.bounds.max.y;
                any = true;
            }
            return any ? top : root.position.y + 1.6f;
        }

        static Transform Anchor(Transform parent, CutsceneAnchor anchor, Vector3 local)
        {
            var go = new GameObject(CutsceneAnchors.TransformName(anchor));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            return go.transform;
        }

        static Transform WorldAnchor(Transform parent, CutsceneAnchor anchor, Vector3 world)
        {
            string name = CutsceneAnchors.TransformName(anchor);
            var t = parent.Find(name);
            if (t == null)
            {
                t = new GameObject(name).transform;
                t.SetParent(parent, false);
            }
            t.position = world;
            t.localRotation = Quaternion.identity;
            return t;
        }

        static AudioSource Voice(Transform parent)
        {
            var t = parent.Find("Voice");
            if (t == null)
            {
                t = new GameObject("Voice").transform;
                t.SetParent(parent, false);
                t.localPosition = Vector3.up * 1.2f;
            }
            var source = t.GetComponent<AudioSource>();
            if (source == null) source = t.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            return source;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
