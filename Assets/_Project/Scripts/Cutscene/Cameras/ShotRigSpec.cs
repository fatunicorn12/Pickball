using System;
using UnityEngine;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// One shot from the shot list's §4 tables, as camera data: how the position is made, the keyed
    /// path, the aim, the lens, the noise, the depth of field and what the framing check holds it to.
    /// Every point is an anchor-relative <see cref="RigPoint"/> and every time a beat-relative
    /// <see cref="TimeRef"/>, so the shot holds for another model size and for re-timed audio. Seeded
    /// from the shot list by the cameras setup menu; tune live in Play mode on the table asset.
    /// </summary>
    [Serializable]
    public sealed class ShotRigSpec
    {
        [Header("Identity")]
        [Tooltip("Shot id, e.g. S2_SH05. Must match the ShotClip on the timeline.")]
        public string shotId = string.Empty;

        [Tooltip("Where and why this shot departs from the shot list's letter, if it does.")]
        [TextArea(1, 4)]
        public string notes = string.Empty;

        [Header("Position")]
        [Tooltip("How the position is made (keyed, crane spline, orbit, follow, scene-4 track, gameplay pose).")]
        public BodyKind body;

        [Tooltip("Position keys. Keyed/Crane: the path. Orbit: [0] is the start point (radius and " +
                 "angle come from it). Follow: [0] is the camera point the offset is taken from.")]
        public RigKey[] path = new RigKey[0];

        [Tooltip("Crane only: the one ease applied over the whole crane, first key to last.")]
        public EaseKind craneEase = EaseKind.InOut;

        [Header("Push (toward the aim)")]
        [Tooltip("Share of the camera-to-aim distance travelled over the push window (0.03 = the " +
                 "shot list's 3% micro push). The composer keeps the framing, so it reads as a creep in.")]
        [Range(0f, 0.5f)]
        public float push;

        [Tooltip("Push start.")]
        public TimeRef pushFrom;

        [Tooltip("Push end.")]
        public TimeRef pushTo = TimeRef.End();

        [Tooltip("Push ease.")]
        public EaseKind pushEase = EaseKind.InOut;

        [Header("Aim")]
        [Tooltip("How the rotation is made.")]
        public AimMode aim;

        [Tooltip("Aim keys (Composer); the first key's target is the look-down point for TopDown.")]
        public AimKey[] aimKeys = new AimKey[0];

        [Tooltip("Pan about the vertical after aiming, degrees (negative = pan left).")]
        public float panDegrees;

        [Tooltip("Pan start.")]
        public TimeRef panFrom;

        [Tooltip("Pan end.")]
        public TimeRef panTo = TimeRef.End();

        [Tooltip("Pan ease.")]
        public EaseKind panEase = EaseKind.InOut;

        [Tooltip("Marker kept out of frame (scene 5's black sun); empty = none. The camera pans or tilts away " +
                 "from it by just enough, so the reveal is saved for scene 6.")]
        public string avoidMarker = string.Empty;

        [Tooltip("Only pan away from the avoided marker, never tilt (a tilt would push a low subject off the top).")]
        public bool avoidPanOnly;

        [Tooltip("Degrees the avoided marker stays outside the frame edge.")]
        [Min(0f)]
        public float avoidMargin = 5f;

        [Header("Lens")]
        [Tooltip("Vertical FOV at the start of the zoom, degrees (the shot list's lens column).")]
        [Range(5f, 120f)]
        public float lensStart = 40f;

        [Tooltip("Vertical FOV at the end of the zoom, degrees.")]
        [Range(5f, 120f)]
        public float lensEnd = 40f;

        [Tooltip("Zoom start.")]
        public TimeRef lensFrom;

        [Tooltip("Zoom end.")]
        public TimeRef lensTo = TimeRef.End();

        [Tooltip("Zoom ease.")]
        public EaseKind lensEase = EaseKind.InOut;

        [Header("Birdy size cap")]
        [Tooltip("Largest share of the frame height Birdy's head box may fill (the TCU rule's frac). " +
                 "When a move would bring the camera closer, it backs off along the line to Birdy.Head. " +
                 "0 = no cap. Keeps non-TCU shots out of the banned 0.30–0.40 band at any model size.")]
        [Range(0f, 0.6f)]
        public float fracCap;

        [Header("Follow / Orbit")]
        [Tooltip("The anchor a Follow or Orbit body tracks.")]
        public RigBasis followTarget;

        [Tooltip("Follow only: the offset is taken from this marker instead of the target, so the " +
                 "camera arrives at the path point when the target arrives at the marker (S2_SH02).")]
        public string followRelativeMarker = string.Empty;

        [Tooltip("Follow damping per axis, seconds. Higher lags further behind the target.")]
        public Vector3 followDamping;

        [Tooltip("Orbit: degrees the horizontal axis turns over the orbit window (positive = clockwise from above).")]
        public float orbitDegrees;

        [Tooltip("Orbit start.")]
        public TimeRef orbitFrom;

        [Tooltip("Orbit end.")]
        public TimeRef orbitTo = TimeRef.End();

        [Tooltip("Orbit ease.")]
        public EaseKind orbitEase = EaseKind.InOut;

        [Header("Track (scene 4)")]
        [Tooltip("Marker at the start of the camera track.")]
        public string trackStart = string.Empty;

        [Tooltip("Marker at the end of the camera track.")]
        public string trackEnd = string.Empty;

        [Tooltip("When the dolly stops while the walkers walk on (none: never).")]
        public TimeRef trackStop = TimeRef.End(1000f);

        [Tooltip("The walkers' speed along the path, m/s, used to hold the dolly once it stops.")]
        [Min(0f)]
        public float walkSpeed;

        [Tooltip("Marker where the walk starts (sets the walk direction).")]
        public string walkFrom = string.Empty;

        [Tooltip("Marker where the walk ends.")]
        public string walkTo = string.Empty;

        [Header("Gameplay pose")]
        [Tooltip("Distance at the start of the push-in, as a multiple of the orbit camera's distance.")]
        [Min(1f)]
        public float gameplayDistanceScale = 1f;

        [Tooltip("Push-in start.")]
        public TimeRef gameplayFrom;

        [Tooltip("Push-in end (the camera sits exactly at pose G from here).")]
        public TimeRef gameplayTo = TimeRef.End();

        [Tooltip("Push-in ease.")]
        public EaseKind gameplayEase = EaseKind.InOut;

        [Header("Noise")]
        [Tooltip("Perlin noise profile.")]
        public NoiseKind noise;

        [Tooltip("Base amplitude gain (× the profile) held through the shot. 0.2–0.3 is light handheld.")]
        [Min(0f)]
        public float noiseAmplitude;

        [Tooltip("Frequency gain (× the profile). Higher jitters faster.")]
        [Min(0f)]
        public float noiseFrequency = 1f;

        [Tooltip("Shake bursts on beats, added to the base amplitude.")]
        public ShakeBurst[] bursts = new ShakeBurst[0];

        [Header("Depth of field")]
        [Tooltip("f-number of the Bokeh depth of field (2.0 = shallow); 0 = no depth of field.")]
        [Min(0f)]
        public float aperture;

        [Tooltip("Focus keys.")]
        public FocusKey[] focus = new FocusKey[0];

        [Header("Validation")]
        [Tooltip("Anchors held to the 90% safe frame, and when.")]
        public SubjectWindow[] subjects = new SubjectWindow[0];

        [Tooltip("INSERT or NO-CHAR: frames an object anchor, exempt from the head check.")]
        public bool insert;

        [Tooltip("Spans where a Birdy tight close-up (frac ≥ 0.30) is allowed.")]
        public TimeWindow[] tcuAllowed = new TimeWindow[0];
    }
}
