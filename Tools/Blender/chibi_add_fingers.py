"""
Adds finger bones to the chibi's Tripo rig, skins the hands to them, and re-exports the FBX.

Why: the Tripo auto-rig has 41 bones and none in the fingers (R_Hand / L_Hand are leaves), so
the hand can't close on the sword, and the 20 finger muscle curves every Mixamo clip carries
have nothing to drive. The hands themselves are modelled with separate fingers (a fingerless
glove: four fingers apart past the knuckles, plus a thumb), so they can take real chains.

What it does, per hand:
  1. Measures the hand in its own frame: u along the hand bone, v toward the thumb (the way the
     character faces, humanoid T-pose), p toward the palm.
  2. Fingers: the hand's vertices past a sweep line along u fall apart into four clusters, one
     per finger. Each cluster's principal axis is the finger's centreline; its edge loops group
     into stations (where it leaves the glove, mid-finger, the tip cap). Joints go on the
     centreline: the knuckle (MCP) at the palm's front edge, PIP and DIP between stations, so
     every loop moves rigidly with one bone and only the faces between loops stretch.
  3. Thumb: fused to the palm, so it is found by surface (geodesic) distance from its tip; its
     principal axis and loops place CMC (inside the palm), MCP and IP the same way.
  4. Adds three bones per digit (Unity Humanoid's proximal / intermediate / distal), parented to
     the hand bone, and moves each digit's vertices onto them: a digit vertex takes its whole
     weight to the chain, blended along the chain at each joint; vertices in the palm next to a
     knuckle blend between the palm's old weights and the chain. Every vertex keeps at most 4
     influences, normalized, matching Unity's import.
  5. Exports with the settings the original was exported with (FBX units scale, face smoothing,
     embedded texture, no leaf bones, no animation), which round-trips the existing 41 bones,
     every vertex, UV and skin weight unchanged (checked to 1e-5).

Run it on the finger-less Tripo FBX. It refuses one that already has finger bones: some digit
weight came off the forearm twist bones (the auto-rig gave the thumb a fifth of its weight on the
forearm), so it can't be handed back exactly. To redo the rig, start from the last finger-less
version in git, e.g.
  git show 902edb3:Assets/_Project/Art/Characters/Chibi/chibi_unity.fbx | git lfs smudge > tripo.fbx

Usage (Blender 5.2, headless):
  blender -b --factory-startup --python Tools/Blender/chibi_add_fingers.py -- \
      --in  Assets/_Project/Art/Characters/Chibi/chibi_unity.fbx \
      --out Assets/_Project/Art/Characters/Chibi/chibi_unity.fbx \
      [--preview DIR] [--curl 85,100,60] [--thumb-curl 0,15,20] [--thumb-swing 80]

--preview renders each hand at rest and closed round a handle into DIR, with a contact sheet
per hand: the fist FingerCurl closes in Unity (FingerCurlMath's axes, the profile's angles) at
the grab point GripFrameSolver.TrySolveClosedFist picks. It checks the skinning and tunes the
profile without Unity.

Measured on the chibi (2026-10-28), in the FBX's own units (Unity imports at x1.7): hand 10 cm
from wrist to middle fingertip; fingers about 2 cm thick, each with three edge loops past the
glove; thumb about 4 cm, pointing mostly along the fingers.
"""
import argparse
import heapq
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

DIGITS = ("Thumb", "Index", "Middle", "Ring", "Pinky")
SEGMENTS = 3                      # proximal, intermediate, distal (Unity Humanoid)

# Measured constants (FBX units). See the module docstring for how each is used.
SWEEP = np.arange(0.03, 0.075, 0.0025)   # u values tried to split the fingers apart
MIN_FINGER_VERTS = 8              # a cluster smaller than this is not a finger
SPLIT_MARGIN = 0.005              # finger clusters are cut this far past where the fingers part
STATION_GAP = 0.004               # edge loops further apart than this along a finger are separate stations
KNUCKLE_INSET = 0.003             # MCP sits this far behind the palm's front edge
KNUCKLE_BLEND = 0.006             # palm vertices within this of the MCP blend onto the finger
JOINT_BLEND = 0.0025              # half-width of the weight blend at PIP / DIP
LATERAL_REACH = 0.011             # palm vertices this close to a finger's axis can follow it
THUMB_REACH = 0.040               # geodesic distance from the thumb tip that is all thumb
THUMB_BLEND = 0.010               # ... and the blend into the palm beyond it
MAX_INFLUENCES = 4                # Unity's maxBonesPerVertex on the chibi import


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="src", required=True)
    ap.add_argument("--out", dest="dst")
    ap.add_argument("--preview")
    # HandGripProfile values to preview (fingerCurl, thumbCurl °; thumbSwing °), defaulting to
    # the chibi's tuned profile.
    ap.add_argument("--curl", default="85,100,60")
    ap.add_argument("--thumb-curl", default="0,15,20")
    ap.add_argument("--thumb-swing", type=float, default=80.0)
    # Both in FBX units (Unity m / 1.7). The grip radius places the handle the way
    # HandGripProfile.gripRadius does; the handle radius is only what the preview draws
    # (the compass's neck and knob, where the fist closes, are 1.0-1.85 cm in radius).
    ap.add_argument("--grip-radius", type=float, default=0.015 / 1.7)
    ap.add_argument("--handle-radius", type=float, default=0.015 / 1.7)
    ap.add_argument("--views", default="thumbside,back,palm,out,threeq")
    ap.add_argument("--sides", default="RL")
    ap.add_argument("--tag", default="")
    return ap.parse_args(argv)


def bone_name(side, digit, seg):
    return f"{side}_{digit}{seg + 1}"


# --------------------------------------------------------------------------- scene helpers

def load(src):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = next(o for o in bpy.data.objects if o.type == "MESH")
    return arm, body


def existing_fingers(arm):
    names = {bone_name(s, d, k) for s in "RL" for d in DIGITS for k in range(SEGMENTS)}
    return [b.name for b in arm.data.bones if b.name in names]


# --------------------------------------------------------------------------- measurement

class Hand:
    """One hand's vertices in its own frame, and the joints measured from them."""

    def __init__(self, side, arm, body):
        self.side = side
        self.arm = arm
        self.body = body
        bone = arm.data.bones[f"{side}_Hand"]
        head = arm.matrix_world @ bone.head_local
        tail = arm.matrix_world @ bone.tail_local
        u = (tail - head).normalized()
        fwd = Vector((0, -1, 0))                      # the way the character faces = thumb side
        v = (fwd - u * fwd.dot(u)).normalized()
        down = Vector((0, 0, -1))                     # T-pose: palms down
        p = (down - u * down.dot(u) - v * down.dot(v)).normalized()
        self.origin, self.u, self.v, self.p = head, u, v, p
        self.R = np.array([u, v, p])                  # world -> (u, v, p)

        mw = body.matrix_world
        self.world = np.array([list(mw @ vx.co) for vx in body.data.vertices])
        self.local = (self.world - np.array(head)) @ self.R.T
        gidx = {g.name: g.index for g in body.vertex_groups}
        hi = gidx[f"{side}_Hand"]
        self.weights = [{g.group: g.weight for g in vx.groups if g.weight > 0} for vx in body.data.vertices]
        self.hand_index = hi
        self.H = [i for i, w in enumerate(self.weights) if w.get(hi, 0) > 0]
        self.Hset = set(self.H)
        bm = bmesh.new()
        bm.from_mesh(body.data)
        self.adj = {i: [] for i in self.H}
        for e in bm.edges:
            a, b = e.verts[0].index, e.verts[1].index
            if a in self.Hset and b in self.Hset:
                d = float(np.linalg.norm(self.world[a] - self.world[b]))
                self.adj[a].append((b, d))
                self.adj[b].append((a, d))
        self.full_adj = {}
        for e in bm.edges:
            a, b = e.verts[0].index, e.verts[1].index
            self.full_adj.setdefault(a, []).append(b)
            self.full_adj.setdefault(b, []).append(a)
        bm.free()

    def to_world(self, q):
        return Vector(np.array(self.origin) + np.asarray(q) @ self.R)

    # ---- fingers -----------------------------------------------------------------------

    def _components(self, sel):
        seen, out = set(), []
        for s in sel:
            if s in seen:
                continue
            stack, comp = [s], []
            seen.add(s)
            while stack:
                x = stack.pop()
                comp.append(x)
                for y, _ in self.adj[x]:
                    if y in sel and y not in seen:
                        seen.add(y)
                        stack.append(y)
            out.append(comp)
        return out

    def _fingers_past(self, t):
        """The finger clusters past u = t: big enough, joined to the hand below t (which rules
        out stray inner shells), and not hanging palm-ward (which rules out the thumb)."""
        L = self.local
        comps = []
        for c in self._components({i for i in self.H if L[i, 0] > t}):
            cs = set(c)
            attached = any(n not in cs for i in c for n in self.full_adj.get(i, ()))
            if len(c) >= MIN_FINGER_VERTS and attached and L[c, 2].mean() < 0.015:
                comps.append(c)
        return comps

    def find_fingers(self):
        """Four clusters, index first. Cut SPLIT_MARGIN past where the fingers first come apart,
        so the palm's front edge loop is never counted as part of a finger: it stays palm and
        sets where the knuckle goes."""
        for t in SWEEP:
            if len(self._fingers_past(t)) == 4:
                self.split = t
                comps = self._fingers_past(t + SPLIT_MARGIN)
                if len(comps) != 4:
                    comps = self._fingers_past(t)
                comps.sort(key=lambda c: -self.local[c, 1].mean())   # thumb side first: index .. pinky
                return comps
        raise RuntimeError(f"{self.side}: never found four separate fingers")

    def finger_chain(self, comp, others_palm):
        L = self.local
        P = L[comp]
        c = P.mean(axis=0)
        _, _, vt = np.linalg.svd(P - c)
        d = vt[0] if vt[0][0] > 0 else -vt[0]
        s = (P - c) @ d
        order = np.sort(s)
        groups, cur = [], [order[0]]
        for x in order[1:]:
            if x - cur[-1] > STATION_GAP:
                groups.append(cur)
                cur = []
            cur.append(x)
        groups.append(cur)
        # Front edge of the palm in front of this finger.
        rel = L[others_palm] - c
        sp = rel @ d
        lateral = np.linalg.norm(rel - np.outer(sp, d), axis=1)
        near = (lateral < LATERAL_REACH) & (sp < order[0])
        s_edge = sp[near].max() if near.any() else order[0] - 0.012
        s_mcp = s_edge - KNUCKLE_INSET
        if len(groups) >= 3:
            # The last three stations: where it leaves the glove, mid-finger, the tip cap.
            s_pip = (max(groups[-3]) + min(groups[-2])) / 2
            s_dip = (max(groups[-2]) + min(groups[-1])) / 2
        else:                                  # fallback: classic 45/30/25 proportions
            span = order[-1] - s_mcp
            s_pip, s_dip = s_mcp + 0.45 * span, s_mcp + 0.75 * span
        s_tip = order[-1]
        joints = [c + d * x for x in (s_mcp, s_pip, s_dip, s_tip)]
        return {"centre": c, "dir": d, "s": [s_mcp, s_pip, s_dip, s_tip], "joints": joints,
                "stations": [len(g) for g in groups], "verts": comp}

    # ---- thumb -------------------------------------------------------------------------

    def thumb_chain(self, index_v):
        L = self.local
        cand = [i for i in self.H if L[i, 0] > 0]
        seed = max(cand, key=lambda i: L[i, 2] + (L[i, 1] - index_v))
        dist = {seed: 0.0}
        pq = [(0.0, seed)]
        while pq:
            d0, x = heapq.heappop(pq)
            if d0 > dist[x]:
                continue
            for y, w in self.adj[x]:
                if d0 + w < dist.get(y, 1e9):
                    dist[y] = d0 + w
                    heapq.heappush(pq, (d0 + w, y))
        core = [i for i, g in dist.items() if g < THUMB_REACH]
        P = L[core]
        c = P.mean(axis=0)
        _, _, vt = np.linalg.svd(P - c)
        tip_dir = L[seed] - c
        d = vt[0] if vt[0] @ tip_dir > 0 else -vt[0]
        s = (P - c) @ d
        s_root, s_tip = s.min(), s.max()
        span = s_tip - s_root
        s_cmc = s_root - 0.5 * span
        s_mcp = s_root + 0.2 * span
        s_ip = s_root + 0.6 * span
        joints = [c + d * x for x in (s_cmc, s_mcp, s_ip, s_tip)]
        return {"centre": c, "dir": d, "s": [s_cmc, s_mcp, s_ip, s_tip], "joints": joints,
                "geodesic": dist, "seed": seed, "verts": core}


# --------------------------------------------------------------------------- weights

def smoothstep(e0, e1, x):
    t = min(max((x - e0) / (e1 - e0), 0.0), 1.0)
    return t * t * (3 - 2 * t)


def chain_split(s, chain):
    """Share of a digit vertex on proximal / intermediate / distal from its position s along the chain."""
    _, s1, s2, _ = chain["s"]
    a = smoothstep(s1 - JOINT_BLEND, s1 + JOINT_BLEND, s)      # past PIP
    b = smoothstep(s2 - JOINT_BLEND, s2 + JOINT_BLEND, s)      # past DIP
    return [1 - a, a - b, b]


def skin_hand(hand, chains, groups):
    """Move each digit's vertices onto its chain. chains: digit -> chain dict."""
    L = hand.local
    new = {}
    finger_verts = {d: set(ch["verts"]) for d, ch in chains.items() if d != "Thumb"}
    for i in hand.H:
        best, best_m, best_s = None, 0.0, 0.0
        for digit, ch in chains.items():
            rel = L[i] - ch["centre"]
            s = rel @ ch["dir"]
            lateral = np.linalg.norm(rel - s * ch["dir"])
            if digit == "Thumb":
                g = ch["geodesic"].get(i)
                m = 0.0 if g is None else 1.0 - smoothstep(THUMB_REACH, THUMB_REACH + THUMB_BLEND, g)
            elif i in finger_verts[digit]:
                m = 1.0
            else:
                s0 = ch["s"][0]
                m = smoothstep(s0 - KNUCKLE_BLEND, s0 + KNUCKLE_BLEND, s) * (1 - smoothstep(0.6 * LATERAL_REACH, LATERAL_REACH, lateral))
            if m > best_m:
                best, best_m, best_s = digit, m, s
        if best is None or best_m < 1e-3:
            continue
        w = dict(hand.weights[i])
        total = sum(w.values())
        out = {k: x * (1 - best_m) for k, x in w.items()}
        for k, share in enumerate(chain_split(best_s, chains[best])):
            if share > 0:
                gi = groups[(best, k)].index
                out[gi] = out.get(gi, 0) + best_m * total * share
        top = sorted(out.items(), key=lambda kv: -kv[1])[:MAX_INFLUENCES]
        norm = sum(x for _, x in top)
        new[i] = {k: x / norm for k, x in top if x / norm > 1e-4}
    return new


def apply_weights(body, new):
    for i, w in new.items():
        vx = body.data.vertices[i]
        for g in list(vx.groups):
            body.vertex_groups[g.group].remove([i])
        for gi, x in w.items():
            body.vertex_groups[gi].add([i], x, "REPLACE")


# --------------------------------------------------------------------------- bones

def add_bones(arm, side, chains, back_of_hand):
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.data.edit_bones
    inv = arm.matrix_world.inverted()
    hand = eb[f"{side}_Hand"]
    for digit in DIGITS:
        ch = chains[digit]
        parent = hand
        for k in range(SEGMENTS):
            b = eb.new(bone_name(side, digit, k))
            b.head = inv @ ch["world"][k]
            b.tail = inv @ ch["world"][k + 1]
            b.parent = parent
            b.use_connect = k > 0
            b.use_deform = True
            b.align_roll(inv.to_3x3() @ back_of_hand)
            parent = b
    bpy.ops.object.mode_set(mode="OBJECT")


# --------------------------------------------------------------------------- export

def export(dst):
    bpy.ops.export_scene.fbx(
        filepath=dst, use_selection=False, add_leaf_bones=False, bake_anim=False,
        apply_scale_options="FBX_SCALE_UNITS", mesh_smooth_type="FACE",
        path_mode="COPY", embed_textures=True)


# --------------------------------------------------------------------------- preview

def grab_point(hand, chains, new, groups, radius):
    """GripFrameSolver.TrySolveClosedFist, in Blender world space."""
    knuckles = [chains[d]["world"][0] for d in DIGITS[1:]]
    kc = sum(knuckles, Vector()) / 4
    knuckle = (kc - hand.origin).normalized()
    across = knuckles[0] - knuckles[-1]
    thumb = (across - knuckle * across.dot(knuckle)).normalized()
    palm = knuckle.cross(thumb)
    if palm.dot(hand.p) < 0:
        palm = -palm
    hi = hand.hand_index
    prox = {groups[(d, 0)].index for d in DIGITS[1:]}
    palm_pts, finger_pts = [], []
    for i in hand.H:
        w = new.get(i, hand.weights[i])
        if w.get(hi, 0) >= 0.5:
            palm_pts.append(Vector(hand.world[i]))
        elif any(w.get(g, 0) >= 0.5 for g in prox):
            finger_pts.append(Vector(hand.world[i]))
    fd = [(x - kc).dot(palm) for x in finger_pts]
    finger_half = max(0.0, (max(fd) - min(fd)) / 2) if fd else 0.0
    # GripFrameSolver.PalmUnderHandle: the palm's depth on the strip the handle lies across.
    back = finger_half + 2 * radius
    strip = [(x - kc).dot(palm) for x in palm_pts if -back <= (x - kc).dot(knuckle) <= 0]
    palm_half = max(0.0, max(strip) if strip else max((x - kc).dot(palm) for x in palm_pts))
    centre = kc + palm * (palm_half + radius) - knuckle * (finger_half + radius)
    return centre, knuckle, thumb, palm, palm_half, finger_half


def pose_fist(arm, side, frame, curl, thumb_curl, thumb_swing):
    """FingerCurl's pose: fingers bent about the handle; the thumb bent about its own flex
    axis (FingerCurlMath.ThumbFlexAxis) after its first joint swings across the palm
    (ThumbSwingAxis, Opposed). Every angle turns its digit toward the palm."""
    centre, knuckle, thumb_side, palm, *_ = frame
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="POSE")
    for digit in DIGITS:
        is_thumb = digit == "Thumb"
        angles = thumb_curl if is_thumb else curl
        pb0 = arm.pose.bones[bone_name(side, digit, 0)]
        rest_dir = (arm.matrix_world.to_3x3() @ (pb0.bone.tail_local - pb0.bone.head_local)).normalized()
        if is_thumb:
            flat = rest_dir - palm * rest_dir.dot(palm)
            axis = flat.cross(palm).normalized()
        else:
            axis = thumb_side.copy()
        if axis.cross(rest_dir).dot(palm) < 0:
            axis = -axis
        swing_axis = palm.copy()
        if swing_axis.cross(rest_dir).dot(knuckle - thumb_side) < 0:
            swing_axis = -swing_axis
        for k in range(SEGMENTS):
            pb = arm.pose.bones[bone_name(side, digit, k)]
            rest = (arm.matrix_world.to_3x3() @ pb.bone.matrix_local.to_3x3())
            q = Quaternion((rest.inverted() @ axis).normalized(), math.radians(angles[k]))
            if is_thumb and k == 0:
                q = Quaternion((rest.inverted() @ swing_axis).normalized(), math.radians(thumb_swing)) @ q
            pb.rotation_mode = "QUATERNION"
            pb.rotation_quaternion = q
    bpy.ops.object.mode_set(mode="OBJECT")


def clear_pose(arm):
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = Quaternion()


def render_views(hand, body, outdir, tag, centre, wanted):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.display.shading.show_cavity = True
    scene.render.resolution_x = scene.render.resolution_y = 480
    if scene.world is None:
        scene.world = bpy.data.worlds.new("W")
    cam = scene.camera
    if cam is None:
        cd = bpy.data.cameras.new("PreviewCam")
        cd.type = "ORTHO"
        cam = bpy.data.objects.new("PreviewCam", cd)
        scene.collection.objects.link(cam)
        scene.camera = cam
    cam.data.ortho_scale = 0.16
    u, v, p = hand.u, hand.v, hand.p
    views = {"thumbside": v, "back": -p, "palm": p, "out": u, "threeq": (v * 0.7 - p * 0.5 + u * 0.5)}
    for name, d in views.items():
        if name not in wanted:
            continue
        d = d.normalized()
        up = -p if abs(d.dot(p)) < 0.9 else u
        z = d
        x = up.cross(z).normalized()
        y = z.cross(x)
        m = Matrix((x, y, z)).transposed().to_4x4()
        m.translation = centre + d * 1.0
        cam.matrix_world = m
        scene.render.filepath = os.path.join(outdir, f"{hand.side}_{tag}_{name}.png")
        bpy.ops.render.render(write_still=True)


def contact_sheet(paths, cols, out):
    """Tile equal-size renders into one PNG, row-major from the top left."""
    imgs = [bpy.data.images.load(p) for p in paths]
    w, h = imgs[0].size
    rows = (len(imgs) + cols - 1) // cols
    sheet = np.zeros((rows * h, cols * w, 4), dtype=np.float32)
    for k, im in enumerate(imgs):
        px = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)
        r, c = divmod(k, cols)
        y0 = (rows - 1 - r) * h                      # Blender pixels start bottom-left
        sheet[y0:y0 + h, c * w:(c + 1) * w] = px
    res = bpy.data.images.new("sheet", cols * w, rows * h, alpha=True)
    res.pixels = sheet.ravel()
    res.filepath_raw = out
    res.file_format = "PNG"
    res.save()


def preview(arm, body, hands, chains_by_side, new_by_side, groups, args):
    tex = next((im for im in bpy.data.images if im.size[0] > 0), None)
    curl = [float(x) for x in args.curl.split(",")]
    thumb_curl = [float(x) for x in args.thumb_curl.split(",")]
    wanted = set(args.views.split(","))
    for side, hand in hands.items():
        if side not in args.sides:
            continue
        frame = grab_point(hand, chains_by_side[side], new_by_side[side], groups[side], args.grip_radius)
        centre, knuckle, thumb_dir, palm, ph, fh = frame
        print(f"{side} grab point {tuple(round(x, 4) for x in centre)}  palm half {ph:.4f}  finger half {fh:.4f}")
        bpy.ops.mesh.primitive_cylinder_add(radius=args.handle_radius, depth=0.09, location=centre)
        cyl = bpy.context.active_object
        cyl.name = f"Handle_{side}"
        cyl.rotation_mode = "QUATERNION"
        cyl.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(thumb_dir)
        clear_pose(arm)
        cyl.hide_render = True
        render_views(hand, body, args.preview, args.tag + "rest", centre, wanted)
        cyl.hide_render = False
        pose_fist(arm, side, frame, curl, thumb_curl, args.thumb_swing)
        bpy.context.view_layer.update()
        render_views(hand, body, args.preview, args.tag + "fist", centre, wanted)
        clear_pose(arm)
        cyl.hide_render = True
        order = [v for v in ("thumbside", "back", "palm", "out", "threeq") if v in wanted]
        paths = [os.path.join(args.preview, f"{side}_{args.tag}{state}_{v}.png")
                 for state in ("rest", "fist") for v in order]
        contact_sheet(paths, len(order), os.path.join(args.preview, f"{side}_{args.tag}sheet.png"))


# --------------------------------------------------------------------------- main

def main():
    args = parse_args()
    arm, body = load(args.src)
    present = existing_fingers(arm)
    if present:
        sys.exit(f"{args.src} already has {len(present)} finger bones ({present[0]}, ...). Run this on "
                 "the finger-less Tripo FBX; see the module docstring for how to get it from git.")

    hands, chains_by_side, new_by_side, groups = {}, {}, {}, {}
    for side in "RL":
        hand = Hand(side, arm, body)
        comps = hand.find_fingers()
        in_fingers = set().union(*map(set, comps))
        palm_only = [i for i in hand.H if i not in in_fingers]
        chains = {}
        for digit, comp in zip(DIGITS[1:], comps):
            chains[digit] = hand.finger_chain(comp, palm_only)
        index_v = hand.local[chains["Index"]["verts"], 1].mean()
        chains["Thumb"] = hand.thumb_chain(index_v)
        for digit, ch in chains.items():
            ch["world"] = [hand.to_world(j) for j in ch["joints"]]
            print(f"{side} {digit:6s} joints(u,v,p) " +
                  "  ".join("(" + ",".join(f"{x:+.4f}" for x in j) + ")" for j in ch["joints"]) +
                  (f"  stations {ch['stations']}" if "stations" in ch else f"  thumb verts {len(ch['verts'])}"))
        print(f"{side} fingers split at u>{hand.split:.4f}")
        groups[side] = {}
        for digit in DIGITS:
            for k in range(SEGMENTS):
                groups[side][(digit, k)] = body.vertex_groups.new(name=bone_name(side, digit, k))
        back = -hand.p
        add_bones(arm, side, chains, back)
        new = skin_hand(hand, chains, groups[side])
        apply_weights(body, new)
        print(f"{side}: reweighted {len(new)} of {len(hand.H)} hand vertices")
        hands[side], chains_by_side[side], new_by_side[side] = hand, chains, new

    if args.dst:
        export(args.dst)
        print("exported", args.dst)
    if args.preview:
        os.makedirs(args.preview, exist_ok=True)
        preview(arm, body, hands, chains_by_side, new_by_side, groups, args)
        print("preview written to", args.preview)


main()
