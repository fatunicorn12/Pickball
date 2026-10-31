"""Extract Birdy's propeller (mesh part tripo_part_1 of Birdy.fbx) as its own FBX.

The opening cutscenes fly the propeller on its own (it pops off mid-tumble, lands on a ledge,
is carried back and seated), so it needs to exist as a separate rigid prop. Birdy's own
propeller stays on him, skinned to the Rotor bone that BirdyModelPostprocessor adds; the
cutscene hides it by scaling that bone to zero while the prop is detached.

The prop's frame matches the Rotor bone's exactly, so a prop seated on Birdy's PropellerSocket
(the Rotor bone's rest pose) covers the skinned propeller to the millimetre:
- origin: the hub axis at the foot of the mast (the middle of the blades' span in the disc's
  plane, at the lowest point along the spin axis), the same rule as the postprocessor;
- +Z (Blender up, Unity +Y): the spin axis, the normal of the blade disc (its axis of least
  spread), pointing away from the head;
- -Y (Blender forward, Unity +Z): Birdy's forward projected onto the disc.
Vertices, UVs and normals are kept; the armature, vertex groups and weights are dropped.

Run headless (never writes Birdy.fbx):
  blender.exe -b --factory-startup --python Tools/Blender/cutscene_extract_propeller.py -- <Birdy.fbx> <out.fbx>
Export settings are the ones that round-trip the Tripo chibi exactly (Tools/Blender/chibi_add_fingers.py).
"""
import sys
import numpy as np
import bpy
from mathutils import Matrix, Vector

PART = "tripo_part_1"


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    src, dst = argv[0], argv[1]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src)
    part = bpy.data.objects[PART]
    head_up = Vector((0, 0, 1))

    # World-space vertices of the part at bind (the armature is at rest after import).
    mw = part.matrix_world.copy()
    P = np.array([tuple(mw @ v.co) for v in part.data.vertices], dtype=np.float64)

    # Principal axes: the two widest directions span the blade disc, the least is the spin axis.
    c = P.mean(axis=0)
    cov = np.cov((P - c).T)
    vals, vecs = np.linalg.eigh(cov)  # ascending
    axis = Vector(vecs[:, 0]).normalized()
    if axis.dot(head_up) < 0:
        axis = -axis
    wide = Vector(vecs[:, 2]).normalized()
    across = axis.cross(wide).normalized()
    wide = across.cross(axis).normalized()

    w = P @ np.array(wide)
    a = P @ np.array(across)
    h = P @ np.array(axis)
    origin = wide * ((w.min() + w.max()) * 0.5) + across * ((a.min() + a.max()) * 0.5) + axis * h.min()

    # Forward: Birdy faces -Y in Blender (Unity +Z); project it onto the disc.
    fwd = Vector((0, -1, 0))
    fwd = (fwd - axis * fwd.dot(axis)).normalized()
    # Local frame columns in Blender convention (right-handed, X = Y x Z): Y = back (-forward),
    # Z = up, so X comes out as the model's left, as on every Blender-exported character.
    x_axis = (-fwd).cross(axis).normalized()
    y_axis = -fwd
    z_axis = axis
    frame = Matrix((
        (x_axis.x, y_axis.x, z_axis.x, origin.x),
        (x_axis.y, y_axis.y, z_axis.y, origin.y),
        (x_axis.z, y_axis.z, z_axis.z, origin.z),
        (0, 0, 0, 1)))
    to_local = frame.inverted() @ mw

    # A fresh mesh object carrying the part's geometry in the prop frame, no armature.
    me = part.data.copy()
    me.name = "BirdyPropeller"
    me.transform(to_local)
    prop = bpy.data.objects.new("BirdyPropeller", me)
    bpy.context.scene.collection.objects.link(prop)
    prop.vertex_groups.clear()

    # Remove everything else so the export holds the prop only.
    for o in list(bpy.data.objects):
        if o != prop:
            bpy.data.objects.remove(o, do_unlink=True)

    Q = np.array([tuple(v.co) for v in me.vertices])
    radius = np.sqrt(Q[:, 0] ** 2 + Q[:, 1] ** 2).max()
    print(f"PROP axis tilt from head up {np.degrees(np.arccos(max(-1, min(1, axis.dot(head_up))))):.1f} deg, "
          f"blade radius {radius:.3f}, height {Q[:, 2].max():.3f}, min z {Q[:, 2].min():.4f}, verts {len(Q)}, "
          f"origin {tuple(round(v, 4) for v in origin)}")

    bpy.ops.object.select_all(action='DESELECT')
    prop.select_set(True)
    bpy.context.view_layer.objects.active = prop
    bpy.ops.export_scene.fbx(
        filepath=dst, use_selection=True, object_types={'MESH'},
        apply_scale_options='FBX_SCALE_UNITS', mesh_smooth_type='FACE',
        add_leaf_bones=False, bake_anim=False, path_mode='STRIP', embed_textures=False)
    print("SAVED", dst)


main()
