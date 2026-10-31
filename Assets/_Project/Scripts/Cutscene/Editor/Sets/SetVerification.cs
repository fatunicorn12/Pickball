using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Pickleball.Cutscene.Editor.Sets
{
    /// <summary>
    /// In-Editor checks of the sets against the real greybox, for a scene that holds both (the
    /// scene builder's copy of AcademyGreybox, or a scratch scene with the set prefabs placed at
    /// identity). Raycasts the physics scene, so it sees exactly what the player and the cameras
    /// will. Call from a script or the Editor console; nothing here changes the scene.
    /// </summary>
    public static class SetVerification
    {
        /// <summary>MC's eye height above his feet, metres (0.9 H).</summary>
        public const float EyeHeight = 1.45f;

        /// <summary>
        /// Line-of-sight checks: from Crest_View's eye to the gnomon, the rim gaze point, the black
        /// sun and the Overlook's north parapet; from MC_Start to the ledge and Birdy_Land; from
        /// Ledge_View to the ledge and the top two steps. Returns one line per check.
        /// </summary>
        public static string Sightlines()
        {
            Physics.SyncTransforms();
            var report = new StringBuilder();
            Vector3 crestEye = SetLayout.Ground(SetLayout.CrestViewXZ) + Vector3.up * EyeHeight;
            Check(report, "Crest → Gnomon_Aim", crestEye, SetLayout.GnomonAim, 1.2f);
            Check(report, "Crest → Rim_Gaze", crestEye, SetLayout.RimGaze, 0f);
            Check(report, "Crest → BlackSun", crestEye, SetLayout.BlackSun, GreyboxReference.BlackSunRadius);
            foreach (float x in new[] { -25f, -10f, 10f, 25f })
                Check(report, $"Crest → north parapet x {x}", crestEye, new Vector3(x, 0.95f, 59.7f), 0.5f);

            // The propeller (about 0.35 m tall) on the ledge, and the ledge's trimmed front edge.
            Vector3 prop = SetLayout.PropellerLedge + Vector3.up * 0.35f;
            Vector3 ledgeEdge = new(SetLayout.PierCentre.x, SetLayout.LedgeTop - 0.15f,
                                    SetLayout.PierCentre.y - SetLayout.PierSize.y * 0.5f - 0.1f);
            Vector3 mcEye = SetLayout.ShrineCentre + Vector3.up * 1.3f;
            Check(report, "MC_Start → propeller on the ledge", mcEye, prop, 0.1f);
            Check(report, "MC_Start → ledge trim", mcEye, ledgeEdge, 0f);
            Check(report, "MC_Start → Birdy_Land", mcEye, SetLayout.BirdyLand + Vector3.up * 0.5f, 0f);
            Check(report, "Ledge_View → propeller on the ledge", SetLayout.LedgeView, prop, 0.1f);
            for (int i = 1; i <= 2; i++)
            {
                Vector3 s = SetLayout.LedgeSteps[i];
                var front = new Vector3(s.x, s.y - 0.1f, s.z - SetLayout.StepSize.y * 0.5f - 0.1f);
                Check(report, $"Ledge_View → Ledge_Step_{i + 1} front edge", SetLayout.LedgeView, front, 0f);
            }
            Vector3 meadow = SetLayout.Ground(0f, -82f) + Vector3.up * EyeHeight;
            Check(report, "Meadow (0, -82) → propeller on the ledge", meadow, prop, 0.1f);
            Check(report, "Meadow (0, -82) → ledge trim (east face)", meadow,
                  new Vector3(SetLayout.PierCentre.x + SetLayout.PierSize.x * 0.5f + 0.1f, SetLayout.LedgeTop - 0.15f, SetLayout.PierCentre.y), 0f);
            return report.ToString();
        }

        private static void Check(StringBuilder report, string label, Vector3 from, Vector3 to, float stopShort)
        {
            Vector3 d = to - from;
            float length = d.magnitude - stopShort;
            bool blocked = Physics.Raycast(from, d.normalized, out RaycastHit hit, length, ~0, QueryTriggerInteraction.Ignore);
            report.AppendLine(blocked ? $"BLOCKED {label}: by {hit.collider.name} at {hit.point}" : $"clear   {label}");
        }

        /// <summary>
        /// Walks the two routes the player needs (spawn to the ledge; shrine up the path, over the
        /// crest and down into the Gymnasium), sampling the ground by raycast every 0.25 m. Reports
        /// every rise the controller can't walk (over its 0.3 m step and 50° slope) with the jump it
        /// needs, every drop, and anything solid in the way at chest height.
        /// </summary>
        public static string Walk()
        {
            Physics.SyncTransforms();
            var report = new StringBuilder();
            var toLedge = new List<Vector3>
            {
                SetLayout.PlaySpawn, SetLayout.Ground(-4.5f, -88.6f), SetLayout.Ground(-13.75f, -89.2f),
                SetLayout.LedgeSteps[0], SetLayout.LedgeSteps[1], SetLayout.LedgeSteps[2],
                new Vector3(SetLayout.LedgeSteps[2].x, SetLayout.LedgeTop, SetLayout.PropellerLedge.z), SetLayout.PropellerLedge,
            };
            WalkRoute(report, "Play_Spawn → ledge", toLedge, allowJumpsUpTo: 2f);

            var up = new List<Vector3> { SetLayout.ShrineCentre };
            foreach (Vector2 p in SetLayout.PathPoints) if (p.y > -89f) up.Add(SetLayout.Ground(p));
            foreach (Vector2 p in SetLayout.DescentPoints) up.Add(SetLayout.Ground(p));
            up.Add(new Vector3(24.5f, 0.9f, -50.2f));
            up.Add(new Vector3(24.5f, 0f, -46f));
            WalkRoute(report, "Shrine → crest → Gymnasium lawn", up, allowJumpsUpTo: 0f);
            return report.ToString();
        }

        private static void WalkRoute(StringBuilder report, string label, List<Vector3> route, float allowJumpsUpTo)
        {
            report.AppendLine($"== {label}");
            float maxRise = 0f, maxSlope = 0f;
            int problems = 0;
            Vector3? last = null;
            for (int i = 0; i < route.Count - 1; i++)
            {
                Vector3 a = route[i], b = route[i + 1];
                float len = new Vector2(b.x - a.x, b.z - a.z).magnitude;
                int steps = Mathf.Max(1, Mathf.CeilToInt(len / 0.25f));
                for (int k = 0; k <= steps; k++)
                {
                    Vector3 p = Vector3.Lerp(a, b, k / (float)steps);
                    if (!Physics.Raycast(new Vector3(p.x, p.y + 20f, p.z), Vector3.down, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        report.AppendLine($"  NO GROUND at {p}");
                        problems++;
                        continue;
                    }
                    Vector3 g = hit.point;
                    if (last.HasValue)
                    {
                        Vector3 prev = last.Value;
                        float rise = g.y - prev.y;
                        float run = new Vector2(g.x - prev.x, g.z - prev.z).magnitude;
                        float slope = run > 1e-4f ? Mathf.Atan2(rise, run) * Mathf.Rad2Deg : 90f;
                        if (rise > 0.3f && slope > 50f)
                        {
                            maxRise = Mathf.Max(maxRise, rise);
                            bool ok = rise <= allowJumpsUpTo;
                            if (!ok) problems++;
                            report.AppendLine($"  {(ok ? "jump " : "WALL ")} {rise:0.00} m up at {g} (onto {hit.collider.name})");
                        }
                        else if (rise > 0f) maxSlope = Mathf.Max(maxSlope, slope);
                        if (rise < -1.2f) report.AppendLine($"  drop {-rise:0.00} m at {g} (onto {hit.collider.name})");

                        Vector3 chestA = prev + Vector3.up * 1f, chestB = g + Vector3.up * 1f;
                        Vector3 dir = chestB - chestA;
                        if (dir.sqrMagnitude > 1e-6f && rise <= 0.3f &&
                            Physics.SphereCast(chestA, 0.3f, dir.normalized, out RaycastHit block, dir.magnitude, ~0, QueryTriggerInteraction.Ignore))
                        {
                            report.AppendLine($"  BLOCKED at chest height by {block.collider.name} near {block.point}");
                            problems++;
                        }
                    }
                    last = g;
                }
            }
            report.AppendLine($"  max walkable slope {maxSlope:0.0}°, max jump {maxRise:0.00} m, problems {problems}");
        }

        /// <summary>Renders the scene from a pose to a PNG (outside Assets). Uses a temporary
        /// camera that is never saved.</summary>
        public static void Render(Vector3 position, Quaternion rotation, float verticalFov, string pngPath, int width = 1280, int height = 720)
        {
            var go = new GameObject("SetVerificationCamera") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.transform.SetPositionAndRotation(position, rotation);
                cam.fieldOfView = verticalFov;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 3000f;
                cam.clearFlags = CameraClearFlags.Skybox;
                var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = previous;
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(pngPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
