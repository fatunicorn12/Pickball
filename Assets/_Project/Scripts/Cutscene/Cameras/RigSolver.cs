using UnityEngine;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// Resolves the shot list's notation against a <see cref="RigScene"/>: points, frames, Pair.Side,
    /// keyed paths, crane splines, aim interpolation, focus pulls and the Birdy size cap. Pure and
    /// allocation-free, so the rigs call it every frame and the tests call it with made-up actors.
    /// </summary>
    public static class RigSolver
    {
        /// <summary>Azimuth of the golden-hour key light, degrees clockwise from north (south-west).</summary>
        public const float SunAzimuth = 225f;

        /// <summary>Resolves a point expression to a world position.</summary>
        public static Vector3 Resolve(in RigPoint p, RigScene s)
        {
            Vector3 origin = Basis(p.basis, p.marker, s);
            Quaternion frame = Frame(p, s, origin);
            Vector3 offset = p.h * s.H + p.hb * s.HB + p.m;
            Vector3 result = origin + frame * offset;
            if (p.level != RigBasis.None)
                result.y = Basis(p.level, p.levelMarker, s).y + offset.y;
            return result;
        }

        /// <summary>The world position of a basis point.</summary>
        public static Vector3 Basis(RigBasis basis, string marker, RigScene s)
        {
            switch (basis)
            {
                case RigBasis.MCHead: return s.MC.Head;
                case RigBasis.MCEyes: return s.MC.Eyes;
                case RigBasis.MCHandR: return s.MC.HandR;
                case RigBasis.MCFeet: return s.MC.Feet;
                case RigBasis.MCMid: return s.MC.Mid;
                case RigBasis.MCBlade: return s.MC.Blade;
                case RigBasis.BirdyHead: return s.Birdy.Head;
                case RigBasis.BirdyEyes: return s.Birdy.Eyes;
                case RigBasis.BirdyHandR: return s.Birdy.HandR;
                case RigBasis.BirdyFeet: return s.Birdy.Feet;
                case RigBasis.BirdySocket: return s.Birdy.Socket;
                case RigBasis.PairMid: return (s.MC.Head + s.Birdy.Head) * 0.5f;
                case RigBasis.FeetMid: return (s.MC.Feet + s.Birdy.Feet) * 0.5f;
                case RigBasis.HandHeadMid: return (s.MC.HandR + s.Birdy.Head) * 0.5f;
                case RigBasis.PropRoot: return s.HasProp ? s.Prop : s.Birdy.Socket;
                case RigBasis.Marker:
                    return s.Markers != null && s.Markers.TryGetMarker(marker, out var pos, out _) ? pos : Vector3.zero;
                default: return Vector3.zero;
            }
        }

        /// <summary>The yaw-only rotation an offset is written in.</summary>
        public static Quaternion Frame(in RigPoint p, RigScene s, Vector3 origin)
        {
            float yaw;
            switch (p.frame)
            {
                case RigFrame.MC: yaw = s.MC.Yaw; break;
                case RigFrame.Birdy: yaw = s.Birdy.Yaw; break;
                case RigFrame.PairSide: yaw = CameraMath.YawOf(PairSide(s, p.side)); break;
                case RigFrame.Marker:
                    yaw = s.Markers != null && s.Markers.TryGetMarker(p.frameMarker, out _, out float my) ? my : 0f;
                    break;
                case RigFrame.Toward:
                    yaw = s.Markers != null && s.Markers.TryGetMarker(p.frameMarker, out var mp, out _)
                        ? CameraMath.YawOf(mp - origin) : 0f;
                    break;
                case RigFrame.TowardBirdy: yaw = CameraMath.YawOf(s.Birdy.Feet - s.MC.Feet); break;
                default: yaw = 0f; break;
            }
            return Quaternion.Euler(0f, yaw + p.yaw, 0f);
        }

        /// <summary>
        /// Pair.Side: the horizontal unit vector perpendicular to the MC→Birdy line on the side
        /// <paramref name="rule"/> picks. Falls back to MC's right when they stand on one spot.
        /// </summary>
        public static Vector3 PairSide(RigScene s, SideRule rule)
        {
            Vector3 line = s.Birdy.Feet - s.MC.Feet;
            line.y = 0f;
            if (line.sqrMagnitude < 1e-6f) line = s.MC.YawRotation * Vector3.forward;
            line.Normalize();
            Vector3 perp = Vector3.Cross(Vector3.up, line);   // right of the MC→Birdy line
            bool flip;
            switch (rule)
            {
                case SideRule.South:
                    flip = Vector3.Dot(perp, Vector3.back) < 0f;
                    break;
                case SideRule.MCLeft:
                case SideRule.MCRight:
                {
                    // The camera looks along -side; its right is Cross(up, -side).
                    Vector3 camRight = Vector3.Cross(Vector3.up, -perp);
                    bool mcLeft = Vector3.Dot(s.MC.Feet - s.Birdy.Feet, camRight) < 0f;
                    flip = rule == SideRule.MCLeft ? !mcLeft : mcLeft;
                    break;
                }
                default:
                    flip = Vector3.Dot(perp, CameraMath.YawDirection(SunAzimuth)) < 0f;
                    break;
            }
            return flip ? -perp : perp;
        }

        /// <summary>
        /// Index of the key segment containing <paramref name="t"/> and the eased progress through
        /// it. Before the first key: (0, 0); after the last: (last, 1) with segment = last.
        /// </summary>
        public static void Segment(RigKey[] keys, float[] beats, float duration, float t, out int index, out float u)
        {
            index = 0;
            u = 0f;
            if (keys == null || keys.Length == 0) return;
            int last = keys.Length - 1;
            if (t <= keys[0].time.Resolve(beats, duration))
            {
                index = 0;
                u = 0f;
                return;
            }
            for (int i = 1; i <= last; i++)
            {
                float t1 = keys[i].time.Resolve(beats, duration);
                if (t < t1)
                {
                    float t0 = keys[i - 1].time.Resolve(beats, duration);
                    index = i - 1;
                    u = CameraMath.Progress(t, t0, t1, keys[i].ease);
                    return;
                }
            }
            index = last;
            u = 0f;
        }

        /// <summary>The keyed path's position at <paramref name="t"/>: straight lines between keys.</summary>
        public static Vector3 Keyed(RigKey[] keys, float[] beats, float duration, float t, RigScene s)
        {
            if (keys == null || keys.Length == 0) return Vector3.zero;
            Segment(keys, beats, duration, t, out int i, out float u);
            Vector3 a = Resolve(keys[i].point, s);
            if (i >= keys.Length - 1 || u <= 0f) return a;
            return Vector3.LerpUnclamped(a, Resolve(keys[i + 1].point, s), u);
        }

        /// <summary>
        /// The crane's position at <paramref name="t"/>: a centripetal Catmull-Rom spline through the
        /// keys, travelled with ONE ease over the first-to-last key span (the shot list's "ease-in-out
        /// over the crane"), the middle keys' times pacing the segments.
        /// </summary>
        public static Vector3 Crane(RigKey[] keys, float[] beats, float duration, float t, RigScene s, EaseKind ease)
        {
            if (keys == null || keys.Length == 0) return Vector3.zero;
            int last = keys.Length - 1;
            if (last == 0) return Resolve(keys[0].point, s);
            float t0 = keys[0].time.Resolve(beats, duration);
            float tN = keys[last].time.Resolve(beats, duration);
            // Ease the global clock, then find the segment on the eased clock.
            float te = t0 + CameraMath.Progress(t, t0, tN, ease) * (tN - t0);
            int seg = last - 1;
            float frac = 1f;
            for (int i = 1; i <= last; i++)
            {
                float a = keys[i - 1].time.Resolve(beats, duration);
                float b = keys[i].time.Resolve(beats, duration);
                if (te <= b || i == last)
                {
                    seg = i - 1;
                    frac = b > a + 1e-6f ? Mathf.Clamp01((te - a) / (b - a)) : 1f;
                    break;
                }
            }
            Vector3 p1 = Resolve(keys[seg].point, s);
            Vector3 p2 = Resolve(keys[seg + 1].point, s);
            Vector3 p0 = seg > 0 ? Resolve(keys[seg - 1].point, s) : p1 + (p1 - p2);
            Vector3 p3 = seg + 2 <= last ? Resolve(keys[seg + 2].point, s) : p2 + (p2 - p1);
            return CatmullRom(p0, p1, p2, p3, frac);
        }

        /// <summary>Centripetal Catmull-Rom between p1 and p2 (no cusps or overshoot loops).</summary>
        public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float k0 = 0f;
            float k1 = k0 + Knot(p0, p1);
            float k2 = k1 + Knot(p1, p2);
            float k3 = k2 + Knot(p2, p3);
            if (k2 - k1 < 1e-6f) return Vector3.Lerp(p1, p2, t);
            float k = Mathf.Lerp(k1, k2, t);
            Vector3 a1 = Blend(p0, p1, k0, k1, k);
            Vector3 a2 = Blend(p1, p2, k1, k2, k);
            Vector3 a3 = Blend(p2, p3, k2, k3, k);
            Vector3 b1 = Blend(a1, a2, k0, k2, k);
            Vector3 b2 = Blend(a2, a3, k1, k3, k);
            return Blend(b1, b2, k1, k2, k);
        }

        static float Knot(Vector3 a, Vector3 b) => Mathf.Max(1e-4f, Mathf.Sqrt((b - a).magnitude));

        static Vector3 Blend(Vector3 a, Vector3 b, float ka, float kb, float k) =>
            kb - ka < 1e-6f ? a : (a * (kb - k) + b * (k - ka)) / (kb - ka);

        /// <summary>The aim target and screen position at <paramref name="t"/>.</summary>
        public static void Aim(AimKey[] keys, float[] beats, float duration, float t, RigScene s,
            out Vector3 target, out Vector2 screen)
        {
            target = Vector3.zero;
            screen = new Vector2(0.5f, 0.5f);
            if (keys == null || keys.Length == 0) return;
            int last = keys.Length - 1;
            int i = 0;
            float u = 0f;
            if (t > keys[0].time.Resolve(beats, duration))
            {
                i = last;
                for (int k = 1; k <= last; k++)
                {
                    float t1 = keys[k].time.Resolve(beats, duration);
                    if (t < t1)
                    {
                        i = k - 1;
                        u = CameraMath.Progress(t, keys[k - 1].time.Resolve(beats, duration), t1, keys[k].ease);
                        break;
                    }
                }
            }
            target = Resolve(keys[i].target, s);
            screen = keys[i].screen;
            if (i < last && u > 0f)
            {
                target = Vector3.LerpUnclamped(target, Resolve(keys[i + 1].target, s), u);
                screen = Vector2.LerpUnclamped(screen, keys[i + 1].screen, u);
            }
        }

        /// <summary>
        /// The focus distance at <paramref name="t"/>: the distance from <paramref name="camera"/> to the
        /// keyed focus point, eased between keys in dioptres. 0 when there are no keys.
        /// </summary>
        public static float Focus(FocusKey[] keys, float[] beats, float duration, float t, RigScene s, Vector3 camera)
        {
            if (keys == null || keys.Length == 0) return 0f;
            int last = keys.Length - 1;
            int i = last;
            float u = 0f;
            if (t <= keys[0].time.Resolve(beats, duration)) i = 0;
            else
            {
                for (int k = 1; k <= last; k++)
                {
                    float t1 = keys[k].time.Resolve(beats, duration);
                    if (t < t1)
                    {
                        i = k - 1;
                        u = CameraMath.Progress(t, keys[k - 1].time.Resolve(beats, duration), t1, keys[k].ease);
                        break;
                    }
                }
            }
            float d0 = Mathf.Max(0.05f, Vector3.Distance(camera, Resolve(keys[i].target, s)));
            if (i >= last || u <= 0f) return d0;
            float d1 = Mathf.Max(0.05f, Vector3.Distance(camera, Resolve(keys[i + 1].target, s)));
            return 1f / Mathf.Lerp(1f / d0, 1f / d1, u);
        }

        /// <summary>
        /// Backs <paramref name="camera"/> off along the line from Birdy.Head until Birdy's head box
        /// fills at most <paramref name="cap"/> of the frame height (a small margin covers the
        /// depth-versus-distance difference off the optical axis). Unchanged when already far enough.
        /// </summary>
        public static Vector3 CapBirdyFraction(Vector3 camera, RigScene s, float cap, float verticalFov)
        {
            if (cap <= 0f || !s.Birdy.Valid) return camera;
            Vector3 head = s.Birdy.Head;
            Vector3 d = camera - head;
            float dist = d.magnitude;
            float need = CameraMath.DepthForFraction(s.HB, cap * 0.97f, verticalFov);
            if (dist >= need || dist < 1e-4f) return camera;
            return head + d / dist * need;
        }

        /// <summary>
        /// As <see cref="CapBirdyFraction(Vector3, RigScene, float, float)"/>, but measures Birdy's
        /// depth along the camera's view toward <paramref name="aim"/> (the TCU rule's d), so an
        /// off-axis Birdy at the frame's side is capped too. Backs off along the line from
        /// Birdy.Head in a few steps. The push fades in over the first quarter of the needed depth,
        /// so it is continuous as Birdy crosses the camera plane: a full push the moment he came in
        /// front of the lens made S2_SH10's crane and S3_SH07 jump by up to 1.4 m (review round 3).
        /// </summary>
        public static Vector3 CapBirdyFraction(Vector3 camera, RigScene s, float cap, float verticalFov, Vector3 aim)
        {
            camera = CapBirdyFraction(camera, s, cap, verticalFov);
            if (cap <= 0f || !s.Birdy.Valid) return camera;
            Vector3 head = s.Birdy.Head;
            float need = CameraMath.DepthForFraction(s.HB, cap * 0.97f, verticalFov);
            for (int i = 0; i < 6; i++)
            {
                Vector3 fwd = aim - camera;
                if (fwd.sqrMagnitude < 1e-8f) break;
                fwd.Normalize();
                float depth = Vector3.Dot(head - camera, fwd);
                if (depth <= 0f || depth >= need) break;
                float w = Mathf.Clamp01(depth / (0.25f * need));
                w = w * w * (3f - 2f * w);
                Vector3 away = camera - head;
                if (away.sqrMagnitude < 1e-8f || w <= 1e-4f) break;
                camera += away.normalized * (w * (need - depth));
            }
            return camera;
        }

        /// <summary>The summed noise gain (base plus bursts) at <paramref name="t"/>.</summary>
        public static float NoiseGain(ShotRigSpec spec, float[] beats, float duration, float t)
        {
            float g = spec.noiseAmplitude;
            if (spec.bursts == null) return g;
            for (int i = 0; i < spec.bursts.Length; i++)
                g += spec.bursts[i].GainAt(spec.bursts[i].at.Resolve(beats, duration), t);
            return g;
        }

        /// <summary>MC's body axis for a top-down shot: Feet→Head flattened, or his forward when he stands.</summary>
        public static Vector3 BodyAxis(RigScene s)
        {
            Vector3 axis = s.MC.Head - s.MC.Feet;
            axis.y = 0f;
            if (axis.magnitude > 0.3f * s.H) return axis.normalized;
            return s.MC.YawRotation * Vector3.forward;
        }
    }
}
