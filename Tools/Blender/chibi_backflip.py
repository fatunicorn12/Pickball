"""
Authors the chibi's backflip (Animator state SideFlip, clip role `sideFlip`) on the
Mixamo X Bot skeleton and writes `Back Flip.fbx`.

    "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --factory-startup \
        --python Tools/Blender/chibi_backflip.py -- \
        "Assets/_Project/Animation/Chibi/Run To Dive.fbx" \
        "Assets/_Project/Animation/Chibi/Run To Dive.fbx.meta" \
        "Assets/_Project/Animation/Chibi/Back Flip.fbx"

Arguments: the X Bot FBX the armature comes from, the .meta holding the canonical reference
skeleton, and the output path.

How the pose maths works
------------------------
Unity's Humanoid import reads a pose as each bone's rotation relative to the reference
skeleton in the .meta (`humanDescription.skeleton`), not relative to the FBX's bind pose, and
that skeleton differs from the bind pose by large twists along the limbs. So every pose here is
built on the .meta's skeleton:

    bone world rotation = C(bone) * T(bone)

T is the bone's world rotation in the reference skeleton. C is the rotation applied to that bone
in character axes, chained down the hierarchy, so the numbers below read the same on every bone:
+X is the character's left, +Y up, +Z forward (the FBX's own axes, which is also the armature
space Blender imports it into). The .meta of `Back Flip.fbx` must carry those same reference
rotations, or the knees and feet play 30-40 degrees off on the chibi. See
Docs/Systems/Movement.md, the clip notes.

The flip lives in the Hips, as it does in Front Flip.fbx: a pure rotation about X, negative for a
backward flip (head toward -Z, chest toward +Y). The import bakes root rotation into the pose,
and root motion is off on the Animator, so it reaches the body while the motor keeps all travel.

Timing: the driver scales every flip by flipAirborneSeconds / airtime, and that shared value is
1.2 s. So frame 36 (1.20 s) is touchdown on level ground, and the flip is over by frame 33. The clip
runs on to frame 40 (1.33 s) so that on level ground the state is at 90% when the feet land. The
flip states leave for Fall at 97%, and that transition can't be interrupted: a clip that ended
exactly at touchdown would blend toward Fall just before landing and hold Land back by 0.12 s.
Falling further than the launch height still reaches Fall from the same feet-first pose.
"""
import math
import re
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

FPS = 30
LAST_FRAME = 40

# ------------------------------------------------------------------------- timing

# Total backward rotation of the hips (degrees) at each key frame. Slow while the body is open,
# fast in the tuck, slow as it opens, as a real flip's angular speed behaves. Reaches exactly 360
# and holds it, so the landing pose is square. Interpolated monotonically: it can't overshoot.
FLIP_KEYS = [
    (0, 0.0), (3, 12.0), (6, 34.0), (9, 76.0), (12, 132.0), (15, 194.0),
    (18, 252.0), (21, 302.0), (24, 336.0), (27, 352.0), (30, 358.5), (33, 360.0), (40, 360.0),
]

# ------------------------------------------------------------------------- key poses
#
# Left-side values; the right side mirrors them, except where a pose gives r_<name> (also written
# with x out, so it is mirrored too). The right hand holds the weapon, so it can't grip the shin: a
# blade held there runs through the head and the thigh. It stays out to the side in the tuck, with
# the blade running forward past the knee, clear of everything. Angles are degrees.
#   spine   forward bend of Spine, Spine1, Spine2 (negative arches the back)
#   neck, head   forward bend (negative looks up)
#   clav    collarbone raise
#   arm     upper arm direction in the chest frame (x out, y up, z forward)
#   twist   upper arm roll about its own axis; positive lifts the elbow when the arm is forward
#   elbow   elbow bend, 0 straight
#   wrist   hand folded back against the elbow bend, keeps the thumb side (the blade) from swinging in
#   pron    hand rolled about the forearm, positive turns the thumb side (the blade) downward
#   thigh   thigh direction in the pelvis frame (x out, y up, z forward)
#   knee_out   thigh roll, positive turns the knee outward
#   knee    knee bend, 0 straight
#   foot, toe  positive points the toes
#   curl    finger curl per joint
POSES = [
    # Leaving the ground: already extended, arms on their way up, toes just left the floor. The
    # spine matches the last pose: Unity's root rotation averages the pelvis and the shoulders,
    # so a different spine at the two ends leaves the hips short of (or past) a clean 360.
    (0, dict(spine=(1, 1, 1), neck=0, head=-6, clav=4,
             arm=(0.42, 0.62, 0.66), twist=0, elbow=30,
             thigh=(0.07, -1.0, -0.04), knee_out=4, knee=14, foot=35, toe=0, curl=12)),
    # Arms driving high, chest open to the sky: this is what starts the rotation.
    (4, dict(spine=(-8, -8, -6), neck=-4, head=-14, clav=14,
             arm=(0.32, 0.94, 0.06), twist=0, elbow=14,
             thigh=(0.08, -1.0, -0.12), knee_out=4, knee=16, foot=40, toe=0, curl=8)),
    # Knees start to come through while the arms come off the top.
    (7, dict(spine=(-2, -2, -2), neck=-2, head=-10, clav=10,
             arm=(0.46, 0.82, 0.33), twist=0, elbow=28,
             thigh=(0.12, -0.72, 0.68), knee_out=8, knee=62, foot=35, toe=0, curl=14)),
    # Arms come over the front: the left to meet the shin, the right out to the side.
    (10, dict(spine=(10, 10, 8), neck=2, head=-4, clav=4,
              arm=(0.56, 0.24, 0.79), twist=-20, elbow=40,
              r_arm=(0.86, 0.30, 0.40), r_twist=0, r_elbow=30, r_wrist=16,
              thigh=(0.18, -0.02, 0.98), knee_out=12, knee=108, foot=25, toe=0, curl=30)),
    # Tight tuck: knees to chest, left hand on the shin.
    (13, dict(spine=(14, 14, 11), neck=4, head=0, clav=0,
              arm=(0.60, -0.34, 0.72), twist=-30, elbow=62,
              r_arm=(0.96, -0.24, 0.14), r_twist=0, r_elbow=24, r_wrist=26,
              thigh=(0.20, 0.42, 0.88), knee_out=14, knee=132, foot=20, toe=0, curl=48)),
    # Tightest, at the apex.
    (18, dict(spine=(16, 16, 12), neck=5, head=0, clav=0,
              arm=(0.62, -0.38, 0.68), twist=-30, elbow=66,
              r_arm=(0.96, -0.26, 0.12), r_twist=0, r_elbow=26, r_wrist=28,
              thigh=(0.20, 0.50, 0.84), knee_out=14, knee=136, foot=20, toe=0, curl=52)),
    # Still tucked, starting to let go.
    (23, dict(spine=(13, 13, 10), neck=4, head=-2, clav=0,
              arm=(0.64, -0.30, 0.70), twist=-26, elbow=58,
              r_arm=(0.96, -0.24, 0.14), r_twist=0, r_elbow=24, r_wrist=26,
              thigh=(0.20, 0.34, 0.92), knee_out=14, knee=124, foot=20, toe=0, curl=40)),
    # Opening: legs shoot down for the floor, arms spread for balance.
    (27, dict(spine=(6, 6, 5), neck=1, head=-5, clav=2,
              arm=(0.80, -0.08, 0.59), twist=-8, elbow=36,
              r_arm=(0.94, -0.28, 0.20), r_twist=0, r_elbow=20, r_wrist=26, r_pron=10,
              thigh=(0.13, -0.52, 0.84), knee_out=8, knee=72, foot=15, toe=0, curl=20)),
    (31, dict(spine=(1, 1, 1), neck=0, head=-3, clav=2,
              arm=(0.78, -0.48, 0.40), twist=0, elbow=30,
              r_arm=(0.82, -0.54, 0.20), r_twist=0, r_elbow=16, r_wrist=26, r_pron=22,
              thigh=(0.11, -0.84, 0.53), knee_out=6, knee=46, foot=12, toe=0, curl=14)),
    # Touchdown on level ground: hands Land a balanced, feet-first pose.
    (36, dict(spine=(1, 1, 1), neck=0, head=-1, clav=0,
              arm=(0.66, -0.68, 0.32), twist=0, elbow=26,
              r_arm=(0.72, -0.68, 0.14), r_twist=0, r_elbow=14, r_wrist=26, r_pron=30,
              thigh=(0.10, -0.93, 0.36), knee_out=5, knee=32, foot=10, toe=0, curl=14)),
    # Only seen when the drop is longer than the launch height: legs keep reaching, then Fall.
    (40, dict(spine=(1, 1, 1), neck=0, head=-1, clav=0,
              arm=(0.66, -0.68, 0.32), twist=0, elbow=26,
              r_arm=(0.72, -0.68, 0.14), r_twist=0, r_elbow=14, r_wrist=26, r_pron=30,
              thigh=(0.09, -0.96, 0.27), knee_out=5, knee=26, foot=8, toe=0, curl=14)),
]

# Rough mass fractions, to hold the centre of mass still in the clip. Unity pins the Humanoid root
# at the centre of mass anyway, so this only keeps the source clean: the hips carry no travel.
MASS = {
    "Hips": 0.14, "Spine": 0.10, "Spine1": 0.10, "Spine2": 0.12, "Neck": 0.02, "Head": 0.08,
    "LeftArm": 0.03, "LeftForeArm": 0.02, "LeftHand": 0.01,
    "RightArm": 0.03, "RightForeArm": 0.02, "RightHand": 0.01,
    "LeftUpLeg": 0.10, "LeftLeg": 0.05, "LeftFoot": 0.015,
    "RightUpLeg": 0.10, "RightLeg": 0.05, "RightFoot": 0.015,
}

# Stops the two sides moving as a mirror image, which reads as robotic. Offsets the right side
# (frames of lag) and is small enough not to change the silhouette.
RIGHT_LAG_FRAMES = 0.6

PREFIX = "mixamorig:"

# ------------------------------------------------------------------------ interpolation


def pchip(keys):
    """Monotone cubic (Fritsch-Carlson) through (x, y) keys. Returns f(x)."""
    xs = [k[0] for k in keys]
    ys = [k[1] for k in keys]
    n = len(xs)
    h = [xs[i + 1] - xs[i] for i in range(n - 1)]
    d = [(ys[i + 1] - ys[i]) / h[i] for i in range(n - 1)]
    m = [0.0] * n
    m[0] = d[0]
    m[-1] = d[-1]
    for i in range(1, n - 1):
        if d[i - 1] * d[i] <= 0:
            m[i] = 0.0
        else:
            w1 = 2 * h[i] + h[i - 1]
            w2 = h[i] + 2 * h[i - 1]
            m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i])

    def f(x):
        if x <= xs[0]:
            return ys[0]
        if x >= xs[-1]:
            return ys[-1]
        i = 0
        while x > xs[i + 1]:
            i += 1
        t = (x - xs[i]) / h[i]
        t2, t3 = t * t, t * t * t
        return ((2 * t3 - 3 * t2 + 1) * ys[i] + (t3 - 2 * t2 + t) * h[i] * m[i]
                + (-2 * t3 + 3 * t2) * ys[i + 1] + (t3 - t2) * h[i] * m[i + 1])

    return f


class Track:
    """Every pose parameter as a smooth function of the frame."""

    def __init__(self):
        frames = [p[0] for p in POSES]
        self.scalars = {}
        for name in ("neck", "head", "clav", "knee_out", "knee", "foot", "toe", "curl"):
            self.scalars[name] = pchip([(f, p[name]) for f, p in POSES])
        # Per-side arm values: the right side's r_<name>, or the left side's where none is given.
        for name in ("twist", "elbow", "wrist", "pron"):
            self.scalars["l_" + name] = pchip([(f, p.get(name, 0)) for f, p in POSES])
            self.scalars["r_" + name] = pchip([(f, p.get("r_" + name, p.get(name, 0))) for f, p in POSES])
        for axis in range(3):
            self.scalars["spine%d" % axis] = pchip([(f, p["spine"][axis]) for f, p in POSES])
            self.scalars["thigh%d" % axis] = pchip(
                [(f, Vector(p["thigh"]).normalized()[axis]) for f, p in POSES])
            self.scalars["l_arm%d" % axis] = pchip(
                [(f, Vector(p["arm"]).normalized()[axis]) for f, p in POSES])
            self.scalars["r_arm%d" % axis] = pchip(
                [(f, Vector(p.get("r_arm", p["arm"])).normalized()[axis]) for f, p in POSES])
        self.flip = pchip(FLIP_KEYS)
        assert frames[0] == 0 and frames[-1] == LAST_FRAME

    def s(self, name, frame):
        return self.scalars[name](frame)

    def vec(self, name, frame):
        return Vector([self.s("%s%d" % (name, a), frame) for a in range(3)]).normalized()


# ------------------------------------------------------------------------ rotations


def rot(axis, degrees):
    return Quaternion(axis, math.radians(degrees))


def Rx(d):
    return rot((1, 0, 0), d)


def Ry(d):
    return rot((0, 1, 0), d)


def Rz(d):
    return rot((0, 0, 1), d)


def swing(frm, to):
    """The shortest rotation taking direction `frm` to `to`."""
    return frm.rotation_difference(to)


def mirror(v):
    return Vector((-v.x, v.y, v.z))


# ------------------------------------------------------------------- reference skeleton


def read_reference(meta_path):
    """World rotation and local position of each bone in the .meta's skeleton, in FBX axes.

    Unity is left handed with X flipped from the FBX: position (-x, y, z), quaternion (x, -y, -z, w).
    Positions come back in centimetres, the armature's units.
    """
    text = open(meta_path, encoding="utf8").read()
    text = text[text.index("skeleton:"):]
    entry = re.compile(
        r"- name: (.+)\n\s+parentName: (.*)\n"
        r"\s+position: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}\n"
        r"\s+rotation: \{x: ([^,]+), y: ([^,]+), z: ([^,]+), w: ([^}]+)\}")
    local = {}
    for name, parent, px, py, pz, qx, qy, qz, qw in entry.findall(text):
        if not name.startswith(PREFIX):
            continue
        local[name[len(PREFIX):].strip()] = (
            parent[len(PREFIX):].strip() if parent.startswith(PREFIX) else None,
            Vector((-float(px), float(py), float(pz))) * 100.0,
            Quaternion((float(qw), float(qx), -float(qy), -float(qz))),
        )
    world = {}

    def resolve(name):
        if name not in world:
            parent, _, q = local[name]
            world[name] = (resolve(parent) @ q) if parent else q
        return world[name]

    for name in local:
        resolve(name)
    return world, local


# ------------------------------------------------------------------------------- pose


FINGERS = ("Thumb", "Index", "Middle", "Ring", "Pinky")


def arm_c(direction, twist, side):
    """The upper arm's rotation in the chest frame. The arm lies along +-X in the T-pose."""
    rest = Vector((side, 0, 0))
    return rot(direction, twist * side) @ swing(rest, direction)


def leg_c(direction, knee_out, side):
    """The thigh's rotation in the pelvis frame. The leg lies along -Y in the T-pose."""
    return rot(direction, -knee_out * side) @ swing(Vector((0, -1, 0)), direction)


def pose_rotations(track, frame):
    """C for every animated bone, as described at the top of this file."""
    c = {}
    c["Hips"] = Rx(-track.flip(frame))

    def bend(name, parent, q):
        c[name] = c[parent] @ q

    bend("Spine", "Hips", Rx(track.s("spine0", frame)))
    bend("Spine1", "Spine", Rx(track.s("spine1", frame)))
    bend("Spine2", "Spine1", Rx(track.s("spine2", frame)))
    bend("Neck", "Spine2", Rx(track.s("neck", frame)))
    bend("Head", "Neck", Rx(track.s("head", frame)))
    bend("HeadTop_End", "Head", Quaternion())

    for prefix, side, f in (("Left", 1, frame), ("Right", -1, max(0.0, frame - RIGHT_LAG_FRAMES))):
        tag = "l_" if side > 0 else "r_"
        arm = track.vec(tag + "arm", f)
        if side < 0:
            arm = mirror(arm)
        bend(prefix + "Shoulder", "Spine2", Rz(track.s("clav", f) * side))
        bend(prefix + "Arm", "Spine2", arm_c(arm, track.s(tag + "twist", f), side))
        # The T-pose elbow is a hinge about Y: the forearm folds from +-X toward +Z.
        bend(prefix + "ForeArm", prefix + "Arm", Ry(-track.s(tag + "elbow", f) * side))
        bend(prefix + "Hand", prefix + "ForeArm",
             Rx(track.s(tag + "pron", f)) @ Ry(track.s(tag + "wrist", f) * side))
        curl = track.s("curl", f)
        for digit in FINGERS:
            parent = prefix + "Hand"
            for joint in range(1, 5):
                name = "%sHand%s%d" % (prefix, digit, joint)
                amount = 0.0 if joint == 4 else curl * (0.5 if digit == "Thumb" else 1.0)
                # Palm down in the T-pose: fingers close from +-X toward -Y.
                bend(name, parent, Rz(-amount * side))
                parent = name

        thigh = track.vec("thigh", f)
        if side < 0:
            thigh = mirror(thigh)
        bend(prefix + "UpLeg", "Hips", leg_c(thigh, track.s("knee_out", f), side))
        # The knee folds about X, the shin swinging back from -Y toward -Z.
        bend(prefix + "Leg", prefix + "UpLeg", Rx(track.s("knee", f)))
        bend(prefix + "Foot", prefix + "Leg", Rx(track.s("foot", f)))
        bend(prefix + "ToeBase", prefix + "Foot", Rx(track.s("toe", f)))
        bend(prefix + "Toe_End", prefix + "ToeBase", Quaternion())
    return c


def world_pose(c, t_world, local, order, hips_pos):
    """World rotation and position (armature space, cm) of each bone for a set of Cs."""
    rots = {name: c[name] @ t_world[name] for name in order}
    pos = {}
    for name in order:
        parent, offset, _ = local[name]
        pos[name] = hips_pos.copy() if parent is None else pos[parent] + rots[parent] @ offset
    return rots, pos


def centre_of_mass(pos):
    total = sum(MASS.values())
    com = Vector()
    for name, m in MASS.items():
        com += pos[name] * m
    return com / total


# ----------------------------------------------------------------------------- author


def author(arm, t_world, local):
    order = []

    def walk(bone):
        order.append(bone.name[len(PREFIX):])
        for child in bone.children:
            walk(child)

    roots = [b for b in arm.data.bones if b.parent is None]
    assert len(roots) == 1 and roots[0].name == PREFIX + "Hips"
    walk(roots[0])
    missing = [n for n in order if n not in local]
    assert not missing, "bones missing from the reference skeleton: %s" % missing

    track = Track()
    hips_rest = arm.data.bones[PREFIX + "Hips"].head_local.copy()
    com_ref = None

    arm.animation_data_clear()
    arm.animation_data_create()
    action = bpy.data.actions.new("mixamo.com")
    arm.animation_data.action = action

    rest = {b.name[len(PREFIX):]: b.matrix_local.copy() for b in arm.data.bones}
    previous = {}
    for frame in range(0, LAST_FRAME + 1):
        c = pose_rotations(track, frame)
        rots, pos = world_pose(c, t_world, local, order, hips_rest)
        com = centre_of_mass(pos)
        if com_ref is None:
            com_ref = com
        # Keep the centre of mass where it was at takeoff: the hips carry the flip, not travel.
        hips = hips_rest + (com_ref - com)
        rots, pos = world_pose(c, t_world, local, order, hips)

        posed = {}
        for name in order:
            pb = arm.pose.bones[PREFIX + name]
            parent = local[name][0]
            target = Matrix.Translation(pos[name]) @ rots[name].to_matrix().to_4x4()
            posed[name] = target
            if parent is None:
                basis = rest[name].inverted() @ target
            else:
                basis = (rest[parent].inverted() @ rest[name]).inverted() @ posed[parent].inverted() @ target
            loc, q, _ = basis.decompose()
            # Stay on one side of the quaternion double cover, frame to frame.
            if name in previous and previous[name].dot(q) < 0:
                q = -q
            previous[name] = q
            pb.rotation_mode = "QUATERNION"
            pb.rotation_quaternion = q
            pb.location = loc
            pb.scale = (1, 1, 1)
            pb.keyframe_insert("rotation_quaternion", frame=frame, group=pb.name)
            pb.keyframe_insert("location", frame=frame, group=pb.name)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    source, meta, out = argv[0], argv[1], argv[2]

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=source)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    # A root named exactly "Armature" exports as no node at all, so Hips stays the top bone, as
    # in the Mixamo files.
    assert arm.name == "Armature"

    t_world, local = read_reference(meta)
    author(arm, t_world, local)

    scene = bpy.context.scene
    scene.name = "mixamo.com"   # names the FBX take; the other X Bot clips use this take name
    scene.render.fps = FPS
    scene.render.fps_base = 1.0
    scene.frame_start = 0
    scene.frame_end = LAST_FRAME

    bpy.ops.export_scene.fbx(
        filepath=out,
        object_types={"ARMATURE"},
        add_leaf_bones=False,
        use_armature_deform_only=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,   # a key on every frame
    )
    print("BACKFLIP exported", out, "frames 0-%d at %d fps" % (LAST_FRAME, FPS))

    # Round trip: what the file actually says the hips do.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=out)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    action = arm.animation_data.action
    print("BACKFLIP action", action.name, tuple(action.frame_range), "bones", len(arm.pose.bones))
    scene = bpy.context.scene
    hips = arm.pose.bones[PREFIX + "Hips"]
    last = None
    total = 0.0
    for frame in range(int(action.frame_range[0]), int(action.frame_range[1]) + 1):
        scene.frame_set(frame)
        up = hips.matrix.to_3x3() @ Vector((0, 1, 0))
        angle = math.degrees(math.atan2(up.z, up.y))  # FBX axes: +Z forward, +Y up
        if last is not None:
            step = (angle - last + 180.0) % 360.0 - 180.0
            total += step
        last = angle
        print("BACKFLIP f%02d hips-up pitch %7.1f  total %7.1f  head %s" % (
            frame, angle, total, [round(v, 1) for v in hips.head]))


main()
