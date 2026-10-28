using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// The maths of the conversation camera, kept pure so it is tested rather
    /// than eyeballed.
    ///
    /// <b>Position: over the shoulder, never through the head.</b> The camera
    /// sits behind the player's head (the pivot) on the flat line from the
    /// player to the NPC, raised, and shifted sideways. The chibi's head and hair
    /// are huge, so a fixed sideways shift hides a short NPC behind them at
    /// close range. The shift is therefore the larger of the configured one and
    /// the one at which the sightline from the camera to the NPC's head passes
    /// <see cref="DialogueFramingSettings.PlayerClearance"/> from the player's
    /// head (<see cref="RequiredSideOffset"/>), capped. The left shoulder is the
    /// default: the sword is carried on the right.
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
        /// <paramref name="playerHead"/> (the pivot: the Head bone, ideally)
        /// talking to an NPC whose focus point is <paramref name="npcFocus"/> and
        /// whose feet are at <paramref name="npcFeet"/>, seen through a camera of
        /// the given <paramref name="aspect"/> (width / height). When the NPC is
        /// straight above or below the pivot, <paramref name="playerForward"/>
        /// stands in for the line between them, and world forward if that is
        /// vertical too.
        /// </summary>
        public static DialogueShot Solve(Vector3 playerHead, Vector3 playerForward, Vector3 npcFocus,
                                         Vector3 npcFeet, float aspect, in DialogueFramingSettings settings)
        {
            Vector3 pivot = playerHead;

            Vector3 line = npcFocus - pivot;
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

            float side = SideSign(settings) * Mathf.Min(
                Mathf.Max(Mathf.Abs(settings.SideOffset),
                          RequiredSideOffset(distance, settings.PushInDistance, settings.PlayerClearance)),
                Mathf.Max(settings.MaxSideOffset, Mathf.Abs(settings.SideOffset)));

            Vector3 position = pivot
                               - toNpc * settings.PushInDistance
                               + right * side
                               + Vector3.up * settings.CameraHeight;
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
        /// The least sideways camera offset at which the flat sightline from the
        /// camera (<paramref name="pushIn"/> behind the player's head) to an NPC
        /// <paramref name="npcDistance"/> in front passes
        /// <paramref name="clearance"/> from the player's head. From the distance
        /// of a point to a line: s·d / √((d + p)² + s²) ≥ c, so
        /// s ≥ c·(d + p) / √(d² − c²). Infinite when the NPC is closer than the
        /// clearance itself.
        /// </summary>
        public static float RequiredSideOffset(float npcDistance, float pushIn, float clearance)
        {
            if (clearance <= 0f) return 0f;
            float d2 = npcDistance * npcDistance - clearance * clearance;
            return d2 > 1e-6f ? clearance * (npcDistance + pushIn) / Mathf.Sqrt(d2) : float.PositiveInfinity;
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
