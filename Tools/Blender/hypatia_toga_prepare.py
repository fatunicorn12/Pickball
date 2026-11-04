"""
Hypatia toga: inspect, render, rig-test and prepare the Tripo "ancient toga dress" model.

Headless only, never against the user's live Blender. All paths are arguments, so nothing is
read from or run inside the (untrusted) source folder beyond the FBX and JPEG themselves.

    blender.exe -b --factory-startup --python Tools/Blender/hypatia_toga_prepare.py -- <mode> ...

The deliverable comes from `build`, then `verify` diffs it against the source:

    build <fbx> <jpg> <out.fbx> <out.jpg> <report.json> [noshell]
    verify <source.fbx> <out.fbx> <report.json.weights.npz> <verify.json>

What build changes, and nothing else (bone names, hierarchy and rest pose are the source's):
    weights    hand bones reaching past the hand (left thumb tip into the toga), the other foot's
               influence on each sandal. Dress and veil weights are left to the clothing pass.
    texture    four colour defects, painted through the UVs by 3D position (paint_texture).
    back shell reversed twins of the open cloth that shows its back, because URP Lit's default
               Cull Back otherwise leaves the veil and robe see-through. `noshell` skips it, for
               a material with Render Face = Both. Twin vertices come after the source's 5009, in
               order of first use, with the front vertex's UV and weights, SHELL_INSET behind it.
               Re-weighting the cloth must update them too: a nearest-position transfer does.

Looking and measuring, none of which writes to the project:
    inspect <fbx> <jpg> <report.json>
    turnaround <fbx> <jpg> <outdir> <prefix> [cull]    front, 3/4, side, back, face, hands, feet
    posetest <fbx> <jpg> <clip.fbx> <outdir> <prefix>  pose sheet, fist, retargeted clip, stretch
    weights <fbx> <report.json>                        how far each bone reaches
    diag <fbx> <jpg> <outdir>                          components, strays, exposure, close-ups
    shading <fbx> <report.json>                        winding, normals, weights near the eyes
    rigcompare <fbx-a> <fbx-b> <report.json>           skeleton and file settings side by side
    shots <fbx> <jpg> <shots.json> <outdir>            arbitrary close-ups
    texstats <fbx> <jpg> <out.png> x0 y0 z0 x1 y1 z1   hue statistics for a 3D box
    thumb <image> <out.png> <width> [x0 y0 x1 y1]      resize or crop an image
    sheet <dir> <out.png> <cols> <cell> <name>...      contact sheet of <dir>/<name>.png
"""

import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Euler, Matrix, Quaternion, Vector


# ---------------------------------------------------------------------------------------------
# Scene helpers
# ---------------------------------------------------------------------------------------------

def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    arm = next((o for o in new if o.type == "ARMATURE"), None)
    meshes = [o for o in new if o.type == "MESH"]
    return arm, meshes, new


def set_texture(mesh_obj, jpg):
    """Point the mesh's first material at the given JPEG, on a plain Principled BSDF."""
    img = bpy.data.images.load(jpg, check_existing=True)
    mat = mesh_obj.data.materials[0] if mesh_obj.data.materials else bpy.data.materials.new("Mat")
    if not mesh_obj.data.materials:
        mesh_obj.data.materials.append(mat)
    mat.use_nodes = True
    tree = mat.node_tree
    tree.nodes.clear()
    out = tree.nodes.new("ShaderNodeOutputMaterial")
    bsdf = tree.nodes.new("ShaderNodeBsdfPrincipled")
    tex = tree.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    tree.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    bsdf.inputs["Roughness"].default_value = 0.8
    return mat


def evaluated_bounds(objs):
    """World-space min and max of the evaluated (deformed) meshes."""
    dg = bpy.context.evaluated_depsgraph_get()
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        ev = o.evaluated_get(dg)
        me = ev.to_mesh()
        for v in me.vertices:
            w = ev.matrix_world @ v.co
            for i in range(3):
                lo[i] = min(lo[i], w[i])
                hi[i] = max(hi[i], w[i])
        ev.to_mesh_clear()
    return lo, hi


def setup_render(res_x=900, res_y=1200, samples=32):
    scene = bpy.context.scene
    if scene.world is None:
        scene.world = bpy.data.worlds.new("World")
    world = scene.world
    world.use_nodes = True
    bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs[0].default_value = (0.42, 0.44, 0.48, 1.0)
    bg.inputs[1].default_value = 0.75

    for rot, energy, name in (((55, 0, 35), 2.4, "Key"), ((60, 0, 200), 0.9, "Rim")):
        sun = bpy.data.lights.new(name, "SUN")
        sun.energy = energy
        sun.angle = math.radians(12)
        obj = bpy.data.objects.new(name, sun)
        obj.rotation_euler = Euler([math.radians(a) for a in rot])
        scene.collection.objects.link(obj)

    cam_data = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam

    r = scene.render
    r.resolution_x, r.resolution_y = res_x, res_y
    r.resolution_percentage = 100
    r.image_settings.file_format = "PNG"
    r.film_transparent = False
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    try:
        if os.environ.get("HYPATIA_ENGINE") == "CYCLES":
            raise TypeError("Cycles asked for")
        r.engine = "BLENDER_EEVEE"
        scene.eevee.taa_render_samples = samples
    except (TypeError, AttributeError):
        r.engine = "CYCLES"
        scene.cycles.device = "CPU"
        scene.cycles.samples = samples
    return cam


def frame_camera(cam, target, direction, distance, lens=85.0):
    """Put the camera at target + direction * distance, looking at target."""
    cam.data.lens = lens
    cam.data.clip_start = 0.01
    cam.data.clip_end = 100
    d = Vector(direction).normalized()
    cam.location = Vector(target) + d * distance
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()


def render_to(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def bone_world(arm, name, tail=False):
    b = arm.pose.bones[name]
    return arm.matrix_world @ (b.tail if tail else b.head)


def bone_name(arm, short):
    for n in arm.pose.bones.keys():
        if n.split(":")[-1] == short:
            return n
    raise KeyError(short)


# ---------------------------------------------------------------------------------------------
# inspect
# ---------------------------------------------------------------------------------------------

def mesh_report(arm, mesh):
    import bmesh
    me = mesh.data
    rep = {
        "object": mesh.name,
        "verts": len(me.vertices),
        "tris": sum(len(p.vertices) - 2 for p in me.polygons),
        "polys": len(me.polygons),
        "uv_layers": [u.name for u in me.uv_layers],
        "materials": [m.name if m else None for m in me.materials],
        "mesh_matrix": [list(r) for r in mesh.matrix_world],
        "arm_matrix": [list(r) for r in arm.matrix_world] if arm else None,
        "parent": mesh.parent.name if mesh.parent else None,
        "has_custom_normals": me.has_custom_normals,
        "smooth_polys": sum(1 for p in me.polygons if p.use_smooth),
    }
    co = np.array([mesh.matrix_world @ v.co for v in me.vertices])
    rep["bounds_min"] = co.min(axis=0).tolist()
    rep["bounds_max"] = co.max(axis=0).tolist()

    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    bm.edges.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    rep["non_manifold_edges"] = sum(1 for e in bm.edges if not e.is_manifold)
    rep["boundary_edges"] = sum(1 for e in bm.edges if e.is_boundary)
    rep["wire_edges"] = sum(1 for e in bm.edges if e.is_wire)
    rep["loose_verts"] = sum(1 for v in bm.verts if not v.link_edges)
    rep["degenerate_faces"] = sum(1 for f in bm.faces if f.calc_area() < 1e-9)
    # Connected components, largest first, with where they sit.
    seen = set()
    comps = []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack = [v]
        seen.add(v.index)
        ids = []
        while stack:
            cur = stack.pop()
            ids.append(cur.index)
            for e in cur.link_edges:
                o = e.other_vert(cur)
                if o.index not in seen:
                    seen.add(o.index)
                    stack.append(o)
        pts = co[ids]
        tris = sum(len(f.verts) - 2 for f in {f for i in ids for f in bm.verts[i].link_faces})
        comps.append({"verts": len(ids), "tris": tris,
                      "min": pts.min(axis=0).round(3).tolist(),
                      "max": pts.max(axis=0).round(3).tolist()})
    comps.sort(key=lambda c: -c["verts"])
    rep["components"] = comps[:40]
    rep["component_count"] = len(comps)
    # Inside-out check per largest component: signed volume about the bounds centre.
    bm.faces.ensure_lookup_table()
    centre = Vector(co.mean(axis=0))
    vol = 0.0
    for f in bm.faces:
        pts = [mesh.matrix_world @ l.vert.co - centre for l in f.loops]
        for i in range(1, len(pts) - 1):
            vol += pts[0].dot(pts[i].cross(pts[i + 1])) / 6.0
    rep["signed_volume"] = vol
    bm.free()

    # Weights.
    groups = {g.index: g.name for g in mesh.vertex_groups}
    rep["vertex_groups"] = len(groups)
    bones = list(arm.data.bones.keys()) if arm else []
    rep["bones"] = len(bones)
    rep["bones_without_group"] = [b for b in bones if b not in groups.values()]
    rep["groups_without_bone"] = [g for g in groups.values() if g not in bones]
    unweighted, not_norm = 0, 0
    max_inf = 0
    per_group_count = {n: 0 for n in groups.values()}
    per_group_max = {n: 0.0 for n in groups.values()}
    totals = []
    for v in me.vertices:
        s = 0.0
        n = 0
        for g in v.groups:
            if g.weight > 1e-4:
                s += g.weight
                n += 1
                nm = groups[g.group]
                per_group_count[nm] += 1
                per_group_max[nm] = max(per_group_max[nm], g.weight)
        totals.append(s)
        max_inf = max(max_inf, n)
        if n == 0:
            unweighted += 1
        elif abs(s - 1.0) > 0.01:
            not_norm += 1
    rep["unweighted_verts"] = unweighted
    rep["non_normalized_verts"] = not_norm
    rep["max_influences"] = max_inf
    rep["weight_sum_min_max"] = [min(totals), max(totals)]
    rep["group_vertex_counts"] = {k.split(":")[-1]: v for k, v in per_group_count.items()}
    rep["group_max_weight"] = {k.split(":")[-1]: round(v, 3) for k, v in per_group_max.items()}
    return rep


def mode_inspect(args):
    fbx, jpg, out = args
    clear_scene()
    arm, meshes, new = import_fbx(fbx)
    rep = {"objects": [(o.name, o.type) for o in new]}
    rep["mesh"] = [mesh_report(arm, m) for m in meshes]
    hier = {}
    for b in arm.data.bones:
        hier[b.name] = {"parent": b.parent.name if b.parent else None,
                        "head": [round(x, 4) for x in b.head_local],
                        "tail": [round(x, 4) for x in b.tail_local]}
    rep["bones"] = hier
    rep["arm_scale"] = list(arm.scale)
    rep["arm_rotation"] = list(arm.rotation_euler)
    rep["unit_scale"] = bpy.context.scene.unit_settings.scale_length
    img = bpy.data.images.load(jpg)
    rep["image"] = list(img.size)
    with open(out, "w", encoding="utf-8") as f:
        json.dump(rep, f, indent=1)
    print("WROTE", out)


# ---------------------------------------------------------------------------------------------
# turnaround
# ---------------------------------------------------------------------------------------------

def mode_turnaround(args):
    fbx, jpg, outdir, prefix = args[:4]
    cull = len(args) > 4 and args[4] == "cull"
    os.makedirs(outdir, exist_ok=True)
    clear_scene()
    arm, meshes, _ = import_fbx(fbx)
    for m in meshes:
        set_texture(m, jpg)
        set_cull(m, cull)  # what URP Lit's default Cull Back will show
    cam = setup_render()
    bpy.context.view_layer.update()
    lo, hi = evaluated_bounds(meshes)
    centre = (lo + hi) / 2
    h = hi.z - lo.z
    dist = h * 3.4

    # Mixamo faces +Z in the FBX; Blender's importer turns that to -Y.
    views = {"front": (0, -1, 0.05), "q34": (0.7, -0.7, 0.05), "side": (1, 0, 0.05),
             "back": (0, 1, 0.05)}
    for name, d in views.items():
        frame_camera(cam, centre, d, dist)
        render_to(os.path.join(outdir, f"{prefix}_{name}.png"))

    head = bone_world(arm, bone_name(arm, "Head"))
    htop = bone_world(arm, bone_name(arm, "Head"), tail=True)
    face_centre = (head + htop) / 2
    r = bpy.context.scene.render
    r.resolution_x, r.resolution_y = 900, 900
    frame_camera(cam, face_centre, (0, -1, 0.0), h * 0.75)
    render_to(os.path.join(outdir, f"{prefix}_face.png"))
    frame_camera(cam, face_centre, (0.75, -0.65, 0.0), h * 0.75)
    render_to(os.path.join(outdir, f"{prefix}_face34.png"))

    for side in ("Left", "Right"):
        hand = bone_world(arm, bone_name(arm, f"{side}Hand"))
        tip = bone_world(arm, bone_name(arm, f"{side}HandMiddle2"))
        c = (hand + tip) / 2
        frame_camera(cam, c, (0, -0.25, 1), h * 0.5)
        render_to(os.path.join(outdir, f"{prefix}_hand{side}_top.png"))
        frame_camera(cam, c, (0, -1, 0.1), h * 0.5)
        render_to(os.path.join(outdir, f"{prefix}_hand{side}_front.png"))

    foot_c = Vector((centre.x, centre.y, lo.z + h * 0.07))
    frame_camera(cam, foot_c, (0.35, -1, 0.3), h * 0.8)
    render_to(os.path.join(outdir, f"{prefix}_feet.png"))
    print("TURNAROUND DONE")


# ---------------------------------------------------------------------------------------------
# Image helpers (no PIL in Blender's Python; numpy and bpy.data.images only)
# ---------------------------------------------------------------------------------------------

def load_rgba(path, width=None):
    img = bpy.data.images.load(path)
    if width and img.size[0] != width:
        img.scale(width, int(img.size[1] * width / img.size[0]))
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4)  # row 0 is the bottom


def save_rgba(arr, path, quality=None):
    h, w = arr.shape[:2]
    img = bpy.data.images.new("tmp_out", w, h, alpha=False)
    img.pixels.foreach_set(arr.reshape(-1).astype(np.float32))
    img.filepath_raw = path
    fmt = "JPEG" if path.lower().endswith((".jpg", ".jpeg")) else "PNG"
    scene = bpy.context.scene
    settings = scene.render.image_settings
    settings.file_format = fmt
    settings.color_mode = "RGB"
    if quality:
        settings.quality = quality
    img.save_render(path, scene=scene)
    bpy.data.images.remove(img)


def montage(paths, cols, out, cell_w=450):
    cells = [load_rgba(p, cell_w) for p in paths]
    ch = max(c.shape[0] for c in cells)
    rows = int(math.ceil(len(cells) / cols))
    canvas = np.ones((rows * ch, cols * cell_w, 4), dtype=np.float32)
    canvas[..., :3] = 0.2
    for i, c in enumerate(cells):
        r = rows - 1 - i // cols  # row 0 is the bottom
        x = (i % cols) * cell_w
        canvas[r * ch:r * ch + c.shape[0], x:x + cell_w] = c
    save_rgba(canvas, out)


def mode_sheet(args):
    folder, out, cols, cell = args[0], args[1], int(args[2]), int(args[3])
    clear_scene()
    setup_render()
    montage([os.path.join(folder, n + ".png") for n in args[4:]], cols, out, cell)
    print("WROTE", out)


def mode_thumb(args):
    """thumb <image> <out.png> <width> [x0 y0 x1 y1 in fractions of the image, y up]"""
    src, out, width = args[0], args[1], int(args[2])
    clear_scene()
    setup_render()
    if len(args) > 3:
        x0, y0, x1, y1 = [float(a) for a in args[3:7]]
        a = load_rgba(src)
        h, w = a.shape[:2]
        a = a[int(y0 * h):int(y1 * h), int(x0 * w):int(x1 * w)]
        tmp = out + ".tmp.png"
        save_rgba(a, tmp)
        a = load_rgba(tmp, width)
        os.remove(tmp)
    else:
        a = load_rgba(src, width)
    save_rgba(a, out)
    print("WROTE", out)


# ---------------------------------------------------------------------------------------------
# weights: which bone drives what, and where the strays are
# ---------------------------------------------------------------------------------------------

def segment_distance(points, a, b):
    ab = b - a
    t = np.clip(((points - a) @ ab) / max(ab @ ab, 1e-12), 0.0, 1.0)
    return np.linalg.norm(points - (a + np.outer(t, ab)), axis=1)


def weight_matrix(mesh):
    """verts x groups array of weights, plus group names."""
    names = [g.name for g in mesh.vertex_groups]
    w = np.zeros((len(mesh.data.vertices), len(names)), dtype=np.float32)
    for v in mesh.data.vertices:
        for g in v.groups:
            w[v.index, g.group] = g.weight
    return w, names


def mode_weights(args):
    fbx, out = args
    clear_scene()
    arm, meshes, _ = import_fbx(fbx)
    mesh = meshes[0]
    co = np.array([mesh.matrix_world @ v.co for v in mesh.data.vertices])
    w, names = weight_matrix(mesh)
    rep = {}
    bones = arm.data.bones
    for gi, name in enumerate(names):
        b = bones[name]
        a_ = np.array(arm.matrix_world @ b.head_local)
        b_ = np.array(arm.matrix_world @ b.tail_local)
        ids = np.where(w[:, gi] > 0.1)[0]
        if len(ids) == 0:
            rep[name.split(":")[-1]] = {"n": 0, "max_w": float(w[:, gi].max())}
            continue
        d = segment_distance(co[ids], a_, b_)
        far = ids[np.argsort(-d)[:3]]
        rep[name.split(":")[-1]] = {
            "n": int(len(ids)),
            "max_w": round(float(w[:, gi].max()), 3),
            "d50": round(float(np.median(d)), 3),
            "d99": round(float(np.percentile(d, 99)), 3),
            "dmax": round(float(d.max()), 3),
            "zrange": [round(float(co[ids, 2].min()), 3), round(float(co[ids, 2].max()), 3)],
            "far": [[round(float(x), 3) for x in co[i]] + [round(float(w[i, gi]), 3)] for i in far],
        }
    with open(out, "w", encoding="utf-8") as f:
        json.dump(rep, f, indent=0)
    for k, v in rep.items():
        print("BONE", k, v.get("n"), "maxw", v.get("max_w"), "d50", v.get("d50"), "d99", v.get("d99"),
              "dmax", v.get("dmax"), "z", v.get("zrange"))
    print("WROTE", out)


# ---------------------------------------------------------------------------------------------
# posetest
# ---------------------------------------------------------------------------------------------

def rotate_world(arm, pb, axis, degrees):
    """Rotate a pose bone around its own head by a world-space axis, keeping children attached."""
    bpy.context.view_layer.update()
    head = pb.head.copy()
    rot = Quaternion(Vector(axis).normalized(), math.radians(degrees)).to_matrix().to_4x4()
    pb.matrix = Matrix.Translation(head) @ rot @ Matrix.Translation(-head) @ pb.matrix
    bpy.context.view_layer.update()


def reset_pose(arm):
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


FINGERS = ("Thumb", "Index", "Middle", "Ring", "Pinky")


def pose_library(arm):
    """Name -> function that poses the armature. X is her left in Blender space (arm +X)."""
    def b(short):
        return arm.pose.bones[bone_name(arm, short)]

    def arms_up():
        rotate_world(arm, b("LeftArm"), (0, 1, 0), -100)
        rotate_world(arm, b("RightArm"), (0, 1, 0), 100)

    def arms_forward():
        rotate_world(arm, b("LeftArm"), (0, 1, 0), -30)
        rotate_world(arm, b("RightArm"), (0, 1, 0), 30)
        rotate_world(arm, b("LeftArm"), (0, 0, 1), -85)
        rotate_world(arm, b("RightArm"), (0, 0, 1), 85)

    def spine_bend():
        for s in ("Spine", "Spine1", "Spine2"):
            rotate_world(arm, b(s), (1, 0, 0), 15)

    def head_turn():
        rotate_world(arm, b("Neck"), (0, 0, 1), 20)
        rotate_world(arm, b("Head"), (0, 0, 1), 45)

    def hips_rotated():
        rotate_world(arm, b("Hips"), (0, 0, 1), 35)

    def legs_apart():
        rotate_world(arm, b("LeftUpLeg"), (0, 1, 0), -28)
        rotate_world(arm, b("RightUpLeg"), (0, 1, 0), 28)

    def stride():
        rotate_world(arm, b("LeftUpLeg"), (1, 0, 0), 35)
        rotate_world(arm, b("RightUpLeg"), (1, 0, 0), -30)
        rotate_world(arm, b("LeftLeg"), (1, 0, 0), -25)

    def curl_axis(side, finger):
        """Axis such that a positive rotation about it carries the finger toward the palm."""
        rest = arm.data.bones
        head = lambda s: rest[bone_name(arm, s)].head_local
        along = (head(f"{side}HandMiddle1") - head(f"{side}Hand")).normalized()
        across = (head(f"{side}HandIndex1") - head(f"{side}HandPinky1")).normalized()
        palm = along.cross(across).normalized()
        if palm.z > 0:  # palms face down in this A-pose
            palm = -palm
        if finger == "Thumb":
            d = (head(f"{side}HandThumb2") - head(f"{side}HandThumb1")).normalized()
        else:
            d = along
        return d.cross(palm).normalized()

    def fist():
        for side in ("Left", "Right"):
            for finger in FINGERS:
                for i in (1, 2, 3):
                    pb = b(f"{side}Hand{finger}{i}")
                    # World axis that bends the bone toward the palm, from the hand's own shape.
                    deg = 28 if finger == "Thumb" else 70
                    rotate_world(arm, pb, curl_axis(side, finger), deg)

    return {"arms_up": arms_up, "arms_forward": arms_forward, "spine_bend": spine_bend,
            "head_turn": head_turn, "hips_rotated": hips_rotated, "legs_apart": legs_apart,
            "stride": stride, "fist": fist}


def edge_arrays(mesh):
    me = mesh.data
    e = np.empty(len(me.edges) * 2, dtype=np.int32)
    me.edges.foreach_get("vertices", e)
    return e.reshape(-1, 2)


def deformed_coords(mesh):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    me = ev.to_mesh()
    n = len(me.vertices)
    c = np.empty(n * 3, dtype=np.float64)
    me.vertices.foreach_get("co", c)
    ev.to_mesh_clear()
    c = c.reshape(n, 3)
    m = np.array(mesh.matrix_world)
    return c @ m[:3, :3].T + m[:3, 3]


def stretch_report(mesh, rest, posed, edges, w, names):
    r = np.linalg.norm(rest[edges[:, 0]] - rest[edges[:, 1]], axis=1)
    p = np.linalg.norm(posed[edges[:, 0]] - posed[edges[:, 1]], axis=1)
    ratio = p / np.maximum(r, 1e-6)
    worst = np.argsort(-ratio)[:6]
    res = {"over_1.5": int((ratio > 1.5).sum()), "over_2": int((ratio > 2).sum()),
           "under_0.5": int((ratio < 0.5).sum()), "lowest_z": round(float(posed[:, 2].min()), 4)}
    res["worst"] = []
    for e in worst:
        v = edges[e, 0]
        top = np.argsort(-w[v])[:3]
        res["worst"].append({"ratio": round(float(ratio[e]), 2),
                             "at": [round(float(x), 3) for x in rest[v]],
                             "bones": {names[t].split(":")[-1]: round(float(w[v, t]), 2) for t in top if w[v, t] > 0}})
    return res


def load_clip(path):
    """Import a clip FBX; returns its armature and frame range."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    clip = next(o for o in new if o.type == "ARMATURE")
    for o in new:
        if o.type != "ARMATURE":
            o.hide_render = True
            o.hide_viewport = True
    action = clip.animation_data.action if clip.animation_data else None
    rng = action.frame_range if action else (1, 1)
    return clip, new, (int(rng[0]), int(rng[1]))


def rest_rot(arm, name):
    m = arm.matrix_world.to_3x3().normalized() @ arm.data.bones[name].matrix_local.to_3x3()
    return m.to_quaternion().normalized()


def rest_dir(arm, name):
    b = arm.data.bones[name]
    return (arm.matrix_world.to_3x3() @ (b.tail_local - b.head_local)).normalized()


def retarget_frame(target, clip, frame, height_ratio, align):
    """Absolute-direction retarget: the target's bones point where the clip's point, so an A-pose
    rest takes a T-pose clip correctly. Close to what Unity Humanoid does in muscle space, but it
    is not Unity's solver, so treat it as a skinning test rather than a preview of the game."""
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    clip_world = clip.matrix_world.to_3x3().normalized()
    for b in target.data.bones:  # parents come first in this list
        name = b.name
        if name not in clip.pose.bones:
            continue
        pb = target.pose.bones[name]
        cb = clip.pose.bones[name]
        Rc = (clip_world @ cb.matrix.to_3x3()).normalized().to_quaternion()
        Cinv = rest_rot(clip, name).inverted()
        goal = (Rc @ Cinv @ align[name] @ rest_rot(target, name)).normalized()
        mw_inv = target.matrix_world.inverted().to_3x3().normalized().to_quaternion()
        rot_arm = (mw_inv @ goal).to_matrix().to_4x4()
        if b.parent is None:
            cw_head = clip.matrix_world @ cb.head
            cw_rest = clip.matrix_world @ clip.data.bones[name].head_local
            delta = (cw_head - cw_rest) * height_ratio
            head = b.head_local + target.matrix_world.inverted().to_3x3() @ delta
        else:
            bpy.context.view_layer.update()
            head = pb.head.copy()
        pb.matrix = Matrix.Translation(head) @ rot_arm
    bpy.context.view_layer.update()


def build_align(target, clip):
    """World rotation taking each target rest bone direction onto the clip's rest direction."""
    align = {}
    for b in target.data.bones:
        if b.name in clip.data.bones:
            d_t = rest_dir(target, b.name)
            d_c = rest_dir(clip, b.name)
            align[b.name] = d_t.rotation_difference(d_c)
    return align


def set_cull(mesh, on=True):
    """Match URP Lit's default: Cull Back in the colour pass and in the shadow caster pass."""
    for m in mesh.data.materials:
        m.use_backface_culling = on
        m.use_backface_culling_shadow = on


def posed_views(cam, centre, h, base, views):
    paths = []
    for vname, d in views:
        frame_camera(cam, centre, d, h * 3.4)
        p = f"{base}_{vname}.png"
        render_to(p)
        paths.append(p)
    return paths


def mode_posetest(args):
    fbx, jpg, clip_path, outdir, prefix = args
    os.makedirs(outdir, exist_ok=True)
    clear_scene()
    arm, meshes, _ = import_fbx(fbx)
    mesh = meshes[0]
    set_texture(mesh, jpg)
    set_cull(mesh)  # URP Lit culls back faces; see what the game would.
    cam = setup_render(600, 800, 16)
    bpy.context.view_layer.update()
    rest = deformed_coords(mesh)
    lo, hi = rest.min(axis=0), rest.max(axis=0)
    h = hi[2] - lo[2]
    centre = Vector(((lo[0] + hi[0]) / 2, 0, (lo[2] + hi[2]) / 2 + 0.05 * h))
    edges = edge_arrays(mesh)
    w, names = weight_matrix(mesh)

    views = [("front", (0, -1, 0.1)), ("q34", (0.7, -0.7, 0.1)), ("back", (-0.5, 0.85, 0.1))]
    report = {}
    all_paths = []
    lib = pose_library(arm)
    for pname, fn in lib.items():
        reset_pose(arm)
        fn()
        bpy.context.view_layer.update()
        report[pname] = stretch_report(mesh, rest, deformed_coords(mesh), edges, w, names)
        all_paths += posed_views(cam, centre, h, os.path.join(outdir, f"{prefix}_{pname}"), views)
    reset_pose(arm)
    montage(all_paths, 6, os.path.join(outdir, f"{prefix}_poses.png"), 300)

    # Close-up of the fist.
    fn = lib["fist"]
    fn()
    bpy.context.view_layer.update()
    r = bpy.context.scene.render
    fist_paths = []
    for side in ("Left", "Right"):
        c = bone_world(arm, bone_name(arm, f"{side}Hand"), tail=True)
        for vname, d in (("a", (0, -0.3, 1)), ("b", (0, -1, 0.2))):
            r.resolution_x, r.resolution_y = 500, 500
            frame_camera(cam, c, d, h * 0.45)
            p = os.path.join(outdir, f"{prefix}_fist_{side}_{vname}.png")
            render_to(p)
            fist_paths.append(p)
    montage(fist_paths, 4, os.path.join(outdir, f"{prefix}_fist.png"), 400)
    reset_pose(arm)

    # Real motion.
    clip, clip_objs, (f0, f1) = load_clip(clip_path)
    clip_hips = next(n for n in clip.data.bones.keys() if n.endswith("Hips"))
    target_hips = bone_name(arm, "Hips")
    ratio = (arm.data.bones[target_hips].head_local.z / clip.data.bones[clip_hips].head_local.z
             if clip.data.bones[clip_hips].head_local.z else 1.0)
    missing = [b.name for b in arm.data.bones if b.name not in clip.data.bones]
    report["clip"] = {"file": os.path.basename(clip_path), "frames": [f0, f1],
                      "clip_bones": len(clip.data.bones),
                      "target_bones_missing_from_clip": missing}
    align = build_align(arm, clip)
    ratio_world = ((arm.matrix_world @ arm.data.bones[target_hips].head_local).z /
                   max((clip.matrix_world @ clip.data.bones[clip_hips].head_local).z, 1e-6))
    clip_paths = []
    r.resolution_x, r.resolution_y = 600, 800
    N = 6
    worst_low = 1e9
    for i in range(N):
        fr = int(f0 + (f1 - f0) * i / (N - 1))
        reset_pose(arm)
        retarget_frame(arm, clip, fr, ratio_world, align)
        pts = deformed_coords(mesh)
        worst_low = min(worst_low, float(pts[:, 2].min()))
        report[f"clip_f{fr}"] = stretch_report(mesh, rest, pts, edges, w, names)
        frame_camera(cam, centre, (0.45, -0.9, 0.1), h * 3.4)
        p = os.path.join(outdir, f"{prefix}_clip_f{fr:03d}.png")
        render_to(p)
        clip_paths.append(p)
    report["clip"]["lowest_z"] = worst_low
    montage(clip_paths, 6, os.path.join(outdir, f"{prefix}_clip.png"), 300)
    with open(os.path.join(outdir, f"{prefix}_rig.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=1)
    print("POSETEST DONE")


# ---------------------------------------------------------------------------------------------
# diag: components, strays, non-manifold edges, exposed back faces, close-ups
# ---------------------------------------------------------------------------------------------

def components(me):
    """Vertex ids of each connected component, largest first."""
    edges = np.empty(len(me.edges) * 2, dtype=np.int32)
    me.edges.foreach_get("vertices", edges)
    edges = edges.reshape(-1, 2)
    parent = list(range(len(me.vertices)))

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    for a, b in edges:
        ra, rb = find(int(a)), find(int(b))
        if ra != rb:
            parent[ra] = rb
    groups = {}
    for v in range(len(me.vertices)):
        groups.setdefault(find(v), []).append(v)
    return sorted(groups.values(), key=len, reverse=True)


def fibonacci_dirs(n):
    out = []
    golden = math.pi * (3 - math.sqrt(5))
    for i in range(n):
        z = 1 - 2 * (i + 0.5) / n
        r = math.sqrt(max(0.0, 1 - z * z))
        a = golden * i
        out.append(Vector((r * math.cos(a), r * math.sin(a), z)))
    return out


def face_exposure(mesh, n_dirs=128):
    """For each face, how many of n_dirs orthographic views see its front and its back."""
    from mathutils.bvhtree import BVHTree
    me = mesh.data
    mw = mesh.matrix_world
    verts = [mw @ v.co for v in me.vertices]
    polys = [tuple(p.vertices) for p in me.polygons]
    bvh = BVHTree.FromPolygons(verts, polys, all_triangles=False, epsilon=0.0)
    dirs = fibonacci_dirs(n_dirs)
    front = np.zeros(len(polys), dtype=np.int32)
    back = np.zeros(len(polys), dtype=np.int32)
    eps = 1e-4
    for i, p in enumerate(me.polygons):
        c = sum((verts[k] for k in p.vertices), Vector()) / len(p.vertices)
        n = (mw.to_3x3() @ p.normal).normalized()
        for d in dirs:
            cosang = n.dot(d)
            if abs(cosang) < 0.2:
                continue
            start = c + (n if cosang > 0 else -n) * eps + d * eps
            hit = bvh.ray_cast(start, d)
            if hit[0] is None:
                if cosang > 0:
                    front[i] += 1
                else:
                    back[i] += 1
    return front, back


def mode_diag(args):
    fbx, jpg, outdir = args
    os.makedirs(outdir, exist_ok=True)
    clear_scene()
    arm, meshes, _ = import_fbx(fbx)
    mesh = meshes[0]
    me = mesh.data
    set_texture(mesh, jpg)
    co = np.array([mesh.matrix_world @ v.co for v in me.vertices])
    w, names = weight_matrix(mesh)
    short = [n.split(":")[-1] for n in names]
    rep = {}

    comps = components(me)
    rep["components"] = []
    for c in comps:
        wsum = w[c].sum(axis=0)
        top = np.argsort(-wsum)[:5]
        rep["components"].append({
            "verts": len(c),
            "min": co[c].min(axis=0).round(3).tolist(),
            "max": co[c].max(axis=0).round(3).tolist(),
            "bones": {short[t]: round(float(wsum[t] / len(c)), 3) for t in top if wsum[t] > 0},
        })

    # Hand and finger bones reaching beyond the hand.
    rep["hand_strays"] = []
    for gi, name in enumerate(names):
        s = short[gi]
        if "Hand" not in s:
            continue
        b = arm.data.bones[name]
        a_ = np.array(arm.matrix_world @ b.head_local)
        b_ = np.array(arm.matrix_world @ b.tail_local)
        ids = np.where(w[:, gi] > 1e-3)[0]
        if len(ids) == 0:
            continue
        d = segment_distance(co[ids], a_, b_)
        for i, dist in zip(ids, d):
            if dist > 0.045:
                rep["hand_strays"].append({"bone": s, "vert": int(i), "w": round(float(w[i, gi]), 3),
                                           "dist": round(float(dist), 3), "at": co[i].round(3).tolist()})
    # Any influence above 1e-3 from a bone on the opposite side, for the limbs.
    cross = {}
    for gi, s in enumerate(short):
        for side, sign in (("Left", 1), ("Right", -1)):
            if s.startswith(side) and ("Hand" in s or "Foot" in s or "Toe" in s or "Leg" in s or "Arm" in s):
                ids = np.where((w[:, gi] > 1e-3) & (co[:, 0] * sign < -0.03))[0]
                if len(ids):
                    cross[s] = {"n": int(len(ids)), "max_w": round(float(w[ids, gi].max()), 3),
                                "zrange": [round(float(co[ids, 2].min()), 3), round(float(co[ids, 2].max()), 3)]}
    rep["cross_side"] = cross

    # Edges shared by more than two faces.
    from collections import Counter
    ec = Counter()
    for p in me.polygons:
        for k in p.edge_keys:
            ec[k] += 1
    rep["edges_over_2_faces"] = [[list(k), n, co[k[0]].round(3).tolist()] for k, n in ec.items() if n > 2]
    rep["boundary_edges"] = sum(1 for n in ec.values() if n == 1)

    # Back-face exposure at the rest pose.
    front, back = face_exposure(mesh)
    tris = np.array([len(p.vertices) - 2 for p in me.polygons])
    rep["faces"] = len(front)
    for k in (1, 3, 8, 16):
        rep[f"back_exposed_ge{k}"] = {"faces": int((back >= k).sum()), "tris": int(tris[back >= k].sum())}
    rep["front_never_but_back_seen"] = int(((front == 0) & (back >= 3)).sum())
    rep["never_visible_faces"] = int(((front == 0) & (back == 0)).sum())
    rep["never_visible_tris"] = int(tris[(front == 0) & (back == 0)].sum())
    np.savez(os.path.join(outdir, "exposure.npz"), front=front, back=back)
    with open(os.path.join(outdir, "diag.json"), "w", encoding="utf-8") as f:
        json.dump(rep, f, indent=1)

    # Close-ups, culled the way URP Lit will.
    set_cull(mesh)
    cam = setup_render(800, 800, 16)
    bpy.context.view_layer.update()
    shots = [
        ("headback", (0, 0.1, 0.92), (0, 1, 0.5), 0.75),
        ("headtop", (0, 0, 0.95), (0, -0.15, 1), 0.75),
        ("ornament", (0, 0.11, 0.69), (0.1, 1, 0.05), 0.5),
        ("belt", (-0.08, -0.04, 0.64), (-0.4, -1, 0.05), 0.5),
        ("brooches", (0, -0.02, 0.82), (0, -1, 0.15), 0.6),
        ("bracelet", (0.285, 0.01, 0.62), (0.4, -0.6, 0.6), 0.3),
        ("below", (0, 0, 0.15), (0, -0.55, -0.85), 1.5),
        ("underfront", (0, 0, 0.5), (0, -1, -0.45), 3.0),
        ("armpit", (0.13, 0.02, 0.78), (0.8, -0.5, -0.1), 0.8),
        ("tuck", (0.1, -0.1, 0.5), (0.8, -0.6, 0), 1.2),
        ("backveil", (0, 0.1, 0.65), (0.3, 1, -0.2), 1.4),
        ("skirtback", (0, 0.1, 0.25), (0.4, 1, -0.1), 1.2),
    ]
    for name, target, d, dist in shots:
        frame_camera(cam, target, d, dist)
        render_to(os.path.join(outdir, f"{name}.png"))

    # Back-exposed faces in red.
    red = bpy.data.materials.new("Red")
    red.diffuse_color = (1, 0, 0, 1)
    red.use_nodes = True
    n = next(x for x in red.node_tree.nodes if x.type == "BSDF_PRINCIPLED")
    n.inputs["Base Color"].default_value = (1, 0.05, 0.05, 1)
    red.use_backface_culling = False
    me.materials.append(red)
    for i, p in enumerate(me.polygons):
        p.material_index = 1 if back[i] >= 3 else 0
    for name, d in (("exp_front", (0, -1, 0.05)), ("exp_back", (0, 1, 0.05)), ("exp_side", (1, -0.2, 0.05)),
                    ("exp_below", (0.1, -0.6, -0.8))):
        frame_camera(cam, (0, 0, 0.5), d, 3.4)
        render_to(os.path.join(outdir, f"{name}.png"))
    print("DIAG DONE")


def fbx_summary(path):
    """Axis settings and root transforms straight from the file, without Blender's import."""
    from io_scene_fbx import parse_fbx
    root, version = parse_fbx.parse(path)
    out = {"version": version, "global": {}, "models": {}, "unit": None}

    def child(el, name):
        return next((c for c in el.elems if c.id == name.encode()), None)

    gs = child(root, "GlobalSettings")
    props = child(gs, "Properties70") if gs else None
    if props:
        for p in props.elems:
            key = p.props[0].decode()
            if key in ("UpAxis", "UpAxisSign", "FrontAxis", "FrontAxisSign", "CoordAxis", "CoordAxisSign",
                       "UnitScaleFactor", "OriginalUnitScaleFactor"):
                out["global"][key] = p.props[-1]
    objects = child(root, "Objects")
    want = ("Armature", "Hips", "Head")
    counts = {}
    for o in objects.elems:
        counts[o.id.decode()] = counts.get(o.id.decode(), 0) + 1
        if o.id == b"Model":
            name = o.props[1].split(b"\x00")[0].decode(errors="replace")
            kind = o.props[2].decode(errors="replace") if len(o.props) > 2 else ""
            if kind == "Mesh" or any(w in name for w in want):
                t = {"kind": kind}
                pr = child(o, "Properties70")
                if pr:
                    for p in pr.elems:
                        k = p.props[0].decode()
                        if k in ("Lcl Translation", "Lcl Rotation", "Lcl Scaling", "PreRotation", "PostRotation"):
                            t[k] = [round(float(x), 4) for x in p.props[-3:]]
                out["models"][name] = t
    out["counts"] = counts
    return out


def skeleton_of(path):
    clear_scene()
    arm, meshes, _ = import_fbx(path)
    rows = {}
    for b in arm.data.bones:
        rows[b.name] = {"parent": b.parent.name if b.parent else None,
                        "head": np.array(arm.matrix_world @ b.head_local),
                        "tail": np.array(arm.matrix_world @ b.tail_local)}
    height = max(np.array(m.matrix_world @ v.co)[2] for m in meshes for v in m.data.vertices)
    return rows, height, [m.name for m in meshes]


def mode_rigcompare(args):
    a_path, b_path, out = args
    a, ha, ma = skeleton_of(a_path)
    b, hb, mb = skeleton_of(b_path)
    rep = {"a": os.path.basename(a_path), "b": os.path.basename(b_path), "a_height": ha, "b_height": hb,
           "a_meshes": ma, "b_meshes": mb, "a_bones": len(a), "b_bones": len(b),
           "only_a": sorted(set(a) - set(b)), "only_b": sorted(set(b) - set(a)),
           "parent_diff": [n for n in a if n in b and a[n]["parent"] != b[n]["parent"]],
           "a_fbx": fbx_summary(a_path), "b_fbx": fbx_summary(b_path)}

    def angle(rows, name):
        d = rows[name]["tail"] - rows[name]["head"]
        horizontal = math.hypot(d[0], d[1])
        return round(math.degrees(math.atan2(d[2], horizontal)), 1)

    for n in ("mixamorig:LeftArm", "mixamorig:RightArm", "mixamorig:LeftForeArm", "mixamorig:RightForeArm"):
        rep[n + " elevation deg"] = [angle(a, n), angle(b, n)]
    rep["facing_toe_y"] = [float(a["mixamorig:LeftToeBase"]["tail"][1] - a["mixamorig:LeftToeBase"]["head"][1]),
                           float(b["mixamorig:LeftToeBase"]["tail"][1] - b["mixamorig:LeftToeBase"]["head"][1])]
    with open(out, "w", encoding="utf-8") as f:
        json.dump(rep, f, indent=1, default=str)
    print("WROTE", out)


def mode_shading(args):
    """Winding consistency, normals against faces, and weights on and around the eyes."""
    fbx, out = args
    clear_scene()
    arm, meshes, _ = import_fbx(fbx)
    mesh = meshes[0]
    me = mesh.data
    rep = {}
    # Each shared edge should be walked in opposite directions by its two faces.
    seen = {}
    bad = []
    for p in me.polygons:
        vs = list(p.vertices)
        for i in range(len(vs)):
            a, b = vs[i], vs[(i + 1) % len(vs)]
            k = (min(a, b), max(a, b))
            d = 1 if a < b else -1
            if k in seen and seen[k][1] == d:
                bad.append((k, seen[k][0], p.index))
            seen.setdefault(k, (p.index, d))
    rep["inconsistent_winding_edges"] = len(bad)
    co = np.array([mesh.matrix_world @ v.co for v in me.vertices])
    rep["inconsistent_at"] = [co[k[0]].round(3).tolist() for k, _, _ in bad[:40]]
    flagged = set()
    for _, a, b in bad:
        flagged.add(a)
        flagged.add(b)
    rep["faces_at_inconsistent_edges"] = len(flagged)
    # Loop normals against face normals.
    me.update()
    n = len(me.loops)
    ln = np.empty(n * 3, dtype=np.float32)
    me.corner_normals.foreach_get("vector", ln)
    ln = ln.reshape(n, 3)
    fn = np.array([p.normal for p in me.polygons])
    loop_face = np.zeros(n, dtype=np.int32)
    for p in me.polygons:
        loop_face[p.loop_start:p.loop_start + p.loop_total] = p.index
    dots = (ln * fn[loop_face]).sum(axis=1)
    rep["loop_normal_vs_face"] = {"min": float(dots.min()), "lt0": int((dots < 0).sum()),
                                  "lt0.5": int((dots < 0.5).sum()), "mean": float(dots.mean())}
    rep["faces_with_opposed_loop_normals"] = int(len(set(loop_face[dots < 0].tolist())))
    w, names = weight_matrix(mesh)
    short = [x.split(":")[-1] for x in names]

    def summarize(mask):
        s = w[mask].mean(axis=0)
        return {short[i]: round(float(s[i]), 3) for i in np.argsort(-s)[:4] if s[i] > 1e-3}

    comps = components(me)
    eyes = [c for c in comps if len(c) == 41]
    eye_ids = [i for c in eyes for i in c]
    rep["eyes"] = summarize(eye_ids)
    rep["eye_rows"] = [w[i, short.index("Neck")].round(3).item() for i in eye_ids[:8]]
    around = (np.abs(co[:, 0]) < 0.05) & (co[:, 1] < -0.03) & (co[:, 2] > 0.90) & (co[:, 2] < 0.945)
    around[eye_ids] = False
    rep["face_around_eyes"] = summarize(around)
    rep["face_around_eyes_n"] = int(around.sum())
    mouth = (np.abs(co[:, 0]) < 0.04) & (co[:, 1] < -0.03) & (co[:, 2] > 0.87) & (co[:, 2] < 0.90)
    rep["mouth"] = summarize(mouth)
    # Skinned-cloth summary for the clothing agent: panel bones by height.
    with open(out, "w", encoding="utf-8") as f:
        json.dump(rep, f, indent=1)
    print("WROTE", out)


# ---------------------------------------------------------------------------------------------
# build: weights
# ---------------------------------------------------------------------------------------------

HAND_STRAY_DISTANCE = 0.045   # model metres: past this a hand or finger bone has no business


def write_weights(mesh, w, names):
    """Replace every vertex group's contents from a verts x groups array."""
    groups = {n: mesh.vertex_groups[n] for n in names}
    all_ids = list(range(w.shape[0]))
    for gi, name in enumerate(names):
        g = groups[name]
        g.remove(all_ids)
        ids = np.nonzero(w[:, gi] > 0.0)[0]
        for i in ids:
            g.add([int(i)], float(w[i, gi]), "REPLACE")


def clean_weights(arm, mesh):
    """Only rig defects with no reading as cloth motion: a hand bone pulling the toga, one foot
    dragging the other sandal. Dress and veil weights are left for the clothing pass."""
    me = mesh.data
    co = np.array([mesh.matrix_world @ v.co for v in me.vertices])
    w, names = weight_matrix(mesh)
    short = [n.split(":")[-1] for n in names]
    rep = {"hand_strays": {}, "sandal_cross": 0}

    for gi, name in enumerate(names):
        if "Hand" not in short[gi]:
            continue
        b = arm.data.bones[name]
        a_ = np.array(arm.matrix_world @ b.head_local)
        b_ = np.array(arm.matrix_world @ b.tail_local)
        ids = np.nonzero(w[:, gi] > 0)[0]
        if len(ids) == 0:
            continue
        d = segment_distance(co[ids], a_, b_)
        far = ids[d > HAND_STRAY_DISTANCE]
        if len(far):
            rep["hand_strays"][short[gi]] = {"verts": int(len(far)), "max_w": round(float(w[far, gi].max()), 3)}
            w[far, gi] = 0.0

    comps = components(me)
    for c in comps:
        c = np.array(c)
        lo, hi = co[c].min(axis=0), co[c].max(axis=0)
        if hi[2] < 0.05 and lo[2] < 0.002 and len(c) < 200:  # a sandal
            side, other = ("Left", "Right") if co[c, 0].mean() > 0 else ("Right", "Left")
            for gi, s in enumerate(short):
                if s.startswith(other):
                    n = int((w[c, gi] > 0).sum())
                    rep["sandal_cross"] += n
                    w[c, gi] = 0.0

    totals = w.sum(axis=1, keepdims=True)
    w = w / np.maximum(totals, 1e-9)
    write_weights(mesh, w, names)
    rep["unweighted_after"] = int((totals[:, 0] <= 1e-6).sum())
    rep["max_influences_after"] = int((w > 1e-4).sum(axis=1).max())
    return rep


# ---------------------------------------------------------------------------------------------
# build: texture repairs, painted through the UVs by 3D position
# ---------------------------------------------------------------------------------------------

def rgb_to_hsv(rgb):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = rgb.max(axis=-1)
    mn = rgb.min(axis=-1)
    d = mx - mn + 1e-9
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-9), 0)
    return h, s, mx


def hsv_to_rgb(h, s, v):
    c = v * s
    x = c * (1 - np.abs((h / 60) % 2 - 1))
    m = v - c
    zero = np.zeros_like(c)
    sector = (h // 60).astype(int) % 6
    table = np.stack([np.stack([c, x, zero], -1), np.stack([x, c, zero], -1), np.stack([zero, c, x], -1),
                      np.stack([zero, x, c], -1), np.stack([x, zero, c], -1), np.stack([c, zero, x], -1)])
    return table[sector, np.arange(len(h))] + m[..., None]


def bake_position_map(mesh, width, height):
    """Per texel: the face index (-1 = none) and the world position, by rasterising the UVs."""
    me = mesh.data
    me.calc_loop_triangles()
    uv = np.empty(len(me.loops) * 2, dtype=np.float32)
    me.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    co = np.array([mesh.matrix_world @ v.co for v in me.vertices], dtype=np.float32)
    face = np.full((height, width), -1, dtype=np.int32)
    pos = np.zeros((height, width, 3), dtype=np.float32)
    for tri in me.loop_triangles:
        p = uv[list(tri.loops)] * (width, height)
        q = co[list(tri.vertices)]
        x0, y0 = np.floor(p.min(axis=0)).astype(int)
        x1, y1 = np.ceil(p.max(axis=0)).astype(int)
        x0, y0 = max(x0 - 1, 0), max(y0 - 1, 0)
        x1, y1 = min(x1 + 1, width - 1), min(y1 + 1, height - 1)
        if x1 < x0 or y1 < y0:
            continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        d = (p[1, 1] - p[2, 1]) * (p[0, 0] - p[2, 0]) + (p[2, 0] - p[1, 0]) * (p[0, 1] - p[2, 1])
        if abs(d) < 1e-9:
            continue
        a = ((p[1, 1] - p[2, 1]) * (xs - p[2, 0]) + (p[2, 0] - p[1, 0]) * (ys - p[2, 1])) / d
        b = ((p[2, 1] - p[0, 1]) * (xs - p[2, 0]) + (p[0, 0] - p[2, 0]) * (ys - p[2, 1])) / d
        c = 1 - a - b
        # A little slack so the gutters next to a face come along too (bilinear and mips read them).
        inside = (a > -0.03) & (b > -0.03) & (c > -0.03)
        if not inside.any():
            continue
        yy = ys[inside].astype(int)
        xx = xs[inside].astype(int)
        face[yy, xx] = tri.polygon_index
        pos[yy, xx] = (a[inside, None] * q[0] + b[inside, None] * q[1] + c[inside, None] * q[2])
    return face, pos


def in_box(pos, lo, hi):
    return ((pos[..., 0] >= lo[0]) & (pos[..., 0] <= hi[0]) & (pos[..., 1] >= lo[1]) & (pos[..., 1] <= hi[1]) &
            (pos[..., 2] >= lo[2]) & (pos[..., 2] <= hi[2]))


def gaussian_crop(a, sigma):
    """Separable Gaussian over a 2D or 3D array (axes 0 and 1), with numpy only."""
    radius = int(math.ceil(sigma * 3))
    k = np.exp(-0.5 * (np.arange(-radius, radius + 1) / sigma) ** 2)
    k /= k.sum()
    out = np.apply_along_axis(lambda m: np.convolve(m, k, mode="same"), 0, a)
    return np.apply_along_axis(lambda m: np.convolve(m, k, mode="same"), 1, out)


def dilate(mask, n):
    out = mask.copy()
    for _ in range(n):
        o = out.copy()
        o[1:, :] |= out[:-1, :]
        o[:-1, :] |= out[1:, :]
        o[:, 1:] |= out[:, :-1]
        o[:, :-1] |= out[:, 1:]
        out = o
    return out


def crop_of(mask, margin):
    ys, xs = np.nonzero(mask)
    if len(ys) == 0:
        return None
    h, w = mask.shape
    return (max(ys.min() - margin, 0), min(ys.max() + margin + 1, h),
            max(xs.min() - margin, 0), min(xs.max() + margin + 1, w))


def fill_from_support(rgb, mask, support, sigma):
    """Replace mask texels with the blurred colour of support texels, patch by patch."""
    from collections import deque
    # Separate the mask into atlas patches so each is filled from its own surroundings.
    h, w = mask.shape
    labels = np.zeros((h, w), dtype=np.int32)
    ys, xs = np.nonzero(mask)
    count = 0
    filled = 0
    for y0, x0 in zip(ys.tolist(), xs.tolist()):
        if labels[y0, x0]:
            continue
        count += 1
        q = deque([(y0, x0)])
        labels[y0, x0] = count
        pts = []
        while q:
            y, x = q.popleft()
            pts.append((y, x))
            for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
                if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not labels[ny, nx]:
                    labels[ny, nx] = count
                    q.append((ny, nx))
        pts = np.array(pts)
        m = int(sigma * 4) + 8
        y_lo, y_hi = max(pts[:, 0].min() - m, 0), min(pts[:, 0].max() + m + 1, h)
        x_lo, x_hi = max(pts[:, 1].min() - m, 0), min(pts[:, 1].max() + m + 1, w)
        sup = support[y_lo:y_hi, x_lo:x_hi] & ~mask[y_lo:y_hi, x_lo:x_hi]
        sub = rgb[y_lo:y_hi, x_lo:x_hi]
        weight = gaussian_crop(sup.astype(np.float32), sigma)
        colour = gaussian_crop(sub * sup[..., None], sigma)
        ok = weight > 1e-3
        here = labels[y_lo:y_hi, x_lo:x_hi] == count
        use = here & ok
        sub[use] = colour[use] / weight[use, None]
        filled += int(use.sum())
    return count, filled


def smooth_within(rgb, mask, sigma, feather=3.0):
    """Blur only among mask texels, feathered back into the original, to erase thin painted lines."""
    box = crop_of(mask, int(sigma * 4) + 4)
    if box is None:
        return 0
    y0, y1, x0, x1 = box
    m = mask[y0:y1, x0:x1]
    sub = rgb[y0:y1, x0:x1]
    weight = gaussian_crop(m.astype(np.float32), sigma)
    colour = gaussian_crop(sub * m[..., None], sigma)
    blurred = colour / np.maximum(weight, 1e-4)[..., None]
    alpha = np.clip(gaussian_crop(m.astype(np.float32), feather) * 2 - 1, 0, 1) * m
    sub[:] = sub * (1 - alpha[..., None]) + blurred * alpha[..., None]
    return int(m.sum())


def paint_texture(mesh, jpg, out_jpg, preview=None):
    rgba = load_rgba(jpg)
    h, w = rgba.shape[:2]
    rgb = rgba[..., :3].copy()
    orig = rgb.copy()
    face, pos = bake_position_map(mesh, w, h)
    covered = face >= 0
    hue, sat, val = rgb_to_hsv(rgb)
    rep = {"texels_covered": float(covered.mean())}

    # Toga cloth is hue ~33-46. Skin and hair smears on it read hue < 30.
    # 1. The reddish streak on the toga in front of her left shoulder.
    box = in_box(pos, (0.0, -0.15, 0.50), (0.135, -0.005, 0.815)) & covered
    # Per face: only faces that are mostly toga, so the shoulder's real skin next door is safe.
    n_faces = int(face.max()) + 1
    box_ids = face[box]
    hue_box = hue[box]
    toga_hits = np.bincount(box_ids[(hue_box > 31) & (hue_box < 50) & (sat[box] > 0.35)], minlength=n_faces)
    all_hits = np.bincount(box_ids, minlength=n_faces)
    toga_face = (toga_hits > 0.55 * np.maximum(all_hits, 1))
    in_toga = box & toga_face[np.maximum(face, 0)]
    streak = in_toga & (hue < 33.5) & (val < 0.82)
    support = in_toga & (hue >= 34.5) & (hue < 40)
    count, filled = fill_from_support(rgb, dilate(streak, 3) & in_toga, support, 12.0)
    rep["shoulder_streak"] = {"patches": count, "texels": filled}

    # 2. Skin-coloured "mouth" smear inside the toga fold at her left hip.
    box = in_box(pos, (0.085, -0.135, 0.345), (0.165, -0.035, 0.47)) & covered
    smear = box & ((hue < 32.5) | ((hue < 33.5) & (sat < 0.45)))
    # Keep the embroidered border out of the source colours: it is paler and yellower.
    support = box & (hue >= 33.5) & (hue < 39) & (sat > 0.5)
    count, filled = fill_from_support(rgb, dilate(smear, 3) & box, support, 10.0)
    rep["fold_smear"] = {"patches": count, "texels": filled}

    # 3. Red-brown weave on the veil at the crown: the generator's hair bled onto the cloth. Turn
    # its hue to the embroidery's gold and keep its pattern, so it reads as more of the embroidery.
    box = in_box(pos, (-0.06, -0.015, 0.90), (0.06, 0.14, 1.01)) & covered
    brown = box & (hue < 31.5) & (sat > 0.16)
    rgb[brown] = hsv_to_rgb(np.full(int(brown.sum()), 37.0), sat[brown] * 0.95,
                            np.clip(val[brown] * 1.12, 0, 1))
    rep["crown_recolour"] = int(brown.sum())

    # 4. The bracelet's two parts: cream and brown patches that read as a wristwatch.
    me = mesh.data
    comps = components(me)
    co = np.array([mesh.matrix_world @ v.co for v in me.vertices])
    bracelet = set()
    for c in comps:
        c_co = co[c]
        if len(c) < 200 and c_co[:, 0].min() > 0.25 and c_co[:, 2].max() < 0.66:
            bracelet.update(c)
    bracelet_faces = np.zeros(n_faces, dtype=bool)
    for p in me.polygons:
        if p.vertices[0] in bracelet:
            bracelet_faces[p.index] = True
    # The source's plaque is almost flat cream; lift the contrast and lay a gold ramp over it.
    mask = bracelet_faces[np.maximum(face, 0)] & covered
    lum = rgb[mask].mean(axis=1)
    lum = np.clip((lum - lum.mean()) * 1.45 + 0.60, 0.12, 0.98)
    ramp_dark = np.array([0.36, 0.22, 0.07])
    ramp_mid = np.array([0.78, 0.59, 0.24])
    ramp_hi = np.array([1.0, 0.92, 0.66])
    t = lum[..., None]
    rgb[mask] = np.where(t < 0.6, ramp_dark + (ramp_mid - ramp_dark) * (t / 0.6),
                         ramp_mid + (ramp_hi - ramp_mid) * ((t - 0.6) / 0.4))
    rep["bracelet_texels"] = int(mask.sum())

    # 5. Thin painted lines on skin: the dotted seam along the left forearm, the throat crease.
    hue3, sat3, val3 = rgb_to_hsv(rgb)
    skin = (hue3 < 30) & (sat3 < 0.55) & (val3 > 0.45) & covered
    forearm = covered & (hue3 < 36) & (sat3 < 0.6) & in_box(pos, (0.19, -0.05, 0.60), (0.27, 0.05, 0.72))
    forearm &= ~bracelet_faces[np.maximum(face, 0)]
    rep["forearm_texels"] = smooth_within(rgb, forearm, 7.0)
    throat = skin & in_box(pos, (-0.035, -0.09, 0.785), (0.035, -0.01, 0.855))
    rep["throat_texels"] = smooth_within(rgb, throat, 7.0)

    changed = (np.abs(rgb - orig).max(axis=2) > 1e-3)
    rep["texels_changed"] = int(changed.sum())
    out = np.ones((h, w, 4), dtype=np.float32)
    out[..., :3] = np.clip(rgb, 0, 1)

    img = bpy.data.images.new("HypatiaToga_Color_tmp", w, h, alpha=False)
    img.pixels.foreach_set(out.reshape(-1))
    img.filepath_raw = out_jpg
    img.file_format = "JPEG"
    bpy.context.scene.render.image_settings.quality = 95
    img.save(filepath=out_jpg, quality=95)
    bpy.data.images.remove(img)

    # Round-trip check: untouched texels must come back as they went in.
    back = load_rgba(out_jpg)[..., :3]
    untouched = ~dilate(changed, 8)
    diff = np.abs(back - orig)[untouched]
    rep["roundtrip_mean_abs"] = float(diff.mean())
    rep["roundtrip_p999"] = float(np.percentile(diff[::97], 99.9))
    if preview:
        np.savez_compressed(preview, changed=changed)
    return rep


# ---------------------------------------------------------------------------------------------
# build: back shell, so URP Lit's default Cull Back does not leave open cloth see-through
# ---------------------------------------------------------------------------------------------

def triangle_exposure(mesh, n_dirs=160):
    """Per polygon: number of views that see its back (a Fibonacci sphere of orthographic views)."""
    from mathutils.bvhtree import BVHTree
    me = mesh.data
    me.calc_loop_triangles()
    mw = mesh.matrix_world
    verts = [mw @ v.co for v in me.vertices]
    tris = [tuple(t.vertices) for t in me.loop_triangles]
    bvh = BVHTree.FromPolygons(verts, tris, all_triangles=True)
    dirs = fibonacci_dirs(n_dirs)
    back = np.zeros(len(me.polygons), dtype=np.int32)
    front = np.zeros(len(me.polygons), dtype=np.int32)
    for t in me.loop_triangles:
        a, b, c = (verts[i] for i in t.vertices)
        n = (b - a).cross(c - a)
        if n.length < 1e-10:
            continue
        n.normalize()
        o = (a + b + c) / 3
        for d in dirs:
            cosang = n.dot(d)
            if abs(cosang) < 0.15:
                continue
            start = o + (n if cosang > 0 else -n) * 1e-5 + d * 3e-4
            if bvh.ray_cast(start, d)[0] is None:
                if cosang > 0:
                    front[t.polygon_index] += 1
                else:
                    back[t.polygon_index] += 1
    return front, back


def shell_selection(mesh, back, rgb):
    """Faces worth a back side: seen from behind, then closed up over the panel they belong to."""
    me = mesh.data
    n = len(me.polygons)
    adj = [[] for _ in range(n)]
    by_edge = {}
    for p in me.polygons:
        for k in p.edge_keys:
            by_edge.setdefault(k, []).append(p.index)
    for fs in by_edge.values():
        for f in fs:
            for g in fs:
                if f != g:
                    adj[f].append(g)
    # Skin, hair, sandals, eyes and the hard ornaments are closed surfaces: their inside only shows
    # through the hollow body, and giving them a twin adds cost and shadow noise for nothing.
    comps = components(me)
    main = set(comps[0])
    cloth = np.array([p.vertices[0] in main for p in me.polygons]) & cloth_faces(mesh, rgb)
    sel = (back >= 2) & cloth
    # Two rounds of "most of my neighbours are in": removes the checkering a per-face test gives.
    for _ in range(3):
        add = sel.copy()
        for f in range(n):
            if not sel[f] and adj[f]:
                k = sum(1 for g in adj[f] if sel[g])
                if k >= 2 or k / len(adj[f]) >= 0.5:
                    add[f] = True
        sel = add & cloth
    # Small islands are glimpses through the hollow body from extreme angles, not panels.
    seen = np.zeros(n, dtype=bool)
    for f in np.nonzero(sel)[0]:
        if seen[f]:
            continue
        stack = [int(f)]
        seen[f] = True
        island = []
        while stack:
            c = stack.pop()
            island.append(c)
            for g in adj[c]:
                if sel[g] and not seen[g]:
                    seen[g] = True
                    stack.append(g)
        if len(island) < SHELL_MIN_ISLAND:
            sel[island] = False
    return sel


def cloth_faces(mesh, rgb):
    """True for faces whose texture reads as toga, dress or veil rather than skin or hair."""
    me = mesh.data
    h, w = rgb.shape[:2]
    uv = np.empty(len(me.loops) * 2, dtype=np.float32)
    me.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    out = np.zeros(len(me.polygons), dtype=bool)
    for p in me.polygons:
        ls = uv[p.loop_start:p.loop_start + p.loop_total]
        samples = np.concatenate([ls.mean(axis=0, keepdims=True), (ls + ls.mean(axis=0)) / 2])
        xs = np.clip((samples[:, 0] * w).astype(int), 0, w - 1)
        ys = np.clip((samples[:, 1] * h).astype(int), 0, h - 1)
        hue, sat, val = rgb_to_hsv(rgb[ys, xs])
        # Toga and embroidery are yellow-gold, the veil and dress cream. Skin and hair sit nearer red.
        out[p.index] = np.median(hue) >= 30.5
    return out


SHELL_INSET = 0.0012  # model metres (about 2 mm at her 1.7 m): keeps the twin off the front surface
SHELL_MIN_ISLAND = 12


def rebuild_with_shell(arm, mesh, sel):
    """New mesh: original faces first (same order), then reversed twins of sel. Twins share the
    UV and weights of their front vertex, carry the opposite normal and sit a hair behind it."""
    me = mesh.data
    n_v, n_l = len(me.vertices), len(me.loops)
    co = np.empty(n_v * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    uv = np.empty(n_l * 2, dtype=np.float32)
    uv_layer = me.uv_layers.active
    uv_layer.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    nor = np.empty(n_l * 3, dtype=np.float32)
    me.corner_normals.foreach_get("vector", nor)
    nor = nor.reshape(-1, 3)
    w, names = weight_matrix(mesh)

    polys = [list(p.vertices) for p in me.polygons]
    loop_start = [p.loop_start for p in me.polygons]
    new_uv = [uv]
    new_nor = [nor]
    twin = {}
    new_polys = list(polys)
    for f in np.nonzero(sel)[0]:
        vs = polys[f]
        ls = list(range(loop_start[f], loop_start[f] + len(vs)))
        rev_v = list(reversed(vs))
        rev_l = list(reversed(ls))
        mapped = []
        for v in rev_v:
            if v not in twin:
                twin[v] = n_v + len(twin)
            mapped.append(twin[v])
        new_polys.append(mapped)
        new_uv.append(uv[rev_l])
        new_nor.append(-nor[rev_l])
    twins = sorted(twin, key=twin.get)
    # Inset along the area-weighted normal of the selected faces at each vertex.
    inset = np.zeros((n_v, 3), dtype=np.float32)
    for f in np.nonzero(sel)[0]:
        p = me.polygons[int(f)]
        inset[polys[f]] += np.array(p.normal, dtype=np.float32) * p.area
    length = np.linalg.norm(inset, axis=1, keepdims=True)
    inset = np.where(length > 1e-12, inset / np.maximum(length, 1e-12), 0.0)
    all_co = np.concatenate([co, co[twins] - inset[twins] * SHELL_INSET]) if twins else co
    all_uv = np.concatenate(new_uv)
    all_nor = np.concatenate(new_nor)
    all_w = np.concatenate([w, w[twins]]) if twins else w

    new = bpy.data.meshes.new(me.name)
    new.from_pydata(all_co.tolist(), [], new_polys)
    new.shade_smooth()
    for m in me.materials:
        new.materials.append(m)
    layer = new.uv_layers.new(name=uv_layer.name)
    layer.data.foreach_set("uv", all_uv.reshape(-1))
    new.update()
    new.normals_split_custom_set(all_nor.tolist())

    mesh.data = new
    bpy.data.meshes.remove(me)
    mesh.vertex_groups.clear()
    for nm in names:
        mesh.vertex_groups.new(name=nm)
    write_weights(mesh, all_w, names)
    return {"front_verts": n_v, "twin_verts": len(twins), "twin_faces": int(sel.sum())}


def mode_build(args):
    fbx, jpg, out_fbx, out_jpg, report_path = args[:5]
    flags = set(args[5:])
    clear_scene()
    arm, meshes, _ = import_fbx(fbx)
    mesh = meshes[0]
    bpy.context.scene.render.image_settings.file_format = "PNG"
    bpy.context.scene.view_settings.view_transform = "Standard"
    rep = {"source": os.path.basename(fbx)}
    rep["before"] = {"verts": len(mesh.data.vertices), "tris": sum(len(p.vertices) - 2 for p in mesh.data.polygons)}

    rep["weights"] = clean_weights(arm, mesh)
    rep["texture"] = paint_texture(mesh, jpg, out_jpg)

    if "noshell" not in flags:
        front, back = triangle_exposure(mesh)
        sel = shell_selection(mesh, back, load_rgba(out_jpg)[..., :3])
        me = mesh.data
        tris = np.array([len(p.vertices) - 2 for p in me.polygons])
        rep["shell"] = {"back_ge2_tris": int(tris[back >= 2].sum()), "selected_tris": int(tris[sel].sum())}
        rep["shell"].update(rebuild_with_shell(arm, mesh, sel))

    me = mesh.data
    mesh.name = "HypatiaToga"
    me.name = "HypatiaToga"
    mat = me.materials[0]
    mat.name = "HypatiaToga"
    set_texture(mesh, out_jpg)
    rep["after"] = {"verts": len(me.vertices), "tris": sum(len(p.vertices) - 2 for p in me.polygons)}
    np.savez_compressed(report_path + ".weights.npz", w=weight_matrix(mesh)[0],
                        names=np.array(weight_matrix(mesh)[1]))

    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.export_scene.fbx(filepath=out_fbx, object_types={"ARMATURE", "MESH"},
                             apply_scale_options="FBX_SCALE_UNITS", mesh_smooth_type="FACE",
                             path_mode="COPY", embed_textures=True, add_leaf_bones=False,
                             bake_anim=False, use_custom_props=False)
    with open(report_path, "w", encoding="utf-8") as f:
        json.dump(rep, f, indent=1)
    print("WROTE", out_fbx)
    print("REPORT", json.dumps(rep))


def mode_verify(args):
    """Re-import the export and diff it against the source and the weights we meant to write."""
    src_fbx, out_fbx, weights_npz, report_path = args
    rep = {}
    clear_scene()
    a_arm, a_m, _ = import_fbx(src_fbx)
    a_mesh = a_m[0]
    src = {b.name: (b.parent.name if b.parent else None, np.array(b.matrix_local)) for b in a_arm.data.bones}
    n_src = len(a_mesh.data.vertices)
    src_co = np.array([a_mesh.matrix_world @ v.co for v in a_mesh.data.vertices])
    src_uv = np.empty(len(a_mesh.data.loops) * 2, dtype=np.float32)
    a_mesh.data.uv_layers.active.data.foreach_get("uv", src_uv)
    src_nor = np.empty(len(a_mesh.data.loops) * 3, dtype=np.float32)
    a_mesh.data.corner_normals.foreach_get("vector", src_nor)
    src_w, src_names = weight_matrix(a_mesh)

    clear_scene()
    b_arm, b_m, objs = import_fbx(out_fbx)
    b_mesh = b_m[0]
    me = b_mesh.data
    rep["objects"] = [(o.name, o.type) for o in objs]
    rep["bones"] = len(b_arm.data.bones)
    out = {b.name: (b.parent.name if b.parent else None, np.array(b.matrix_local)) for b in b_arm.data.bones}
    rep["bone_name_diff"] = sorted(set(src) ^ set(out))
    rep["bone_parent_diff"] = [n for n in src if n in out and src[n][0] != out[n][0]]
    rep["bone_matrix_max_diff"] = float(max(np.abs(src[n][1] - out[n][1]).max() for n in src if n in out))
    rep["verts"] = len(me.vertices)
    rep["tris"] = sum(len(p.vertices) - 2 for p in me.polygons)
    rep["polys"] = len(me.polygons)
    rep["uv_layers"] = len(me.uv_layers)
    rep["materials"] = [m.name for m in me.materials]
    rep["images"] = [(i.name, list(i.size), i.filepath) for i in bpy.data.images]
    rep["armature_matrix_identity"] = bool(np.allclose(np.array(b_arm.matrix_world), np.eye(4), atol=1e-5))

    co = np.array([b_mesh.matrix_world @ v.co for v in me.vertices])
    rep["front_pos_max_diff"] = float(np.abs(co[:n_src] - src_co).max())
    rep["bounds"] = [co.min(axis=0).tolist(), co.max(axis=0).tolist()]
    uv = np.empty(len(me.loops) * 2, dtype=np.float32)
    me.uv_layers.active.data.foreach_get("uv", uv)
    n_src_loops = len(src_uv) // 2
    rep["front_uv_max_diff"] = float(np.abs(uv[:n_src_loops * 2] - src_uv).max())
    nor = np.empty(len(me.loops) * 3, dtype=np.float32)
    me.corner_normals.foreach_get("vector", nor)
    rep["front_normal_max_diff"] = float(np.abs(nor[:n_src_loops * 3] - src_nor).max())
    rep["front_normal_mean_diff"] = float(np.abs(nor[:n_src_loops * 3] - src_nor).mean())
    # The twins: behind their front vertex, and shaded on the side they face.
    if rep["verts"] > n_src:
        from mathutils.kdtree import KDTree
        tree = KDTree(n_src)
        for i, c in enumerate(co[:n_src]):
            tree.insert(c, i)
        tree.balance()
        gaps = [tree.find(c)[2] for c in co[n_src:]]
        rep["twin_verts"] = len(gaps)
        rep["twin_gap_max"] = float(max(gaps))
        rep["twin_gap_mean"] = float(np.mean(gaps))
        twin_polys = [p for p in me.polygons if p.vertices[0] >= n_src]
        rep["twin_polys"] = len(twin_polys)
        rep["twin_polys_mixed_with_front"] = sum(1 for p in me.polygons
                                                 if len({v >= n_src for v in p.vertices}) > 1)
        nor3 = nor.reshape(-1, 3)
        dots = [float(np.dot(nor3[l], p.normal)) for p in twin_polys
                for l in range(p.loop_start, p.loop_start + p.loop_total)]
        rep["twin_normal_dot_face_mean"] = float(np.mean(dots))

    w, names = weight_matrix(b_mesh)
    saved = np.load(weights_npz)
    want, want_names = saved["w"], [str(x) for x in saved["names"]]
    rep["group_count"] = len(names)
    rep["group_name_diff"] = sorted(set(names) ^ set(want_names))
    order = [names.index(n) for n in want_names]
    rep["weight_max_diff"] = float(np.abs(w[:, order] - want).max())
    totals = w.sum(axis=1)
    rep["unweighted"] = int((totals < 1e-6).sum())
    rep["non_normalized"] = int((np.abs(totals - 1) > 0.01).sum())
    rep["max_influences"] = int((w > 1e-4).sum(axis=1).max())
    # Which source weights changed on the original vertices.
    src_order = [src_names.index(n) for n in names]
    changed = np.abs(w[:n_src] - src_w[:, src_order]).max(axis=1) > 0.002
    rep["front_verts_with_changed_weights"] = int(changed.sum())
    rep["fbx"] = fbx_summary(out_fbx)
    with open(report_path, "w", encoding="utf-8") as f:
        json.dump(rep, f, indent=1, default=str)
    print("VERIFY", json.dumps({k: rep[k] for k in rep if k != "fbx"}, default=str))
    print("WROTE", report_path)


def mode_texstats(args):
    """texstats <fbx> <jpg> <out.png> x0 y0 z0 x1 y1 z1: hue stats for a 3D box, and an atlas crop."""
    fbx, jpg, out = args[:3]
    lo = [float(a) for a in args[3:6]]
    hi = [float(a) for a in args[6:9]]
    clear_scene()
    arm, meshes, _ = import_fbx(fbx)
    mesh = meshes[0]
    setup_render()
    rgb = load_rgba(jpg)[..., :3]
    h, w = rgb.shape[:2]
    face, pos = bake_position_map(mesh, w, h)
    box = in_box(pos, lo, hi) & (face >= 0)
    hue, sat, val = rgb_to_hsv(rgb)
    print("STATS texels", int(box.sum()))
    for name, a in (("hue", hue), ("sat", sat), ("val", val)):
        print("STATS", name, np.percentile(a[box], [1, 5, 25, 50, 75, 95, 99]).round(3).tolist())
    hist, edges = np.histogram(hue[box], bins=np.arange(0, 62, 2))
    print("STATS huehist", [(int(e), int(n)) for e, n in zip(edges, hist) if n > 0])
    # Save the box's texels with everything else greyed, cropped to its bounding box.
    show = rgb.copy()
    show[~box] = show[~box] * 0.15
    y0, y1, x0, x1 = crop_of(box, 10)
    crop = show[y0:y1, x0:x1]
    out_rgba = np.ones(crop.shape[:2] + (4,), dtype=np.float32)
    out_rgba[..., :3] = crop
    save_rgba(out_rgba, out)
    print("STATS crop", y0, y1, x0, x1)


def add_wire(mesh, thickness=0.00025):
    """A dark wireframe copy, for reading the topology in a render."""
    wire = mesh.copy()
    wire.data = mesh.data.copy()
    bpy.context.scene.collection.objects.link(wire)
    wire.name = "Wire"
    for md in list(wire.modifiers):
        if md.type != "ARMATURE":
            wire.modifiers.remove(md)
    mod = wire.modifiers.new("Wire", "WIREFRAME")
    mod.thickness = thickness
    mod.use_replace = True
    mat = bpy.data.materials.new("WireMat")
    mat.use_nodes = True
    n = next(x for x in mat.node_tree.nodes if x.type == "BSDF_PRINCIPLED")
    n.inputs["Base Color"].default_value = (0.02, 0.02, 0.05, 1)
    wire.data.materials.clear()
    wire.data.materials.append(mat)
    return wire


def mode_shots(args):
    """shots <fbx> <jpg> <shots.json> <outdir>: list of {name,target,dir,dist,wire,cull,size}."""
    fbx, jpg, shots_path, outdir = args
    os.makedirs(outdir, exist_ok=True)
    with open(shots_path, encoding="utf-8-sig") as f:
        shots = json.load(f)
    clear_scene()
    arm, meshes, _ = import_fbx(fbx)
    mesh = meshes[0]
    set_texture(mesh, jpg)
    cam = setup_render(800, 800, 16)
    wire = add_wire(mesh)
    tex_mat = mesh.data.materials[0]
    clay = bpy.data.materials.new("Clay")
    clay.use_nodes = True
    node = next(x for x in clay.node_tree.nodes if x.type == "BSDF_PRINCIPLED")
    node.inputs["Base Color"].default_value = (0.6, 0.6, 0.6, 1)
    node.inputs["Roughness"].default_value = 0.45
    for s in shots:
        mesh.data.materials[0] = clay if s.get("clay") else tex_mat
        set_cull(mesh, s.get("cull", True))
        for light in bpy.data.lights:
            light.use_shadow = s.get("shadow", True)
        wire.hide_render = not s.get("wire", False)
        r = bpy.context.scene.render
        r.resolution_x = r.resolution_y = s.get("size", 800)
        frame_camera(cam, s["target"], s["dir"], s["dist"])
        render_to(os.path.join(outdir, s["name"] + ".png"))
    print("SHOTS DONE")


# ---------------------------------------------------------------------------------------------
# Dispatch
# ---------------------------------------------------------------------------------------------

def main():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    mode = argv[0]
    {"inspect": mode_inspect, "turnaround": mode_turnaround, "thumb": mode_thumb,
     "weights": mode_weights, "posetest": mode_posetest, "diag": mode_diag,
     "shots": mode_shots, "rigcompare": mode_rigcompare, "shading": mode_shading,
     "build": mode_build, "verify": mode_verify, "texstats": mode_texstats,
     "sheet": mode_sheet}[mode](argv[1:])


if __name__ == "__main__":
    main()
