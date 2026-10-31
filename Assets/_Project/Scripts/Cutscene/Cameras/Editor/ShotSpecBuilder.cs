using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Cutscene.Cameras.Editor
{
    /// <summary>
    /// Fluent shorthand for writing <see cref="ShotRigSpec"/>s the way the shot list's tables read
    /// ("locked at X, aim Y at (sx, sy), handheld 0.2"), so <see cref="OpeningShotSeed"/> stays a
    /// line or two per shot and can be checked against the tables by eye.
    /// </summary>
    public sealed class ShotSpecBuilder
    {
        readonly ShotRigSpec _s;
        readonly List<RigKey> _path = new List<RigKey>();
        readonly List<AimKey> _aim = new List<AimKey>();
        readonly List<FocusKey> _focus = new List<FocusKey>();
        readonly List<ShakeBurst> _bursts = new List<ShakeBurst>();
        readonly List<SubjectWindow> _subjects = new List<SubjectWindow>();
        readonly List<TimeWindow> _tcu = new List<TimeWindow>();

        /// <summary>Starts a shot with its lens (vertical FOV, degrees).</summary>
        public ShotSpecBuilder(string id, float lensStart, float lensEnd)
        {
            _s = new ShotRigSpec { shotId = id, lensStart = lensStart, lensEnd = lensEnd, lensFrom = TimeRef.Start(), lensTo = TimeRef.End() };
        }

        // ── Points ──────────────────────────────────────────────────────────────

        /// <summary>basis + (x, y, z)·H in a frame.</summary>
        public static RigPoint H(RigBasis basis, RigFrame frame, float x, float y, float z) =>
            new RigPoint { basis = basis, frame = frame, h = new Vector3(x, y, z) };

        /// <summary>basis + (x, y, z)·HB in a frame.</summary>
        public static RigPoint HB(RigBasis basis, RigFrame frame, float x, float y, float z) =>
            new RigPoint { basis = basis, frame = frame, hb = new Vector3(x, y, z) };

        /// <summary>An anchor or derived point.</summary>
        public static RigPoint At(RigBasis basis) => RigPoint.At(basis);

        /// <summary>A marker.</summary>
        public static RigPoint Marker(string id) => RigPoint.AtMarker(id);

        /// <summary>Frame turned toward a marker (the black sun, Birdy_Land).</summary>
        public static RigPoint Toward(RigPoint p, string marker)
        {
            p.frame = RigFrame.Toward;
            p.frameMarker = marker;
            return p;
        }

        /// <summary>Frame = a marker's yaw.</summary>
        public static RigPoint InMarker(RigPoint p, string marker)
        {
            p.frame = RigFrame.Marker;
            p.frameMarker = marker;
            return p;
        }

        /// <summary>Turns the frame about the vertical by <paramref name="degrees"/>.</summary>
        public static RigPoint Yaw(RigPoint p, float degrees)
        {
            p.yaw = degrees;
            return p;
        }

        /// <summary>Levels the point with another basis's height.</summary>
        public static RigPoint Level(RigPoint p, RigBasis level)
        {
            p.level = level;
            return p;
        }

        /// <summary>Pair.Side frame with a side rule.</summary>
        public static RigPoint Side(RigPoint p, SideRule rule)
        {
            p.frame = RigFrame.PairSide;
            p.side = rule;
            return p;
        }

        // ── Position ────────────────────────────────────────────────────────────

        /// <summary>A position key (Keyed body by default).</summary>
        public ShotSpecBuilder Key(TimeRef time, RigPoint point, EaseKind ease = EaseKind.Linear)
        {
            _path.Add(new RigKey { time = time, point = point, ease = ease });
            return this;
        }

        /// <summary>A locked position.</summary>
        public ShotSpecBuilder Locked(RigPoint point) => Key(TimeRef.Start(), point);

        /// <summary>Sets the body kind.</summary>
        public ShotSpecBuilder Body(BodyKind body)
        {
            _s.body = body;
            return this;
        }

        /// <summary>Crane through the keys with one ease.</summary>
        public ShotSpecBuilder Crane(EaseKind ease)
        {
            _s.body = BodyKind.Crane;
            _s.craneEase = ease;
            return this;
        }

        /// <summary>The shot list's "n% push" toward the aim.</summary>
        public ShotSpecBuilder Push(float fraction, EaseKind ease = EaseKind.InOut)
        {
            _s.push = fraction;
            _s.pushFrom = TimeRef.Start();
            _s.pushTo = TimeRef.End();
            _s.pushEase = ease;
            return this;
        }

        /// <summary>Caps Birdy's frame share.</summary>
        public ShotSpecBuilder Cap(float frac)
        {
            _s.fracCap = frac;
            return this;
        }

        /// <summary>Follow body on an anchor with damping.</summary>
        public ShotSpecBuilder Follow(RigBasis target, Vector3 damping, string relativeMarker = "")
        {
            _s.body = BodyKind.Follow;
            _s.followTarget = target;
            _s.followDamping = damping;
            _s.followRelativeMarker = relativeMarker;
            return this;
        }

        /// <summary>Orbit body round an anchor.</summary>
        public ShotSpecBuilder Orbit(RigBasis target, float degrees, TimeRef from, TimeRef to, EaseKind ease)
        {
            _s.body = BodyKind.Orbit;
            _s.followTarget = target;
            _s.orbitDegrees = degrees;
            _s.orbitFrom = from;
            _s.orbitTo = to;
            _s.orbitEase = ease;
            return this;
        }

        /// <summary>The scene-4 side-on track.</summary>
        public ShotSpecBuilder Track(string start, string end, TimeRef stop, float walkSpeed, string walkFrom, string walkTo)
        {
            _s.body = BodyKind.Track;
            _s.trackStart = start;
            _s.trackEnd = end;
            _s.trackStop = stop;
            _s.walkSpeed = walkSpeed;
            _s.walkFrom = walkFrom;
            _s.walkTo = walkTo;
            return this;
        }

        /// <summary>The push-in to pose G.</summary>
        public ShotSpecBuilder Gameplay(float distanceScale, TimeRef from, TimeRef to, EaseKind ease)
        {
            _s.body = BodyKind.Gameplay;
            _s.aim = AimMode.Gameplay;
            _s.gameplayDistanceScale = distanceScale;
            _s.gameplayFrom = from;
            _s.gameplayTo = to;
            _s.gameplayEase = ease;
            return this;
        }

        // ── Aim ─────────────────────────────────────────────────────────────────

        /// <summary>An aim key.</summary>
        public ShotSpecBuilder Aim(TimeRef time, RigPoint target, float sx, float sy, EaseKind ease = EaseKind.InOut)
        {
            _aim.Add(new AimKey { time = time, target = target, screen = new Vector2(sx, sy), ease = ease });
            return this;
        }

        /// <summary>A fixed aim for the whole shot.</summary>
        public ShotSpecBuilder Aim(RigPoint target, float sx, float sy) => Aim(TimeRef.Start(), target, sx, sy);

        /// <summary>Straight down at a point, MC's head at the top of the frame.</summary>
        public ShotSpecBuilder TopDown(RigPoint target)
        {
            _s.aim = AimMode.TopDown;
            return Aim(target, 0.5f, 0.5f);
        }

        /// <summary>Keeps a marker at least <paramref name="margin"/>° outside the frame.</summary>
        public ShotSpecBuilder Avoid(string marker, float margin, bool panOnly = false)
        {
            _s.avoidMarker = marker;
            _s.avoidMargin = margin;
            _s.avoidPanOnly = panOnly;
            return this;
        }

        /// <summary>The zoom window (defaults to the whole shot).</summary>
        public ShotSpecBuilder Zoom(TimeRef from, TimeRef to, EaseKind ease)
        {
            _s.lensFrom = from;
            _s.lensTo = to;
            _s.lensEase = ease;
            return this;
        }

        // ── Noise and focus ─────────────────────────────────────────────────────

        /// <summary>Handheld or shake noise at a base amplitude.</summary>
        public ShotSpecBuilder Noise(NoiseKind kind, float amplitude, float frequency = 1f)
        {
            _s.noise = kind;
            _s.noiseAmplitude = amplitude;
            _s.noiseFrequency = frequency;
            return this;
        }

        /// <summary>A shake burst.</summary>
        public ShotSpecBuilder Burst(TimeRef at, float attack, float hold, float decay, float peak)
        {
            _bursts.Add(new ShakeBurst { at = at, attack = attack, hold = hold, decay = decay, peak = peak });
            return this;
        }

        /// <summary>Depth of field at an f-number.</summary>
        public ShotSpecBuilder Dof(float aperture)
        {
            _s.aperture = aperture;
            return this;
        }

        /// <summary>A focus key.</summary>
        public ShotSpecBuilder Focus(TimeRef time, RigPoint target, EaseKind ease = EaseKind.InOut)
        {
            _focus.Add(new FocusKey { time = time, target = target, ease = ease });
            return this;
        }

        // ── Validation ──────────────────────────────────────────────────────────

        /// <summary>A subject for the whole shot.</summary>
        public ShotSpecBuilder Subject(RigBasis subject) => Subject(subject, TimeRef.Start(), TimeRef.End());

        /// <summary>A subject for part of the shot.</summary>
        public ShotSpecBuilder Subject(RigBasis subject, TimeRef from, TimeRef to)
        {
            _subjects.Add(new SubjectWindow { subject = subject, from = from, to = to });
            return this;
        }

        /// <summary>INSERT / NO-CHAR: frames an object anchor.</summary>
        public ShotSpecBuilder Insert(RigBasis anchor)
        {
            _s.insert = true;
            return Subject(anchor);
        }

        /// <summary>A span where a Birdy TCU is allowed.</summary>
        public ShotSpecBuilder AllowTcu(TimeRef from, TimeRef to)
        {
            _tcu.Add(new TimeWindow { from = from, to = to });
            return this;
        }

        /// <summary>A note on a deviation from the shot list.</summary>
        public ShotSpecBuilder Note(string note)
        {
            _s.notes = note;
            return this;
        }

        /// <summary>Finishes the spec.</summary>
        public ShotRigSpec Build()
        {
            _s.path = _path.ToArray();
            _s.aimKeys = _aim.ToArray();
            _s.focus = _focus.ToArray();
            _s.bursts = _bursts.ToArray();
            _s.subjects = _subjects.ToArray();
            _s.tcuAllowed = _tcu.ToArray();
            return _s;
        }
    }
}
