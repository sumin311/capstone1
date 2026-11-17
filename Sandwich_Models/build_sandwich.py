# 샌드위치 관련 소품 3D 모델 생성 스크립트 (Blender 5.x)
# 실행: blender -b --factory-startup --python build_sandwich.py -- <출력폴더>
# 결과: <출력폴더>/sandwich_set.blend, fbx/*.fbx (+라벨 텍스처), preview.png
import bpy, bmesh, math, random, os, sys
from mathutils import Vector, Matrix, noise

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else os.path.dirname(os.path.abspath(__file__))
CHECK = "--check" in argv
FBX_DIR = os.path.join(OUT, "fbx")
os.makedirs(FBX_DIR, exist_ok=True)
random.seed(3)

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
    "body_red": mat("Press_Red", "#D8473A", 0.35, coat=0.3),
    "plate": mat("Press_Plate", "#2B2B30", 0.45, 0.3),
    "rubber": mat("Press_Rubber", "#1C1C1F", 0.7),
    "knob": mat("Press_Knob", "#232326", 0.4),
    "led_r": mat("LED_Red", "#FF3B2F", 0.3, emit="#FF3B2F"),
    "led_g": mat("LED_Green", "#3BE36A", 0.3, emit="#3BE36A"),
    "bread_crust": mat("Bread_Crust", "#C4843F", 0.7),
    "bread_crumb": mat("Bread_Crumb", "#EBD3A2", 0.8),
    "toast_crust": mat("Toast_Crust", "#9C4E1C", 0.65),
    "toast_crumb": mat("Toast_Crumb", "#D9974A", 0.7),
    "lettuce": mat("Lettuce", "#6DBA3C", 0.5),
    "lettuce_in": mat("Lettuce_Light", "#C4E68A", 0.5),
    "ham": mat("Ham", "#EB8E98", 0.45),
    "ham_rim": mat("Ham_Rim", "#C8606C", 0.45),
    "cheese": mat("Cheese", "#F8C838", 0.35),
    "butter": mat("Butter", "#F8DE78", 0.35),
    "metal": mat("Bowl_Steel", "#C8CDD3", 0.28, 0.85),
    "dish": mat("Plate_Ceramic", "#F4F1EC", 0.22),
    "ketchup_btl": mat("KetchupBottle", "#D3242B", 0.2),
    "mustard_btl": mat("MustardBottle", "#F2B705", 0.25),
    "cap_white": mat("Bottle_Cap", "#F5F4EF", 0.35),
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


# ---------------------------------------------------------------- 파츠
def bread_slice(name, toasted=False, s=0.055, t=0.014):
    N = 72
    bm = bmesh.new()
    vs = []
    for i in range(N):
        a = 2 * math.pi * i / N
        c, sn = math.cos(a), math.sin(a)
        n = 5
        x = s * math.copysign(abs(c) ** (2 / n), c)
        y = s * math.copysign(abs(sn) ** (2 / n), sn)
        if y > 0:  # 식빵 윗부분 볼록하게
            y += 0.16 * s * (1 - min(1, (x / s) ** 2)) * (y / s)
            x *= 1 + 0.06 * (y / s)
        vs.append(bm.verts.new((x, y - s * 0.06, 0)))
    f = bm.faces.new(vs)
    ret = bmesh.ops.extrude_face_region(bm, geom=[f])
    nv = [e for e in ret["geom"] if isinstance(e, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=nv, vec=(0, 0, t))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    caps = [fc for fc in bm.faces if abs(fc.normal.z) > 0.9]
    bmesh.ops.inset_individual(bm, faces=caps, thickness=0.0042, depth=-0.0004)
    for fc in bm.faces:
        fc.material_index = 0
    for fc in caps:
        fc.material_index = 1
    o = mesh_obj(name, bm)
    assign(o, M["toast_crust" if toasted else "bread_crust"], M["toast_crumb" if toasted else "bread_crumb"])
    bv = o.modifiers.new("bv", "BEVEL")
    bv.width = 0.0016
    bv.segments = 2
    bv.limit_method = "ANGLE"
    apply_all(o)
    shade(o, 40)
    return o


def leaf(name, L, W, seed, cup=0.015, ruffle=0.005, R=10, A=56):
    bm = bmesh.new()
    center = bm.verts.new((0, 0, 0))
    rings = []
    for k in range(1, R + 1):
        f = k / R
        ring = []
        for j in range(A):
            a = 2 * math.pi * j / A
            wob = 1 + 0.07 * math.sin(a * 9 + seed) + 0.04 * math.sin(a * 17 + seed * 2)
            x = math.cos(a) * L * f * wob
            y = math.sin(a) * W * f * wob
            z = cup * f * f + ruffle * math.sin(a * 7 + seed * 3) * f ** 3 \
                + ruffle * 0.35 * math.sin(a * 15 + seed) * f ** 4
            ring.append(bm.verts.new((x, y, z)))
        rings.append(ring)
    pale = []
    for j in range(A):
        fc = bm.faces.new((center, rings[0][j], rings[0][(j + 1) % A]))
        pale.append(fc)
    for k in range(R - 1):
        for j in range(A):
            jj = (j + 1) % A
            fc = bm.faces.new((rings[k][j], rings[k + 1][j], rings[k + 1][jj], rings[k][jj]))
            a = 2 * math.pi * (j + 0.5) / A
            if k < 3 or abs(math.sin(a)) < 0.07:  # 가운데와 잎맥은 연한 색
                pale.append(fc)
    for fc in bm.faces:
        fc.material_index = 1 if fc in pale else 0
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = mesh_obj(name, bm)
    assign(o, M["lettuce"], M["lettuce_in"])
    so = o.modifiers.new("thick", "SOLIDIFY")
    so.thickness = 0.0009
    apply_all(o)
    smooth(o)
    return o


def ham_disc(name, r, loc, t=0.0028):
    o = cyl(name, r, t, loc, M["ham"], verts=48)
    o.data.materials.append(M["ham_rim"])
    bv = o.modifiers.new("bv", "BEVEL")
    bv.width = t * 0.4
    bv.segments = 2
    apply_all(o)
    for p in o.data.polygons:
        p.material_index = 1 if abs(p.normal.z) < 0.6 else 0
    shade(o, 50)
    return o


def cheese_drape(name, half, z, bread_half, rot=45):
    bm = bmesh.new()
    ret = bmesh.ops.create_grid(bm, x_segments=16, y_segments=16, size=half)
    rm = Matrix.Rotation(math.radians(rot), 3, "Z")
    for v in ret["verts"]:
        p = rm @ Vector((v.co.x, v.co.y, 0))
        ex = max(abs(p.x), abs(p.y)) - bread_half
        p.z = z - (max(0, ex) * 1.1 + max(0, ex) ** 2 * 30)
        v.co = p
    o = mesh_obj(name, bm)
    assign(o, M["cheese"])
    so = o.modifiers.new("thick", "SOLIDIFY")
    so.thickness = 0.0018
    so.offset = 1.0
    apply_all(o)
    smooth(o)
    return o


def toasted_sandwich(name, rotz=0.0):
    t = 0.013
    parts = [bread_slice("bot", True, t=t)]
    lf = leaf("lettuce", 0.066, 0.062, 3, cup=0.0, ruffle=0.0025)
    lf.location.z = t + 0.0018
    parts.append(lf)
    parts.append(ham_disc("ham", 0.058, (0.002, -0.004, t + 0.0045)))
    parts.append(cheese_drape("cheese", 0.05, t + 0.0062, 0.053))
    top = bread_slice("top", True, t=t)
    top.location.z = t + 0.0085
    parts.append(top)
    finalize(parts)
    o = join(parts, name)
    o.data.transform(Matrix.Rotation(rotz, 4, "Z"))
    return o


def dish(name, R=0.11):
    return lathe(name, [
        (0, 0.002), (R * 0.5, 0.002), (R * 0.55, 0.0), (R * 0.6, 0.0), (R * 0.62, 0.004),
        (R * 0.9, 0.011), (R * 0.99, 0.0155), (R, 0.0172), (R * 0.985, 0.0185),
        (R * 0.9, 0.0145), (R * 0.62, 0.0078), (0, 0.0078)], M["dish"], 64)


def sandwich_plate(name):
    d = dish(name + "_dish")
    s = toasted_sandwich("sand", math.radians(18))
    for v in s.data.vertices:
        v.co.z += 0.0079
    o = join([d, s], name)
    shade(o, 40)
    return o


def bread_stack(name):
    parts = []
    t = 0.014
    for i, (dx, dy, rz) in enumerate([(0, 0, 0), (0.004, 0.002, -4), (-0.003, 0.003, 5)]):
        b = bread_slice(f"slice{i}", False, t=t)
        b.data.transform(Matrix.Translation((dx, dy, i * (t + 0.0004))) @ Matrix.Rotation(math.radians(rz), 4, "Z"))
        parts.append(b)
    return join(parts, name)


def ham_slices(name):
    parts = []
    for i, (dx, dy) in enumerate([(-0.02, 0.006), (-0.006, 0.0), (0.008, -0.004), (0.022, -0.008)]):
        parts.append(ham_disc(f"ham{i}", 0.045, (dx, dy, 0.0014 + i * 0.0032)))
    finalize(parts)
    return join(parts, name)


def cheese_slices(name):
    parts = []
    for sx, base_rot in ((-0.052, 8), (0.052, -6)):
        for i in range(3):
            parts.append(rbox("ch", (0.082, 0.082, 0.0026),
                              (sx + i * 0.004, -i * 0.003, 0.0013 + i * 0.0029), 0.0008, M["cheese"], 2,
                              (0, 0, math.radians(base_rot + i * 4))))
    finalize(parts)
    o = join(parts, name)
    shade(o, 40)
    return o


def butter_bowl(name):
    b, top = bowl(name + "_bowl", 0.047, 0.034)
    bt = rbox("butter", (0.034, 0.028, 0.02), (0.0, 0.0, 0.018), 0.003, M["butter"], 3, (0, 0, math.radians(15)))
    finalize([b, bt])
    o = join([b, bt], name)
    shade(o, 45)
    return o


def lettuce(name):
    parts = []
    for i, (dx, dy, rz, tilt) in enumerate([(-0.03, 0.01, 20, 12), (0.03, 0.0, -25, -12), (0.0, -0.015, 95, 6)]):
        lf = leaf(f"leaf{i}", 0.06, 0.045, i * 5 + 1, cup=0.022, ruffle=0.005)
        lf.data.transform(Matrix.Translation((dx, dy, 0.004 + i * 0.003)) @
                          Matrix.Rotation(math.radians(rz), 4, "Z") @ Matrix.Rotation(math.radians(tilt), 4, "X"))
        parts.append(lf)
    o = join(parts, name)
    return o


# ---------------------------------------------------------------- 라벨 텍스처
LABEL_ASPECT = 1.45


def ketchup_label(u, v):
    x, y = (u - 0.5) * LABEL_ASPECT, v - 0.5
    bg = srgb("#FFF6E6")
    red, red_d, green = srgb("#E63946"), srgb("#B5212C"), srgb("#3E9B3A")
    col = bg
    if v < 0.07 or v > 0.93:
        col = srgb("#D62828")
    # 토마토
    d = math.hypot(x, (y + 0.04) * 1.08) - 0.25
    tom = mix(red, red_d, (x + 0.25) * 1.2 + 0.2)
    hl = cover(math.hypot(x + 0.09, y - 0.04) - 0.05)
    tom = mix(tom, (1, 0.85, 0.85), hl * 0.7)
    col = mix(col, tom, cover(d))
    # 꼭지 (별 모양)
    lx, ly = x, y - 0.2
    th = math.atan2(ly, lx)
    rr = 0.035 + 0.04 * (0.5 + 0.5 * math.cos(5 * th))
    col = mix(col, green, cover(math.hypot(lx, ly) - rr))
    return col


def mustard_label(u, v):
    x, y = (u - 0.5) * LABEL_ASPECT, v - 0.5
    bg = srgb("#FFF8E8")
    yel, yel_d = srgb("#F6B800"), srgb("#D18E00")
    col = bg
    if v < 0.07 or v > 0.93:
        col = srgb("#E0A100")
    cy = -0.07
    dc = math.hypot(x, y - cy) - 0.19
    # 위로 뾰족한 물방울
    if y > cy:
        w = 0.19 * max(0.0, (0.3 - y) / (0.3 - cy))
        dt = abs(x) - w
        d = min(dc, max(dt, y - 0.3))
    else:
        d = dc
    drop = mix(yel, yel_d, (x + 0.19) * 1.6 - 0.3)
    hl = cover(math.hypot(x + 0.07, y + 0.02) - 0.045)
    drop = mix(drop, (1, 0.97, 0.85), hl * 0.75)
    col = mix(col, yel_d, cover(d - 0.012))
    col = mix(col, drop, cover(d))
    return col


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


def sauce_bottle(name, m_body, m_label, H=0.14, r=0.033):
    body = lathe(name + "_body", [
        (0, 0), (r - 0.005, 0), (r - 0.001, 0.0015), (r, 0.006), (r, H * 0.72),
        (r * 0.96, H - 0.018), (r * 0.85, H - 0.009), (r * 0.68, H - 0.003), (r * 0.56, H), (0, H)], m_body, 48)
    cap = lathe(name + "_cap", [
        (0, H - 0.001), (r * 0.6, H - 0.001), (r * 0.63, H + 0.002), (r * 0.63, H + 0.026),
        (r * 0.58, H + 0.031), (r * 0.36, H + 0.033), (r * 0.26, H + 0.036), (r * 0.2, H + 0.042),
        (0.0035, H + 0.046), (0, H + 0.046)], M["cap_white"], 40)
    lab = label_band(name + "_label", r * 1.006, 0.022, 0.066, m_label)
    o = join([lab, body, cap], name)
    o.data.uv_layers["UVMap"].active = True
    o.data.uv_layers["UVMap"].active_render = True
    shade(o, 35)
    return o


# ---------------------------------------------------------------- 샌드위치 프레스
BODY_TOP = 0.072
HINGE = Vector((0, 0.15, 0.09))


def sandwich_press(name):
    parts = [rbox(name + "_body", (0.30, 0.30, 0.06), (0, 0, 0.042), 0.02, M["body_red"], 5)]
    parts.append(rbox("lower_plate", (0.27, 0.25, 0.012), (0, 0, BODY_TOP), 0.004, M["plate"], 2))
    for k in range(6):
        y = -0.09 + 0.036 * k
        parts.append(rbox("ridge", (0.24, 0.006, 0.004), (0, y, BODY_TOP + 0.007), 0.0018, M["plate"], 2))
    parts.append(cyl("knob", 0.016, 0.014, (0.1, -0.157, 0.042), M["knob"], (math.radians(90), 0, 0)))
    parts.append(rbox("knob_grip", (0.006, 0.01, 0.024), (0.1, -0.166, 0.042), 0.002, M["knob"], 2))
    parts.append(sphere("led_r", 0.0035, (0.05, -0.1495, 0.05), M["led_r"], 12, 8))
    parts.append(sphere("led_g", 0.0035, (0.05, -0.1495, 0.036), M["led_g"], 12, 8))
    for sx in (-1, 1):
        parts.append(cyl("hinge", 0.009, 0.05, (sx * 0.08, 0.152, 0.086), M["plate"], (0, math.radians(90), 0), 20))
        for sy in (-1, 1):
            parts.append(cyl("foot", 0.012, 0.014, (sx * 0.12, sy * 0.11, 0.007), M["rubber"], verts=20))
    finalize(parts)
    body = join(parts, name)
    shade(body, 40)

    # 뚜껑 (닫힌 상태로 만든 뒤 경첩 기준으로 열기)
    lp = [rbox("lid_body", (0.30, 0.30, 0.04), (0, 0, 0.112), 0.016, M["body_red"], 5)]
    lp.append(rbox("lid_plate", (0.27, 0.25, 0.008), (0, 0, 0.0925), 0.003, M["plate"], 2))
    for k in range(6):
        y = -0.09 + 0.036 * k
        lp.append(rbox("lid_ridge", (0.24, 0.006, 0.004), (0, y, 0.0875), 0.0018, M["plate"], 2))
    lp.append(rbox("handle_bar", (0.13, 0.016, 0.016), (0, -0.184, 0.114), 0.006, M["knob"], 3))
    for sx in (-1, 1):
        lp.append(rbox("handle_post", (0.016, 0.042, 0.012), (sx * 0.05, -0.162, 0.114), 0.004, M["knob"], 2))
    finalize(lp)
    lid = join(lp, name + "_Lid")
    lid.data.transform(Matrix.Translation(-HINGE))
    lid.location = HINGE
    shade(lid, 40)
    lid.rotation_euler = (math.radians(-100), 0, 0)
    lid.parent = body
    return body


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


LBL_K = make_tex_mat("Ketchup_Label", 384, 256, ketchup_label)
LBL_M = make_tex_mat("Mustard_Label", 384, 256, mustard_label)

ITEMS = [
    (lambda: sandwich_press("Sandwich_Press"), "Sandwich_Press", (-0.56, 0.30)),
    (lambda: sandwich_plate("Sandwich_Plate"), "Sandwich_Plate", (-0.18, 0.30)),
    (lambda: bread_stack("Bread_Stack"), "Bread_Stack", (0.06, 0.32)),
    (lambda: sauce_bottle("Ketchup_Bottle", M["ketchup_btl"], LBL_K), "Ketchup_Bottle", (0.22, 0.32)),
    (lambda: sauce_bottle("Mustard_Bottle", M["mustard_btl"], LBL_M), "Mustard_Bottle", (0.32, 0.32)),
    (lambda: ham_slices("Ham_Slices"), "Ham_Slices", (-0.62, 0.0)),
    (lambda: butter_bowl("Butter_Bowl"), "Butter_Bowl", (-0.47, 0.0)),
    (lambda: lettuce("Lettuce"), "Lettuce", (-0.31, 0.0)),
    (lambda: cheese_slices("Cheese_Slices"), "Cheese_Slices", (-0.10, 0.0)),
    (lambda: bread_slice("Bread_Slice", False), "Bread_Slice", (0.10, 0.0)),
    (lambda: toasted_sandwich("Toasted_Sandwich"), "Toasted_Sandwich", (0.25, 0.0)),
]

for build, nm, pos in ITEMS:
    root = build()
    if root.location.length > 0 and not root.children:
        bake_loc(root)
    export(root, nm)
    root.location = (pos[0], pos[1], 0)
    print("built", nm, len(root.data.polygons), "faces")

# 미리보기용: 프레스 안에 토스트 하나 올려두기 (내보내기에는 포함 안 됨)
pv = toasted_sandwich("_PreviewToast", math.radians(-8))
pv.location = (-0.56, 0.29, BODY_TOP + 0.009)

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
    for nm, (tx, ty), sc in (("press", (-0.56, 0.36), 0.42), ("plate", (-0.05, 0.32), 0.5),
                             ("front", (-0.38, 0.0), 0.55), ("front2", (0.1, 0.0), 0.5)):
        shot(os.path.join(OUT, "_check_" + nm + ".png"), tx, ty, sc, 1000, 700)
shot(os.path.join(OUT, "preview.png"), -0.15, 0.2, 1.25)

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "sandwich_set.blend"))
print("DONE")
