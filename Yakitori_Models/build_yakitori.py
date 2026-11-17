# 꼬치구이(야키토리) 관련 소품 3D 모델 생성 스크립트 (Blender 5.x)
# 실행: blender -b --factory-startup --python build_yakitori.py -- <출력폴더>
# 결과: <출력폴더>/yakitori_set.blend, fbx/*.fbx (+라벨 텍스처), preview.png
import bpy, bmesh, math, random, os, sys
from mathutils import Vector, Matrix, noise

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else os.path.dirname(os.path.abspath(__file__))
CHECK = "--check" in argv
FBX_DIR = os.path.join(OUT, "fbx")
os.makedirs(FBX_DIR, exist_ok=True)
random.seed(5)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


# ---------------------------------------------------------------- 공통 유틸
def hx(h):
    h = h.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def srgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


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
    "led_r": mat("LED_Red", "#FF3B2F", 0.3, emit="#FF3B2F"),
    "led_g": mat("LED_Green", "#3BE36A", 0.3, emit="#3BE36A"),
    "metal": mat("Bowl_Steel", "#C8CDD3", 0.28, 0.85),
}


def active():
    return bpy.context.view_layer.objects.active


def select_only(objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def assign(o, *mats):
    # 슬롯을 비우면 면의 material_index가 0으로 초기화되므로 보존했다가 복원
    idx = [p.material_index for p in o.data.polygons]
    o.data.materials.clear()
    for m in mats:
        o.data.materials.append(m)
    for p, i in zip(o.data.polygons, idx):
        p.material_index = min(i, len(mats) - 1)


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


def bake_loc(o):
    # matrix_world는 depsgraph 갱신 전엔 옛 값이라 matrix_basis 사용
    o.data.transform(o.matrix_basis if o.parent is None else o.matrix_world)
    o.matrix_world = Matrix.Identity(4)


def finalize(parts):
    for p in parts:
        if p.modifiers:
            apply_all(p)
        bake_loc(p)


def mesh_obj(name, bm):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    scene.collection.objects.link(o)
    return o


def rbox(name, size, loc, radius, m, seg=4, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
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
    o = mesh_obj(name, bm)
    o.location = loc
    assign(o, m)
    smooth(o)
    return o


def bowl(name, R, D, t=0.0035, segs=48):
    a0 = math.asin(0.45)
    prof = [(0, 0), (R * 0.4, 0), (R * 0.44, 0.002)]
    n = 12
    for k in range(n + 1):
        a = a0 + (math.pi / 2 - a0) * k / n
        prof.append((R * math.sin(a), 0.002 + D * (math.cos(a0) - math.cos(a)) / math.cos(a0)))
    top = prof[-1][1]
    prof += [(R + 0.0015, top + 0.0012), (R - t * 0.5, top + 0.0022), (R - t, top)]
    Ri = R - t
    for k in range(n, -1, -1):
        a = a0 + (math.pi / 2 - a0) * k / n
        prof.append((Ri * math.sin(a), t + 0.002 + (D - t) * (math.cos(a0) - math.cos(a)) / math.cos(a0)))
    prof.append((0, t + 0.002))
    o = lathe(name, prof, M["metal"], segs)
    return o, top


def make_tex_mat(name, W, H, fn, rough=0.4):
    """fn(u, v) -> sRGB (r,g,b). PNG로 저장하고 Base Color에 연결한 머티리얼 반환"""
    px = [0.0] * (W * H * 4)
    for j in range(H):
        v = (j + 0.5) / H
        for i in range(W):
            c = fn((i + 0.5) / W, v)
            k = (j * W + i) * 4
            px[k:k + 4] = (c[0], c[1], c[2], 1.0)
    img = bpy.data.images.new(name, W, H)
    img.pixels = px
    img.filepath_raw = os.path.join(FBX_DIR, name + ".png")
    img.file_format = "PNG"
    img.save()
    m = mat(name, "#FFFFFF", rough)
    nt = m.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nt.links.new(tex.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    return m


def mix(a, b, t):
    t = max(0.0, min(1.0, t))
    return tuple(x + (y - x) * t for x, y in zip(a, b))


def cover(d, aa=0.006):
    """부호 거리 d(<0 안쪽)를 0~1 커버리지로 (가장자리 부드럽게)"""
    return max(0.0, min(1.0, 0.5 - d / aa))


# ---------------------------------------------------------------- 꼬치구이 머티리얼
M.update({
    "body_red": mat("Grill_Red", "#D8473A", 0.35, coat=0.3),
    "plate": mat("Grill_Plate", "#2B2B30", 0.45, 0.3),
    "rubber": mat("Grill_Rubber", "#1C1C1F", 0.7),
    "knob": mat("Grill_Knob", "#232326", 0.4),
    "wire": mat("Grill_Wire", "#A7ACB2", 0.3, 0.9),
    "heater": mat("Grill_Heater", "#FF6A1F", 0.4, emit="#FF5A12"),
    "bamboo": mat("Bamboo_Skewer", "#D8B47A", 0.6),
    "chicken": mat("Chicken_Grilled", "#B9662E", 0.3, coat=0.4),
    "chicken_char": mat("Chicken_Charred", "#7A3714", 0.35, coat=0.4),
    "chicken_raw": mat("Chicken_Raw", "#EC8C88", 0.4),
    "chicken_fat": mat("Chicken_Fat", "#F5C3B8", 0.4),
    "negi": mat("Negi_Grilled", "#6E9E3F", 0.4),
    "negi_raw": mat("Negi_Green", "#5DAE3B", 0.4),
    "negi_cap": mat("Negi_Cut", "#D6E8A6", 0.45),
    "negi_white": mat("Negi_White", "#F2F0E2", 0.4),
    "negi_white_cap": mat("Negi_White_Cut", "#E4EBC2", 0.45),
    "negi_mid": mat("Negi_Light", "#A9D46E", 0.45),
    "root": mat("Negi_Root", "#E8DCC0", 0.7),
    "slate": mat("Slate_Plate", "#2E2E33", 0.6),
    "tare_btl": mat("TareBottle", "#3A1D0E", 0.12, coat=0.5),
    "tare_cap": mat("TareBottle_Cap", "#24130A", 0.35),
    "shaker_cap": mat("Shichimi_Cap", "#D92A2A", 0.35),
    "spice": mat("Shichimi_Powder", "#D3602A", 0.8),
    "spice_dark": mat("Shichimi_Flakes", "#8E2A12", 0.8),
})


def glass_mat():
    m = mat("Shichimi_Glass", "#E8F0F2", 0.05)
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Alpha"].default_value = 0.28
    for attr, val in (("surface_render_method", "BLENDED"), ("blend_method", "BLEND")):
        try:
            setattr(m, attr, val)
        except Exception:
            pass
    return m


# ---------------------------------------------------------------- 꼬치 파츠
WIDTH = {"chicken": 0.027, "chicken_raw": 0.027, "negi": 0.019, "negi_raw": 0.019, "negi_white": 0.02}


def chicken_piece(name, loc, seed, raw=False):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=3, radius=1)
    o = active()
    o.name = name
    rnd = random.Random(seed)
    sx, sy, sz = 0.013, rnd.uniform(0.0145, 0.0165), rnd.uniform(0.0125, 0.014)
    off = Vector((seed * 1.3, seed * 0.7, seed * 2.1))
    for v in o.data.vertices:
        n = v.co.normalized()
        k = 1 + noise.noise(n * 1.8 + off) * 0.16
        # 꼬치 방향(x)으로는 납작하게 잘린 모양
        v.co = Vector((max(-0.85, min(0.85, n.x)) * sx * k, n.y * sy * k, n.z * sz * k))
    assign(o, M["chicken_raw"] if raw else M["chicken"], M["chicken_fat"] if raw else M["chicken_char"])
    thr = 0.3 if raw else 0.28
    for p in o.data.polygons:
        if noise.noise(p.center * 140 + off + Vector((4, 4, 4))) > thr:
            p.material_index = 1
    smooth(o)
    o.location = loc
    return o


def negi_piece(name, loc, kind):
    r, w = (0.0118, WIDTH[kind]) if kind == "negi_white" else (0.0108, WIDTH[kind])
    side = {"negi": M["negi"], "negi_raw": M["negi_raw"], "negi_white": M["negi_white"]}[kind]
    cap = M["negi_white_cap"] if kind == "negi_white" else M["negi_cap"]
    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=r, depth=w, location=(0, 0, 0),
                                        rotation=(0, math.radians(90), 0), end_fill_type="NGON")
    o = active()
    o.name = name
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    bm = bmesh.new()
    bm.from_mesh(o.data)
    caps = [f for f in bm.faces if abs(f.normal.x) > 0.9]
    ret = bmesh.ops.inset_individual(bm, faces=caps, thickness=r * 0.3, depth=0)
    ring1 = set(ret["faces"])
    ret2 = bmesh.ops.inset_individual(bm, faces=caps, thickness=r * 0.3, depth=-r * 0.08)
    for f in bm.faces:
        f.material_index = 0
    for f in caps:
        f.material_index = 1
    for f in ring1:
        f.material_index = 0
    for f in ret2["faces"]:
        f.material_index = 2
    bm.to_mesh(o.data)
    bm.free()
    assign(o, side, cap, M["negi_mid"] if kind != "negi_white" else cap)
    bv = o.modifiers.new("bv", "BEVEL")
    bv.width = 0.0012
    bv.segments = 2
    bv.limit_method = "ANGLE"
    apply_all(o)
    shade(o, 40)
    o.location = loc
    return o


def skewer(name, seq, seed=0):
    L = 0.21
    parts = [cyl("stick", 0.0022, L, (0, 0, 0), M["bamboo"], (0, math.radians(90), 0), 12)]
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.0022, radius2=0.0002, depth=0.012,
                                    location=(L / 2 + 0.006, 0, 0), rotation=(0, math.radians(90), 0))
    tip = active()
    assign(tip, M["bamboo"])
    parts.append(tip)
    x = -0.04
    for i, kind in enumerate(seq):
        w = WIDTH[kind]
        c = (x + w / 2, 0, 0)
        if kind.startswith("chicken"):
            parts.append(chicken_piece(f"p{i}", c, seed * 10 + i, kind == "chicken_raw"))
        else:
            parts.append(negi_piece(f"p{i}", c, kind))
        x += w + 0.0008
    finalize(parts)
    o = join(parts, name)
    zmin = min(v.co.z for v in o.data.vertices)
    o.data.transform(Matrix.Translation((0, 0, -zmin)))
    return o


COOKED = ["chicken", "negi", "chicken", "negi", "chicken"]
RAW_MIX = ["chicken_raw", "negi_raw", "chicken_raw", "negi_raw", "chicken_raw"]


# ---------------------------------------------------------------- 그릴
GRATE_TOP = 0.0846


def yakitori_grill(name):
    parts = [rbox(name + "_body", (0.40, 0.22, 0.06), (0, 0, 0.04), 0.02, M["body_red"], 5)]
    # 테두리 틀
    for (sx, sy, x, y) in ((0.40, 0.014, 0, 0.103), (0.40, 0.014, 0, -0.103),
                           (0.014, 0.22, 0.193, 0), (0.014, 0.22, -0.193, 0)):
        parts.append(rbox("rim", (sx, sy, 0.016), (x, y, 0.076), 0.004, M["plate"], 2))
    parts.append(rbox("pan", (0.375, 0.195, 0.006), (0, 0, 0.071), 0.002, M["plate"], 2))
    for y in (-0.055, 0.0, 0.055):  # 열선
        parts.append(cyl("heater", 0.0028, 0.34, (0, y, 0.0765), M["heater"], (0, math.radians(90), 0), 12))
    for k in range(18):  # 석쇠
        x = -0.17 + 0.02 * k
        parts.append(cyl("wire", 0.0016, 0.196, (x, 0, GRATE_TOP - 0.0016), M["wire"], (math.radians(90), 0, 0), 8))
    for y in (-0.07, 0.07):
        parts.append(cyl("wire_x", 0.0018, 0.38, (0, y, GRATE_TOP - 0.0048), M["wire"], (0, math.radians(90), 0), 8))
    parts.append(cyl("knob", 0.017, 0.014, (0.15, -0.117, 0.04), M["knob"], (math.radians(90), 0, 0)))
    parts.append(rbox("knob_grip", (0.006, 0.01, 0.026), (0.15, -0.126, 0.04), 0.002, M["knob"], 2))
    parts.append(sphere("led_r", 0.0035, (0.095, -0.1095, 0.048), M["led_r"], 12, 8))
    parts.append(sphere("led_g", 0.0035, (0.095, -0.1095, 0.034), M["led_g"], 12, 8))
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts.append(cyl("foot", 0.013, 0.016, (sx * 0.17, sy * 0.08, 0.008), M["rubber"], verts=20))
    finalize(parts)
    o = join(parts, name)
    shade(o, 40)
    return o


def yakitori_grill_cooking(name):
    g = yakitori_grill(name)
    for i in range(5):
        s = skewer(f"{name}_Skewer_{i + 1}", COOKED, seed=i + 1)
        s.rotation_euler = (0, 0, math.radians(90))
        s.location = (-0.12 + 0.06 * i, 0.0, GRATE_TOP)
        s.parent = g
    return g


def yakitori_plate(name):
    parts = [rbox(name + "_slate", (0.30, 0.17, 0.012), (0, 0, 0.006), 0.004, M["slate"], 3)]
    for i in range(4):
        s = skewer(f"s{i}", COOKED, seed=20 + i)
        s.data.transform(Matrix.Translation((0.005 * (i % 2), -0.054 + 0.036 * i, 0.012)) @
                         Matrix.Rotation(math.radians(-10), 4, "Z"))
        parts.append(s)
    finalize(parts)
    o = join(parts, name)
    return o


# ---------------------------------------------------------------- 재료
def chicken_bowl(name):
    b, top = bowl(name + "_bowl", 0.062, 0.046)
    parts = [b]
    rnd = random.Random(9)
    level = top * 0.45
    for i in range(16):
        a = rnd.uniform(0, 2 * math.pi)
        d = 0.034 * math.sqrt(rnd.uniform(0, 1))
        z = level + 0.012 * (1 - (d / 0.034) ** 2) + rnd.uniform(0, 0.004)
        p = chicken_piece(f"c{i}", (0, 0, 0), 100 + i, raw=True)
        p.data.transform(Matrix.Rotation(rnd.uniform(0, 6.3), 4, "Z") @ Matrix.Rotation(rnd.uniform(-0.6, 0.6), 4, "X"))
        p.location = (d * math.cos(a), d * math.sin(a), z)
        parts.append(p)
    finalize(parts)
    o = join(parts, name)
    shade(o, 50)
    return o


def stalk(name, seed):
    rnd = random.Random(seed)
    r = 0.0072
    L = 0.17
    body = lathe("stalk", [(0, 0), (r * 0.8, 0.0008), (r, 0.006), (r, L * 0.7), (r * 0.95, L), (r * 0.6, L + 0.004),
                           (0, L + 0.005)], M["negi_white"], 24)
    body.data.materials.append(M["negi_mid"])
    body.data.materials.append(M["negi_raw"])
    for p in body.data.polygons:
        z = p.center.z
        p.material_index = 0 if z < 0.07 else (1 if z < 0.09 else 2)
    parts = [body]
    for k, ang in enumerate((-9, 7)):  # 갈라지는 잎
        lf = lathe("leaf", [(0, 0), (0.0045, 0.002), (0.0045, 0.07), (0.003, 0.095), (0, 0.1)], M["negi_raw"], 16)
        lf.data.transform(Matrix.Translation((0, 0, L - 0.012)) @
                          Matrix.Rotation(math.radians(ang + rnd.uniform(-2, 2)), 4, "X"))
        parts.append(lf)
    for k in range(9):  # 뿌리
        a = 2 * math.pi * k / 9
        bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.0005, depth=0.012,
                                            location=(math.cos(a) * 0.003, math.sin(a) * 0.003, -0.005),
                                            rotation=(math.cos(a) * 0.5, math.sin(a) * 0.5, 0))
        rt = active()
        assign(rt, M["root"])
        parts.append(rt)
    finalize(parts)
    o = join(parts, name)
    # 눕히기 (z축 → x축)
    o.data.transform(Matrix.Translation((-0.12, 0, r)) @ Matrix.Rotation(math.radians(90), 4, "Y"))
    return o


def green_onion(name):
    parts = []
    for i, (dy, rz, dz) in enumerate(((-0.014, 3, 0), (0.0, -4, 0.008), (0.014, 1, 0))):
        s = stalk(f"st{i}", i)
        s.data.transform(Matrix.Translation((i * 0.006, dy, dz)) @ Matrix.Rotation(math.radians(rz), 4, "Z"))
        parts.append(s)
    o = join(parts, name)
    shade(o, 45)
    return o


def onion_pieces(name):
    parts = []
    for i, (loc, rot) in enumerate((((-0.0115, 0, 0.0108), (0, 0, 0.2)), ((0.0115, 0.002, 0.0108), (0, 0, -0.3)),
                                    ((0.0, 0.0, 0.0365), (0, 0, 0.6)))):
        p = negi_piece(f"n{i}", (0, 0, 0), "negi_raw")
        p.data.transform(Matrix.Rotation(rot[2], 4, "Z") @ Matrix.Rotation(math.radians(90), 4, "Z"))
        p.location = loc
        parts.append(p)
    # 위의 한 조각은 세워서 단면이 보이게
    parts[2].data.transform(Matrix.Rotation(math.radians(90), 4, "Y"))
    finalize(parts)
    return join(parts, name)


def tare_bottle(name):
    r, H = 0.028, 0.13
    body = lathe(name + "_body", [
        (0, 0), (r - 0.004, 0), (r, 0.004), (r, H * 0.62), (r * 0.94, H * 0.74), (r * 0.7, H * 0.84),
        (r * 0.45, H * 0.92), (r * 0.4, H), (0, H)], M["tare_btl"], 48)
    cap = lathe(name + "_cap", [
        (0, H - 0.003), (r * 0.46, H - 0.003), (r * 0.48, H), (r * 0.48, H + 0.02), (r * 0.42, H + 0.024),
        (0, H + 0.024)], M["tare_cap"], 32)
    o = join([body, cap], name)
    shade(o, 35)
    return o


def shichimi_shaker(name):
    # 유리병 속 가루가 보이는 모습을 불투명 재질로 표현 (Unity에서도 그대로 보이도록)
    r, H = 0.018, 0.05
    glass = mat("Shichimi_Glass", "#E3EBEE", 0.05)
    base = lathe(name + "_base", [(0, 0), (r - 0.002, 0), (r, 0.002), (r, 0.004), (0, 0.004)], glass, 40)
    powder = lathe("powder", [(0, 0.004), (r, 0.004), (r, 0.036), (0, 0.036)], M["spice"], 40)
    powder.data.materials.append(M["spice_dark"])
    for p in powder.data.polygons:
        if noise.noise(p.center * 700) > 0.3:
            p.material_index = 1
    upper = lathe("upper", [(0, 0.036), (r, 0.036), (r, H), (0, H)], glass, 40)
    cap = lathe(name + "_cap", [(0, H - 0.004), (r + 0.0006, H - 0.004), (r + 0.0012, H - 0.002),
                                (r + 0.0012, H + 0.012), (r * 0.9, H + 0.015), (0, H + 0.015)], M["shaker_cap"], 40)
    o = join([powder, base, upper, cap], name)
    shade(o, 35)
    return o


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
    (lambda: yakitori_grill("Yakitori_Grill"), "Yakitori_Grill", (-0.80, 0.32)),
    (lambda: yakitori_grill_cooking("Yakitori_Grill_Cooking"), "Yakitori_Grill_Cooking", (-0.34, 0.32)),
    (lambda: yakitori_plate("Yakitori_Plate"), "Yakitori_Plate", (0.04, 0.32)),
    (lambda: tare_bottle("Tare_Bottle"), "Tare_Bottle", (0.25, 0.34)),
    (lambda: shichimi_shaker("Shichimi_Shaker"), "Shichimi_Shaker", (0.34, 0.32)),
    (lambda: chicken_bowl("Chicken_Bowl"), "Chicken_Bowl", (-0.80, 0.0)),
    (lambda: green_onion("Green_Onion"), "Green_Onion", (-0.58, 0.0)),
    (lambda: skewer("Raw_Chicken_Skewer", ["chicken_raw"] * 4, 3), "Raw_Chicken_Skewer", (-0.34, 0.045)),
    (lambda: skewer("Raw_Negi_Skewer", ["negi_white"] * 4, 4), "Raw_Negi_Skewer", (-0.34, -0.045)),
    (lambda: onion_pieces("Green_Onion_Pieces"), "Green_Onion_Pieces", (-0.17, 0.0)),
    (lambda: skewer("Yakitori_Skewer_Raw", RAW_MIX, 5), "Yakitori_Skewer_Raw", (0.0, 0.045)),
    (lambda: skewer("Yakitori_Skewer", COOKED, 6), "Yakitori_Skewer", (0.0, -0.045)),
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

bpy.ops.object.camera_add()
cam = active()
cam.data.type = "ORTHO"
cam.rotation_euler = (math.radians(55), 0, 0)
scene.camera = cam


def shot(path, tx, ty, sc, w=1800, h=1000):
    cam.data.ortho_scale = sc
    cam.location = (tx, ty - 1.64, 1.2)
    scene.render.resolution_x, scene.render.resolution_y = w, h
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


if CHECK:
    for nm, (tx, ty), sc in (("grill", (-0.34, 0.32), 0.45), ("plate", (0.15, 0.32), 0.45),
                             ("front", (-0.6, 0.0), 0.45), ("front2", (-0.12, 0.0), 0.4)):
        shot(os.path.join(OUT, "_check_" + nm + ".png"), tx, ty, sc, 1000, 700)
shot(os.path.join(OUT, "preview.png"), -0.25, 0.2, 1.5)

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "yakitori_set.blend"))
print("DONE")
