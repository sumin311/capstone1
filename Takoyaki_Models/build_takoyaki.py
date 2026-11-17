# 타코야끼 관련 소품 3D 모델 생성 스크립트 (Blender 5.x)
# 실행: blender -b --factory-startup --python build_takoyaki.py -- <출력폴더>
# 결과: <출력폴더>/takoyaki_set.blend, fbx/*.fbx, preview.png
import bpy, bmesh, math, random, os, sys
from mathutils import Vector, Matrix, noise

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else os.path.dirname(os.path.abspath(__file__))
FBX_DIR = os.path.join(OUT, "fbx")
os.makedirs(FBX_DIR, exist_ok=True)
random.seed(7)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


# ---------------------------------------------------------------- 공통 유틸
def hx(h):
    h = h.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def mat(name, color, rough=0.5, metal=0.0, emit=None, coat=0.0):
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    try:
        m.use_nodes = True
    except Exception:
        pass
    b = m.node_tree.nodes.get("Principled BSDF")
    col = hx(color)
    b.inputs["Base Color"].default_value = (*col, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if coat:
        b.inputs["Coat Weight"].default_value = coat
    if emit:
        b.inputs["Emission Color"].default_value = (*hx(emit), 1)
        b.inputs["Emission Strength"].default_value = 4.0
    m.diffuse_color = (*col, 1)
    m.roughness = rough
    m.metallic = metal
    return m


M = {
    "body_red": mat("Grill_Red", "#D8473A", 0.35, coat=0.3),
    "plate": mat("Grill_Plate", "#2B2B30", 0.45, 0.3),
    "rubber": mat("Grill_Rubber", "#1C1C1F", 0.7),
    "knob": mat("Grill_Knob", "#232326", 0.4),
    "led_r": mat("LED_Red", "#FF3B2F", 0.3, emit="#FF3B2F"),
    "led_g": mat("LED_Green", "#3BE36A", 0.3, emit="#3BE36A"),
    "tako": mat("Takoyaki_Cooked", "#E3A04A", 0.55),
    "tako_dark": mat("Takoyaki_Browned", "#B8682C", 0.6),
    "tako_raw": mat("Takoyaki_Raw", "#F2DCA0", 0.5),
    "metal": mat("Bowl_Steel", "#C8CDD3", 0.28, 0.85),
    "batter": mat("Batter", "#EFD69A", 0.35),
    "wood": mat("Wood_Spoon", "#B9824A", 0.6),
    "octo_skin": mat("Octopus_Skin", "#C9566E", 0.45),
    "octo_flesh": mat("Octopus_Flesh", "#F5E8E6", 0.4),
    "onion": mat("GreenOnion", "#4FB33A", 0.45),
    "onion_in": mat("GreenOnion_Inner", "#A9DB6A", 0.45),
    "sauce_btl": mat("SauceBottle", "#4A2414", 0.22),
    "sauce_cap": mat("SauceBottle_Cap", "#D9262C", 0.35),
    "mayo_btl": mat("MayoBottle", "#F5EBCB", 0.3),
    "mayo_cap": mat("MayoBottle_Cap", "#EFE2B5", 0.4),
    "glaze": mat("Takoyaki_Sauce", "#5A2711", 0.15, coat=0.5),
    "mayo": mat("Mayonnaise", "#FBF3DA", 0.3),
    "boat": mat("PaperBoat", "#E3C592", 0.85),
    "bonito": mat("Bonito_Flakes", "#C98A62", 0.7),
    "aonori": mat("Aonori", "#2F6E2A", 0.8),
}


def active():
    return bpy.context.view_layer.objects.active


def select_only(objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def assign(o, *mats):
    o.data.materials.clear()
    for m in mats:
        o.data.materials.append(m)


def apply_all(o):
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
    o.modifiers.clear()
    old = o.data
    o.data = me
    me.name = o.name
    if old.users == 0:
        bpy.data.meshes.remove(old)


def shade(o, angle=35):
    select_only([o])
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(angle))
    except Exception:
        bpy.ops.object.shade_smooth()


def smooth(o):
    for p in o.data.polygons:
        p.use_smooth = True


def join(objs, name):
    select_only(objs)
    bpy.ops.object.join()
    o = active()
    o.name = name
    o.data.name = name
    return o


def rbox(name, size, loc, radius, m, seg=4):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o = active()
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bv = o.modifiers.new("bevel", "BEVEL")
    bv.width = radius
    bv.segments = seg
    bv.limit_method = "NONE"
    assign(o, m)
    return o


def cyl(name, r, depth, loc, m, rot=(0, 0, 0), verts=32):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc, rotation=rot)
    o = active()
    o.name = name
    assign(o, m)
    return o


def sphere(name, r, loc, m, seg=24, rings=14, scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, radius=r, location=loc)
    o = active()
    o.name = name
    o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    assign(o, m)
    smooth(o)
    return o


def lathe(name, prof, m, segs=40, loc=(0, 0, 0)):
    """(반지름, 높이) 프로파일을 Z축으로 회전시켜 메쉬 생성. 반지름 0은 극점."""
    bm = bmesh.new()
    rings = []
    for r, z in prof:
        if r < 1e-6:
            rings.append([bm.verts.new((0, 0, z))])
        else:
            rings.append([bm.verts.new((r * math.cos(2 * math.pi * i / segs),
                                        r * math.sin(2 * math.pi * i / segs), z)) for i in range(segs)])
    for a, b in zip(rings, rings[1:]):
        if len(a) == 1 and len(b) == 1:
            continue
        for i in range(segs):
            j = (i + 1) % segs
            if len(a) == 1:
                bm.faces.new((a[0], b[j], b[i]))
            elif len(b) == 1:
                bm.faces.new((a[i], a[j], b[0]))
            else:
                bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    scene.collection.objects.link(o)
    o.location = loc
    assign(o, m)
    smooth(o)
    return o


def bake_loc(o):
    """오브젝트 위치를 메쉬에 굽기 (join 전에 사용)"""
    o.data.transform(o.matrix_world)
    o.matrix_world = Matrix.Identity(4)


# ---------------------------------------------------------------- 파츠
_TAKO_MAT = []


def tako_textured_mat():
    """구운 자국이 있는 타코야끼 텍스처(구면 노이즈, 이음새 없음)를 PNG로 만들어 머티리얼에 연결"""
    if _TAKO_MAT:
        return _TAKO_MAT[0]
    W, H = 512, 256
    gold, brown, deep = (0.93, 0.66, 0.30), (0.74, 0.42, 0.17), (0.55, 0.28, 0.11)
    px = [0.0] * (W * H * 4)
    for j in range(H):
        lat = ((j + 0.5) / H - 0.5) * math.pi
        cl, sl = math.cos(lat), math.sin(lat)
        for i in range(W):
            lon = (i + 0.5) / W * 2 * math.pi
            d = Vector((cl * math.cos(lon), cl * math.sin(lon), sl))
            n = noise.noise(d * 2.2) + 0.5 * noise.noise(d * 5.5 + Vector((3, 1, 2))) + 0.22 * noise.noise(d * 16)
            t = min(1, max(0, (n + 0.05) / 0.6))
            t = t * t * (3 - 2 * t)
            col = [g + (b - g) * t for g, b in zip(gold, brown)]
            if n > 0.55:
                k = min(1, (n - 0.55) / 0.25)
                col = [c + (dd - c) * k for c, dd in zip(col, deep)]
            k = (j * W + i) * 4
            px[k:k + 4] = (*col, 1.0)
    img = bpy.data.images.new("Takoyaki_Cooked", W, H)
    img.pixels = px
    img.filepath_raw = os.path.join(FBX_DIR, "Takoyaki_Cooked.png")
    img.file_format = "PNG"
    img.save()
    m = M["tako"]
    nt = m.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nt.links.new(tex.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    _TAKO_MAT.append(m)
    return m


def takoyaki_ball(name, r, loc=(0, 0, 0), seed=0, raw=False):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=20, radius=r, location=loc)
    o = active()
    o.name = name
    off = Vector((seed * 1.73, seed * 3.11, seed * 0.71))
    for v in o.data.vertices:
        n = v.co.normalized()
        d = noise.noise(n * 2.2 + off) * (0.03 if raw else 0.06)
        v.co = n * r * (1 + d)
        v.co.z *= 0.93
    assign(o, M["tako_raw"] if raw else tako_textured_mat())
    smooth(o)
    return o


CUP_POS = [(x, y) for y in (0.048, -0.048) for x in (-0.12, -0.04, 0.04, 0.12)]
PLATE_TOP = 0.109
CUP_R = 0.022
CUP_Z = PLATE_TOP + 0.004


def grill(name):
    parts = []
    body = rbox(name + "_body", (0.42, 0.26, 0.07), (0, 0, 0.05), 0.022, M["body_red"], 5)
    parts.append(body)
    # 상판 + 8구 홈 (불리언)
    plate = rbox(name + "_plate", (0.38, 0.22, 0.028), (0, 0, PLATE_TOP - 0.014), 0.008, M["plate"], 3)
    cutters = [sphere("cut", CUP_R, (x, y, CUP_Z), M["plate"], 32, 16) for x, y in CUP_POS]
    cutter = join(cutters, "cutter")
    bo = plate.modifiers.new("cups", "BOOLEAN")
    bo.operation = "DIFFERENCE"
    bo.object = cutter
    try:
        bo.solver = "EXACT"
    except Exception:
        pass
    apply_all(plate)
    bpy.data.objects.remove(cutter)
    parts.append(plate)
    # 상판 테두리 프레임
    frame = rbox(name + "_frame", (0.40, 0.24, 0.012), (0, 0, 0.088), 0.006, M["plate"], 3)
    parts.append(frame)
    # 다이얼, LED, 다리
    parts.append(cyl("knob", 0.017, 0.014, (0.15, -0.137, 0.05), M["knob"], (math.radians(90), 0, 0)))
    bev = parts[-1].modifiers.new("bv", "BEVEL"); bev.width = 0.003; bev.segments = 2
    parts.append(rbox("knob_grip", (0.006, 0.01, 0.026), (0.15, -0.146, 0.05), 0.002, M["knob"], 2))
    parts.append(sphere("led_r", 0.0035, (0.095, -0.1295, 0.058), M["led_r"], 12, 8))
    parts.append(sphere("led_g", 0.0035, (0.095, -0.1295, 0.044), M["led_g"], 12, 8))
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts.append(cyl("foot", 0.013, 0.016, (sx * 0.17, sy * 0.095, 0.008), M["rubber"], verts=20))
    for p in parts:
        if p.modifiers:
            apply_all(p)
        bake_loc(p)
    o = join(parts, name)
    shade(o, 40)
    return o


def bowl(name, R, D, t=0.0035, segs=48):
    a0 = math.asin(0.45)
    prof = [(0, 0), (R * 0.4, 0), (R * 0.44, 0.002)]
    n = 12
    for k in range(n + 1):
        a = a0 + (math.pi / 2 - a0) * k / n
        prof.append((R * math.sin(a), 0.002 + D * (math.cos(a0) - math.cos(a)) / math.cos(a0) * 1.0))
    top = prof[-1][1]
    prof += [(R + 0.0015, top + 0.0012), (R - t * 0.5, top + 0.0022), (R - t, top)]
    Ri = R - t
    for k in range(n, -1, -1):
        a = a0 + (math.pi / 2 - a0) * k / n
        prof.append((Ri * math.sin(a), t + 0.002 + (D - t) * (math.cos(a0) - math.cos(a)) / math.cos(a0)))
    prof.append((0, t + 0.002))
    o = lathe(name, prof, M["metal"], segs)
    return o, (lambda z: Ri * math.sin(math.acos(max(-1, min(1, math.cos(a0) - (z - t - 0.002) / (D - t) * math.cos(a0)))))), top


def fill_disc(name, rfun, z, m, dome=0.004):
    r = rfun(z) - 0.0005
    prof = [(0, z - 0.004), (r, z - 0.004), (r, z)]
    for k in range(1, 6):
        f = k / 5
        prof.append((r * (1 - f), z + dome * math.sin(f * math.pi / 2)))
    return lathe(name, prof, m, 48)


def batter_bowl(name):
    R, D = 0.09, 0.075
    b, rfun, top = bowl(name + "_bowl", R, D)
    level = top * 0.62
    batter = fill_disc("batter", rfun, level, M["batter"], 0.003)
    # 나무 숟가락 (손잡이 + 머리)
    tilt = math.radians(32)
    d = Vector((math.sin(tilt), 0, math.cos(tilt)))
    p0 = Vector((-0.025, 0.0, level - 0.01))
    L = 0.17
    handle = cyl("spoon_handle", 0.0055, L, p0 + d * (L / 2), M["wood"], (0, tilt, 0), 16)
    bv = handle.modifiers.new("bv", "BEVEL"); bv.width = 0.002; bv.segments = 2
    head = sphere("spoon_head", 0.02, p0, M["wood"], 20, 12, (1.0, 0.8, 0.3))
    head.rotation_euler = (0, tilt, 0)
    parts = [b, batter, handle, head]
    for p in parts:
        if p.modifiers:
            apply_all(p)
        bake_loc(p)
    o = join(parts, name)
    shade(o, 50)
    return o


def mound_positions(n, rfun, level, spread=0.82, height=0.014, seed=0):
    rnd = random.Random(seed)
    rr = rfun(level) * spread
    pts = []
    for i in range(n):
        a = rnd.uniform(0, 2 * math.pi)
        d = rr * math.sqrt(rnd.uniform(0, 1))
        z = level + height * (1 - (d / rr) ** 2) + rnd.uniform(0, 0.004)
        pts.append(Vector((d * math.cos(a), d * math.sin(a), z)))
    return pts, rnd


def octopus_bowl(name):
    R, D = 0.065, 0.05
    b, rfun, top = bowl(name + "_bowl", R, D)
    level = top * 0.45
    parts = [b]
    pts, rnd = mound_positions(22, rfun, level, 0.8, 0.02, 3)
    for i, p in enumerate(pts):
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=1, location=p)
        o = active()
        s = rnd.uniform(0.009, 0.013)
        o.scale = (s * rnd.uniform(0.9, 1.3), s * rnd.uniform(0.8, 1.1), s * rnd.uniform(0.7, 0.95))
        o.rotation_euler = (rnd.uniform(0, 6.3), rnd.uniform(0, 6.3), rnd.uniform(0, 6.3))
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        off = Vector((i * 2.1, i * 0.7, i * 1.3))
        for v in o.data.vertices:
            v.co *= 1 + noise.noise(v.co * 160 + off) * 0.18
        assign(o, M["octo_flesh"], M["octo_skin"])
        dirv = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(0.2, 1))).normalized()
        for poly in o.data.polygons:
            if poly.normal.dot(dirv) > 0.15:
                poly.material_index = 1
        smooth(o)
        bake_loc(o)
        parts.append(o)
    o = join(parts, name)
    shade(o, 50)
    return o


def onion_bowl(name):
    R, D = 0.055, 0.045
    b, rfun, top = bowl(name + "_bowl", R, D)
    level = top * 0.45
    parts = [b]
    pts, rnd = mound_positions(60, rfun, level, 0.85, 0.016, 5)
    for p in pts:
        rr = rnd.uniform(0.0045, 0.0065)
        bpy.ops.mesh.primitive_torus_add(major_radius=rr, minor_radius=0.0018, major_segments=16,
                                         minor_segments=6, location=p,
                                         rotation=(rnd.uniform(-0.9, 0.9), rnd.uniform(-0.9, 0.9), 0))
        o = active()
        o.scale = (1, 1, 1.6)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        assign(o, M["onion"], M["onion_in"])
        c = Vector(p)
        for poly in o.data.polygons:
            if (poly.center - c).length < rr * 0.98:
                poly.material_index = 1
        smooth(o)
        bake_loc(o)
        parts.append(o)
    o = join(parts, name)
    shade(o, 50)
    return o


def squeeze_bottle(name, m_body, m_cap, H=0.15, r=0.03):
    body = lathe(name + "_body", [
        (0, 0), (r - 0.005, 0), (r - 0.001, 0.0015), (r, 0.006),
        (r, H * 0.55), (r * 1.0, H - 0.03), (r * 0.94, H - 0.018), (r * 0.78, H - 0.008),
        (r * 0.56, H - 0.002), (r * 0.5, H), (0, H)], m_body, 40)
    cap = lathe(name + "_cap", [
        (0, H - 0.001), (r * 0.56, H - 0.001), (r * 0.6, H + 0.002), (r * 0.6, H + 0.016),
        (r * 0.56, H + 0.019), (r * 0.36, H + 0.022), (r * 0.26, H + 0.03),
        (0.0045, H + 0.058), (0.0028, H + 0.064), (0, H + 0.065)], m_cap, 32)
    o = join([body, cap], name)
    shade(o, 35)
    return o


BOAT_TOP = [(0.105, 0), (0.075, 0.06), (-0.075, 0.06), (-0.105, 0), (-0.075, -0.06), (0.075, -0.06)]
BOAT_BOT = [(0.078, 0), (0.058, 0.04), (-0.058, 0.04), (-0.078, 0), (-0.058, -0.04), (0.058, -0.04)]


def boat_tray(name, h=0.03):
    bm = bmesh.new()
    top = [bm.verts.new((x, y, h)) for x, y in BOAT_TOP]
    bot = [bm.verts.new((x, y, 0.0)) for x, y in BOAT_BOT]
    bm.faces.new(bot)
    for i in range(6):
        j = (i + 1) % 6
        bm.faces.new((bot[i], bot[j], top[j], top[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:  # 바깥을 향하게
        if f.calc_center_median().z > 0.001 and f.normal.dot(Vector((f.calc_center_median().x, f.calc_center_median().y, 0))) < 0:
            f.normal_flip()
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    scene.collection.objects.link(o)
    so = o.modifiers.new("thick", "SOLIDIFY")
    so.thickness = 0.0018
    so.offset = 1.0
    bv = o.modifiers.new("bv", "BEVEL")
    bv.width = 0.0008
    bv.segments = 2
    bv.limit_method = "ANGLE"
    assign(o, M["boat"])
    apply_all(o)
    return o


def glaze_cap(name, r, center, seed):
    o = sphere(name, r * 1.05, center, M["glaze"], 32, 20, (1, 1, 0.95))
    bm = bmesh.new()
    bm.from_mesh(o.data)
    c = Vector(center)
    off = Vector((seed, seed * 2.3, 0.5))
    kill = []
    for v in bm.verts:
        rel = v.co - c
        n = rel.normalized()
        thr = -0.3 * r + noise.noise(n * 3 + off) * 0.3 * r
        if rel.z < thr:
            kill.append(v)
    bmesh.ops.delete(bm, geom=kill, context="VERTS")
    bm.to_mesh(o.data)
    bm.free()
    smooth(o)
    return o


def takoyaki_boat(name):
    parts = [boat_tray(name + "_tray")]
    r = 0.021
    balls = [(x, y) for y in (0.024, -0.024) for x in (-0.045, 0.0, 0.045)]
    centers = []
    for i, (x, y) in enumerate(balls):
        c = (x, y, 0.0018 + r * 0.93)
        centers.append(Vector(c))
        b = takoyaki_ball("ball", r, c, seed=i + 10)
        bake_loc(b)
        parts.append(b)
        parts.append(glaze_cap("glaze", r, c, i))

    def h(x, y):
        # 공 사이가 끊기지 않도록 넓은 포락면 위를 따라감
        best = 0.024
        R = r * 1.32
        for c in centers:
            d2 = (x - c.x) ** 2 + (y - c.y) ** 2
            if d2 < R * R:
                best = max(best, c.z + (r * 0.93 + 0.0022) * math.sqrt(1 - d2 / (R * R)))
        return best

    # 마요네즈 지그재그
    cu = bpy.data.curves.new("mayo", "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = 0.0019
    cu.bevel_resolution = 3
    sp = cu.splines.new("POLY")
    pts = []
    legs = 9
    for k in range(legs):
        xa = -0.062 + 0.124 * k / legs
        xb = -0.062 + 0.124 * (k + 1) / legs
        ya, yb = (0.03, -0.03) if k % 2 == 0 else (-0.03, 0.03)
        for s in range(16):
            f = s / 16
            x, y = xa + (xb - xa) * f, ya + (yb - ya) * f
            pts.append((x, y, h(x, y)))
    pts.append((0.062, 0.03 if legs % 2 == 0 else -0.03, 0.03))
    pts[-1] = (pts[-1][0], pts[-1][1], h(pts[-1][0], pts[-1][1]))
    sp.points.add(len(pts) - 1)
    for p, (x, y, z) in zip(sp.points, pts):
        p.co = (x, y, z, 1)
    mo = bpy.data.objects.new("mayo", cu)
    scene.collection.objects.link(mo)
    mo.data.materials.append(M["mayo"])
    select_only([mo])
    bpy.ops.object.convert(target="MESH")
    mo = active()
    smooth(mo)
    parts.append(mo)

    # 가쓰오부시 + 아오노리
    rnd = random.Random(11)
    bm = bmesh.new()
    for i in range(40):
        x, y = rnd.uniform(-0.06, 0.06), rnd.uniform(-0.035, 0.035)
        z = h(x, y) + 0.0012
        ret = bmesh.ops.create_grid(bm, x_segments=3, y_segments=2, size=1.0)
        vs = ret["verts"]
        w, l = rnd.uniform(0.0025, 0.004), rnd.uniform(0.004, 0.0065)
        bend = rnd.uniform(0.4, 0.9)
        for v in vs:
            v.co = Vector((v.co.x * l, v.co.y * w, (v.co.x ** 2) * bend * 0.003))
        rot = (Matrix.Rotation(rnd.uniform(0, 6.28), 4, "Z") @
               Matrix.Rotation(rnd.uniform(-0.35, 0.35), 4, "X"))
        bmesh.ops.transform(bm, matrix=Matrix.Translation((x, y, z)) @ rot, verts=vs)
    me = bpy.data.meshes.new("bonito")
    bm.to_mesh(me)
    bm.free()
    bo = bpy.data.objects.new("bonito", me)
    scene.collection.objects.link(bo)
    assign(bo, M["bonito"])
    so = bo.modifiers.new("thick", "SOLIDIFY")
    so.thickness = 0.0004
    apply_all(bo)
    smooth(bo)
    parts.append(bo)

    bm = bmesh.new()
    for i in range(140):
        x, y = rnd.uniform(-0.068, 0.068), rnd.uniform(-0.044, 0.044)
        z = h(x, y) + 0.0006
        if z < 0.021:
            continue
        ret = bmesh.ops.create_cube(bm, size=rnd.uniform(0.0011, 0.0018))
        bmesh.ops.rotate(bm, verts=ret["verts"], cent=(0, 0, 0),
                         matrix=Matrix.Rotation(rnd.uniform(0, 6.28), 3, "Z"))
        bmesh.ops.translate(bm, verts=ret["verts"], vec=(x, y, z))
    me = bpy.data.meshes.new("aonori")
    bm.to_mesh(me)
    bm.free()
    ao = bpy.data.objects.new("aonori", me)
    scene.collection.objects.link(ao)
    assign(ao, M["aonori"])
    parts.append(ao)

    o = join(parts, name)
    shade(o, 60)
    return o


def grill_cooking(name):
    g = grill(name)
    for i, (x, y) in enumerate(CUP_POS):
        b = takoyaki_ball(f"{name}_ball_{i + 1}", 0.0205, (0, 0, 0), seed=i)
        b.rotation_euler = (random.uniform(-0.6, 0.6), random.uniform(-0.6, 0.6), random.uniform(0, 6.28))
        b.location = (x, y, CUP_Z + 0.003)
        b.parent = g
    return g


# ---------------------------------------------------------------- 빌드 & 내보내기
def export(root, fname):
    objs = [root] + list(root.children_recursive)
    select_only(objs)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(FBX_DIR, fname + ".fbx"),
        use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=True, object_types={"MESH"}, mesh_smooth_type="FACE",
        add_leaf_bones=False, path_mode="AUTO")


ITEMS = [
    # (빌더, 이름, 배치 위치)
    (lambda: grill("Takoyaki_Grill"), "Takoyaki_Grill", (-0.62, 0.32)),
    (lambda: grill_cooking("Takoyaki_Grill_Cooking"), "Takoyaki_Grill_Cooking", (-0.13, 0.32)),
    (lambda: takoyaki_boat("Takoyaki_Boat"), "Takoyaki_Boat", (0.33, 0.32)),
    (lambda: takoyaki_ball("Takoyaki_Ball", 0.0205, (0, 0, 0.019), seed=42), "Takoyaki_Ball", (0.53, 0.30)),
    (lambda: takoyaki_ball("Takoyaki_Ball_Raw", 0.0205, (0, 0, 0.019), seed=7, raw=True), "Takoyaki_Ball_Raw", (0.61, 0.30)),
    (lambda: batter_bowl("Batter_Bowl"), "Batter_Bowl", (-0.62, 0.0)),
    (lambda: octopus_bowl("Octopus_Bowl"), "Octopus_Bowl", (-0.40, 0.0)),
    (lambda: onion_bowl("GreenOnion_Bowl"), "GreenOnion_Bowl", (-0.22, 0.0)),
    (lambda: squeeze_bottle("Takoyaki_Sauce_Bottle", M["sauce_btl"], M["sauce_cap"]), "Takoyaki_Sauce_Bottle", (-0.06, 0.0)),
    (lambda: squeeze_bottle("Mayo_Bottle", M["mayo_btl"], M["mayo_cap"]), "Mayo_Bottle", (0.05, 0.0)),
]

for build, nm, pos in ITEMS:
    root = build()
    if root.location.length > 0 and not root.children:
        bake_loc(root)
    export(root, nm)
    root.location = (pos[0], pos[1], 0)
    print("built", nm, len(root.data.polygons), "faces")

# ---------------------------------------------------------------- 미리보기 렌더
for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
    try:
        scene.render.engine = eng
        break
    except Exception:
        pass
try:
    scene.view_settings.view_transform = "Standard"
except Exception:
    pass
scene.render.resolution_x, scene.render.resolution_y = 1800, 1000
scene.render.film_transparent = False

world = bpy.data.worlds.new("World")
scene.world = world
try:
    world.use_nodes = True
except Exception:
    pass
bg = world.node_tree.nodes.get("Background")
bg.inputs["Color"].default_value = (*hx("#5A4A44"), 1)
bg.inputs["Strength"].default_value = 0.9

bpy.ops.mesh.primitive_plane_add(size=6, location=(0, 0, -0.0005))
floor = active()
floor.name = "_PreviewFloor"
assign(floor, mat("Floor", "#6E5C55", 0.8))

bpy.ops.object.light_add(type="SUN", rotation=(math.radians(45), math.radians(10), math.radians(-30)))
active().data.energy = 3.5
active().data.angle = math.radians(8)
bpy.ops.object.light_add(type="AREA", location=(0, -1.0, 1.2))
fill = active()
fill.data.energy = 120
fill.data.size = 2.0
fill.rotation_euler = (math.radians(40), 0, 0)

bpy.ops.object.camera_add(location=(0, -1.25, 1.05))
cam = active()
cam.data.type = "ORTHO"
cam.data.ortho_scale = 1.42
cam.rotation_euler = (math.radians(55), 0, 0)
cam.data.ortho_scale = 1.62
cam.location = (-0.1, -1.47, 1.2)
scene.camera = cam

scene.render.filepath = os.path.join(OUT, "preview.png")
bpy.ops.render.render(write_still=True)

# 근접 확인용 렌더 (보트, 그릴)
for nm, (tx, ty), sc in (("boat", (0.33, 0.32), 0.3), ("grill", (-0.13, 0.32), 0.5), ("bowls", (-0.42, 0.0), 0.55)):
    cam.data.ortho_scale = sc
    cam.location = (tx, ty - 1.64, 1.2)
    scene.render.resolution_x, scene.render.resolution_y = 1000, 700
    scene.render.filepath = os.path.join(OUT, "_check_" + nm + ".png")
    bpy.ops.render.render(write_still=True)
cam.data.ortho_scale = 1.62
cam.location = (-0.1, -1.47, 1.2)
scene.render.resolution_x, scene.render.resolution_y = 1800, 1000

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "takoyaki_set.blend"))
print("DONE")
