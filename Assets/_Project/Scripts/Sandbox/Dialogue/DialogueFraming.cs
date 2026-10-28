using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// The maths of the conversation camera, kept pure so it is tested rather
    /// than eyeballed.
    ///
    /// <b>Position: over the shoulder, never through the player.</b> The camera
    /// sits behind the player on the flat line from the player's body axis to
    /// the NPC's, raised above the player's head (the pivot), and shifted
    /// sideways. The chibi's head, hair and flared cloak are huge next to a
    /// short NPC, so a fixed sideways shift hides the NPC at close range. The
    /// shift is the larger of the configured one and the one at which every
    /// sightline from the camera to the NPC (head to feet, across its
    /// <see cref="DialogueFramingSettings.NpcHalfWidth"/>) clears a vertical
    /// cylinder of radius <see cref="DialogueFramingSettings.PlayerClearance"/>
    /// round the player's body axis (<see cref="RequiredSideOffset"/>), capped.
    /// The left shoulder is the default: the sword is carried on the right.
    ///
    /// <b>Composition: in screen space.</b> The rotation puts the NPC's focus
    /// point at a chosen screen height (the upper third) and a little toward the
    /// open side; the field of view is chosen from the NPC's height so its feet
    /// land just above the dialogue box (<see cref="FieldOfViewFor"/>), within
    /// limits. So the shot reads the same for a short NPC and a tall one, near or
    /// far, and when a wall pulls the camera in (<see cref="Compose"/> re-runs for
    /// the new position).
    ///
    /// Built on the player → NPC line rather than the player's facing, so the
    /// shot is right even when the player was locked on and facing elsewhere.
    /// </summary>
    public static class DialogueFraming
    {
        /// <summary>
        /// The conversation shot for a player whose head is at
        /// <paramref name="playerHead"/> (the pivot: the Head bone, ideally) and
        /// whose body axis passes through <paramref name="playerAxis"/> (his root),
        /// talking to an NPC whose focus point is <paramref name="npcFocus"/> and
        /// whose feet are at <paramref name="npcFeet"/>, seen through a camera of
        /// the given <paramref name="aspect"/> (width / height). When the NPC
        /// stands on the player's axis, <paramref name="playerForward"/> stands in
        /// for the line between them, and world forward if that is vertical too.
        /// </summary>
        public static DialogueShot Solve(Vector3 playerHead, Vector3 playerAxis, Vector3 playerForward,
                                         Vector3 npcFocus, Vector3 npcFeet, float aspect,
                                         in DialogueFramingSettings settings)
        {
            Vector3 pivot = playerHead;

            Vector3 line = npcFeet - playerAxis;
            line.y = 0f;
            float distance = line.magnitude;
            if (distance < 1e-3f)
            {
                line = new Vector3(playerForward.x, 0f, playerForward.z);
                if (line.sqrMagnitude < 1e-6f) line = Vector3.forward;
            }
            Vector3 toNpc = line.normalized;
            // Unity is left-handed: up × forward is right.
            Vector3 right = Vector3.Cross(Vector3.up, toNpc);

            // The widest the camera may swing; if even that can't clear the player
            // from this far back, come forward (toward a side-on two-shot) until it can.
            float maxSide = Mathf.Max(settings.MaxSideOffset, Mathf.Abs(settings.SideOffset));
            float pushIn = settings.PushInDistance;
            float required = RequiredSideOffset(distance, pushIn, settings.PlayerClearance, settings.NpcHalfWidth);
            if (required > maxSide)
            {
                pushIn = PushInFor(distance, maxSide, settings.PlayerClearance, settings.NpcHalfWidth, pushIn);
                required = RequiredSideOffset(distance, pushIn, settings.PlayerClearance, settings.NpcHalfWidth);
            }
            float side = SideSign(settings) * Mathf.Min(Mathf.Max(Mathf.Abs(settings.SideOffset), required), maxSide);

            Vector3 position = new Vector3(playerAxis.x, pivot.y + settings.CameraHeight, playerAxis.z)
                               - toNpc * pushIn
                               + right * side;
            return Compose(pivot, position, npcFocus, npcFeet, aspect, settings);
        }

        /// <summary>
        /// Rotation and field of view for a camera at <paramref name="position"/>:
        /// the NPC's focus at <see cref="DialogueFramingSettings.NpcHeadScreenHeight"/>,
        /// shifted toward the open side, and wide enough for its feet to reach
        /// <see cref="DialogueFramingSettings.NpcFeetScreenHeight"/>.
        /// </summary>
        public static DialogueShot Compose(Vector3 pivot, Vector3 position, Vector3 npcFocus, Vector3 npcFeet,
                                           float aspect, in DialogueFramingSettings settings)
        {
            Vector3 toNpc = npcFocus - position;
            float depth = toNpc.magnitude;
            float npcHeight = Mathf.Max(0f, npcFocus.y - npcFeet.y);
            float fov = FieldOfViewFor(npcHeight, depth, settings);

            // Over the left shoulder the player fills the right of the frame, so
            // the NPC moves left, to the open side; mirrored for the right.
            float screenX = 0.5f + SideSign(settings) * settings.NpcScreenSide;
            Quaternion rotation = LookPlacing(position, npcFocus, screenX, settings.NpcHeadScreenHeight,
                                              fov, aspect > 0.01f ? aspect : 16f / 9f);
            return new DialogueShot(pivot, position, rotation, fov);
        }

        /// <summary>
        /// The least sideways camera offset s at which every flat sightline from
        /// the camera (<paramref name="pushIn"/> = p behind the player's axis) to
        /// an NPC <paramref name="npcDistance"/> = d ahead, up to
        /// <paramref name="npcHalfWidth"/> = w either side of its axis, passes at
        /// least <paramref name="clearance"/> = r from the player's axis. The
        /// worst sightline goes to the NPC's far edge, (d, −w), from (−p, s); its
        /// distance from the axis is (s·d − w·p) / √((d + p)² + (s + w)²) ≥ r,
        /// whose larger root is solved exactly here. Being flat, it holds for the
        /// NPC's head and feet alike, whatever the heights. Infinite when the NPC
        /// stands inside the clearance.
        /// </summary>
        public static float RequiredSideOffset(float npcDistance, float pushIn, float clearance,
                                               float npcHalfWidth = 0f)
        {
            if (clearance <= 0f) return npcDistance > 1e-6f ? Mathf.Max(0f, npcHalfWidth * pushIn / npcDistance) : 0f;
            float d = npcDistance, p = pushIn, r = clearance, w = Mathf.Max(0f, npcHalfWidth);
            float a = d * d - r * r;
            if (a <= 1e-6f) return float.PositiveInfinity;
            float halfB = w * (d * p + r * r);
            float c = w * w * (p * p - r * r) - r * r * (d + p) * (d + p);
            float disc = halfB * halfB - a * c;
            return disc >= 0f ? (halfB + Mathf.Sqrt(disc)) / a : float.PositiveInfinity;
        }

        /// <summary>
        /// The longest push-in, up to <paramref name="pushIn"/>, at which
        /// <paramref name="maxSide"/> is enough to clear the player
        /// (<see cref="RequiredSideOffset"/> grows with the push-in). 0 when even
        /// a camera level with the player can't.
        /// </summary>
        public static float PushInFor(float npcDistance, float maxSide, float clearance, float npcHalfWidth,
                                      float pushIn)
        {
            if (RequiredSideOffset(npcDistance, 0f, clearance, npcHalfWidth) > maxSide) return 0f;
            float lo = 0f, hi = Mathf.Max(0f, pushIn);
            for (int i = 0; i < 24; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (RequiredSideOffset(npcDistance, mid, clearance, npcHalfWidth) <= maxSide) lo = mid;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// The vertical field of view at which an NPC <paramref name="npcHeight"/>
        /// tall (feet to focus) at <paramref name="depth"/> metres spans from
        /// <see cref="DialogueFramingSettings.NpcFeetScreenHeight"/> to
        /// <see cref="DialogueFramingSettings.NpcHeadScreenHeight"/>, clamped to the
        /// configured range: a short NPC is not zoomed in past the narrowest, a
        /// tall one is cut off at the knees by the box rather than shrunk.
        /// </summary>
        public static float FieldOfViewFor(float npcHeight, float depth, in DialogueFramingSettings settings)
        {
            float span = 2f * (settings.NpcHeadScreenHeight - settings.NpcFeetScreenHeight);   // in NDC units
            float fov = span > 1e-3f && depth > 1e-3f && npcHeight > 1e-3f
                ? 2f * Mathf.Atan(npcHeight / (depth * span)) * Mathf.Rad2Deg
                : settings.MaxFieldOfView;
            return Mathf.Clamp(fov, settings.MinFieldOfView, settings.MaxFieldOfView);
        }

        /// <summary>
        /// The roll-free rotation for a camera at <paramref name="position"/> that
        /// shows <paramref name="target"/> at viewport point
        /// (<paramref name="viewportX"/>, <paramref name="viewportY"/>), 0–1 from
        /// the bottom left, for the given vertical field of view (°) and aspect.
        /// Solved exactly for pitch, then yaw, so the camera never tilts.
        /// </summary>
        public static Quaternion LookPlacing(Vector3 position, Vector3 target, float viewportX, float viewportY,
                                             float verticalFov, float aspect)
        {
            Vector3 v = target - position;
            float length = v.magnitude;
            if (length < 1e-5f) return Quaternion.identity;

            float tanHalf = Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad);
            float tx = (2f * viewportX - 1f) * tanHalf * aspect;
            float ty = (2f * viewportY - 1f) * tanHalf;
            float n = Mathf.Sqrt(tx * tx + ty * ty + 1f);

            // Pitch α (Unity: positive looks down) so the target's elevation matches:
            // ty·cos α − sin α = (v.y / |v|)·n, written as √(ty² + 1)·cos(α + φ).
            float e = v.y / length * n;
            float phi = Mathf.Atan2(1f, ty);
            float alpha = Mathf.Acos(Mathf.Clamp(e / Mathf.Sqrt(ty * ty + 1f), -1f, 1f)) - phi;

            // Yaw β: the camera-space direction, pitched, must point at the target's azimuth.
            float wz = ty * Mathf.Sin(alpha) + Mathf.Cos(alpha);
            float beta = Mathf.Atan2(v.x, v.z) - Mathf.Atan2(tx, wz);
            return Quaternion.Euler(alpha * Mathf.Rad2Deg, beta * Mathf.Rad2Deg, 0f);
        }

        /// <summary>
        /// Where the camera goes once something blocks it. A sweep from
        /// <paramref name="pivot"/> toward <paramref name="desired"/> found clear
        /// space for <paramref name="clearDistance"/> metres; the camera stops
        /// there, but never closer to the pivot than <paramref name="minDistance"/>.
        /// A clear distance at or beyond the desired one leaves it untouched.
        /// </summary>
        public static Vector3 PullIn(Vector3 pivot, Vector3 desired, float clearDistance, float minDistance)
        {
            Vector3 offset = desired - pivot;
            float distance = offset.magnitude;
            if (distance < 1e-5f || clearDistance >= distance) return desired;
            float kept = Mathf.Clamp(clearDistance, Mathf.Min(minDistance, distance), distance);
            return pivot + offset * (kept / distance);
        }

        /// <summary>
        /// The camera pose <paramref name="t"/> (0–1) of the way from
        /// <paramref name="from"/> to <paramref name="to"/>, swinging around
        /// <paramref name="center"/> instead of cutting straight across.
        ///
        /// A straight lerp from an orbit camera in front of the player to the
        /// shot behind him goes through the player and the NPC. Here the yaw
        /// around the centre (the player/NPC midpoint) turns the short way,
        /// the radius and height are lerped, and the camera looks at a point
        /// lerped between where each pose looks, so the swing stays outside both
        /// bodies and keeps them in view. Both ends are exact. With either end
        /// on the centre's vertical axis there is no yaw to turn, and it falls
        /// back to a straight lerp.
        /// </summary>
        public static Pose OrbitBlend(Vector3 center, Pose from, Pose to, float t)
        {
            t = Mathf.Clamp01(t);
            if (t <= 0f) return from;
            if (t >= 1f) return to;

            Vector3 a = from.position - center, b = to.position - center;
            float ra = Mathf.Sqrt(a.x * a.x + a.z * a.z);
            float rb = Mathf.Sqrt(b.x * b.x + b.z * b.z);

            Vector3 position;
            if (ra < MinOrbitRadius || rb < MinOrbitRadius)
                position = Vector3.Lerp(from.position, to.position, t);
            else
            {
                float yaw = Mathf.LerpAngle(Mathf.Atan2(a.x, a.z) * Mathf.Rad2Deg,
                                            Mathf.Atan2(b.x, b.z) * Mathf.Rad2Deg, t) * Mathf.Deg2Rad;
                float r = Mathf.Lerp(ra, rb, t);
                position = center + new Vector3(Mathf.Sin(yaw) * r, Mathf.Lerp(a.y, b.y, t), Mathf.Cos(yaw) * r);
            }

            Vector3 aim = Vector3.Lerp(AimOf(from, center), AimOf(to, center), t);
            Vector3 look = aim - position;
            Quaternion rotation = look.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(look, Vector3.up)
                : Quaternion.Slerp(from.rotation, to.rotation, t);
            return new Pose(position, rotation);
        }

        /// <summary>Smoothstep easing of <paramref name="t"/>, clamped to 0–1:
        /// zero slope at both ends, so a blend neither snaps in nor out.</summary>
        public static float SmoothStep01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        // Below this horizontal distance from the centre a pose has no usable yaw.
        const float MinOrbitRadius = 0.05f;
        // The look-at point is at least this far along a pose's forward.
        const float MinAimDistance = 0.5f;

        /// <summary>+1 for the right shoulder, −1 for the left; 0 counts as right.</summary>
        static float SideSign(in DialogueFramingSettings settings) => settings.SideOffset < 0f ? -1f : 1f;

        /// <summary>A point on <paramref name="pose"/>'s line of sight, as far out as
        /// the centre, so lerping two of them turns the view around the subjects.</summary>
        static Vector3 AimOf(Pose pose, Vector3 center)
            => pose.position + pose.forward * Mathf.Max(MinAimDistance, Vector3.Distance(pose.position, center));
    }
}
