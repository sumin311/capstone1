# 로우폴리 손님 캐릭터 1종 (시험용, 머리카락·가방 없음). Blender 5.2에서 실행:
#   blender -b -P build_lowpoly_customer.py
# 큰 머리 + 작은 몸의 귀여운 비율. 단순 도형으로 만들고, 30도보다 크게 꺾이는 모서리만 또렷하게 남긴다
# (너무 둥글지도, 너무 각지지도 않게). 결과: lowpoly_customer.blend, fbx/lowpoly_customer.fbx, preview.png
import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
SHARP_ANGLE = math.radians(30)

bpy.ops.wm.read_factory_settings(use_empty=True)


def mat(name, rgb):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1)
    bsdf.inputs["Roughness"].default_value = 0.8
    m.diffuse_color = (*rgb, 1)
    return m


SKIN = mat("Skin", (0.93, 0.70, 0.55))
SHIRT = mat("Shirt", (0.95, 0.93, 0.88))
PANTS = mat("Pants", (0.20, 0.32, 0.50))
SHOES = mat("Shoes", (0.10, 0.09, 0.09))
DARK = mat("Face", (0.12, 0.07, 0.05))
HAIR = mat("Hair", (0.16, 0.10, 0.07))
MOUTH = mat("Mouth", (0.45, 0.13, 0.12))

parts = []


def add(obj, material):
    obj.data.materials.append(material)
    parts.append(obj)
    return obj


def sphere(name, loc, scale, material, seg=12, rings=8):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, radius=1, location=loc)
    o = bpy.context.object
    o.name = name
    o.scale = scale
    return add(o, material)


def cyl(name, loc, r1, r2, depth, material, rot=(0, 0, 0), verts=12):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r1, radius2=r2, depth=depth, location=loc, rotation=rot)
    o = bpy.context.object
    o.name = name
    return add(o, material)


def box(name, loc, size, material, rot=(0, 0, 0), bevel=0.25, segments=2):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    o = bpy.context.object
    o.name = name
    o.scale = size
    if bevel > 0:
        b = o.modifiers.new("Bevel", "BEVEL")
        b.width = min(size) * bevel
        b.segments = segments               # small rounding: soft but not blobby
    return add(o, material)


def shoe(name, loc, size):
    """A shoe block with only the toe's top edge rounded off (the rest keeps a tiny chamfer)."""
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0))
    o = bpy.context.object
    o.name = name
    bm = bmesh.new()
    bm.from_mesh(o.data)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    toe = [e for e in bm.edges if all(v.co.y < -size[1] / 2 + 1e-5 and v.co.z > size[2] / 2 - 1e-5 for v in e.verts)]
    bmesh.ops.bevel(bm, geom=toe, offset=size[2] * 0.6, segments=5, profile=0.5, affect="EDGES")
    bm.to_mesh(o.data)
    bm.free()
    o.location = loc
    b = o.modifiers.new("Bevel", "BEVEL")
    b.width = 0.006
    b.segments = 1
    return add(o, SHOES)


def cut(obj, loc, scale):
    """Boolean-subtract a box from obj (used to open the hair at the face and halve the mouth)."""
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    c = bpy.context.object
    c.scale = scale
    m = obj.modifiers.new("Cut", "BOOLEAN")
    m.object = c
    m.operation = "DIFFERENCE"
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier="Cut")
    bpy.data.objects.remove(c)


# ---------------------------------------------------------------- head (키 약 1m, 머리가 거의 절반)
sphere("Head", (0, 0, 0.74), (0.27, 0.25, 0.26), SKIN, 16, 10)
for s in (1, -1):
    sphere("Ear", (0.265 * s, 0.01, 0.725), (0.045, 0.028, 0.058), SKIN, 8, 6)

# scalp: a thin cap under the hair locks that just fills the head with hair colour (no shape of its own;
# its edge is tucked under the locks).
def hairline(phi):
    """Lowest elevation (radians on the shell) that still has hair, for azimuth phi (0 = front, +-pi = back)."""
    a = abs(phi)
    keys = ((0.0, 0.5), (0.9, 0.45), (1.25, 0.3), (1.6, 0.12), (2.1, 0.75), (math.pi, 0.78))
    for (a0, v0), (a1, v1) in zip(keys, keys[1:]):
        if a <= a1:
            t = (a - a0) / (a1 - a0)
            t = t * t * (3 - 2 * t)
            base = v0 + (v1 - v0) * t
            break
    # back: no base there at all, only under the lid; the back locks sit right on the head
    return base


bpy.ops.mesh.primitive_uv_sphere_add(segments=64, ring_count=40, radius=1, location=(0, 0, 0))
hair = bpy.context.object
hair.name = "Hair"
bm = bmesh.new()
bm.from_mesh(hair.data)
def polar(v):
    phi = math.atan2(v.co.x, -v.co.y)
    elev = math.asin(max(-1.0, min(1.0, v.co.z)))
    return phi, elev
bmesh.ops.delete(bm, geom=[f for f in bm.faces if all(polar(v)[1] < hairline(polar(v)[0]) for v in f.verts)], context="FACES")
for v in bm.verts:
    phi, elev = polar(v)
    elev = max(elev, hairline(phi))                # pull the cut edge exactly onto the hairline curve
    v.co = Vector((math.cos(elev) * math.sin(phi), -math.cos(elev) * math.cos(phi), math.sin(elev)))
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
bm.to_mesh(hair.data)
bm.free()
hair.location = (0, 0.0, 0.758)
hair.scale = (0.3, 0.27, 0.31)           # hugs the head front/back and sides, volume on top
sol = hair.modifiers.new("Thickness", "SOLIDIFY")
sol.thickness = 0.17                               # in shell units: thick enough to reach the head (no hollow underneath)
sol.offset = -1
add(hair, HAIR)

# hair locks: flat tapered strands that follow the shell and curve as they fall (like drawn bangs)
import random
HAIR_C = Vector(hair.location)
HAIR_S = Vector(hair.scale)
rng = random.Random(7)                              # fixed seed: same hair every build


def shell_frame(phi, elev, lift, C=None, S=None):
    C = HAIR_C if C is None else C
    S = HAIR_S if S is None else S
    u = Vector((math.cos(elev) * math.sin(phi), -math.cos(elev) * math.cos(phi), math.sin(elev)))
    n = Vector((u.x / S.x, u.y / S.y, u.z / S.z)).normalized()
    p = C + Vector((u.x * S.x, u.y * S.y, u.z * S.z)) + n * lift
    return p, n


def strand(phi0, elev0, drop, curl, width, lift=0.012, steps=8, tuck=0.3, flare=0.0, sink=0.0, taper=0.7, sink_from=0.6, flat=0.0, frame=(None, None)):
    """A lock from (phi0, elev0) falling by `drop` radians of elevation while sweeping `curl` radians sideways.
    Cross-section is a flattened hexagon (wide along the head, thin off it), tapering to a point."""
    pts, norms = [], []
    elev0, drop = elev0 + tuck, drop + tuck        # roots start further up, hidden under the lid
    for i in range(steps + 1):
        t = i / steps
        p, n = shell_frame(phi0 + curl * t * t, elev0 - drop * t, lift - 0.008 * t - sink * max(0.0, (t - sink_from) / (1 - sink_from)) ** 1.5 + flare * t ** 3, *frame)
        pts.append(p)
        norms.append(n)
    bm = bmesh.new()
    rings = []
    for i, (p, n) in enumerate(zip(pts, norms)):
        t = i / steps
        fwd = (pts[min(i + 1, steps)] - pts[max(i - 1, 0)]).normalized()
        side = fwd.cross(n).normalized()
        up = side.cross(fwd).normalized()
        w = width * (1 - t) ** taper * (0.85 + 0.3 * math.sin(math.pi * min(1, t * 1.6)))
        if i == steps:
            rings.append([bm.verts.new(p)])
            break
        ring = []
        for k in range(6):
            a_ = k * math.pi / 3
            off = math.cos(a_) * w
            ring.append(bm.verts.new(p + side * off + up * math.sin(a_) * w * 0.34 * (1 - flat * t) - n * (off * off / (2 * 0.29))))
        rings.append(ring)
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(6):
            if len(r1) == 1:
                bm.faces.new((r0[k], r0[(k + 1) % 6], r1[0]))
            else:
                bm.faces.new((r0[k], r0[(k + 1) % 6], r1[(k + 1) % 6], r1[k]))
    bm.faces.new(list(reversed(rings[0])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("HairLock")
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new("HairLock", me)
    bpy.context.collection.objects.link(o)
    add(o, HAIR)


def jitter(v, amount):
    return v + rng.uniform(-amount, amount)


# bangs: parted slightly right of centre, uneven widths and lengths, inner locks curl toward the part,
# outer locks sweep outward to frame the face
# bangs in two layers, all swept a little toward the viewer's left (anime-style layered fringe):
# a shorter under-layer filling the forehead, then longer pointed locks on top
for phi0 in (-0.72, -0.48, -0.24, 0.0, 0.24, 0.48, 0.72):
    strand(jitter(phi0, 0.03), jitter(0.9, 0.03), jitter(0.74, 0.05), -0.12, jitter(0.085, 0.006),
           lift=0.006, sink=0.022, taper=0.6)
TOP_BANGS = (  # phi0, elev0, drop, curl, half-width
    (0.62, 0.95, 0.92, -0.22, 0.09),
    (0.30, 1.00, 1.00, -0.20, 0.10),
    (-0.02, 1.02, 0.94, -0.18, 0.095),
    (-0.34, 0.98, 1.02, -0.16, 0.10),
    (-0.64, 0.90, 0.92, -0.18, 0.085),
)
for phi0, elev0, drop, curl, w in TOP_BANGS:
    strand(jitter(phi0, 0.02), jitter(elev0, 0.02), jitter(drop, 0.04), curl, jitter(w, 0.005),
           lift=0.018, sink=0.03, taper=0.65)

# sides: long locks lying along the cheek in front of the ears, down to about mouth level, plus one over
# the ear; they settle onto the head early so they hug it instead of floating
for sgn in (1, -1):
    strand(0.98 * sgn, 0.72, 1.12, 0.06 * sgn, 0.085, lift=0.008, sink=0.035, sink_from=0.25, taper=0.7)
    strand(1.22 * sgn, 0.66, 1.05, 0.04 * sgn, 0.09, lift=0.006, sink=0.035, sink_from=0.25, taper=0.7)
    strand(1.5 * sgn, 0.66, 0.78, 0.0, 0.11, lift=0.004, sink=0.03, sink_from=0.25, taper=0.6)

# back: four big chunks lying directly on the back of the head (built on the head's own surface, no base
# under them), roots hidden under the lid, flatter toward their ends
HEAD_FRAME = (Vector((0, 0, 0.74)), Vector((0.27, 0.25, 0.26)))
for phi, drop in ((2.1, 1.6), (2.78, 1.8), (-2.78, 1.8), (-2.1, 1.6)):
    strand(jitter(phi, 0.02), 0.85, jitter(drop, 0.03), 0.0, 0.2, lift=0.014, taper=0.22, steps=10, flat=0.7,
           frame=HEAD_FRAME)

# lid: a smooth cap over the top of the head; the locks come out from under its edge
LID_KEYS = ((0.0, 0.9), (0.9, 0.84), (1.25, 0.72), (1.6, 0.7), (2.2, 0.76), (math.pi, 0.8))


def lid_line(phi):
    a = abs(phi)
    for (a0, v0), (a1, v1) in zip(LID_KEYS, LID_KEYS[1:]):
        if a <= a1:
            t = (a - a0) / (a1 - a0)
            return v0 + (v1 - v0) * t * t * (3 - 2 * t)
    return LID_KEYS[-1][1]


bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=30, radius=1, location=(0, 0, 0))
lid = bpy.context.object
lid.name = "HairLid"
bm = bmesh.new()
bm.from_mesh(lid.data)
bmesh.ops.delete(bm, geom=[f for f in bm.faces if all(polar(v)[1] < lid_line(polar(v)[0]) for v in f.verts)], context="FACES")
for v in bm.verts:
    phi, elev = polar(v)
    elev = max(elev, lid_line(phi))
    edge = lid_line(phi)
    k = min(1.0, max(0.0, (elev - edge) / 0.3))
    swell = 1.0 + 0.07 * k * k * (3 - 2 * k)       # at the edge the lid sits on the scalp, under the locks
    v.co = Vector((math.cos(elev) * math.sin(phi), -math.cos(elev) * math.cos(phi), math.sin(elev))) * swell
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
bm.to_mesh(lid.data)
bm.free()
lid.location = HAIR_C
lid.scale = HAIR_S
add(lid, HAIR)

# face: eyes, brows, open smile
for s in (1, -1):
    sphere("Eye", (0.09 * s, -0.232, 0.72), (0.026, 0.014, 0.036), DARK, 8, 6)
    box("Brow", (0.095 * s, -0.236, 0.782), (0.065, 0.014, 0.016), DARK, rot=(0, math.radians(-6 * s), 0), bevel=0.3)
mouth = cyl("Mouth", (0, -0.246, 0.652), 0.03, 0.03, 0.012, MOUTH, rot=(math.radians(90), 0, 0), verts=12)
cut(mouth, (0, -0.246, 0.672), (0.1, 0.1, 0.04))           # keep the lower half: a D-shaped smile

# ---------------------------------------------------------------- body: t-shirt, shorts
LEG_EXTRA = 0.06      # legs this much longer; everything above them moves up by it
NECK_GAP = 0.09       # head clearance above the old body top: leaves about 3 cm of neck above the shoulder piece
HEAD_UP = LEG_EXTRA + 0.05 + NECK_GAP   # head was sunk 5 cm into the shirt; lift it clear, plus the neck
n_head = len(parts)
cyl("Neck", (0, 0, 0.625), 0.055, 0.055, 0.15, SKIN)   # from inside the shirt up into the head
# shirt: one piece cut from a flat t-shirt outline (front view: neck, a straight shoulder line running into the
# sleeve, sleeve hem, armpit, side, hem), pushed out to full depth, then rounded after the remesh below.
# No separate torso / sleeve parts, so nothing can step where the shoulder meets the sleeve.
HALF = [(0.0, 0.612), (0.06, 0.603), (0.14, 0.572), (0.168, 0.55), (0.222, 0.432),
        (0.163, 0.40), (0.163, 0.30), (0.17, 0.22), (0.0, 0.22)]
outline = HALF[:-1] + [(-x, z) for x, z in reversed(HALF[1:-1])]
SHIRT_DEPTH = 0.125                                         # half depth at the centre of the chest
bm = bmesh.new()
face = bm.faces.new([bm.verts.new((x, -SHIRT_DEPTH, z)) for x, z in outline])
ext = bmesh.ops.extrude_face_region(bm, geom=[face])
bmesh.ops.translate(bm, vec=(0, 2 * SHIRT_DEPTH, 0), verts=[e for e in ext["geom"] if isinstance(e, bmesh.types.BMVert)])
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
me = bpy.data.meshes.new("Body")
bm.to_mesh(me)
bm.free()
torso = bpy.data.objects.new("Body", me)
bpy.context.collection.objects.link(torso)
add(torso, SHIRT)

for s in (1, -1):
    cyl("ShortsLeg", (0.075 * s, 0, 0.2), 0.075, 0.08, 0.07, PANTS).scale = (1, 0.85, 1)

# ---------------------------------------------------------------- arms (A-pose): sleeve out of the torso, arm, hand
ARM_ANGLE = math.radians(20)
for s in (1, -1):
    sh = Vector((0.125 * s, 0, 0.525))                  # shoulder joint; the arm comes out of the sleeve hem
    d = Vector((math.sin(ARM_ANGLE) * s, 0, -math.cos(ARM_ANGLE)))
    rot = (0, -ARM_ANGLE * s, 0)
    cyl("Arm", sh + d * 0.195, 0.04, 0.045, 0.15, SKIN, rot=rot, verts=10)
    hand = sh + d * 0.29
    sphere("Hand", hand, (0.048, 0.045, 0.055), SKIN, 10, 6)

n_upper = len(parts)
for o in parts[:n_head]:
    o.location.z += HEAD_UP
for o in parts[n_head:n_upper]:
    o.location.z += LEG_EXTRA

# shirt as one piece: torso, waist and both sleeves merged into a single volume, then the shoulder / armpit
# junctions softened so the sleeves flow out of the body instead of being stuck on
shirt_parts = [o for o in parts if o.name.startswith(("Body",))]
bpy.ops.object.select_all(action="DESELECT")
for o in shirt_parts:
    o.select_set(True)
bpy.context.view_layer.objects.active = shirt_parts[0]
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.ops.object.join()
shirt = bpy.context.object
shirt.name = "Shirt"
parts[:] = [o for o in parts if o not in shirt_parts or o == shirt]
shirt.data.remesh_voxel_size = 0.012
bpy.ops.object.voxel_remesh()
# round the slab: thinner toward the sides and much thinner out in the sleeves, so the cross-section is an
# oval and the sleeves are tubes, all from one continuous surface
for v in shirt.data.vertices:
    ax = abs(v.co.x)
    t_ = min(1.0, max(0.0, (ax - 0.11) / 0.11))
    t_ = t_ * t_ * (3 - 2 * t_)
    yn = v.co.y / SHIRT_DEPTH                                # -1 back .. 1 front, before squashing
    v.co.y *= (1 - 0.25 * (ax / 0.22) ** 2) * (1 - 0.45 * t_)
    v.co.x *= 1 - 0.2 * yn * yn                                # pull the sides in at front and back: oval, not a box
smooth = shirt.modifiers.new("Soften", "SMOOTH")
smooth.factor = 0.8
smooth.iterations = 14
dec = shirt.modifiers.new("Reduce", "DECIMATE")
dec.ratio = 0.22
shirt.data.materials.clear()
shirt.data.materials.append(SHIRT)

# ---------------------------------------------------------------- legs and shoes
for s in (1, -1):
    cyl("Leg", (0.07 * s, 0, 0.11 + LEG_EXTRA / 2), 0.045, 0.045, 0.12 + LEG_EXTRA, SKIN, verts=10)
    shoe("Shoe", (0.07 * s, -0.025, 0.04), (0.1, 0.16, 0.08))

# ---------------------------------------------------------------- join, sharp-by-angle shading, export
for o in parts:
    for m in list(o.modifiers):
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=m.name)
bpy.ops.object.select_all(action="DESELECT")
for o in parts:
    o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
body = bpy.context.object
body.name = "LowpolyCustomer"
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
bpy.ops.object.shade_smooth_by_angle(angle=SHARP_ANGLE)
print("triangles:", sum(len(p.vertices) - 2 for p in body.data.polygons))

os.makedirs(os.path.join(HERE, "fbx"), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "lowpoly_customer.blend"))
bpy.ops.export_scene.fbx(filepath=os.path.join(HERE, "fbx", "lowpoly_customer.fbx"), use_selection=True,
                         apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True, axis_forward="-Z", axis_up="Y")

# ---------------------------------------------------------------- previews (front + 3/4), joined into preview.png
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
shading = scene.display.shading
shading.light = "STUDIO"
shading.color_type = "MATERIAL"
shading.show_cavity = True
scene.world = bpy.data.worlds.new("W")
scene.world.color = (0.17, 0.27, 0.38)
cam_data = bpy.data.cameras.new("Cam")
cam_data.lens = 50
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
scene.render.resolution_x = 600
scene.render.resolution_y = 700
target = Vector((0, 0, 0.58))
for name, pos in (("front", (0, -2.2, 0.62)), ("side", (1.3, -1.75, 0.72)), ("back", (0, 2.2, 0.62))):
    cam.location = pos
    cam.rotation_euler = (target - Vector(pos)).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(HERE, "preview_" + name + ".png")
    bpy.ops.render.render(write_still=True)
print("done")
