"""Paint cutscene face variants (eyes, mouths) into COPIES of a character's base-colour texture.

Neither rig has blendshapes, so the opening cutscenes change eyes by swapping the base map
(EyeSwap, MaterialPropertyBlock). This script makes the swapped-in texture. Tripo atlases scatter
a face over many small UV islands, so painting in UV space by hand is hopeless; instead every
texel of the face is mapped back to its 3D point (rasterizing each front-facing triangle in UV
space with barycentric interpolation), and the paint is decided in a front-orthographic 2D frame
(x = model left/right, z = up, metres in Blender's import units). The eye ellipses below were
read off front-view renders of the bind pose (see Docs/Art/CutsceneCharacters.md).

Texels in front of the face surface (a hair strand crossing an eye) are left alone: a texel is
painted only if its depth is within DEPTH_TOL of the face depth measured inside that eye.

Run headless (never touches the source texture or FBX):
  blender.exe -b --factory-startup --python Tools/Blender/cutscene_paint_eyes.py -- <mode> <fbx> <src_tex> <out_tex>
modes, joined with "+" (e.g. chibi_closed+mc_mouth_open): chibi_closed | birdy_drop | mc_mouth_half | mc_mouth_open | birdy_beak_half | birdy_beak_open
"""
import sys
import numpy as np
import bpy

DEPTH_TOL = 0.006  # m: a hair strand floating in front of an eye is further out than this

# Eye ellipses in the front frame: centre (x, z), half width, half height (metres, bind pose).
EYES = {
    # MC (chibi_unity.fbx): the whole eye incl. the thick upper lash line.
    "chibi_closed": [((-0.0524, 0.7349), 0.0285, 0.0235), ((0.0487, 0.7374), 0.0290, 0.0240)],
    # Birdy: the cream ring around each dark eye.
    "birdy_drop": [((-0.0572, 0.7339), 0.0172, 0.0210), ((0.0584, 0.7334), 0.0172, 0.0210)],
}


def load_pixels(img):
    w, h = img.size
    return np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)


def face_texels(objs, eyes, tw, th, normal_min=0.05, margin=1.6):
    """Every texel of every front-facing triangle near an eye: (tx, ty, x, z, depth, eye index)."""
    out = []
    dg = bpy.context.evaluated_depsgraph_get()
    for o in objs:
        ev = o.evaluated_get(dg)
        me = ev.to_mesh()
        me.calc_loop_triangles()
        mw = o.matrix_world
        nm = mw.to_3x3()
        co = np.array([tuple(mw @ v.co) for v in me.vertices], dtype=np.float64)
        uvl = me.uv_layers.active.data
        for tri in me.loop_triangles:
            P = co[list(tri.vertices)]
            n = (nm @ tri.normal).normalized()
            if -n.y < normal_min:
                continue
            cx, cz = P[:, 0].mean(), P[:, 2].mean()
            near = -1
            for i, ((ex, ez), hw, hh) in enumerate(eyes):
                if abs(cx - ex) < hw * margin + 0.01 and abs(cz - ez) < hh * margin + 0.01:
                    near = i
            if near < 0:
                continue
            UV = np.array([tuple(uvl[l].uv) for l in tri.loops], dtype=np.float64)
            sx = UV[:, 0] * tw - 0.5
            sy = UV[:, 1] * th - 0.5
            x0, x1 = int(np.floor(sx.min())), int(np.ceil(sx.max()))
            y0, y1 = int(np.floor(sy.min())), int(np.ceil(sy.max()))
            x0, y0 = max(x0, 0), max(y0, 0)
            x1, y1 = min(x1, tw - 1), min(y1, th - 1)
            if x1 < x0 or y1 < y0:
                continue
            d = (sy[1] - sy[2]) * (sx[0] - sx[2]) + (sx[2] - sx[1]) * (sy[0] - sy[2])
            if abs(d) < 1e-12:
                continue
            xs, ys = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
            a = ((sy[1] - sy[2]) * (xs - sx[2]) + (sx[2] - sx[1]) * (ys - sy[2])) / d
            b = ((sy[2] - sy[0]) * (xs - sx[2]) + (sx[0] - sx[2]) * (ys - sy[2])) / d
            c = 1 - a - b
            # A one-texel dilation so island edges get painted too (no seams of old colour).
            eps = 1.5 / max(abs(d), 1e-6) ** 0.5
            m = (a >= -eps) & (b >= -eps) & (c >= -eps)
            if not m.any():
                continue
            a, b, c, xs, ys = a[m], b[m], c[m], xs[m], ys[m]
            X = a * P[0, 0] + b * P[1, 0] + c * P[2, 0]
            Z = a * P[0, 2] + b * P[1, 2] + c * P[2, 2]
            Y = a * P[0, 1] + b * P[1, 1] + c * P[2, 1]
            out.append((xs, ys, X, Z, Y, np.full(xs.shape, near)))
        ev.to_mesh_clear()
    cat = [np.concatenate([o[k] for o in out]) for k in range(6)]
    return cat


def sample_colour(px, tx, ty, X, Z, sel):
    pick = px[ty[sel], tx[sel], :3]
    return np.median(pick, axis=0) if len(pick) else np.array([0.8, 0.7, 0.65])


def paint_eyes(mode, new, px, objs, tw, th):
    """The eye variants, unchanged from the first pass (Chibi_EyesClosed / Birdy_EyesDrop)."""
    eyes = EYES[mode]
    tx, ty, X, Z, Y, eye = face_texels(objs, eyes, tw, th)
    for i, ((ex, ez), hw, hh) in enumerate(eyes):
        mine = eye == i
        u = (X - ex) / hw
        v = (Z - ez) / hh
        r = np.sqrt(u * u + v * v)
        inside = mine & (r < 1.0)
        depth_ref = np.median(Y[inside & (r < 0.6)])
        surf = np.abs(Y - depth_ref) < DEPTH_TOL + 0.02 * np.clip(r, 0, 2)
        print(f"EYE {i}: texels {inside.sum()}, depth ref {depth_ref:.4f}, on surface {(inside & surf).sum()}")
        side = np.sign(ex)  # +1 = model's left eye (image right in a front view)
        if mode == "chibi_closed":
            # Skin from the cheek just under the eye; lash colour from the old upper lash.
            skin = sample_colour(px, tx, ty, X, Z, mine & surf & (np.abs(u) < 0.5) & (v < -1.25) & (v > -1.6))
            lash = np.array([0.10, 0.09, 0.10])
            paint = mine & surf & (r < 1.3)
            soft = np.clip((1.3 - r) / 0.2, 0, 1)
            col = new[ty, tx, :3]
            col_skin = col * (1 - soft[:, None]) + skin[None, :] * soft[:, None]
            # Closed lid: a gentle downward arc (a relaxed sleeping eye), lower than the middle,
            # thicker in the centre, with a short lash flick at the outer corner.
            arc_z = -0.25 - 0.32 * (1 - u * u)
            dist = np.abs(v - arc_z) * hh
            thick = 0.0020 + 0.0012 * (1 - u * u)
            line = (np.abs(u) < 1.0) & (dist < thick)
            flick_u = side * u
            flick = (flick_u > 0.85) & (flick_u < 1.12) & (np.abs(v - (-0.25 + (flick_u - 0.85) * 0.9)) * hh < 0.0016)
            line_soft = np.clip((thick - dist) / 0.0008, 0, 1)
            col_final = col_skin.copy()
            lm = (line | flick)
            col_final[lm] = col_skin[lm] * (1 - line_soft[lm, None]) + lash[None, :] * line_soft[lm, None]
            col_final[flick & ~line] = lash
            new[ty[paint], tx[paint], :3] = col_final[paint]
            print(f"  skin {skin}, painted {paint.sum()}")
        elif mode == "birdy_drop":
            # Half-dropped lids: everything above a lid line that sags toward the outer corner
            # (a sad, faltering look) becomes lid, coloured like the cream eye ring (a bird's
            # lid is the ring's skin), edged by a dark lid line.
            ring = mine & surf & (r > 0.8) & (r < 1.05) & (v < -0.3)
            pick = px[ty[ring], tx[ring], :3]
            lum = pick.sum(axis=1)
            # The brightest ring texels are the cream; the median of the whole ring came out a
            # dull khaki (it catches the dark eye edge), so take the 90th percentile.
            feathers = np.median(pick[lum >= np.percentile(lum, 90)], axis=0)
            lid_edge = np.array([0.06, 0.08, 0.14])
            out_u = side * u  # > 0 toward the outer corner
            lid_v = 0.18 - 0.30 * out_u
            above = mine & surf & (r < 1.12) & (v > lid_v)
            soft = np.clip((v - lid_v) / 0.08, 0, 1) * np.clip((1.12 - r) / 0.12, 0, 1)
            col = new[ty, tx, :3]
            blend = col * (1 - soft[:, None]) + feathers[None, :] * soft[:, None]
            new[ty[above], tx[above], :3] = blend[above]
            edge = mine & surf & (r < 1.02) & (np.abs(v - lid_v) * hh < 0.0014)
            new[ty[edge], tx[edge], :3] = lid_edge
            print(f"  feathers {feathers}, painted {above.sum()} edge {edge.sum()}")


# ---- mouths -------------------------------------------------------------------------------------
# MC's mouth: a short reddish-brown frown line, centre (x, z) and half width, read off a front
# render (8000 px/m). Shapes are an oval opening centred a little under the line.
MC_MOUTH = (-0.0031, 0.6826, 0.0115)
# (half width, half height, drop of the centre below the line), metres.
MC_SHAPES = {"half": (0.0070, 0.0026, 0.0008), "open": (0.0082, 0.0052, 0.0020)}

# Birdy's beak gape: the line between the mandibles sags from the corners (x = +/-0.042,
# z 0.7047) to the centre (z 0.7012). The opening is a lens along that line, widest in the middle.
BEAK = (0.0001, 0.7012, 0.042, 0.0035)  # centre x, centre z, half width, corner rise
BEAK_SHAPES = {"half": 0.0080, "open": 0.0160}  # opening at the centre, metres


def visible(X, Z, Y, cell=0.0015, tol=0.003):
    """Texels on the front-most surface of their small (x, z) cell: a z-buffer in the front frame,
    so hidden layers behind the face (inner geometry, hair at the back) are never painted."""
    ix = np.floor(X / cell).astype(np.int64)
    iz = np.floor(Z / cell).astype(np.int64)
    key = ix * 1000003 + iz
    order = np.argsort(key, kind="stable")
    k = key[order]
    starts = np.r_[0, np.nonzero(np.diff(k))[0] + 1]
    front = np.minimum.reduceat(Y[order], starts)
    per = np.repeat(front, np.diff(np.r_[starts, len(k)]))
    out = np.empty(len(X), dtype=bool)
    out[order] = Y[order] < per + tol
    return out


def _keep(arrs, m):
    return [a[m] for a in arrs]


def paint_mc_mouth(shape, new, px, objs, tw, th):
    mx, mz, mhw = MC_MOUTH
    hw, hh, drop = MC_SHAPES[shape]
    tx, ty, X, Z, Y, _ = face_texels(objs, [((mx, mz - 0.003), 0.016, 0.010)], tw, th, margin=1.3)
    tx, ty, X, Z, Y = _keep([tx, ty, X, Z, Y], visible(X, Z, Y))
    u = (X - mx) / mhw
    # 1. Erase the closed line: skin over a thin ellipse round it, sampled from just above.
    line_v = (Z - mz) / 0.0032
    er = np.sqrt(u * u / 1.25 ** 2 + line_v * line_v)
    skin = sample_colour(px, tx, ty, X, Z, (np.abs(u) < 0.6) & (Z > mz + 0.0045) & (Z < mz + 0.0075))
    soft = np.clip((1.0 - er) / 0.25, 0, 1)
    col = new[ty, tx, :3]
    erase = er < 1.0
    col = col * (1 - soft[:, None]) + skin[None, :] * soft[:, None]
    # 2. The opening: dark interior, a tongue in the lower third when fully open, a lip line.
    cz = mz - drop
    ou = (X - mx) / hw
    ov = (Z - cz) / hh
    orr = np.sqrt(ou * ou + ov * ov)
    inside = orr < 1.0
    interior = np.array([0.27, 0.09, 0.09])
    tongue = np.array([0.72, 0.40, 0.40])
    lip = np.array([0.42, 0.20, 0.19])
    fill = np.repeat(interior[None, :], len(X), axis=0)
    if shape == "open":
        t = np.clip((-ov - 0.25) / 0.35, 0, 1)
        fill = fill * (1 - t[:, None]) + tongue[None, :] * t[:, None]
    edge = np.clip((orr - 0.80) / 0.2, 0, 1)  # the rim darkens toward the lip colour
    fill = fill * (1 - edge[:, None]) + lip[None, :] * edge[:, None]
    aa = np.clip((1.0 - orr) / 0.08, 0, 1)
    col = col * (1 - aa[:, None]) + fill * aa[:, None]
    paint = erase | inside
    new[ty[paint], tx[paint], :3] = col[paint]
    print(f"MC MOUTH {shape}: skin {skin}, painted {paint.sum()} (opening {inside.sum()})")


def paint_beak(shape, new, px, objs, tw, th):
    bx, bz, bhw, rise = BEAK
    gap = BEAK_SHAPES[shape]
    tx, ty, X, Z, Y, _ = face_texels(objs, [((bx, bz), bhw * 1.1, 0.015)], tw, th, margin=1.2, normal_min=-0.2)
    # No visibility filter here: on the beak it dropped most front texels (stripes), and a hidden
    # texel painted dark inside the gape never shows.
    u = (X - bx) / bhw
    zg = bz + rise * np.clip(u * u, 0, 1)  # the gape line
    h = gap * np.clip(1 - u * u, 0, 1) ** 0.8  # opening height, tapering to the corners
    # Centred above the gape line: from a camera level with him or above, what shows of the closed
    # beak near the gape is the lower face of the upper mandible, so the opening is painted there.
    d = Z - (zg + 0.30 * h)
    inside = (np.abs(u) < 0.95) & (np.abs(d) < h / 2)
    interior = np.array([0.80, 0.30, 0.36])  # a bird's gape is pink: it must read against the dark beak
    throat = np.array([0.30, 0.05, 0.08])
    edge_c = np.array([0.04, 0.04, 0.06])
    rel = np.clip(np.abs(d) / np.maximum(h / 2, 1e-5), 0, 1)
    w = rel ** 0.6  # pink, with a dark throat only in the middle
    fill = throat[None, :] * (1 - w[:, None]) + interior[None, :] * w[:, None]
    rim = rel > 0.9
    fill[rim] = edge_c
    new[ty[inside], tx[inside], :3] = fill[inside]
    print(f"BEAK {shape}: painted {inside.sum()} (gap {gap * 1000:.1f} mm)")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    modes, fbx, src, dst = argv[0].split("+"), argv[1], argv[2], argv[3]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx)
    img = bpy.data.images.load(src)
    tw, th = img.size
    px = load_pixels(img)
    objs = [o for o in bpy.data.objects if o.type == 'MESH' and o.name != 'tripo_part_1']
    new = px.copy()
    for mode in modes:
        if mode in EYES:
            paint_eyes(mode, new, px, objs, tw, th)
        elif mode.startswith("mc_mouth_"):
            paint_mc_mouth(mode[len("mc_mouth_"):], new, px, objs, tw, th)
        elif mode.startswith("birdy_beak_"):
            paint_beak(mode[len("birdy_beak_"):], new, px, objs, tw, th)
        else:
            raise SystemExit(f"unknown mode {mode}")
    out = bpy.data.images.new("face", tw, th, alpha=False)
    out.pixels[:] = new.ravel()
    out.filepath_raw = dst
    out.file_format = 'JPEG'
    bpy.context.scene.render.image_settings.quality = 95
    out.save(filepath=dst, quality=95)
    print("SAVED", dst)


main()

