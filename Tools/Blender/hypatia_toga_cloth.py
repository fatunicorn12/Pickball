"""
Hypatia toga: the clothing pass. Re-weights the fused Tripo mesh so the dress hangs from the waist
and shoulders instead of wrapping each leg and arm, and adds Cloth_* spring chains for ClothSetup.

Runs after hypatia_toga_prepare.py, whose helpers it imports. Headless only:

    blender.exe -b --factory-startup --python Tools/Blender/hypatia_toga_cloth.py -- <mode> ...

    build <in.fbx> <jpg> <out.fbx> <report.json>     re-weight, add chains, export
    check <in.fbx> <out.fbx> <jpg> <weights.npz> <report.json>
                                                     the delivery checks on a re-import
    regions <fbx> <jpg> <outdir>                     colour-coded regions, for looking at
    dominance <fbx> <outdir>                         dominant bone per face, for looking at
    clips <fbx> <jpg> <project> <outdir> <prefix> <frames.json> [nosim] [only=a,b]
                                                     clips: stress numbers, and renders of the frames
                                                     in frames.json (written when it does not exist)
    compare <before-dir> <after-dir> <before-prefix> <after-prefix> <frames.json> <out.png> [clip,...]
    closeup <fbx> <jpg> <project> <out.png> <clip:frame>...   chest and shoulders in those frames
    hemviews <fbx> <jpg> <project> <out.png>         the hem standing, idle and lying dead, from below

What build does, region by region (positions are model metres, she is 1.0 tall in the file):

    skirt, toga, bodice   one field of position, so layers lying on each other keep the same weights
                          and cannot pass through each other. Height picks Spine2 > Spine1 > Spine >
                          Hips, then below the hip Hips blends onto the skirt chains. No leg, foot or
                          toe bones, so no thigh or knee can read through the cloth.
    veil                  Head > Neck > Spine2 by height, then one chain per back panel. No arm bones.
    skin, eyes, bracelet  untouched. Cloth next to skin fades back to its original weights along the
                          surface (upper arm shares moved to the collarbone), so no seam opens at an
                          armhole or the hairline.
    sandals               each rigid on the lower bone of the front skirt chain on its side, because
                          there are no legs under the skirt; see SANDALS_RIDE_HEM.
    ornaments             each rigid on the main bone of the cloth it sits on.
    back shell twins      copied from their front vertex, found through the shared UVs.
    hem cap               added after the shell: a dark fan closing the tube, on the skirt's own field,
                          textured from a patch painted into an unused corner of the atlas. The JPEG
                          given to build is painted in place, once.
"""

import heapq
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hypatia_toga_prepare as prep  # noqa: E402


N_FRONT = 5009         # the source mesh's vertices; prepare's back shell comes after them
N_SHELL_END = 6595     # end of the back shell; this pass's hem cap comes after it

# Region ids, per vertex.
SKIN, SKIRT, TOGA, BODICE, VEIL, SANDAL, EYE, BRACELET, ORNAMENT, CAP = range(10)
REGION_NAMES = ("skin", "skirt", "toga", "bodice", "veil", "sandal", "eye", "bracelet", "ornament", "cap")
REGION_COLOURS = ((.55, .55, .55), (.1, .25, .9), (.9, .1, .1), (.1, .8, .1), (.8, .1, .8),
                  (.3, .15, .05), (1, 1, 1), (1, .6, .1), (1, .9, .1), (.02, .02, .02))
CLOTH = (SKIRT, TOGA, BODICE, VEIL)

# Knots of the height fields: at or below a knot's height the vertex belongs to that bone.
BODY_SPINE = ((0.545, "Hips"), (0.600, "Spine"), (0.665, "Spine1"), (0.740, "Spine2"))
VEIL_SPINE = ((0.790, "Spine2"), (0.840, "Neck"), (0.890, "Head"))

# Skirt chains: root, joint and the height the tip ends up at (ClothSetup continues the last
# segment for the tip, so the two segments are equal). Angles are degrees from straight ahead,
# positive toward her left. Two in front of the thighs, where a bent knee pushes; one square behind.
SKIRT_ROOT_Z = 0.47
SKIRT_JOINT_Z = 0.25
SKIRT_TIP_Z = 0.03
SKIRT_ANGLES = (("P035", 35.0), ("M035", -35.0), ("P100", 100.0), ("M100", -100.0), ("B180", 180.0))
SKIRT_HIPS_BLEND = (0.47, 0.36)    # Hips at the first height, all chain at the second
SKIRT_JOINT_BLEND = (0.31, 0.19)
DISC_RADIUS = (0.03, 0.09)         # the skirt's closed bottom: rigid with the Hips at its middle

# Veil chains: one per back panel, either side of the slit that opens below VEIL_ROOT_Z.
VEIL_ROOT_Z = 0.72
VEIL_JOINT_Z = 0.585
VEIL_TIP_Z = 0.45
VEIL_X = 0.09
VEIL_PARENT = "Spine1"             # lower than the veil's top, so a bowing chest does not lever it out
VEIL_ROOT_BLEND = (0.74, 0.62)
VEIL_JOINT_BLEND = (0.64, 0.53)

BORDER_FADE = (0.004, 0.05)        # along the surface from skin: original weights > the field

# The mesh has no legs under the skirt, only two loose sandals. Newton's idle holds her left knee
# bent, so on foot bones that sandal floats out through the front of the skirt in every frame of the
# fight. On the hem, each sandal stays where it was modelled. False puts them back on the foot bones.
SANDALS_RIDE_HEM = True
SANDAL_BONES = {1: "Cloth_Skirt_P035_2", -1: "Cloth_Skirt_M035_2"}  # by the sign of X
SEAM_SMOOTH_PASSES = 4

# The gown is an open tube. Across it, a little above the hem, a fan of dark triangles closes it, so
# from below or lying down the eye stops at shadow rather than the inside of the cloth. Sandals behind
# SOLE_DARK_FROM_Y take the same dark, so only the front of each one reads against the cap.
CAP_Z = 0.03
CAP_RING = 32
CAP_INSET = 0.002
SOLE_DARK_FROM_Y = -0.075
PATCH_MARGIN_PX = 16               # of unused texture kept round the patch, for mips and compression
PATCH_COLOUR = (0.012, 0.010, 0.012)
JPEG_QUALITY = 97
MIN_WEIGHT = 0.01
MAX_INFLUENCES = 4

# Chain bone names carry no word Unity's Humanoid auto-mapper keys on (arm, leg, hand, head, spine,
# hip, left, right, up and so on), hence P and M for her left and right, and the prefix ClothSetup finds.
CHAIN_PREFIX = "Cloth_"
BACK_ORNAMENT_Y = 0.09             # an ornament this far behind her pins the veil's slit, not the toga


def bone(short):
    return "mixamorig:" + short


def smooth(x):
    x = np.clip(x, 0.0, 1.0)
    return x * x * (3.0 - 2.0 * x)


def ramp(z, top, bottom):
    """0 at or above `top`, 1 at or below `bottom`."""
    return smooth((top - z) / max(top - bottom, 1e-9))


# ---------------------------------------------------------------------------------------------
# Back shell mapping
# ---------------------------------------------------------------------------------------------

def uv_array(me):
    uv = np.empty(len(me.loops) * 2, dtype=np.float32)
    me.uv_layers.active.data.foreach_get("uv", uv)
    return uv.reshape(-1, 2)


def twin_map(me):
    """Twin vertex -> its front vertex. Prepare gives a twin polygon the UVs of its front polygon
    in reverse order, so the UVs pair the corners exactly."""
    uv = uv_array(me)
    by_uv = {}
    n_shell = min(len(me.vertices), N_SHELL_END)
    for p in me.polygons:
        if p.vertices[0] < N_FRONT:
            ls = uv[p.loop_start:p.loop_start + p.loop_total]
            by_uv.setdefault(tuple(sorted(map(tuple, ls.round(5).tolist()))), p.index)
    out = {}
    for p in me.polygons:
        if N_FRONT <= p.vertices[0] < n_shell:
            ls = uv[p.loop_start:p.loop_start + p.loop_total]
            q = me.polygons[by_uv[tuple(sorted(map(tuple, ls.round(5).tolist())))]]
            quv = uv[q.loop_start:q.loop_start + q.loop_total]
            for i, v in enumerate(p.vertices):
                out[v] = q.vertices[int(np.argmin(np.abs(quv - ls[i]).sum(axis=1)))]
    if len(out) != n_shell - N_FRONT:
        raise RuntimeError(f"mapped {len(out)} twins of {n_shell - N_FRONT}")
    return out


# ---------------------------------------------------------------------------------------------
# Regions
# ---------------------------------------------------------------------------------------------

def face_colours(me, rgb):
    """Per face: 0 skin or hair, 1 gold, 2 cream, by the median of a few texture samples."""
    h, w = rgb.shape[:2]
    uv = uv_array(me)
    out = np.zeros(len(me.polygons), dtype=np.int32)
    for p in me.polygons:
        ls = uv[p.loop_start:p.loop_start + p.loop_total]
        samples = np.concatenate([ls.mean(axis=0, keepdims=True), (ls + ls.mean(axis=0)) / 2])
        xs = np.clip((samples[:, 0] * w).astype(int), 0, w - 1)
        ys = np.clip((samples[:, 1] * h).astype(int), 0, h - 1)
        hue, sat, _ = prep.rgb_to_hsv(rgb[ys, xs])
        out[p.index] = 0 if np.median(hue) < 30.5 else (1 if np.median(sat) > 0.35 else 2)
    return out


class Regions:
    """Vertex regions, the adjacency between front vertices, and which veil panel a vertex is on."""

    def __init__(self, mesh, rgb):
        me = mesh.data
        self.co = np.array([mesh.matrix_world @ v.co for v in me.vertices])
        co = self.co
        front = [p.index for p in me.polygons if p.vertices[0] < N_FRONT]
        colour = face_colours(me, rgb)

        comps = [c for c in prep.components(me) if c[0] < N_FRONT]
        main = set(comps[0])
        region = np.full(len(me.vertices), -1, dtype=np.int32)
        region[N_SHELL_END:] = CAP
        for c in comps[1:]:
            lo, hi = co[c].min(axis=0), co[c].max(axis=0)
            if hi[2] < 0.05 and lo[2] < 0.002:
                region[c] = SANDAL
            elif len(c) == 41 and lo[2] > 0.9:
                region[c] = EYE
            elif abs(lo[0] + hi[0]) > 0.5:
                region[c] = BRACELET
            else:
                region[c] = ORNAMENT
        self.small_parts = [c for c in comps[1:] if region[c[0]] == ORNAMENT]

        # Faces of one colour joined across shared edges.
        main_faces = [f for f in front if me.polygons[f].vertices[0] in main]
        by_edge = {}
        for f in main_faces:
            for k in me.polygons[f].edge_keys:
                by_edge.setdefault(k, []).append(f)
        parent = {f: f for f in main_faces}

        def find(x):
            while parent[x] != x:
                parent[x] = parent[parent[x]]
                x = parent[x]
            return x

        for fs in by_edge.values():
            for g in fs[1:]:
                if colour[g] == colour[fs[0]]:
                    a, b = find(fs[0]), find(g)
                    if a != b:
                        parent[a] = b
        islands = {}
        for f in main_faces:
            islands.setdefault(find(f), []).append(f)

        # Seeds: the big skin islands (head, two arms), the cloth that reaches the crown (veil), and
        # the three big garment islands. Each small painted fragment joins whichever it touches first.
        label = {}
        panel = {}
        big_garments = []
        for fs in islands.values():
            zs = co[[v for f in fs for v in me.polygons[f].vertices]][:, 2]
            if colour[fs[0]] == 0:
                if len(fs) > 100:
                    label.update({f: SKIN for f in fs})
            elif zs.max() > 0.96:
                label.update({f: VEIL for f in fs})
                if len(fs) > 100:
                    side = 1 if co[[v for f in fs for v in me.polygons[f].vertices]][:, 0].mean() > 0 else -1
                    panel.update({f: side for f in fs})
            elif len(fs) > 100:
                big_garments.append(fs)
        gold = next(fs for fs in big_garments if colour[fs[0]] == 1)
        creams = [fs for fs in big_garments if colour[fs[0]] == 2]
        skirt = min(creams, key=lambda fs: min(co[v, 2] for f in fs for v in me.polygons[f].vertices))
        for fs in big_garments:
            r = TOGA if fs is gold else (SKIRT if fs is skirt else BODICE)
            label.update({f: r for f in fs})

        neighbours = {f: [] for f in main_faces}
        for fs in by_edge.values():
            for a in fs:
                neighbours[a].extend(b for b in fs if b != a)
        queue = list(label)
        i = 0
        while i < len(queue):
            f = queue[i]
            i += 1
            if label[f] == SKIN:
                continue
            for g in neighbours[f]:
                if g not in label:
                    label[g] = label[f]
                    queue.append(g)
        for f in main_faces:
            label.setdefault(f, SKIN)  # a sliver with only skin around it

        # Which back panel each veil face is on, spreading from the two big panels.
        queue = list(panel)
        i = 0
        while i < len(queue):
            f = queue[i]
            i += 1
            for g in neighbours[f]:
                if label[g] == VEIL and g not in panel:
                    panel[g] = panel[f]
                    queue.append(g)

        # A vertex is skin if it touches any skin; otherwise the region with the most area around it.
        area = {}
        panel_of = {}
        for f in main_faces:
            p = me.polygons[f]
            for v in p.vertices:
                a = area.setdefault(v, np.zeros(len(REGION_NAMES)))
                a[label[f]] += p.area
                if f in panel:
                    panel_of[v] = panel[f]
        for v, a in area.items():
            region[v] = SKIN if a[SKIN] > 0 else int(np.argmax(a))
        self.region = region
        self.face_label = label
        self.panel = np.zeros(len(me.vertices), dtype=np.int32)
        for v, s in panel_of.items():
            self.panel[v] = s

        # Edges between front vertices of the main part.
        self.adj = [[] for _ in range(len(me.vertices))]
        for k in by_edge:
            a, b = k
            d = float(np.linalg.norm(co[a] - co[b]))
            self.adj[a].append((b, d))
            self.adj[b].append((a, d))

    def surface_distance_from_skin(self):
        """Per vertex, distance along the surface to skin, walking only within its own garment, so
        a veil panel hanging next to an arm is not 'near' it."""
        out = np.full(len(self.co), np.inf)
        heap = []
        veil = self.region == VEIL
        for v in range(len(self.co)):
            if self.region[v] == SKIN:
                out[v] = 0.0
                heapq.heappush(heap, (0.0, v))
        while heap:
            d, v = heapq.heappop(heap)
            if d > out[v]:
                continue
            for u, e in self.adj[v]:
                if self.region[u] not in CLOTH:
                    continue
                if self.region[v] != SKIN and veil[u] != veil[v]:
                    continue
                if d + e < out[u]:
                    out[u] = d + e
                    heapq.heappush(heap, (d + e, u))
        return out


def load_regions(mesh, jpg):
    rgb = prep.load_rgba(jpg)[..., :3]
    return Regions(mesh, rgb)


def flat_materials(me, colours):
    me.materials.clear()
    for c in colours:
        m = bpy.data.materials.new("flat")
        m.use_nodes = True
        n = next(x for x in m.node_tree.nodes if x.type == "BSDF_PRINCIPLED")
        n.inputs["Base Color"].default_value = (*c, 1)
        n.inputs["Roughness"].default_value = 0.7
        m.use_backface_culling = True
        me.materials.append(m)


ROUND_VIEWS = (("front", (0, -1, .05)), ("q34", (.7, -.7, .05)), ("sideL", (1, 0, .05)), ("sideR", (-1, 0, .05)),
               ("back", (0, 1, .05)), ("backL", (.7, .7, .05)), ("backR", (-.7, .7, .05)), ("below", (.1, -.5, -.85)))


def round_sheet(outdir, prefix):
    cam = prep.setup_render(600, 800, 8)
    paths = []
    for name, d in ROUND_VIEWS:
        prep.frame_camera(cam, (0, 0, .5), d, 3.2)
        p = os.path.join(outdir, f"{prefix}_{name}.png")
        prep.render_to(p)
        paths.append(p)
    prep.montage(paths, 4, os.path.join(outdir, f"{prefix}.png"), 450)


def mode_regions(args):
    fbx, jpg, outdir = args
    os.makedirs(outdir, exist_ok=True)
    prep.clear_scene()
    _, meshes, _ = prep.import_fbx(fbx)
    mesh = meshes[0]
    me = mesh.data
    reg = load_regions(mesh, jpg)
    tw = twin_map(me)
    full = reg.region.copy()
    for t, f in tw.items():
        full[t] = reg.region[f]
    report = {REGION_NAMES[i]: int((full[:N_FRONT] == i).sum()) for i in range(len(REGION_NAMES))}
    report["unclassified"] = int((full < 0).sum())
    report["z_range"] = {REGION_NAMES[i]: [round(float(reg.co[full == i, 2].min()), 3),
                                           round(float(reg.co[full == i, 2].max()), 3)]
                         for i in range(len(REGION_NAMES)) if (full == i).any()}
    # Where the veil touches the rest of the cloth.
    cross = [v for v in range(N_FRONT) if reg.region[v] == VEIL and
             any(reg.region[u] in (SKIRT, TOGA, BODICE) for u, _ in reg.adj[v])]
    report["veil_body_border"] = {"verts": len(cross),
                                  "z": [round(float(reg.co[cross, 2].min()), 3), round(float(reg.co[cross, 2].max()), 3)]
                                  if cross else None}
    print("REGIONS", json.dumps(report))
    with open(os.path.join(outdir, "regions.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=1)
    flat_materials(me, REGION_COLOURS)
    for p in me.polygons:
        votes = np.bincount(full[list(p.vertices)], minlength=len(REGION_NAMES))
        p.material_index = int(np.argmax(votes))
    round_sheet(outdir, "regions")


def mode_dominance(args):
    fbx, outdir = args
    os.makedirs(outdir, exist_ok=True)
    prep.clear_scene()
    _, meshes, _ = prep.import_fbx(fbx)
    mesh = meshes[0]
    me = mesh.data
    w, names = prep.weight_matrix(mesh)
    used = [i for i in range(len(names)) if "Hand" not in names[i]]
    golden = 0.61803398875
    colours = []
    for k in range(len(used)):
        h = (k * golden) % 1.0
        colours.append(tuple(prep.hsv_to_rgb(np.array([h * 360.0]), np.array([0.85]), np.array([0.95]))[0]))
    colours.append((.5, .5, .5))
    flat_materials(me, colours)
    index = {g: k for k, g in enumerate(used)}
    for p in me.polygons:
        g = int(np.argmax(w[list(p.vertices)].sum(axis=0)))
        p.material_index = index.get(g, len(used))
    print("DOMINANT", {names[g].split(":")[-1]: int((w.argmax(axis=1) == g).sum()) for g in used})
    round_sheet(outdir, "dominance")


# ---------------------------------------------------------------------------------------------
# Chains
# ---------------------------------------------------------------------------------------------

HIP_AXIS = (0.002, -0.0215)


def around_hips(co):
    x = co[:, 0] - HIP_AXIS[0]
    y = co[:, 1] - HIP_AXIS[1]
    return np.degrees(np.arctan2(x, -y)), np.hypot(x, y)


def angle_gap(a, b):
    return np.abs((a - b + 180.0) % 360.0 - 180.0)


def surface_radius(co, mask, angle, z):
    """Upper-quartile radius of the cloth around one direction and height: on its surface,
    ignoring the deep folds and the odd flare."""
    ang, rad = around_hips(co)
    near = mask & (angle_gap(ang, angle) < 25.0) & (np.abs(co[:, 2] - z) < 0.05)
    return float(np.percentile(rad[near], 75)) if near.sum() > 3 else None


def design_chains(reg):
    co = reg.co
    body = np.isin(reg.region, (SKIRT, TOGA, BODICE))
    chains = []
    for tag, angle in SKIRT_ANGLES:
        rad = [surface_radius(co, body, angle, z) for z in (SKIRT_ROOT_Z, SKIRT_JOINT_Z, SKIRT_TIP_Z)]
        rad = [r if r is not None else 0.1 for r in rad]
        a = math.radians(angle)
        direction = np.array((math.sin(a), -math.cos(a)))
        joints = []
        for r, z in zip(rad, (SKIRT_ROOT_Z, SKIRT_JOINT_Z)):
            joints.append((HIP_AXIS[0] + direction[0] * r, HIP_AXIS[1] + direction[1] * r, z))
        chains.append({"tag": tag, "angle": angle, "parent": bone("Hips"),
                       "bones": [f"{CHAIN_PREFIX}Skirt_{tag}_1", f"{CHAIN_PREFIX}Skirt_{tag}_2"],
                       "joints": joints})
    veil = reg.region == VEIL
    for tag, side in (("P", 1), ("M", -1)):
        x = VEIL_X * side
        joints = []
        for z in (VEIL_ROOT_Z, VEIL_JOINT_Z):
            near = veil & (reg.panel == side) & (np.abs(co[:, 2] - z) < 0.04) & (np.abs(co[:, 0] - x) < 0.05)
            joints.append((x, float(np.percentile(co[near, 1], 75)), z))
        chains.append({"tag": "Veil" + tag, "side": side, "parent": bone(VEIL_PARENT),
                       "bones": [f"{CHAIN_PREFIX}Veil_{tag}_1", f"{CHAIN_PREFIX}Veil_{tag}_2"],
                       "joints": joints})
    return chains


def add_chain_bones(arm, chains):
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    inv = arm.matrix_world.inverted()
    eb = arm.data.edit_bones
    for ch in chains:
        parent = eb[ch["parent"]]
        heads = [inv @ Vector(j) for j in ch["joints"]]
        tails = heads[1:] + [heads[-1] + (heads[-1] - heads[-2])]
        for name, head, tail in zip(ch["bones"], heads, tails):
            b = eb.new(name)
            b.head, b.tail = head, tail
            b.roll = 0.0
            b.parent = parent
            b.use_connect = False
            b.use_deform = True
            parent = b
    bpy.ops.object.mode_set(mode="OBJECT")
    arm.select_set(False)


# ---------------------------------------------------------------------------------------------
# Weights
# ---------------------------------------------------------------------------------------------

def height_field(w, index, verts, z, knots):
    """Partition of unity over the knots' bones, smooth between consecutive knots."""
    for i, (zk, name) in enumerate(knots):
        if i == 0:
            share = 1.0 - smooth((z - zk) / (knots[1][0] - zk))
        elif i == len(knots) - 1:
            share = smooth((z - knots[i - 1][0]) / (zk - knots[i - 1][0]))
        else:
            rise = smooth((z - knots[i - 1][0]) / (zk - knots[i - 1][0]))
            fall = smooth((z - zk) / (knots[i + 1][0] - zk))
            share = rise - fall
        w[verts, index[bone(name)]] += share


def body_field(w, index, co, verts, chains):
    z = co[verts, 2]
    height_field(w, index, verts, z, BODY_SPINE)

    # Below the hip, the Hips share hands over to the two chains either side of the vertex.
    ang, rad = around_hips(co[verts])
    skirt = [c for c in chains if "angle" in c]
    angles = np.array([c["angle"] for c in skirt])
    gaps = angle_gap(ang[:, None], angles[None, :])
    first = np.argmin(gaps, axis=1)
    masked = gaps.copy()
    masked[np.arange(len(verts)), first] = np.inf
    second = np.argmin(masked, axis=1)
    g1 = gaps[np.arange(len(verts)), first]
    g2 = gaps[np.arange(len(verts)), second]
    t = g1 / np.maximum(g1 + g2, 1e-6)        # 0 on the nearest chain, 0.5 midway to the next
    share2 = smooth(t)                         # smooth(0.5) is 0.5: continuous across the midline
    share1 = 1.0 - share2

    to_chain = ramp(z, *SKIRT_HIPS_BLEND) * smooth((rad - DISC_RADIUS[0]) / (DISC_RADIUS[1] - DISC_RADIUS[0]))
    lower = ramp(z, *SKIRT_JOINT_BLEND)
    hips = index[bone("Hips")]
    moved = w[verts, hips] * to_chain
    w[verts, hips] -= moved
    for k, c in enumerate(skirt):
        upper_bone, lower_bone = index[c["bones"][0]], index[c["bones"][1]]
        here = (first == k) * share1 + (second == k) * share2
        w[verts, upper_bone] += moved * here * (1.0 - lower)
        w[verts, lower_bone] += moved * here * lower


def veil_field(w, index, co, verts, panel, chains):
    z = co[verts, 2]
    height_field(w, index, verts, z, VEIL_SPINE)
    spine2 = index[bone("Spine2")]
    moved = w[verts, spine2] * ramp(z, *VEIL_ROOT_BLEND)
    w[verts, spine2] -= moved
    lower = ramp(z, *VEIL_JOINT_BLEND)
    for c in chains:
        if "side" not in c:
            continue
        here = (panel[verts] == c["side"]).astype(np.float64)
        w[verts, index[c["bones"][0]]] += moved * here * (1.0 - lower)
        w[verts, index[c["bones"][1]]] += moved * here * lower
    # A fragment on neither panel (the centre embroidery above the slit) has no chain to go to.
    unassigned = panel[verts] == 0
    w[verts[unassigned], spine2] += moved[unassigned]


def cap_influences(w):
    """At most MAX_INFLUENCES per vertex, nothing below MIN_WEIGHT, each row normalized."""
    w = np.where(w < MIN_WEIGHT, 0.0, w)
    order = np.argsort(-w, axis=1)
    keep = np.zeros_like(w, dtype=bool)
    rows = np.arange(w.shape[0])[:, None]
    keep[rows, order[:, :MAX_INFLUENCES]] = True
    w = np.where(keep, w, 0.0)
    return w / np.maximum(w.sum(axis=1, keepdims=True), 1e-9)


def build_weights(mesh, reg, names, chains):
    index = {n: i for i, n in enumerate(names)}
    me = mesh.data
    co = reg.co
    orig, orig_names = prep.weight_matrix(mesh)
    w = np.zeros((len(me.vertices), len(names)), dtype=np.float64)
    for j, n in enumerate(orig_names):
        w[:, index[n]] = orig[:, j]
    source = w.copy()

    leggy = [i for i, n in enumerate(names) if any(k in n for k in ("UpLeg", "Leg", "Foot", "Toe"))]
    armish = [i for i, n in enumerate(names) if any(k in n for k in ("Arm", "Hand"))]

    field = np.zeros_like(w)
    body = np.nonzero(np.isin(reg.region[:N_FRONT], (SKIRT, TOGA, BODICE)))[0]
    veil = np.nonzero(reg.region[:N_FRONT] == VEIL)[0]
    body_field(field, index, co, body, chains)
    veil_field(field, index, co, veil, reg.panel, chains)

    # Cloth next to skin keeps what it had, minus the bones cloth should never ride.
    dist = reg.surface_distance_from_skin()
    cloth = np.concatenate([body, veil])
    tau = smooth((dist[cloth] - BORDER_FADE[0]) / (BORDER_FADE[1] - BORDER_FADE[0]))
    kept = source[cloth].copy()
    kept[:, leggy] = 0.0
    for side in ("Left", "Right"):
        # An upper arm's share goes to its collarbone: straps still lift a little, but do not fold.
        upper_arm, collar = index[bone(side + "Arm")], index[bone(side + "Shoulder")]
        kept[:, collar] += kept[:, upper_arm]
        kept[:, upper_arm] = 0.0
    veil_rows = np.isin(cloth, veil)
    kept[np.ix_(veil_rows, armish)] = 0.0
    # What kept lost goes back to the field, so a border vertex is still fully weighted.
    kept_sum = kept.sum(axis=1, keepdims=True)
    blended = kept + field[cloth] * (1.0 - kept_sum)
    w[cloth] = blended * (1.0 - tau)[:, None] + field[cloth] * tau[:, None]

    # Veil and body meet in a few places: soften the join between their two fields.
    seam = [v for v in cloth if any((reg.region[u] == VEIL) != (reg.region[v] == VEIL)
                                    for u, _ in reg.adj[v] if reg.region[u] in CLOTH)]
    rings = set(seam)
    for _ in range(2):
        rings |= {u for v in rings for u, _ in reg.adj[v] if reg.region[u] in CLOTH}
    rings = sorted(rings)
    for _ in range(SEAM_SMOOTH_PASSES):
        new = {}
        for v in rings:
            nb = [u for u, _ in reg.adj[v] if reg.region[u] != SKIN and reg.region[u] in CLOTH]
            if nb:
                new[v] = 0.5 * w[v] + 0.5 * w[nb].mean(axis=0)
        for v, row in new.items():
            w[v] = row

    w[cloth] = cap_influences(w[cloth])

    # Ornaments ride the cloth they sit on, as one rigid piece.
    from mathutils.kdtree import KDTree
    tree = KDTree(len(cloth))
    for i, v in enumerate(cloth):
        tree.insert(co[v], int(v))
    tree.balance()
    veil_tree = KDTree(len(veil))
    for v in veil:
        veil_tree.insert(co[v], int(v))
    veil_tree.balance()
    ornaments = {}
    for part in reg.small_parts:
        centre = co[part].mean(axis=0)
        _, nearest, _ = (veil_tree if centre[1] > BACK_ORNAMENT_Y else tree).find(centre)
        # One bone only: a blend of two would pinch a hard brooch when they turn apart.
        w[part] = 0.0
        w[part, int(np.argmax(w[nearest]))] = 1.0
        ornaments[f"{co[part].mean(axis=0).round(3).tolist()}"] = {
            "verts": len(part), "rides": REGION_NAMES[reg.region[nearest]]}

    if SANDALS_RIDE_HEM:
        sandals = np.nonzero(reg.region[:N_FRONT] == SANDAL)[0]
        w[sandals] = 0.0
        for side, name in SANDAL_BONES.items():
            w[sandals[np.sign(co[sandals, 0]) == side], index[name]] = 1.0

    # Twins exactly as their front vertex.
    for t, f in twin_map(me).items():
        w[t] = w[f]
    return w.astype(np.float32), {"veil_body_seam_verts": len(seam), "ornaments": ornaments}


def unused_square(mesh, jpg):
    """The largest square of the atlas no face uses, as pixel bounds (x0, y0, x1, y1), row 0 at the bottom."""
    rgb = prep.load_rgba(jpg)
    h, w = rgb.shape[:2]
    face, _ = prep.bake_position_map(mesh, w, h)
    g = 8
    empty = (face < 0)[:h // g * g, :w // g * g].reshape(h // g, g, w // g, g).all(axis=(1, 3))
    best = np.zeros(empty.shape, dtype=np.int32)
    for y in range(empty.shape[0]):
        for x in np.nonzero(empty[y])[0]:
            best[y, x] = 1 + (min(best[y - 1, x], best[y, x - 1], best[y - 1, x - 1]) if x and y else 0)
    y, x = np.unravel_index(np.argmax(best), best.shape)
    n = int(best[y, x])
    return (x - n + 1) * g, (y - n + 1) * g, (x + 1) * g, (y + 1) * g, w, h


def paint_patch(jpg, box):
    """Paint the patch into the JPEG, unless it is already there: re-encoding touches every texel a
    little, so it happens once."""
    x0, y0, x1, y1, w, h = box
    rgba = prep.load_rgba(jpg)
    m = PATCH_MARGIN_PX
    if rgba[y0 + m:y1 - m, x0 + m:x1 - m, :3].max() < 0.05:
        return False
    rgba[y0 + m:y1 - m, x0 + m:x1 - m, :3] = PATCH_COLOUR
    img = bpy.data.images.new("patched", w, h, alpha=False)
    img.pixels.foreach_set(rgba.reshape(-1))
    img.filepath_raw = jpg
    img.file_format = "JPEG"
    bpy.context.scene.render.image_settings.quality = JPEG_QUALITY
    img.save(filepath=jpg, quality=JPEG_QUALITY)
    bpy.data.images.remove(img)
    return True


def design_cap(reg, me):
    """A ring just inside the cloth at CAP_Z, found by casting out from the tube's middle, and a fan."""
    from mathutils.bvhtree import BVHTree
    full = reg.region.copy()
    for t, f in twin_map(me).items():
        full[t] = reg.region[f]
    verts = [Vector(c) for c in reg.co]
    polys = [tuple(p.vertices) for p in me.polygons if full[p.vertices[0]] in CLOTH]
    bvh = BVHTree.FromPolygons(verts, polys, all_triangles=False)
    centre = Vector((HIP_AXIS[0], 0.0, CAP_Z))
    ring = []
    for i in range(CAP_RING):
        a = 2.0 * math.pi * i / CAP_RING
        d = Vector((math.sin(a), -math.cos(a), 0.0))
        hit = bvh.ray_cast(centre, d)
        if hit[0] is None:
            raise RuntimeError(f"no cloth around the cap at {math.degrees(a):.0f} degrees")
        ring.append(centre + d * (hit[3] - CAP_INSET))
    co = np.array([tuple(centre)] + [tuple(v) for v in ring])
    first = len(me.vertices)
    # Wound to face down, the only side anyone sees.
    faces = [(first, first + 1 + (i + 1) % CAP_RING, first + 1 + i) for i in range(CAP_RING)]
    return co, faces


def patch_uv(xy, box):
    """Plane-map onto the middle of the patch: never degenerate, always inside it."""
    x0, y0, x1, y1, w, h = box
    c = np.array(((x0 + x1) / 2.0 / w, (y0 + y1) / 2.0 / h))
    half = (x1 - x0 - 2 * PATCH_MARGIN_PX) * 0.3 / w
    return c + np.asarray(xy) / max(np.abs(xy).max(), 1e-6) * half


def rebuild_with_cap(mesh, reg, w, cap_co, cap_faces, box):
    """New mesh data: everything in the same order, the cap after it, sandal backs remapped."""
    me = mesh.data
    n_loops = len(me.loops)
    uv = uv_array(me).copy()
    nor = np.empty(n_loops * 3, dtype=np.float32)
    me.corner_normals.foreach_get("vector", nor)
    nor = nor.reshape(-1, 3)
    co = np.array([v.co for v in me.vertices], dtype=np.float64)
    inv = mesh.matrix_world.inverted()
    local = np.array([tuple(inv @ Vector(c)) for c in cap_co], dtype=np.float64)

    soles = 0
    for p in me.polygons:
        if reg.region[p.vertices[0]] == SANDAL:
            pts = reg.co[list(p.vertices)]
            if pts[:, 1].mean() > SOLE_DARK_FROM_Y:
                uv[p.loop_start:p.loop_start + p.loop_total] = patch_uv(pts[:, :2], box)
                soles += 1

    polys = [list(p.vertices) for p in me.polygons] + [list(f) for f in cap_faces]
    new = bpy.data.meshes.new(me.name)
    new.from_pydata(np.concatenate([co, local]).tolist(), [], polys)
    new.shade_smooth()
    for m in me.materials:
        new.materials.append(m)
    layer = new.uv_layers.new(name=me.uv_layers.active.name)
    cap_uv = np.concatenate([patch_uv(cap_co[list(f)][:, :2] - cap_co[0, :2], box) for f in
                             [[v - len(me.vertices) for v in face] for face in cap_faces]])
    layer.data.foreach_set("uv", np.concatenate([uv, cap_uv]).reshape(-1))
    new.update()
    down = np.tile((0.0, 0.0, -1.0), (len(cap_faces) * 3, 1))
    new.normals_split_custom_set(np.concatenate([nor, down]).tolist())
    names = [g.name for g in mesh.vertex_groups]
    mesh.data = new
    bpy.data.meshes.remove(me)
    mesh.vertex_groups.clear()
    for n in names:
        mesh.vertex_groups.new(name=n)
    return soles, names


def mode_build(args):
    fbx, jpg, out_fbx, report_path = args
    prep.clear_scene()
    arm, meshes, _ = prep.import_fbx(fbx)
    mesh = meshes[0]
    if any(b.name.startswith(CHAIN_PREFIX) for b in arm.data.bones):
        raise RuntimeError("input already has Cloth_ bones: build from prepare's output")
    if len(mesh.data.vertices) <= N_FRONT:
        raise RuntimeError("input has no back shell: this pass expects prepare's output")
    bpy.context.scene.render.image_settings.file_format = "PNG"
    bpy.context.scene.view_settings.view_transform = "Standard"
    reg = load_regions(mesh, jpg)
    chains = design_chains(reg)
    add_chain_bones(arm, chains)

    names = [g.name for g in mesh.vertex_groups]
    for ch in chains:
        for n in ch["bones"]:
            mesh.vertex_groups.new(name=n)
            names.append(n)
    w, rep = build_weights(mesh, reg, names, chains)

    # The cap: dark patch, ring and fan, skinned by the same field as the cloth around it.
    box = unused_square(mesh, jpg)
    rep["patch"] = {"box": [int(x) for x in box], "painted": paint_patch(jpg, box)}
    cap_co, cap_faces = design_cap(reg, mesh.data)
    index = {n: i for i, n in enumerate(names)}
    field = np.zeros((len(cap_co), len(names)), dtype=np.float64)
    body_field(field, index, cap_co, np.arange(len(cap_co)), chains)
    cap_w = cap_influences(field).astype(np.float32)
    soles, names = rebuild_with_cap(mesh, reg, w, cap_co, cap_faces, box)
    w = np.concatenate([w, cap_w])
    rep["cap"] = {"verts": len(cap_co), "tris": len(cap_faces), "sandal_faces_darkened": soles}
    prep.write_weights(mesh, w, names)

    prep.set_texture(mesh, jpg)
    np.savez_compressed(report_path + ".weights.npz", w=w, names=np.array(names))
    rep["chains"] = [{"bones": c["bones"], "parent": c["parent"], "joints": [[round(x, 4) for x in j] for j in c["joints"]]}
                     for c in chains]
    rep["regions"] = {REGION_NAMES[i]: int((reg.region[:N_FRONT] == i).sum()) for i in range(len(REGION_NAMES))}
    rep["influences_max"] = int((w > 1e-4).sum(axis=1).max())

    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.export_scene.fbx(filepath=out_fbx, object_types={"ARMATURE", "MESH"},
                             apply_scale_options="FBX_SCALE_UNITS", mesh_smooth_type="FACE",
                             path_mode="COPY", embed_textures=True, add_leaf_bones=False,
                             bake_anim=False, use_custom_props=False)
    with open(report_path, "w", encoding="utf-8") as f:
        json.dump(rep, f, indent=1)
    print("WROTE", out_fbx)
    print("REPORT", json.dumps({k: v for k, v in rep.items() if k != "chains"}))


def fbx_bones(path):
    """Every limb's parent-relative transform as written in the file, which is what Unity reads."""
    from io_scene_fbx import parse_fbx
    root, _ = parse_fbx.parse(path)
    objects = next(c for c in root.elems if c.id == b"Objects")
    out = {}
    for o in objects.elems:
        if o.id != b"Model" or (len(o.props) > 2 and o.props[2] != b"LimbNode"):
            continue
        name = o.props[1].split(b"\x00")[0].decode(errors="replace")
        row = {}
        pr = next((c for c in o.elems if c.id == b"Properties70"), None)
        if pr:
            for p in pr.elems:
                k = p.props[0].decode()
                if k in ("Lcl Translation", "Lcl Rotation", "Lcl Scaling", "PreRotation", "PostRotation"):
                    row[k] = np.array([float(x) for x in p.props[-3:]])
        out[name] = row
    return out


def fbx_rest_diff(src, out):
    a, b = fbx_bones(src), fbx_bones(out)
    worst = {}
    for name, row in a.items():
        for k, v in row.items():
            default = np.ones(3) if k == "Lcl Scaling" else np.zeros(3)  # a default value is not written
            d = float(np.abs(v - b[name].get(k, default)).max())
            worst[k] = max(worst.get(k, 0.0), d)
    return {"fbx_limbs": [len(a), len(b)], "fbx_lcl_max_diff": worst}


def mode_check(args):
    """The delivery checks, on a re-import of the export against the file it was built from."""
    src, out, jpg, weights_npz, report_path = args
    prep.clear_scene()
    a_arm, a_m, _ = prep.import_fbx(src)
    a_mesh = a_m[0]
    a = {b.name: (b.parent.name if b.parent else None, np.array(a_arm.matrix_world @ b.matrix_local))
         for b in a_arm.data.bones}
    a_w, a_names = prep.weight_matrix(a_mesh)
    a_co = np.array([a_mesh.matrix_world @ v.co for v in a_mesh.data.vertices])
    a_uv = uv_array(a_mesh.data).copy()
    a_nor = np.empty(len(a_mesh.data.loops) * 3, dtype=np.float32)
    a_mesh.data.corner_normals.foreach_get("vector", a_nor)
    a_polys = [tuple(p.vertices) for p in a_mesh.data.polygons]

    prep.clear_scene()
    b_arm, b_m, objs = prep.import_fbx(out)
    mesh = b_m[0]
    me = mesh.data
    b = {x.name: (x.parent.name if x.parent else None, np.array(b_arm.matrix_world @ x.matrix_local))
         for x in b_arm.data.bones}
    w, names = prep.weight_matrix(mesh)
    co = np.array([mesh.matrix_world @ v.co for v in me.vertices])
    uv = uv_array(me)
    changed_uv = {}
    for p in me.polygons[:len(a_polys)]:
        ls = range(p.loop_start, p.loop_start + p.loop_total)
        if np.abs(uv[list(ls)] - a_uv[list(ls)]).max() > 1e-6:
            changed_uv[p.index] = p.vertices[0]
    nor = np.empty(len(me.loops) * 3, dtype=np.float32)
    me.corner_normals.foreach_get("vector", nor)
    extra = sorted(n for n in b if n not in a)
    rep = {
        "objects": [(o.name, o.type) for o in objs],
        "bones_source": len(a), "bones_output": len(b),
        "mixamo_missing": [n for n in a if n not in b],
        "extra_bones": extra,
        "extra_not_cloth": [n for n in extra if not n.startswith(CHAIN_PREFIX)],
        "mixamo_parent_changed": [n for n in a if a[n][0] != b[n][0]],
        "mixamo_rest_max_diff": float(max(np.abs(a[n][1] - b[n][1]).max() for n in a)),
        "extra_parents": {n: b[n][0] for n in extra},
        "verts": [len(a_co), len(me.vertices)],
        "tris": sum(len(p.vertices) - 2 for p in me.polygons),
        "polys_same": a_polys == [tuple(p.vertices) for p in me.polygons][:len(a_polys)],
        "position_max_diff": float(np.abs(co[:len(a_co)] - a_co).max()),
        "normal_max_diff": float(np.abs(nor[:len(a_nor)] - a_nor).max()),
        "materials": [m.name for m in me.materials],
        "images": [(i.name, list(i.size)) for i in bpy.data.images],
        "armature_matrix_identity": bool(np.allclose(np.array(b_arm.matrix_world), np.eye(4), atol=1e-5)),
    }
    saved = np.load(weights_npz)
    want, want_names = saved["w"], [str(x) for x in saved["names"]]
    rep["groups_not_written"] = sorted(set(names) - set(want_names))
    # A bone that lost all its vertices has no group in the file (both thighs, for one); count it as zeros.
    rep["bones_with_no_vertices_now"] = sorted(n for n in want_names if n not in names)
    got = np.zeros_like(want)
    for j, n in enumerate(want_names):
        if n in names:
            got[:, j] = w[:, names.index(n)]
    rep["written_weight_max_diff"] = float(np.abs(got - want).max())
    rep.update(fbx_rest_diff(src, out))
    tw = twin_map(me)
    rep["twins"] = len(tw)
    rep["twin_weight_max_diff"] = float(max(np.abs(w[t] - w[f]).max() for t, f in tw.items()))
    totals = w.sum(axis=1)
    rep["unweighted"] = int((totals < 1e-6).sum())
    rep["weight_sum_range"] = [float(totals.min()), float(totals.max())]
    rep["max_influences"] = int((w > 1e-4).sum(axis=1).max())

    src_w = np.zeros_like(want)
    for j, n in enumerate(a_names):
        src_w[:len(a_co), want_names.index(n)] = a_w[:, j]
    changed = np.abs(got - src_w).max(axis=1) > 0.002
    rep["verts_with_changed_weights"] = int(changed.sum())
    reg = load_regions(mesh, jpg)
    rep["changed_by_region"] = {REGION_NAMES[i]: int((changed[:N_FRONT] & (reg.region[:N_FRONT] == i)).sum())
                                for i in range(len(REGION_NAMES))}
    rep["faces_with_changed_uv"] = {REGION_NAMES[i]: sum(1 for v in changed_uv.values() if reg.region[v] == i)
                                    for i in range(len(REGION_NAMES))}
    cap_verts = np.arange(N_SHELL_END, len(me.vertices))
    rep["cap"] = {"verts": len(cap_verts), "tris": sum(1 for p in me.polygons if p.vertices[0] >= N_SHELL_END),
                  "bones": sorted({names[i].split(":")[-1] for i in np.nonzero(w[cap_verts].sum(axis=0))[0]})}
    cloth = np.isin(reg.region[:N_FRONT], CLOTH)
    for label, keys in (("leg", ("UpLeg", "Leg", "Foot", "Toe")), ("arm", ("Arm", "Hand"))):
        ids = [i for i, n in enumerate(names) if any(k in n for k in keys)]
        rep[f"cloth_verts_on_{label}_bones"] = int((w[:N_FRONT][:, ids].sum(axis=1)[cloth] > 1e-4).sum())
        rep[f"cloth_max_{label}_weight"] = float(w[:N_FRONT][:, ids].sum(axis=1)[cloth].max())
    with open(report_path, "w", encoding="utf-8") as f:
        json.dump(rep, f, indent=1, default=str)
    print("CHECK", json.dumps(rep, default=str))


# ---------------------------------------------------------------------------------------------
# Clips: retarget, the upper-body layer, SpringCloth, stress
# ---------------------------------------------------------------------------------------------

# Upper-body clips play over the idle's hips and legs; the rest take the whole body. Paths are
# from the project root. `take` picks one action out of a multi-clip file; `upto` trims it.
ANIM = "Assets/_Project/Animation/"
CLIPS = (
    {"name": "Idle", "file": ANIM + "Newton/Idle.fbx", "layer": "full"},
    {"name": "Rings", "file": ANIM + "Chibi/SwordShield/sword and shield slash (3).fbx", "layer": "upper"},
    {"name": "Ellipse", "file": ANIM + "Quaternius/UAL1_Standard.fbx", "take": "Swim_Fwd_Loop", "layer": "upper"},
    {"name": "Parabola", "file": ANIM + "Quaternius/UAL2_Standard.fbx", "take": "OverhandThrow", "layer": "upper"},
    {"name": "Hyperbola", "file": ANIM + "Newton/Cast2.fbx", "layer": "upper"},
    {"name": "Pulse", "file": ANIM + "Chibi/SwordShield/sword and shield attack.fbx", "layer": "upper"},
    {"name": "Burning", "file": ANIM + "Quaternius/UAL1_Standard.fbx", "take": "Fixing_Kneeling", "upto": 51, "layer": "upper"},
    {"name": "PhaseTwo", "file": ANIM + "Newton/PowerUp.fbx", "layer": "full"},
    {"name": "Death", "file": ANIM + "Newton/Death.fbx", "layer": "full"},
    {"name": "Topple", "file": ANIM + "Newton/Hit.fbx", "layer": "full"},
    {"name": "Cast", "file": ANIM + "Newton/Cast.fbx", "layer": "full"},
    {"name": "Cast2", "file": ANIM + "Newton/Cast2.fbx", "layer": "full"},
    {"name": "Throw", "file": ANIM + "Newton/Throw.fbx", "layer": "full"},
)

# The Universal Animation Library's rig, by what each bone is in Mixamo's names.
UAL_NAMES = {
    "pelvis": "Hips", "spine_01": "Spine", "spine_02": "Spine1", "spine_03": "Spine2", "neck_01": "Neck", "Head": "Head",
    "clavicle_l": "LeftShoulder", "upperarm_l": "LeftArm", "lowerarm_l": "LeftForeArm", "hand_l": "LeftHand",
    "clavicle_r": "RightShoulder", "upperarm_r": "RightArm", "lowerarm_r": "RightForeArm", "hand_r": "RightHand",
    "thigh_l": "LeftUpLeg", "calf_l": "LeftLeg", "foot_l": "LeftFoot", "ball_l": "LeftToeBase",
    "thigh_r": "RightUpLeg", "calf_r": "RightLeg", "foot_r": "RightFoot", "ball_r": "RightToeBase",
    "DEF-hips": "Hips", "DEF-spine.001": "Spine", "DEF-spine.002": "Spine1", "DEF-spine.003": "Spine2",
    "DEF-neck": "Neck", "DEF-head": "Head",
    "DEF-shoulder.L": "LeftShoulder", "DEF-upper_arm.L": "LeftArm", "DEF-forearm.L": "LeftForeArm", "DEF-hand.L": "LeftHand",
    "DEF-shoulder.R": "RightShoulder", "DEF-upper_arm.R": "RightArm", "DEF-forearm.R": "RightForeArm", "DEF-hand.R": "RightHand",
    "DEF-thigh.L": "LeftUpLeg", "DEF-shin.L": "LeftLeg", "DEF-foot.L": "LeftFoot", "DEF-toe.L": "LeftToeBase",
    "DEF-thigh.R": "RightUpLeg", "DEF-shin.R": "RightLeg", "DEF-foot.R": "RightFoot", "DEF-toe.R": "RightToeBase",
}


def import_clip(project, spec):
    path = os.path.join(project, spec["file"])
    before = set(bpy.data.objects)
    before_actions = set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    clip = next(o for o in new if o.type == "ARMATURE")
    for o in new:
        if o.type != "ARMATURE":
            o.hide_render = True
            o.hide_viewport = True
    actions = [a for a in bpy.data.actions if a not in before_actions]
    if "take" in spec:
        action = next(a for a in actions if a.name.endswith(spec["take"]) or spec["take"] in a.name)
        if clip.animation_data is None:
            clip.animation_data_create()
        clip.animation_data.action = action
        if not clip.animation_data.action_slot and action.slots:
            clip.animation_data.action_slot = action.slots[0]
    else:
        action = clip.animation_data.action if clip.animation_data else actions[0]
    # Names: Mixamo as they come (with or without the prefix), the UAL rig through the table.
    for b in list(clip.data.bones):
        short = b.name.split(":")[-1]
        if short in UAL_NAMES and not b.name.startswith("mixamorig"):
            b.name = bone(UAL_NAMES[short])
        elif not b.name.startswith("mixamorig") and any(short == x for x in UAL_NAMES.values()):
            b.name = bone(short)
    f0, f1 = (int(action.frame_range[0]), int(action.frame_range[1]))
    if "upto" in spec:
        f1 = min(f1, f0 + spec["upto"])
    return clip, new, action, (f0, f1)


def sample_clip(target, clip, frames, height_ratio):
    """Per frame: the world rotation each target bone takes relative to its rest, and hips travel,
    from the same absolute-direction retarget prepare uses."""
    align = prep.build_align(target, clip)
    world = clip.matrix_world.to_3x3().normalized()
    hips = bone("Hips")
    out = {}
    inverse_rest = {n: prep.rest_rot(clip, n).inverted() for n in align}
    for fr in frames:
        bpy.context.scene.frame_set(fr)
        bpy.context.view_layer.update()
        pose = {}
        for name, al in align.items():
            cb = clip.pose.bones[name]
            rc = (world @ cb.matrix.to_3x3()).normalized().to_quaternion()
            pose[name] = (rc @ inverse_rest[name] @ al).normalized()
        cb = clip.pose.bones[hips]
        delta = (clip.matrix_world @ cb.head - clip.matrix_world @ clip.data.bones[hips].head_local) * height_ratio
        out[fr] = (pose, delta)
    return out


def layer_pose(base, over, upper_names):
    """The upper-body layer: the clip's upper body, carried by the base layer's hips."""
    pose_b, delta_b = base
    pose_o, _ = over
    fix = pose_b[bone("Hips")] @ pose_o[bone("Hips")].inverted()
    pose = dict(pose_b)
    for n in upper_names:
        if n in pose_o:
            pose[n] = (fix @ pose_o[n]).normalized()
    return pose, delta_b


def upper_body_names(arm):
    out = []

    def walk(b):
        out.append(b.name)
        for c in b.children:
            walk(c)

    walk(arm.data.bones[bone("Spine")])
    return out


class Poser:
    """Writes a pose by matrix_basis without a depsgraph update per bone, and runs SpringCloth."""

    def __init__(self, arm):
        self.arm = arm
        self.order = list(arm.data.bones)  # parents first
        self.mw_inv = arm.matrix_world.inverted().to_3x3().normalized().to_quaternion()
        self.world3 = arm.matrix_world.to_3x3()
        self.world3_inv = arm.matrix_world.to_3x3().inverted()
        self.rel = {}
        for b in self.order:
            self.rel[b.name] = (b.parent.matrix_local.inverted() @ b.matrix_local) if b.parent else b.matrix_local
        self.rest = {b.name: prep.rest_rot(arm, b.name) for b in self.order}
        self.pose_matrix = {}

    def apply(self, pose, delta):
        for b in self.order:
            name = b.name
            pb = self.arm.pose.bones[name]
            rel = self.rel[name]
            if b.parent:
                parent = self.pose_matrix[b.parent.name]
                placed = parent @ rel
            else:
                parent = None
                placed = Matrix.Translation(b.head_local + self.world3_inv @ delta) @ b.matrix_local.to_3x3().to_4x4()
            if name in pose:
                goal = (pose[name] @ self.rest[name]).normalized()
                m = Matrix.Translation(placed.to_translation()) @ (self.mw_inv @ goal).to_matrix().to_4x4()
            else:
                m = placed
            self.pose_matrix[name] = m
            pb.matrix_basis = (rel.inverted() @ m) if parent is None else (rel.inverted() @ parent.inverted() @ m)

    def set_world_matrix(self, name, m):
        b = self.arm.data.bones[name]
        parent = self.pose_matrix[b.parent.name]
        self.pose_matrix[name] = m
        self.arm.pose.bones[name].matrix_basis = self.rel[name].inverted() @ parent.inverted() @ m


class SpringSim:
    """SpringCloth.Simulate, in the armature's (model metre) space. Unity works in her 1.7 m world;
    every length here is a fraction of height already, so only gravity changes."""

    def __init__(self, poser, config, char_height=1.7):
        self.poser = poser
        self.cfg = config
        self.gravity = Vector((0, 0, config["gravity"] / char_height))
        self.chains = []
        roots = [b for b in poser.arm.data.bones
                 if b.name.startswith(CHAIN_PREFIX) and not b.parent.name.startswith(CHAIN_PREFIX)]
        for r in roots:
            names = []
            b = r
            while b is not None:
                names.append(b.name)
                b = b.children[0] if b.children else None
            self.chains.append({"names": names, "n": len(names) + 1})
        self.reset = True
        self.max_swing = 0.0
        self.max_offset = 0.0
        self.capsules = [(bone("LeftUpLeg"), bone("LeftLeg"), config["thigh_r"]),
                         (bone("LeftLeg"), bone("LeftFoot"), config["shin_r"]),
                         (bone("RightUpLeg"), bone("RightLeg"), config["thigh_r"]),
                         (bone("RightLeg"), bone("RightFoot"), config["shin_r"])]

    def step(self, dt):
        cfg = self.cfg
        pm = self.poser.pose_matrix
        frames = dt * 60.0
        grav = self.gravity * (dt * dt)
        caps = [(pm[a].to_translation(), pm[b].to_translation(), r * cfg["leg_scale"] + cfg["particle_r"])
                for a, b, r in self.capsules]
        for ch in self.chains:
            names, n = ch["names"], ch["n"]
            # Animated pose: joints at their rest local rotation, the tip continuing the last bone.
            heads = [pm[x].to_translation() for x in names]
            tip = heads[-1] + (heads[-1] - heads[-2])
            rest = heads + [tip]
            if self.reset:
                ch["pos"] = [p.copy() for p in rest]
                ch["prev"] = [p.copy() for p in rest]
                continue
            pos, prev = ch["pos"], ch["prev"]
            pos[0] = rest[0].copy()
            prev[0] = rest[0].copy()
            for j in range(1, n):
                k = (j - 1.0) / (n - 2.0) if n > 2 else 1.0
                damping = cfg["damping_root"] + (cfg["damping_tip"] - cfg["damping_root"]) * k
                stiffness = cfg["stiffness_root"] + (cfg["stiffness_tip"] - cfg["stiffness_root"]) * k
                keep = (1.0 - damping) ** frames
                pull = 1.0 - (1.0 - stiffness) ** frames
                p = pos[j].copy()
                velocity = (p - prev[j]) * keep
                prev[j] = p.copy()
                p = p + velocity + grav
                p = p + (rest[j] - p) * pull
                parent = pos[j - 1]
                rest_dir = rest[j] - rest[j - 1]
                length = rest_dir.length
                for _ in range(2):
                    for a, b, r in caps:
                        ab = b - a
                        t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-8)))
                        d = p - (a + ab * t)
                        if 1e-12 < d.length_squared < r * r:
                            p = (a + ab * t) + d * (r / d.length)
                    direction = p - parent
                    if direction.length_squared < 1e-12:
                        direction = rest_dir.copy()
                    swing = rest_dir.rotation_difference(direction)
                    angle = swing.angle
                    limit = math.radians(cfg["max_angle"])
                    if angle > limit:
                        swing = Quaternion(swing.axis, limit)
                    p = parent + (swing @ rest_dir).normalized() * length
                pos[j] = p
                self.max_swing = max(self.max_swing, math.degrees(rest_dir.angle(p - parent, 0.0)))
                self.max_offset = max(self.max_offset, (p - rest[j]).length)
            # Turn each joint so its child lands on the particle.
            cur = [p.copy() for p in rest]
            for j in range(n - 1):
                frm = cur[j + 1] - cur[j]
                to = pos[j + 1] - cur[j]
                if frm.length_squared < 1e-12 or to.length_squared < 1e-12:
                    continue
                turn = (Matrix.Translation(cur[j]) @ frm.rotation_difference(to).to_matrix().to_4x4()
                        @ Matrix.Translation(-cur[j]))
                for k in range(j + 1, n):
                    cur[k] = turn @ cur[k]
                for name in names[j:]:
                    pm[name] = turn @ pm[name]
            for name in names:
                parent_name = self.poser.arm.data.bones[name].parent.name
                self.poser.arm.pose.bones[name].matrix_basis = (
                    self.poser.rel[name].inverted() @ pm[parent_name].inverted() @ pm[name])
        self.reset = False


DEFAULT_CONFIG = {
    # What ClothSetup.LoadOrCreateConfig gives a new character, then the changes this pass asks for.
    "gravity": -6.0, "stiffness_root": 0.30, "stiffness_tip": 0.12, "damping_root": 0.30, "damping_tip": 0.18,
    "max_angle": 25.0, "particle_r": 0.015, "leg_scale": 1.1, "thigh_r": 0.05, "shin_r": 0.05,
}


def region_edges(mesh, region):
    e = prep.edge_arrays(mesh)
    keep = (e[:, 0] < N_FRONT) & (e[:, 1] < N_FRONT)
    e = e[keep]
    out = {}
    for name, ids in (("skirt", (SKIRT,)), ("toga", (TOGA,)), ("bodice", (BODICE,)), ("veil", (VEIL,)),
                      ("skin", (SKIN,)), ("orn", (ORNAMENT,))):
        out[name] = e[np.isin(region[e[:, 0]], ids) & np.isin(region[e[:, 1]], ids)]
    return out


def stress(rest, posed, edges):
    rows = {}
    total = 0.0
    for name, e in edges.items():
        if len(e) == 0:
            continue
        r = np.linalg.norm(rest[e[:, 0]] - rest[e[:, 1]], axis=1)
        p = np.linalg.norm(posed[e[:, 0]] - posed[e[:, 1]], axis=1)
        ratio = p / np.maximum(r, 1e-6)
        rows[name] = (float(ratio.max()), float(np.percentile(ratio, 99)), float(ratio.min()),
                      int((ratio > 1.5).sum()), int((ratio > 2.0).sum()))
        if name in ("skirt", "toga", "veil", "bodice"):
            total += float(np.abs(ratio - 1).sum())
    return rows, total


CLIP_VIEWS = (("q34", (.45, -.9, .1)), ("back", (-.5, .85, .1)), ("low", (.3, -.85, -.45)))


def mode_clips(args):
    fbx, jpg, project, outdir, prefix, frames_path = args[:6]
    flags = args[6:]
    use_sim = "nosim" not in flags
    only = None
    config = dict(DEFAULT_CONFIG)
    for f in flags:
        if f.startswith("only="):
            only = set(f[5:].split(","))
        elif "=" in f:
            k, v = f.split("=")
            config[k] = float(v)
    os.makedirs(outdir, exist_ok=True)
    prep.clear_scene()
    arm, meshes, _ = prep.import_fbx(fbx)
    mesh = meshes[0]
    me = mesh.data
    prep.set_texture(mesh, jpg)
    prep.set_cull(mesh)
    reg = load_regions(mesh, jpg)
    region = reg.region.copy()
    cam = prep.setup_render(600, 800, 16)
    bpy.context.view_layer.update()
    rest = prep.deformed_coords(mesh)
    edges = region_edges(mesh, region)
    poser = Poser(arm)
    has_chains = any(b.name.startswith(CHAIN_PREFIX) for b in arm.data.bones)
    wanted = {}
    if os.path.exists(frames_path):
        with open(frames_path, encoding="utf-8") as f:
            wanted = json.load(f)
    hips = bone("Hips")
    target_hips_z = (arm.matrix_world @ arm.data.bones[hips].head_local).z
    upper = upper_body_names(arm)

    idle_spec = CLIPS[0]
    idle_clip, idle_objs, _, idle_range = import_clip(project, idle_spec)
    idle_ratio = target_hips_z / max((idle_clip.matrix_world @ idle_clip.data.bones[hips].head_local).z, 1e-6)
    idle_frames = list(range(idle_range[0], idle_range[1] + 1))
    idle = sample_clip(arm, idle_clip, idle_frames, idle_ratio)

    report = {}
    worst_frames = dict(wanted)
    for spec in CLIPS:
        if only and spec["name"] not in only:
            continue
        clip, objs, action, (f0, f1) = import_clip(project, spec)
        ratio = target_hips_z / max((clip.matrix_world @ clip.data.bones[hips].head_local).z, 1e-6)
        frames = list(range(f0, f1 + 1))
        samples = sample_clip(arm, clip, frames, ratio)
        sim = SpringSim(poser, config) if (use_sim and has_chains) else None
        dt = 1.0 / 30.0
        render_set = set(wanted.get(spec["name"], []))
        totals = {}
        peaks = {}
        highest_hand = {}
        low = 1e9
        for i, fr in enumerate(frames):
            base = idle[idle_frames[i % len(idle_frames)]]
            s = samples[fr]
            if spec["layer"] == "upper":
                pose, delta = layer_pose(base, s, upper)
            else:
                pose, delta = s
            poser.apply(pose, delta)
            if sim:
                sim.step(dt)
            need_render = fr in render_set
            if i % 2 == 0 or need_render:
                bpy.context.view_layer.update()
                pts = prep.deformed_coords(mesh)
                rows, total = stress(rest, pts, edges)
                totals[fr] = total
                low = min(low, float(pts[:, 2].min()))
                highest_hand[fr] = float(max((arm.matrix_world @ arm.pose.bones[bone(h)].head).z
                                             for h in ("LeftHand", "RightHand")))
                for name, row in rows.items():
                    cur = peaks.get(name)
                    if cur is None:
                        peaks[name] = list(row) + [fr]
                    else:
                        if row[0] > cur[0]:
                            cur[0], cur[5] = row[0], fr
                        cur[1] = max(cur[1], row[1])
                        cur[2] = min(cur[2], row[2])
                        cur[3] = max(cur[3], row[3])
                        cur[4] = max(cur[4], row[4])
            if need_render:
                centre = Vector((float(pts[:, 0].mean()), float(pts[:, 1].mean()),
                                 float((pts[:, 2].min() + pts[:, 2].max()) / 2)))
                paths = []
                for vname, d in CLIP_VIEWS:
                    prep.frame_camera(cam, centre, d, 3.2)
                    p = os.path.join(outdir, f"{prefix}_{spec['name']}_f{fr:03d}_{vname}.png")
                    prep.render_to(p)
                    paths.append(p)
        report[spec["name"]] = {"frames": [f0, f1], "layer": spec["layer"],
                                "peaks[max,p99,min,n>1.5,n>2,frame]": {k: [round(x, 3) if isinstance(x, float) else x
                                                                            for x in v] for k, v in peaks.items()},
                                "lowest_z": round(low, 3)}
        if sim:
            report[spec["name"]]["sim"] = {"max_swing_deg": round(sim.max_swing, 1),
                                           "max_offset_model_m": round(sim.max_offset, 4)}
        if spec["name"] not in worst_frames and totals:
            worst = max(totals, key=totals.get)
            hand = max(highest_hand, key=highest_hand.get)
            worst_frames[spec["name"]] = sorted({worst, hand})
        print("CLIP", spec["name"], json.dumps(report[spec["name"]]))
        for o in objs:
            bpy.data.objects.remove(o)
    if not os.path.exists(frames_path):
        with open(frames_path, "w", encoding="utf-8") as f:
            json.dump(worst_frames, f, indent=1)
    with open(os.path.join(outdir, f"{prefix}_stress.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=1)
    print("CLIPS DONE")


def mode_closeup(args):
    """closeup <fbx> <jpg> <project> <out.png> <clip:frame>...: the chest and shoulders, no sim."""
    fbx, jpg, project, out = args[:4]
    prep.clear_scene()
    arm, meshes, _ = prep.import_fbx(fbx)
    mesh = meshes[0]
    prep.set_texture(mesh, jpg)
    prep.set_cull(mesh)
    cam = prep.setup_render(500, 500, 16)
    poser = Poser(arm)
    hips = bone("Hips")
    z0 = (arm.matrix_world @ arm.data.bones[hips].head_local).z
    upper = upper_body_names(arm)
    idle_clip, _, _, _ = import_clip(project, CLIPS[0])
    idle = sample_clip(arm, idle_clip, [121], z0 / (idle_clip.matrix_world @ idle_clip.data.bones[hips].head_local).z)[121]
    paths = []
    tmp = out + ".tmp"
    os.makedirs(tmp, exist_ok=True)
    for item in args[4:]:
        name, fr = item.split(":")
        fr = int(fr)
        spec = next(c for c in CLIPS if c["name"] == name)
        clip, objs, _, _ = import_clip(project, spec)
        s = sample_clip(arm, clip, [fr], z0 / (clip.matrix_world @ clip.data.bones[hips].head_local).z)[fr]
        pose, delta = layer_pose(idle, s, upper) if spec["layer"] == "upper" else s
        poser.apply(pose, delta)
        bpy.context.view_layer.update()
        c = (arm.matrix_world @ arm.pose.bones[bone("Spine2")].head)
        for vname, d in (("f", (0.1, -1, .15)), ("l", (1, -.3, .2)), ("r", (-1, -.3, .2)), ("b", (0, 1, .2))):
            prep.frame_camera(cam, c, d, 1.5)
            q = os.path.join(tmp, f"{name}_{fr}_{vname}.png")
            prep.render_to(q)
            paths.append(q)
        for o in objs:
            bpy.data.objects.remove(o)
    prep.montage(paths, 4, out, 400)
    print("WROTE", out)


def mode_hemviews(args):
    """hemviews <fbx> <jpg> <project> <out.png>: standing, hovering and Death's last frame, from below."""
    fbx, jpg, project, out = args
    prep.clear_scene()
    arm, meshes, _ = prep.import_fbx(fbx)
    mesh = meshes[0]
    prep.set_texture(mesh, jpg)
    prep.set_cull(mesh)
    cam = prep.setup_render(600, 600, 16)
    poser = Poser(arm)
    hips = bone("Hips")
    z0 = (arm.matrix_world @ arm.data.bones[hips].head_local).z
    tmp = out + ".tmp"
    os.makedirs(tmp, exist_ok=True)
    paths = []

    def shoot(tag, target, dirs, dist):
        for i, d in enumerate(dirs):
            prep.frame_camera(cam, target, d, dist)
            p = os.path.join(tmp, f"{tag}_{i}.png")
            prep.render_to(p)
            paths.append(p)

    bpy.context.view_layer.update()
    hem = (0, 0, 0.08)
    shoot("rest", hem, ((.3, -1, .05), (-.9, -.5, .05), (.3, -.85, -.6), (0, -.1, -1)), 1.1)
    idle_clip, objs, _, _ = import_clip(project, CLIPS[0])
    idle = sample_clip(arm, idle_clip, [121], z0 / (idle_clip.matrix_world @ idle_clip.data.bones[hips].head_local).z)[121]
    poser.apply(*idle)
    bpy.context.view_layer.update()
    shoot("idle", hem, ((.3, -1, .05), (-.8, .6, -.45), (.3, -.85, -.6), (0, -.1, -1)), 1.1)
    spec = next(c for c in CLIPS if c["name"] == "Death")
    clip, objs2, _, (f0, f1) = import_clip(project, spec)
    pose, delta = sample_clip(arm, clip, [f1], z0 / (clip.matrix_world @ clip.data.bones[hips].head_local).z)[f1]
    poser.apply(pose, delta)
    bpy.context.view_layer.update()
    h = arm.matrix_world @ arm.pose.bones[hips].head
    p035 = next((n for n in arm.pose.bones.keys() if n.endswith("P035_2")), None)
    if p035:
        hem_now = arm.matrix_world @ arm.pose.bones[p035].head
    else:
        hem_now = arm.matrix_world @ (arm.pose.bones[bone("LeftFoot")].head + arm.pose.bones[bone("RightFoot")].head) / 2
    axis = (Vector(hem_now) - Vector(h))
    axis.z = 0
    axis.normalize()
    side = Vector((-axis.y, axis.x, 0))
    target = Vector(hem_now) + axis * 0.1
    a = axis + Vector((0, 0, .25))
    shoot("death", target, (tuple(a), tuple(axis + side * .6 + Vector((0, 0, .3))),
                            tuple(axis - side * .6 + Vector((0, 0, .3))), tuple(side + Vector((0, 0, .4)))), 1.2)
    prep.montage(paths, 4, out, 450)
    print("WROTE", out)


def mode_compare(args):
    """compare <dirA> <dirB> <prefixA> <prefixB> <frames.json> <out.png> [clip,clip...]: rows of
    before then after, one clip-frame per row pair."""
    dir_a, dir_b, pa, pb, frames_path, out = args[:6]
    only = set(args[6].split(",")) if len(args) > 6 else None
    prep.clear_scene()
    prep.setup_render()
    with open(frames_path, encoding="utf-8") as f:
        frames = json.load(f)
    paths = []
    for name, fs in frames.items():
        if only and name not in only:
            continue
        for fr in fs:
            for d, p in ((dir_a, pa), (dir_b, pb)):
                for v, _ in CLIP_VIEWS:
                    paths.append(os.path.join(d, f"{p}_{name}_f{fr:03d}_{v}.png"))
    prep.montage(paths, 6, out, 300)
    print("WROTE", out)


def main():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    {"build": mode_build, "check": mode_check, "regions": mode_regions, "dominance": mode_dominance,
     "clips": mode_clips, "compare": mode_compare, "closeup": mode_closeup,
     "hemviews": mode_hemviews}[argv[0]](argv[1:])


if __name__ == "__main__":
    main()
