using System.Collections.Generic;
using System.Text;
using Pickleball.Cutscene.Actors;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pickleball.Cutscene.Editor.Actors
{
    /// <summary>
    /// Turns any humanoid into a cutscene slot: anchors from the avatar's bones, rest-pose size,
    /// a voice source, mouth flap, eye swap (when the model's texture has a painted variant) and
    /// the rotor (when the model has a <c>Rotor</c> bone). Model-agnostic on purpose: it is how
    /// the actor prefabs are built, how the gameplay Player becomes MC's slot, and how a swapped
    /// model is re-measured (replace the model, run 4b, nothing else changes).
    /// </summary>
    /// <remarks>
    /// <para><b>Rest pose, whatever the scene holds.</b> Every point is read from the skinned
    /// meshes' bind data (vertices in renderer space, bone bind matrices from the bind poses),
    /// never from the current bone transforms. A Player saved mid-animation, or an actor posed by
    /// a timeline preview, measures exactly like the asset.</para>
    /// <para><b>Anchors ride the bones.</b> Head and Eyes are children of the head bone, Hand_R of
    /// the right hand, PropellerSocket of the head (at the Rotor bone's rest pose), Blade of the
    /// weapon socket; Feet is a child of the actor root (a blocking mark, so it doesn't follow a
    /// lying body). Each anchor faces the actor's forward at rest.</para>
    /// <para>Re-running finds its own anchors (the slot's references, or a childless, non-bone
    /// child with the anchor's name) and rewrites the same values, so a second run changes
    /// nothing.</para>
    /// </remarks>
    public static class ActorRigger
    {
        /// <summary>Name of the voice child.</summary>
        public const string VoiceName = "Voice";

        /// <summary>Name of the Rotor bone BirdyModelPostprocessor adds under Head.</summary>
        public const string RotorBoneName = "Rotor";

        /// <summary>Default eye-height / head-bone-height ratio for an unknown model.</summary>
        public const float DefaultEyeRatio = 1.1f;

        /// <summary>
        /// Eye height over head-bone height, measured once per known model from front-view
        /// renders of the bind pose (eye centres in Blender: chibi z 0.736 over head bone 0.676;
        /// Birdy 0.734 over 0.626).
        /// </summary>
        public static float EyeRatioFor(Avatar avatar)
        {
            string n = avatar != null ? avatar.name.ToLowerInvariant() : "";
            if (n.Contains("chibi")) return 1.089f;
            if (n.Contains("birdy")) return 1.172f;
            return DefaultEyeRatio;
        }

        private sealed class BindData
        {
            public readonly Dictionary<Transform, Matrix4x4> Bones = new Dictionary<Transform, Matrix4x4>();
            public readonly List<Vector3> Verts = new List<Vector3>();
            public readonly List<Vector3> HeadVerts = new List<Vector3>();
            public readonly List<Vector3> HandVerts = new List<Vector3>();
        }

        /// <summary>
        /// Rig a slot. <paramref name="bladeSource"/> (optional) is the weapon's guard pose: the
        /// Blade anchor is parented to its parent at its pose. Returns a one-line report.
        /// </summary>
        public static string Rig(GameObject actorRoot, Animator animator, string actorId,
            MouthFlapConfig flapConfig, PropellerConfig propellerConfig, Transform bladeParent, Vector3 bladeWorld,
            Quaternion bladeRotation)
        {
            var log = new StringBuilder();
            Transform root = actorRoot.transform;
            var actor = actorRoot.GetComponent<CutsceneActor>();
            if (actor == null) actor = actorRoot.AddComponent<CutsceneActor>();

            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            Transform handR = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform lowerArmR = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Transform middleR = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            Transform footL = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform footR = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform jaw = animator.GetBoneTransform(HumanBodyBones.Jaw);
            if (head == null || handR == null || footL == null || footR == null)
                return $"{actorRoot.name}: not a usable humanoid (missing head, right hand or feet); skipped";

            BindData bind = ReadBind(animator, head, handR);
            Matrix4x4 W(Transform t) => bind.Bones.TryGetValue(t, out var m) ? m : t.localToWorldMatrix;
            Vector3 P(Transform t) => W(t).MultiplyPoint3x4(Vector3.zero);

            // The model's own facing at bind, from its shoulders: a raw FBX can face −Z (the Tripo
            // guardians do) while every humanoid clip turns the body to the actor's +Z anyway.
            Transform armL = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform armR = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Vector3 right = armL != null && armR != null
                ? Vector3.ProjectOnPlane(P(armR) - P(armL), Vector3.up)
                : Vector3.zero;
            right = right.sqrMagnitude > 1e-6f ? right.normalized : root.right;
            Vector3 fwd = Vector3.Cross(right, Vector3.up).normalized;
            Quaternion facing = Quaternion.LookRotation(fwd, Vector3.up);

            // ---- sizes ------------------------------------------------------------------------
            // Measured against the bind pose's own floor (its lowest skin point), not the actor
            // root: on the sandbox Player (cloth-generated mesh) the bind frame measured 0.64 m off
            // the root, so root-relative heights came out 0.64 m too tall. Sizes are relative to
            // that floor; only Feet uses the root.
            float top = float.NegativeInfinity, ground = float.PositiveInfinity;
            foreach (var v in bind.Verts)
            {
                top = Mathf.Max(top, v.y);
                ground = Mathf.Min(ground, v.y);
            }
            if (float.IsInfinity(top))
            {
                top = P(head).y + 0.2f;
                ground = Mathf.Min(P(footL).y, P(footR).y);
            }

            Vector3 headCentre = P(head);
            float headSkinHeight = -1f;
            if (bind.HeadVerts.Count > 0)
            {
                Vector3 mn = bind.HeadVerts[0], mx = mn;
                foreach (var v in bind.HeadVerts) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
                headCentre = ActorMeasurement.BoxCentre(mn, mx);
                headSkinHeight = mx.y - mn.y;
            }

            // ---- anchor points ------------------------------------------------------------------
            // Feet: on the actor root's ground, offset from the root as the feet sit from the hips
            // at bind (the root is the blocking mark; the hips stand over it).
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Vector3 feetBind = ActorMeasurement.FeetPoint(P(footL), P(footR), ground);
            Vector3 hipsBind = hips != null ? P(hips) : feetBind;
            Vector3 feetOffset = Vector3.ProjectOnPlane(feetBind - hipsBind, Vector3.up);
            Vector3 feet = ActorMeasurement.FeetPoint(root.position + feetOffset, root.position + feetOffset, root.position.y);

            float eyeY = ActorMeasurement.EyeHeight(ground, P(head).y, EyeRatioFor(animator.avatar));
            float best = float.NegativeInfinity;
            foreach (var v in bind.HeadVerts)
            {
                if (Mathf.Abs(v.y - eyeY) > 0.02f || Mathf.Abs(Vector3.Dot(v - headCentre, right)) > 0.025f) continue;
                best = Mathf.Max(best, Vector3.Dot(v - headCentre, fwd));
            }
            if (float.IsInfinity(best)) best = 0.5f * Mathf.Max(0.05f, top - headCentre.y);
            Vector3 eyes = headCentre + fwd * best;
            eyes.y = eyeY;

            Vector3 hand;
            if (middleR != null && bind.Bones.ContainsKey(middleR))
                hand = Vector3.Lerp(P(handR), P(middleR), 0.5f);
            else
            {
                hand = P(handR);
                Vector3 along = lowerArmR != null ? (P(handR) - P(lowerArmR)).normalized : right;
                float far = float.NegativeInfinity;
                foreach (var v in bind.HandVerts)
                {
                    float d = Vector3.Dot(v - P(handR), along);
                    if (d > far) { far = d; hand = v; }
                }
            }

            // ---- anchors --------------------------------------------------------------------------
            var bones = new HashSet<Transform>(bind.Bones.Keys);
            var anchors = new Transform[CutsceneAnchors.Count];
            anchors[(int)CutsceneAnchor.Head] = Place(actor, CutsceneAnchor.Head, head, W(head), headCentre, facing, bones);
            anchors[(int)CutsceneAnchor.Eyes] = Place(actor, CutsceneAnchor.Eyes, head, W(head), eyes, facing, bones);
            anchors[(int)CutsceneAnchor.HandR] = Place(actor, CutsceneAnchor.HandR, handR, W(handR), hand, facing, bones);
            anchors[(int)CutsceneAnchor.Feet] = Place(actor, CutsceneAnchor.Feet, root, root.localToWorldMatrix, feet, root.rotation, bones);

            Transform rotor = FindRotor(head, bones);
            if (rotor != null)
            {
                Matrix4x4 rb = W(rotor);
                anchors[(int)CutsceneAnchor.PropellerSocket] =
                    Place(actor, CutsceneAnchor.PropellerSocket, head, W(head), rb.MultiplyPoint3x4(Vector3.zero), rb.rotation, bones);
            }
            else Remove(actor, CutsceneAnchor.PropellerSocket);

            if (actorId == CutsceneSpeakers.MC)
            {
                anchors[(int)CutsceneAnchor.Blade] = bladeParent != null
                    ? Place(actor, CutsceneAnchor.Blade, bladeParent, bladeParent.localToWorldMatrix, bladeWorld, bladeRotation, bones)
                    : Place(actor, CutsceneAnchor.Blade, handR, W(handR), hand, facing, bones);
            }
            else Remove(actor, CutsceneAnchor.Blade);

            // ---- voice, size, slot ------------------------------------------------------------------
            AudioSource voice = EnsureVoice(root);
            float height = ActorMeasurement.Height(top, ground);
            // HB is the visible head: the height of the skin weighted to the head bone. Twice the
            // head centre's distance to the model's top counted Birdy's propeller mast (on the
            // Rotor bone, above the helmet) and read about 1.9x his real head, so every Birdy
            // framing in HB was looser than the shot list meant and the "TCUs" were mediums.
            float headBox = headSkinHeight > 0f
                ? Mathf.Max(ActorMeasurement.MinSize, headSkinHeight)
                : ActorMeasurement.HeadBox(top, headCentre.y);
            actor.Configure(actorId, anchors, voice, height, headBox);
            Record(actor);

            // ---- presentation components ------------------------------------------------------------
            var flap = actorRoot.GetComponent<MouthFlap>();
            if (flap == null) flap = actorRoot.AddComponent<MouthFlap>();
            flap.Configure(flapConfig, voice, jaw != null ? jaw : head, root);
            Record(flap);

            string eyeNote = ConfigureEyeSwap(animator);

            string rotorNote = "no rotor";
            var cutsceneRotor = animator.GetComponent<CutsceneRotor>();
            if (rotor != null)
            {
                if (cutsceneRotor == null) cutsceneRotor = animator.gameObject.AddComponent<CutsceneRotor>();
                cutsceneRotor.Configure(propellerConfig, rotor);
                Record(cutsceneRotor);
                rotorNote = "rotor on " + rotor.name;
            }
            else if (cutsceneRotor != null) Object.DestroyImmediate(cutsceneRotor, true);

            log.Append($"{actorRoot.name} ({actorId}): H {height:0.000} m, HB {headBox:0.000} m, head centre +{headCentre.y - ground:0.000}, " +
                       $"eyes +{eyeY - ground:0.000}, {eyeNote}, {rotorNote}");
            return log.ToString();
        }

        private static BindData ReadBind(Animator animator, Transform head, Transform handR)
        {
            var data = new BindData();
            foreach (var smr in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = smr.sharedMesh;
                if (mesh == null) continue;
                Transform[] bones = smr.bones;
                Matrix4x4[] bindposes = mesh.bindposes;
                Matrix4x4 toWorld = smr.transform.localToWorldMatrix;
                int headIndex = -1, handIndex = -1;
                for (int i = 0; i < bones.Length && i < bindposes.Length; i++)
                {
                    if (bones[i] == null) continue;
                    if (!data.Bones.ContainsKey(bones[i])) data.Bones[bones[i]] = toWorld * bindposes[i].inverse;
                    if (bones[i] == head) headIndex = i;
                    if (bones[i] == handR) handIndex = i;
                }
                Vector3[] verts = mesh.vertices;
                BoneWeight[] weights = mesh.boneWeights;
                for (int v = 0; v < verts.Length; v++)
                {
                    Vector3 w = toWorld.MultiplyPoint3x4(verts[v]);
                    data.Verts.Add(w);
                    if (v >= weights.Length) continue;
                    if (headIndex >= 0 && Weight(weights[v], headIndex) >= 0.5f) data.HeadVerts.Add(w);
                    if (handIndex >= 0 && Weight(weights[v], handIndex) >= 0.5f) data.HandVerts.Add(w);
                }
            }
            return data;
        }

        private static float Weight(BoneWeight w, int bone)
        {
            float s = 0f;
            if (w.boneIndex0 == bone) s += w.weight0;
            if (w.boneIndex1 == bone) s += w.weight1;
            if (w.boneIndex2 == bone) s += w.weight2;
            if (w.boneIndex3 == bone) s += w.weight3;
            return s;
        }

        private static Transform FindRotor(Transform head, HashSet<Transform> bones)
        {
            for (int i = 0; i < head.childCount; i++)
            {
                Transform c = head.GetChild(i);
                if (c.name == RotorBoneName && bones.Contains(c)) return c;
            }
            return null;
        }

        /// <summary>Find or create an anchor under a parent and put it at a world point and rotation (as at bind).</summary>
        private static Transform Place(CutsceneActor actor, CutsceneAnchor which, Transform parent, Matrix4x4 parentBind,
            Vector3 world, Quaternion worldRotation, HashSet<Transform> bones)
        {
            string name = CutsceneAnchors.TransformName(which);
            Transform a = actor.GetAnchor(which);
            if (a != null && (a.name != name || bones.Contains(a) || a.childCount > 0)) a = null;
            if (a == null)
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform c = parent.GetChild(i);
                    if (c.name == name && !bones.Contains(c) && c.childCount == 0) { a = c; break; }
                }
            }
            if (a == null)
            {
                a = new GameObject(name).transform;
                Undo.RegisterCreatedObjectUndo(a.gameObject, "Rig cutscene actor");
            }
            if (a.parent != parent) a.SetParent(parent, false);
            Matrix4x4 inv = parentBind.inverse;
            Vector3 local = inv.MultiplyPoint3x4(world);
            Quaternion localRot = Quaternion.Normalize(Quaternion.Inverse(parentBind.rotation) * worldRotation);
            a.localPosition = Round(local);
            a.localRotation = localRot;
            a.localScale = Vector3.one;
            Record(a);
            return a;
        }

        private static Vector3 Round(Vector3 v)
        {
            // Six decimals: tiny float noise between runs must not show up as a diff.
            return new Vector3((float)System.Math.Round(v.x, 6), (float)System.Math.Round(v.y, 6), (float)System.Math.Round(v.z, 6));
        }

        private static void Remove(CutsceneActor actor, CutsceneAnchor which)
        {
            Transform a = actor.GetAnchor(which);
            if (a == null || a.name != CutsceneAnchors.TransformName(which) || a.childCount > 0) return;
            // An anchor that came with a prefab can't be destroyed on its instance; leave it unused.
            bool fromPrefab = PrefabUtility.IsPartOfPrefabInstance(a.gameObject) &&
                              !PrefabUtility.IsAddedGameObjectOverride(a.gameObject);
            if (!fromPrefab) Undo.DestroyObjectImmediate(a.gameObject);
        }

        private static AudioSource EnsureVoice(Transform root)
        {
            Transform v = root.Find(VoiceName);
            if (v == null)
            {
                v = new GameObject(VoiceName).transform;
                v.SetParent(root, false);
                Undo.RegisterCreatedObjectUndo(v.gameObject, "Rig cutscene actor");
            }
            var src = v.GetComponent<AudioSource>();
            if (src == null) src = v.gameObject.AddComponent<AudioSource>();
            // 2D: cutscene dialogue must read the same in a wide shot 15 m away as in a close-up,
            // and the camera cuts would make a 3D voice jump in level and pan.
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f;
            src.priority = 64;
            src.dopplerLevel = 0f;
            Record(src);
            return src;
        }

        private static string ConfigureEyeSwap(Animator animator)
        {
            var variants = new List<(string original, string name, string texture)>
            {
                (CutsceneActorPaths.ChibiBaseColor, "Closed", CutsceneActorPaths.ChibiEyesClosed),
                (CutsceneActorPaths.BirdyBaseColor, "Drop", CutsceneActorPaths.BirdyEyesDrop),
            };
            var swap = animator.GetComponent<EyeSwap>();
            foreach (var (original, name, texture) in variants)
            {
                var orig = AssetDatabase.LoadAssetAtPath<Texture>(original);
                var tex = AssetDatabase.LoadAssetAtPath<Texture>(texture);
                if (orig == null || tex == null) continue;
                var targets = new List<Renderer>();
                foreach (var r in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Material m = r.sharedMaterial;
                    if (m != null && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") == orig) targets.Add(r);
                }
                if (targets.Count == 0) continue;
                if (swap == null) swap = animator.gameObject.AddComponent<EyeSwap>();
                swap.Configure(targets.ToArray(), "_BaseMap", new[] { name }, new[] { tex });
                Record(swap);
                return $"eye swap '{name}' on {targets.Count} renderer(s)";
            }
            if (swap != null) Object.DestroyImmediate(swap, true);
            return "no eye variant for this texture";
        }

        private static void Record(Object o)
        {
            if (o != null && PrefabUtility.IsPartOfPrefabInstance(o)) PrefabUtility.RecordPrefabInstancePropertyModifications(o);
            EditorUtility.SetDirty(o);
        }
    }
}
