# 크레페 관련 소품 3D 모델 생성 스크립트 (Blender 5.x)
# 실행: blender -b --factory-startup --python build_crepe.py -- <출력폴더>
# 결과: <출력폴더>/crepe_set.blend, fbx/*.fbx (+라벨 텍스처), preview.png
import bpy, bmesh, math, random, os, sys
from mathutils import Vector, Matrix, noise

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else os.path.dirname(os.path.abspath(__file__))
CHECK = "--check" in argv
FBX_DIR = os.path.join(OUT, "fbx")
os.makedirs(FBX_DIR, exist_ok=True)
random.seed(8)

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


# ---------------------------------------------------------------- 크레페 머티리얼
M.update({
    "body_red": mat("Maker_Red", "#D8473A", 0.35, coat=0.3),
    "plate": mat("Maker_Plate", "#2B2B30", 0.4, 0.3),
    "rubber": mat("Maker_Rubber", "#1C1C1F", 0.7),
    "knob": mat("Maker_Knob", "#232326", 0.4),
    "crepe": mat("Crepe", "#EDC27C", 0.6),
    "crepe_spot": mat("Crepe_Browned", "#C98A3F", 0.6),
    "batter": mat("Crepe_Batter", "#E8C98A", 0.35),
    "wood": mat("Wood_Spoon", "#B9824A", 0.6),
    "cream": mat("Whipped_Cream", "#FFFBF2", 0.45),
    "choco": mat("Chocolate_Sauce", "#4A2210", 0.15, coat=0.6),
    "berry": mat("Strawberry", "#E2283A", 0.3, coat=0.3),
    "berry_in": mat("Strawberry_Inside", "#F2868D", 0.45),
    "banana": mat("Banana", "#F6D13C", 0.45),
    "banana_end": mat("Banana_Tip", "#5B3A1A", 0.6),
    "jar_body": mat("ChocoJar", "#4B2512", 0.12, coat=0.5),
    "jar_lid": mat("ChocoJar_Lid", "#F7F5F0", 0.35),
    "bag": mat("Piping_Bag", "#EFE9DF", 0.3),
    "tip": mat("Piping_Tip", "#B8BDC2", 0.25, 0.9),
    "paper_pink": mat("Cone_Paper_Pink", "#F2A2BE", 0.6),
    "paper_white": mat("Cone_Paper_White", "#FFF4F7", 0.6),
    "cookie": mat("Cookie", "#D7A15A", 0.7),
    "dish": mat("Plate_Ceramic", "#F4F1EC", 0.22),
})


def spots(o, idx=1, scale=180, thr=0.25, seed=0):
    off = Vector((seed, seed * 2.0, seed * 0.5))
    for p in o.data.polygons:
        if noise.noise(p.center * scale + off) > thr:
            p.material_index = idx


def crepe_tex(u, v):
    base, brown, deep = srgb("#EDC27C"), srgb("#C98A3F"), srgb("#A8622A")
    n1 = noise.noise(Vector((u * 9, v * 9, 0.3))) + 0.45 * noise.noise(Vector((u * 26, v * 26, 1.7)))
    t = (n1 - 0.1) / 0.45
    t = max(0.0, min(1.0, t))
    col = mix(base, brown, t * t * (3 - 2 * t) * 0.85)
    lace = abs(noise.noise(Vector((u * 13, v * 13, 4.2))))
    return mix(col, deep, cover(lace - 0.035, 0.03) * 0.55)


_CREPE = []


def crepe_mat():
    if not _CREPE:
        _CREPE.append(make_tex_mat("Crepe_Tex", 512, 512, crepe_tex, 0.6))
    return _CREPE[0]


def set_uv(o, fn):
    me = o.data
    uvl = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
    for lp in me.loops:
        uvl.data[lp.index].uv = fn(me.vertices[lp.vertex_index].co)


def uv_active(o):
    if "UVMap" in o.data.uv_layers:
        o.data.uv_layers["UVMap"].active = True
        o.data.uv_layers["UVMap"].active_render = True
    return o


def dish(name, R=0.12):
    return lathe(name, [
        (0, 0.002), (R * 0.5, 0.002), (R * 0.55, 0.0), (R * 0.6, 0.0), (R * 0.62, 0.004),
        (R * 0.9, 0.011), (R * 0.99, 0.0155), (R, 0.0172), (R * 0.985, 0.0185),
        (R * 0.9, 0.0145), (R * 0.62, 0.0078), (0, 0.0078)], M["dish"], 64)


def drizzle(name, pts, r=0.0016):
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = r
    cu.bevel_resolution = 3
    sp = cu.splines.new("POLY")
    sp.points.add(len(pts) - 1)
    for p, (x, y, z) in zip(sp.points, pts):
        p.co = (x, y, z, 1)
    o = bpy.data.objects.new(name, cu)
    scene.collection.objects.link(o)
    o.data.materials.append(M["choco"])
    select_only([o])
    bpy.ops.object.convert(target="MESH")
    o = active()
    smooth(o)
    return o


def cream_swirl(name, r0, h, turns=1.3, points=6, K=28, A=64):
    """별 모양 단면이 꼬이며 올라가는 휘핑크림"""
    bm = bmesh.new()
    rings = []
    for k in range(K + 1):
        f = k / K
        rad = r0 * (1 - f) ** 0.75 * (1 + 0.12 * math.sin(f * math.pi))
        rot = turns * 2 * math.pi * f
        z = h * f
        ring = []
        for j in range(A):
            a = 2 * math.pi * j / A
            rr = rad * (1 + 0.16 * math.cos(points * (a - rot)))
            ring.append(bm.verts.new((rr * math.cos(a), rr * math.sin(a), z)))
        rings.append(ring)
    bot = bm.verts.new((0, 0, 0))
    top = bm.verts.new((0, 0, h + r0 * 0.05))
    for k in range(K):
        for j in range(A):
            jj = (j + 1) % A
            bm.faces.new((rings[k][j], rings[k][jj], rings[k + 1][jj], rings[k + 1][j]))
    for j in range(A):
        jj = (j + 1) % A
        bm.faces.new((bot, rings[0][jj], rings[0][j]))
        bm.faces.new((rings[K][j], rings[K][jj], top))
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-7)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = mesh_obj(name, bm)
    assign(o, M["cream"])
    smooth(o)
    return o


def strawberry_half(name, s=1.0):
    o = lathe(name, [(0, 0), (0.006 * s, 0.004 * s), (0.011 * s, 0.011 * s), (0.0135 * s, 0.019 * s),
                     (0.013 * s, 0.024 * s), (0.009 * s, 0.028 * s), (0, 0.029 * s)], M["berry"], 24)
    o.data.materials.append(M["berry_in"])
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, 0),
                           plane_no=(1, 0, 0), clear_outer=True)
    edges = [e for e in bm.edges if e.is_boundary]
    ret = bmesh.ops.holes_fill(bm, edges=edges, sides=0)
    for f in ret["faces"]:
        f.material_index = 1
    bm.to_mesh(o.data)
    bm.free()
    shade(o, 50)
    return o  # 잘린 면은 +x를 향함


def bowl_with(name, R, D, contents):
    b, top = bowl(name + "_bowl", R, D)
    parts = [b] + contents(top)
    finalize(parts)
    o = join(parts, name)
    shade(o, 50)
    return o


def fill_disc(name, R, z, m, dome=0.003):
    prof = [(0, z - 0.004), (R, z - 0.004), (R, z)]
    for k in range(1, 6):
        f = k / 5
        prof.append((R * (1 - f), z + dome * math.sin(f * math.pi / 2)))
    return lathe(name, prof, m, 48)


def label_band(name, r, z0, h, m, arc=150, segs=32):
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    rows = [[], []]
    for i in range(segs + 1):
        phi = math.radians(-arc / 2 + arc * i / segs)
        for k, z in enumerate((z0, z0 + h)):
            rows[k].append(bm.verts.new((r * math.sin(phi), -r * math.cos(phi), z)))
    for i in range(segs):
        f = bm.faces.new((rows[0][i], rows[0][i + 1], rows[1][i + 1], rows[1][i]))
        for loop, c in zip(f.loops, ((i / segs, 0), ((i + 1) / segs, 0), ((i + 1) / segs, 1), (i / segs, 1))):
            loop[uv].uv = c
    o = mesh_obj(name, bm)
    assign(o, m)
    smooth(o)
    return o


# ---------------------------------------------------------------- 크레페 기계
PLATE_TOP = 0.064


def crepe_maker(name):
    parts = [lathe(name + "_body", [(0, 0.004), (0.16, 0.004), (0.172, 0.01), (0.176, 0.02), (0.176, 0.042),
                                    (0.17, 0.05), (0.16, 0.052), (0, 0.052)], M["body_red"], 72)]
    parts.append(lathe("griddle", [(0, 0.05), (0.158, 0.05), (0.162, 0.056), (0.16, PLATE_TOP), (0.154, PLATE_TOP + 0.001),
                                   (0.15, PLATE_TOP), (0, PLATE_TOP)], M["plate"], 72))
    ang = math.radians(-115)
    kx, ky = 0.176 * math.cos(ang), 0.176 * math.sin(ang)
    parts.append(cyl("knob", 0.017, 0.016, (kx, ky, 0.028), M["knob"], (math.radians(90), 0, ang + math.pi / 2)))
    parts.append(rbox("knob_grip", (0.006, 0.01, 0.026), (kx * 1.05, ky * 1.05, 0.028), 0.002, M["knob"], 2,
                      (0, 0, ang + math.pi / 2)))
    for dz, m in ((0.034, M["led_r"]), (0.022, M["led_g"])):
        a2 = math.radians(-90)
        parts.append(sphere("led", 0.0035, (0.1755 * math.cos(a2) * 0.995, 0.1755 * math.sin(a2) * 0.995, dz), m, 12, 8))
    for k in range(4):
        a = math.radians(45 + 90 * k)
        parts.append(cyl("foot", 0.013, 0.008, (0.13 * math.cos(a), 0.13 * math.sin(a), 0.004), M["rubber"], verts=20))
    finalize(parts)
    o = join(parts, name)
    shade(o, 40)
    return o


def crepe_sheet(name, R=0.14):
    o = lathe(name, [(0, 0), (R * 0.98, 0), (R, 0.0012), (R * 0.98, 0.0024), (0, 0.0024)], crepe_mat(), 72)
    set_uv(o, lambda c: (c.x / (2.1 * R) + 0.5, c.y / (2.1 * R) + 0.5))
    shade(o, 50)
    return uv_active(o)


# ---------------------------------------------------------------- 크레페 요리
def rolled_crepe(name):
    """접시 위 말린 크레페 (x축 방향), 바닥 z=0"""
    bm = bmesh.new()
    NX, A = 28, 40
    L, ry, rz = 0.15, 0.03, 0.019
    rings = []
    for i in range(NX + 1):
        x = -L / 2 + L * i / NX
        ring = []
        for j in range(A):
            a = 2 * math.pi * j / A
            flare = 1 + 0.12 * max(0, (abs(x) - L * 0.38) / (L * 0.12))
            ring.append(bm.verts.new((x, ry * flare * math.cos(a), rz * flare * (1 + math.sin(a)) + 0.0015)))
        rings.append(ring)
    for i in range(NX):
        for j in range(A):
            jj = (j + 1) % A
            bm.faces.new((rings[i][j], rings[i][jj], rings[i + 1][jj], rings[i + 1][j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    tube = mesh_obj("tube", bm)
    assign(tube, crepe_mat())
    zc = rz + 0.0015
    set_uv(tube, lambda c: (c.x / 0.17 + 0.5, (math.atan2(c.y, c.z - zc) / (2 * math.pi) + 0.5) * 0.6))
    so = tube.modifiers.new("thick", "SOLIDIFY")
    so.thickness = 0.0016
    apply_all(tube)
    smooth(tube)
    parts = [tube]
    # 양 끝으로 삐져나온 크림과 딸기
    for sx in (1, -1):
        c = cream_swirl("cr", 0.02, 0.022, points=7)
        c.data.transform(Matrix.Translation((sx * (L / 2 - 0.012), 0, rz + 0.0015)) @
                         Matrix.Rotation(math.radians(80 * sx), 4, "Y"))
        parts.append(c)
    for k, (dy, dz, rot) in enumerate(((0.011, 0.026, 20), (-0.012, 0.022, -25))):
        s = strawberry_half("sb")
        s.data.transform(Matrix.Translation((L / 2 - 0.004, dy, dz)) @ Matrix.Rotation(math.radians(rot), 4, "X") @
                         Matrix.Rotation(math.radians(-70), 4, "Y") @ Matrix.Translation((0, 0, -0.012)))
        parts.append(s)
    # 초콜릿 지그재그
    pts = []
    legs = 9
    for k in range(legs):
        xa = -L * 0.42 + L * 0.84 * k / legs
        xb = -L * 0.42 + L * 0.84 * (k + 1) / legs
        ya, yb = (ry * 0.8, -ry * 0.8) if k % 2 == 0 else (-ry * 0.8, ry * 0.8)
        for s_ in range(14):
            f = s_ / 14
            x, y = xa + (xb - xa) * f, ya + (yb - ya) * f
            pts.append((x, y, rz * (1 + math.sqrt(max(0, 1 - (y / ry) ** 2))) + 0.0032))
    parts.append(drizzle("choco", pts))
    finalize(parts)
    return uv_active(join(parts, name))


def crepe_plate(name):
    d = dish(name + "_dish")
    c = rolled_crepe("crepe")
    c.data.transform(Matrix.Translation((0, 0, 0.0079)) @ Matrix.Rotation(math.radians(-15), 4, "Z"))
    o = join([c, d], name)
    shade(o, 45)
    return uv_active(o)


def heart_cookie(name, s=0.017, t=0.005):
    bm = bmesh.new()
    vs = []
    for i in range(48):
        a = 2 * math.pi * i / 48
        x = 16 * math.sin(a) ** 3
        y = 13 * math.cos(a) - 5 * math.cos(2 * a) - 2 * math.cos(3 * a) - math.cos(4 * a)
        vs.append(bm.verts.new((x / 16 * s, y / 16 * s, 0)))
    f = bm.faces.new(vs)
    ret = bmesh.ops.extrude_face_region(bm, geom=[f])
    nv = [e for e in ret["geom"] if isinstance(e, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=nv, vec=(0, 0, t))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = mesh_obj(name, bm)
    assign(o, M["cookie"])
    bv = o.modifiers.new("bv", "BEVEL")
    bv.width = 0.0015
    bv.segments = 2
    apply_all(o)
    shade(o, 40)
    return o


def crepe_cone(name):
    H, R = 0.11, 0.043
    wr = lambda z: 0.003 + (R - 0.003) * z / H
    # 분홍 체크 포장지
    bm = bmesh.new()
    A, NZ = 32, 10
    tipv = bm.verts.new((0, 0, 0))
    rings = []
    for k in range(1, NZ + 1):
        z = H * k / NZ
        rings.append([bm.verts.new((wr(z) * math.cos(2 * math.pi * j / A), wr(z) * math.sin(2 * math.pi * j / A), z))
                      for j in range(A)])
    for j in range(A):
        f = bm.faces.new((tipv, rings[0][(j + 1) % A], rings[0][j]))
        f.material_index = (j // 2) % 2
    for k in range(NZ - 1):
        for j in range(A):
            jj = (j + 1) % A
            f = bm.faces.new((rings[k][j], rings[k][jj], rings[k + 1][jj], rings[k + 1][j]))
            f.material_index = ((k + 1) + j // 2) % 2
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    paper = mesh_obj("paper", bm)
    assign(paper, M["paper_pink"], M["paper_white"])
    so = paper.modifiers.new("thick", "SOLIDIFY")
    so.thickness = 0.001
    apply_all(paper)
    parts = [paper]
    # 포장지 위로 나온 크레페 (비스듬한 물결 테두리)
    bm = bmesh.new()
    rings = []
    NZ2 = 14
    for k in range(NZ2 + 1):
        f = k / NZ2
        ring = []
        for j in range(48):
            a = 2 * math.pi * j / 48
            ztop = H + 0.028 + 0.016 * math.sin(a) + 0.003 * math.sin(a * 6)
            z = 0.06 + (ztop - 0.06) * f
            rr = (wr(z) - 0.0012) if z < H else (wr(H) - 0.0012 + (z - H) * 0.45)
            ring.append(bm.verts.new((rr * math.cos(a), rr * math.sin(a), z)))
        rings.append(ring)
    for k in range(NZ2):
        for j in range(48):
            jj = (j + 1) % 48
            bm.faces.new((rings[k][j], rings[k][jj], rings[k + 1][jj], rings[k + 1][j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    cr = mesh_obj("crepe", bm)
    assign(cr, crepe_mat())
    set_uv(cr, lambda c: ((math.atan2(c.x, -c.y) / (2 * math.pi) + 0.5) * 0.9, c.z / 0.18))
    so = cr.modifiers.new("thick", "SOLIDIFY")
    so.thickness = 0.0015
    apply_all(cr)
    smooth(cr)
    parts.append(cr)
    # 크림, 딸기, 쿠키, 초콜릿
    swirl_r, swirl_h, z0 = 0.038, 0.05, H + 0.008
    c = cream_swirl("cream", swirl_r, swirl_h)
    c.location.z = z0
    parts.append(c)
    for k, (a, tilt) in enumerate(((math.radians(-60), 25), (math.radians(-130), 20), (math.radians(20), 30))):
        s = strawberry_half("sb", 1.05)
        s.data.transform(Matrix.Translation((0.03 * math.cos(a), 0.03 * math.sin(a), z0 + 0.008)) @
                         Matrix.Rotation(a, 4, "Z") @ Matrix.Rotation(math.radians(tilt), 4, "Y"))
        parts.append(s)
    ck = heart_cookie("cookie")
    ck.data.transform(Matrix.Translation((0.008, 0.024, z0 + 0.03)) @ Matrix.Rotation(math.radians(70), 4, "X"))
    parts.append(ck)
    pts = []
    for i in range(90):
        f = i / 89
        ang = f * 2.6 * 2 * math.pi
        zz = z0 + swirl_h * (0.85 - 0.6 * f)
        ff = (zz - z0) / swirl_h
        rr = swirl_r * (1 - ff) ** 0.75 * (1 + 0.12 * math.sin(ff * math.pi)) * 1.16 + 0.001
        pts.append((rr * math.cos(ang), rr * math.sin(ang), zz))
    parts.append(drizzle("choco", pts, 0.0018))
    finalize(parts)
    o = join([cr] + [p for p in parts if p is not cr], name)
    shade(o, 45)
    return uv_active(o)


# ---------------------------------------------------------------- 재료
def crepe_batter_bowl(name):
    R, D = 0.09, 0.075

    def contents(top):
        level = top * 0.62
        # bowl()의 안쪽 곡선 식을 역으로 풀어 반죽 높이에서의 안쪽 반지름 계산
        ca0 = math.cos(math.asin(0.45))
        rin = 0.0865 * math.sin(math.acos(max(-1, min(1, ca0 * (1 - (level - 0.0055) / 0.0715))))) - 0.0005
        bat = fill_disc("batter", rin, level, M["batter"])
        tilt = math.radians(32)
        d = Vector((math.sin(tilt), 0, math.cos(tilt)))
        p0 = Vector((-0.025, 0.0, level - 0.01))
        Ls = 0.17
        handle = cyl("handle", 0.0055, Ls, p0 + d * (Ls / 2), M["wood"], (0, tilt, 0), 16)
        head = sphere("head", 0.02, p0, M["wood"], 20, 12, (1.0, 0.8, 0.3))
        head.rotation_euler = (0, tilt, 0)
        return [bat, handle, head]
    return bowl_with(name, R, D, contents)


def whipped_cream_bowl(name):
    def contents(top):
        c = cream_swirl("cream", 0.04, 0.05)
        c.location.z = top * 0.35
        return [c]
    return bowl_with(name, 0.058, 0.044, contents)


def strawberry_bowl(name):
    def contents(top):
        rnd = random.Random(4)
        out = []
        for i in range(11):
            a = rnd.uniform(0, 6.28)
            d = 0.028 * math.sqrt(rnd.uniform(0, 1))
            s = strawberry_half("sb", rnd.uniform(0.85, 1.0))
            s.data.transform(Matrix.Translation((d * math.cos(a), d * math.sin(a),
                                                 top * 0.45 + 0.012 * (1 - d / 0.028) + rnd.uniform(0, 0.004))) @
                             Matrix.Rotation(rnd.uniform(0, 6.28), 4, "Z") @
                             Matrix.Rotation(rnd.uniform(-1.4, 1.4), 4, "Y") @ Matrix.Translation((0, 0, -0.014)))
            out.append(s)
        return out
    return bowl_with(name, 0.055, 0.042, contents)


def banana(name, L=0.19, R=0.017, bend=0.75, seed=0):
    Rc = L / bend
    N, A = 30, 20
    bm = bmesh.new()
    rings = []
    for i in range(N + 1):
        t = i / N
        ang = -bend / 2 + bend * t
        c = Vector((Rc * math.sin(ang), Rc * (1 - math.cos(ang)), 0))
        T = Vector((math.cos(ang), math.sin(ang), 0))
        N1 = Vector((-math.sin(ang), math.cos(ang), 0))
        B = Vector((0, 0, 1))
        if t < 0.08:
            r = 0.0045
        else:
            tt = (t - 0.08) / 0.92
            r = max(0.003, R * math.sin(math.pi * min(1, tt * 0.92 + 0.05)) ** 0.5)
        ring = []
        for j in range(A):
            a = 2 * math.pi * j / A
            rr = r * (1 - 0.07 * math.cos(5 * a))
            ring.append(bm.verts.new(c + N1 * (rr * math.cos(a)) + B * (rr * math.sin(a))))
        rings.append(ring)
    p0 = bm.verts.new(Vector((Rc * math.sin(-bend / 2), Rc * (1 - math.cos(-bend / 2)), 0)) - Vector((0.003, 0, 0)))
    p1 = bm.verts.new(Vector((Rc * math.sin(bend / 2), Rc * (1 - math.cos(bend / 2)), 0)) + Vector((0.002, 0.001, 0)))
    for i in range(N):
        for j in range(A):
            jj = (j + 1) % A
            f = bm.faces.new((rings[i][j], rings[i][jj], rings[i + 1][jj], rings[i + 1][j]))
            f.material_index = 1 if (i < 2 or i >= N - 1) else 0
    for j in range(A):
        jj = (j + 1) % A
        bm.faces.new((p0, rings[0][jj], rings[0][j])).material_index = 1
        bm.faces.new((rings[N][j], rings[N][jj], p1)).material_index = 1
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = mesh_obj(name, bm)
    assign(o, M["banana"], M["banana_end"])
    shade(o, 60)
    return o


def bananas(name):
    b1 = banana("b1", seed=1)
    b2 = banana("b2", 0.18, 0.016, 0.8, seed=2)
    b2.data.transform(Matrix.Translation((0.004, -0.02, 0.0)) @ Matrix.Rotation(math.radians(-8), 4, "Z"))
    o = join([b1, b2], name)
    zmin = min(v.co.z for v in o.data.vertices)
    cx = sum(v.co.x for v in o.data.vertices) / len(o.data.vertices)
    cy = sum(v.co.y for v in o.data.vertices) / len(o.data.vertices)
    o.data.transform(Matrix.Translation((-cx, -cy, -zmin)))
    return o


def choco_label(u, v):
    x, y = (u - 0.5) * 1.95, (v - 0.5) * 0.85
    col = srgb("#FFF8EE")
    if v < 0.07 or v > 0.93:
        col = srgb("#6B3A1E")
    choc, choc_l = srgb("#5A2C14"), srgb("#8A4E2A")
    # 물결 테두리를 가진 초콜릿 웅덩이 + 숟가락 자국 같은 소용돌이
    th = math.atan2(y + 0.02, x)
    rr = 0.3 + 0.03 * math.sin(th * 7)
    d = math.hypot(x, (y + 0.02) * 1.15) - rr
    sw = 0.5 + 0.5 * math.sin(math.hypot(x, y + 0.02) * 60 - th * 2)
    blob = mix(choc, choc_l, sw * 0.5)
    hl = cover(math.hypot(x + 0.08, y - 0.06) - 0.035)
    blob = mix(blob, (1, 0.93, 0.85), hl * 0.6)
    return mix(col, blob, cover(d))


def choco_jar(name):
    r, H = 0.032, 0.075
    body = lathe(name + "_body", [(0, 0), (r - 0.004, 0), (r, 0.004), (r, H - 0.01), (r * 0.94, H - 0.004),
                                  (r * 0.9, H), (0, H)], M["jar_body"], 48)
    lid = lathe(name + "_lid", [(0, H - 0.003), (r * 0.93, H - 0.003), (r * 0.95, H), (r * 0.95, H + 0.016),
                                (r * 0.9, H + 0.019), (0, H + 0.019)], M["jar_lid"], 48)
    lab = label_band(name + "_label", r * 1.006, 0.014, 0.046, LBL_C, arc=160)
    o = join([lab, body, lid], name)
    o.data.uv_layers["UVMap"].active = True
    o.data.uv_layers["UVMap"].active_render = True
    shade(o, 35)
    return o


def piping_bag(name):
    bag = lathe("bag", [(0, 0.016), (0.0072, 0.017), (0.012, 0.035), (0.03, 0.12), (0.036, 0.15),
                        (0.03, 0.168), (0.014, 0.18), (0.009, 0.19), (0.011, 0.2), (0, 0.202)], M["bag"], 40)
    # 별 모양 깍지
    bm = bmesh.new()
    rings = []
    for k, (z, r) in enumerate(((0.0, 0.0045), (0.012, 0.0065), (0.022, 0.0085))):
        rings.append([bm.verts.new(((r * (1 + 0.18 * math.cos(8 * 2 * math.pi * j / 32))) * math.cos(2 * math.pi * j / 32),
                                    (r * (1 + 0.18 * math.cos(8 * 2 * math.pi * j / 32))) * math.sin(2 * math.pi * j / 32), z))
                      for j in range(32)])
    for k in range(2):
        for j in range(32):
            jj = (j + 1) % 32
            bm.faces.new((rings[k][j], rings[k][jj], rings[k + 1][jj], rings[k + 1][j]))
    bm.faces.new(rings[0][::-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    tip = mesh_obj("tip", bm)
    assign(tip, M["tip"])
    so = tip.modifiers.new("thick", "SOLIDIFY")
    so.thickness = 0.0008
    apply_all(tip)
    o = join([bag, tip], name)
    shade(o, 45)
    # 비스듬히 눕히기
    o.data.transform(Matrix.Rotation(math.radians(35), 4, "Z") @ Matrix.Rotation(math.radians(-72), 4, "Y"))
    zmin = min(v.co.z for v in o.data.vertices)
    o.data.transform(Matrix.Translation((0, 0, -zmin)))
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


LBL_C = make_tex_mat("ChocoJar_Label", 384, 256, choco_label)

ITEMS = [
    (lambda: crepe_maker("Crepe_Maker"), "Crepe_Maker", (-0.76, 0.32)),
    (lambda: crepe_sheet("Crepe_Sheet"), "Crepe_Sheet", (-0.76, 0.32)),
    (lambda: crepe_plate("Crepe_Plate"), "Crepe_Plate", (-0.42, 0.32)),
    (lambda: crepe_cone("Crepe_Cone"), "Crepe_Cone", (-0.22, 0.34)),
    (lambda: choco_jar("Choco_Spread_Jar"), "Choco_Spread_Jar", (-0.08, 0.34)),
    (lambda: piping_bag("Piping_Bag"), "Piping_Bag", (0.08, 0.06)),
    (lambda: crepe_batter_bowl("Crepe_Batter_Bowl"), "Crepe_Batter_Bowl", (-0.78, 0.0)),
    (lambda: whipped_cream_bowl("Whipped_Cream_Bowl"), "Whipped_Cream_Bowl", (-0.58, 0.0)),
    (lambda: strawberry_bowl("Strawberry_Bowl"), "Strawberry_Bowl", (-0.43, 0.0)),
    (lambda: bananas("Bananas"), "Bananas", (-0.24, 0.0)),
]

for build, nm, pos in ITEMS:
    root = build()
    if root.location.length > 0 and not root.children:
        bake_loc(root)
    export(root, nm)
    root.location = (pos[0], pos[1], PLATE_TOP if nm == "Crepe_Sheet" else 0)
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
    for nm, (tx, ty), sc in (("maker", (-0.6, 0.32), 0.5), ("cone", (-0.12, 0.34), 0.35),
                             ("front", (-0.6, 0.0), 0.45), ("front2", (-0.3, 0.0), 0.4)):
        shot(os.path.join(OUT, "_check_" + nm + ".png"), tx, ty, sc, 1000, 700)
shot(os.path.join(OUT, "preview.png"), -0.33, 0.2, 1.15)

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "crepe_set.blend"))
print("DONE")
