"""Slim the ruler sword's handle so a chibi fist can close round it.

Blender 5.2 headless:
  blender.exe -b --factory-startup --python slim_ruler_handle.py -- IN.fbx OUT.fbx [--preview DIR]

The Tripo model's wrapped handle is about 5.4 cm across (Blender metres, Z up,
pommel at Z=0). At the game length that is a 3 cm radius, twice what the
chibi's short fingers can wrap. Every vertex below the guard disc (Z < HANDLE_TOP)
is scaled toward the handle's own axis; the guard and blade are left
exactly as exported. UVs and topology are untouched, so the texture still
lines up. The pommel and its ring are slimmed a little less, so they stay a
knob below the fist.
"""
import bpy, sys, math, os

argv = sys.argv[sys.argv.index("--") + 1:]
src, dst = argv[0], argv[1]
preview = argv[argv.index("--preview") + 1] if "--preview" in argv else None

HANDLE_TOP = 0.1785     # everything below the guard disc (its underside is at Z 0.18): pommel, grip, ferrule
POMMEL_TOP = 0.057      # pommel bulb and its ring end here
GRIP_SCALE = 0.66       # wrapped grip and ferrule: 2.7 cm radius -> 1.8 cm
POMMEL_SCALE = 0.74     # pommel: stays a knob, ~2 mm proud of the grip
AXIS = (0.0055, 0.0)    # the handle's own axis in X/Y (5 mm off the bounds centre)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
obj = next(o for o in bpy.data.objects if o.type == 'MESH')
M = obj.matrix_world
Mi = M.inverted()
moved = 0
for v in obj.data.vertices:
    w = M @ v.co
    if w.z >= HANDLE_TOP:
        continue
    k = POMMEL_SCALE if w.z < POMMEL_TOP else GRIP_SCALE
    w.x = AXIS[0] + (w.x - AXIS[0]) * k
    w.y = AXIS[1] + (w.y - AXIS[1]) * k
    v.co = Mi @ w
    moved += 1
obj.data.update()
print(f"SLIM moved {moved} of {len(obj.data.vertices)} vertices")

for z0 in (0.02, 0.05, 0.08, 0.11, 0.14, 0.17, 0.19):
    sl = [M @ v.co for v in obj.data.vertices if z0 <= (M @ v.co).z < z0 + 0.02]
    if sl:
        r = max(math.hypot(p.x - AXIS[0], p.y - AXIS[1]) for p in sl)
        print(f"SLIM z {z0:.2f}-{z0+0.02:.2f}: r {r:.4f}")

os.makedirs(os.path.dirname(dst), exist_ok=True)
bpy.ops.export_scene.fbx(filepath=dst, use_selection=False, object_types={'MESH'},
                         apply_scale_options='FBX_SCALE_UNITS', mesh_smooth_type='FACE',
                         path_mode='STRIP', embed_textures=False, add_leaf_bones=False,
                         bake_anim=False)
print("SLIM wrote", dst)

if preview:
    os.makedirs(preview, exist_ok=True)
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_WORKBENCH'
    scn.display.shading.color_type = 'TEXTURE'
    scn.display.shading.light = 'STUDIO'
    scn.render.resolution_x = scn.render.resolution_y = 700
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scn.collection.objects.link(cam); scn.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 0.34
    for name, loc, rot in (("front", (0, -2, 0.12), (math.pi/2, 0, 0)), ("side", (2, 0, 0.12), (math.pi/2, 0, math.pi/2))):
        cam.location = loc; cam.rotation_euler = rot
        scn.render.filepath = os.path.join(preview, f"slim_{name}.png")
        bpy.ops.render.render(write_still=True)
