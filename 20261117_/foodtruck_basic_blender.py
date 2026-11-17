# ============================================================
#  푸드트럭 v2 - 지오메트리 라이브러리 (순수 파이썬, Blender/GLB 공용)
#  좌표계: Blender 기준 (X=오른쪽, Y=안쪽, Z=위), 단위 m
#  - rbox  : 모서리가 둥근 박스 (베벨 + 부드러운 노멀)
#  - lathe : 회전체 (냄비/프라이팬/병/과일 등)  profile=[(반지름, 높이), ...]
#  - hexa  : 8개 꼭짓점으로 만드는 자유 육면체 (기울어진 차양/후드 등)
# ============================================================
import math

PARTS = []        # 일반 파츠
ROOF_PARTS = []   # 지붕 (단면 보기용으로 숨길 수 있음)
_cur = ["Truck"]


def group(name):
    _cur[0] = name


def _add(kind, name, color, roof, **kw):
    d = dict(kind=kind, name=name, color=color, group=_cur[0], **kw)
    (ROOF_PARTS if roof else PARTS).append(d)


def box(name, x0, x1, y0, y1, z0, z1, color, r=0.008, roof=False):
    _add("box", name, color, roof, b=(x0, x1, y0, y1, z0, z1), r=r)


def lathe(name, cx, cy, z0, profile, color, seg=28, rot=None):
    _add("lathe", name, color, False, c=(cx, cy, z0), prof=list(profile), seg=seg, rot=rot or [])


def hexa(name, pts, color):
    _add("hexa", name, color, False, pts=list(pts))


# ---------------- 메쉬 누적기 (같은 위치+노멀 정점은 합침) ----------------
class MB:
    def __init__(s):
        s.v, s.n, s.f, s.m = [], [], [], {}

    def vert(s, p, n):
        k = (round(p[0], 5), round(p[1], 5), round(p[2], 5),
             round(n[0], 3), round(n[1], 3), round(n[2], 3))
        i = s.m.get(k)
        if i is None:
            i = len(s.v)
            s.m[k] = i
            s.v.append((p[0], p[1], p[2]))
            s.n.append((n[0], n[1], n[2]))
        return i

    def face(s, *ids):
        if len(set(ids)) >= 3:
            s.f.append(tuple(ids))


def _axis_coords(a, b, r, steps):
    ds = [r * math.tan(math.radians(45.0 * k / steps)) for k in range(steps + 1)]
    return [a + r - d for d in reversed(ds)] + [b - r + d for d in ds]


def _rbox(mb, bb, r):
    lo = [bb[0], bb[2], bb[4]]
    hi = [bb[1], bb[3], bb[5]]
    r = min([r] + [(hi[i] - lo[i]) * 0.499 for i in range(3)])
    flat = r <= 0.003
    steps = 3 if r >= 0.02 else 2
    co = [[lo[i], hi[i]] if flat else _axis_coords(lo[i], hi[i], r, steps) for i in range(3)]
    for k in range(3):
        u, v = (k + 1) % 3, (k + 2) % 3
        for side in (0, 1):
            fixed = hi[k] if side else lo[k]
            grid = []
            for a in co[u]:
                row = []
                for b in co[v]:
                    p = [0.0, 0.0, 0.0]
                    p[k], p[u], p[v] = fixed, a, b
                    if flat:
                        n = [0.0, 0.0, 0.0]
                        n[k] = 1.0 if side else -1.0
                        pos = p
                    else:
                        cl = [min(max(p[i], lo[i] + r), hi[i] - r) for i in range(3)]
                        d = [p[i] - cl[i] for i in range(3)]
                        l = math.sqrt(sum(t * t for t in d)) or 1.0
                        n = [t / l for t in d]
                        pos = [cl[i] + n[i] * r for i in range(3)]
                    row.append(mb.vert(pos, n))
                grid.append(row)
            for i in range(len(co[u]) - 1):
                for j in range(len(co[v]) - 1):
                    q = (grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1])
                    mb.face(*(q if side else q[::-1]))


def _rot(p, ops):
    x, y, z = p
    for ax, deg in ops:
        c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
        if ax == "x":
            y, z = y * c - z * s, y * s + z * c
        elif ax == "y":
            x, z = x * c + z * s, -x * s + z * c
        else:
            x, y = x * c - y * s, x * s + y * c
    return (x, y, z)


def _lathe(mb, c, prof, seg, rot):
    cx, cy, z0 = c
    P = []
    for i, p in enumerate(prof):
        p = (round(p[0], 6), round(p[1], 6))
        if not P or p != P[-1]:
            P.append(p)
    m = len(P) - 1
    SN = []
    for i in range(m):
        dr, dz = P[i + 1][0] - P[i][0], P[i + 1][1] - P[i][1]
        l = math.hypot(dr, dz)
        SN.append((dz / l, -dr / l))

    def endn(i, start):
        own = SN[i]
        oth = None
        if start and i > 0:
            oth = SN[i - 1]
        if (not start) and i < m - 1:
            oth = SN[i + 1]
        if oth and own[0] * oth[0] + own[1] * oth[1] > 0.64:
            s = (own[0] + oth[0], own[1] + oth[1])
            l = math.hypot(*s)
            return (s[0] / l, s[1] / l)
        return own

    cs = []
    for i in range(seg + 1):
        a = 2 * math.pi * (i % seg) / seg
        cs.append((math.cos(a), math.sin(a)))

    def V(r, z, nn, i):
        co, si = cs[i]
        pos = _rot((r * co, r * si, z), rot)
        nrm = _rot((nn[0] * co, nn[0] * si, nn[1]), rot)
        return mb.vert((pos[0] + cx, pos[1] + cy, pos[2] + z0), nrm)

    for j in range(m):
        (r0, za), (r1, zb) = P[j], P[j + 1]
        n0, n1 = endn(j, True), endn(j, False)
        for i in range(seg):
            if abs(r0) < 1e-9:
                mb.face(V(r0, za, n0, i), V(r1, zb, n1, i + 1), V(r1, zb, n1, i))
            elif abs(r1) < 1e-9:
                mb.face(V(r0, za, n0, i), V(r0, za, n0, i + 1), V(r1, zb, n1, i))
            else:
                mb.face(V(r0, za, n0, i), V(r0, za, n0, i + 1), V(r1, zb, n1, i + 1), V(r1, zb, n1, i))


def _hexa(mb, pts):
    F = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    for f in F:
        a, b, c = pts[f[0]], pts[f[1]], pts[f[2]]
        u = [b[i] - a[i] for i in range(3)]
        w = [c[i] - a[i] for i in range(3)]
        n = [u[1] * w[2] - u[2] * w[1], u[2] * w[0] - u[0] * w[2], u[0] * w[1] - u[1] * w[0]]
        l = math.sqrt(sum(t * t for t in n)) or 1.0
        n = [t / l for t in n]
        mb.face(*[mb.vert(pts[i], n) for i in f])


def build_mesh(p):
    mb = MB()
    if p["kind"] == "box":
        _rbox(mb, p["b"], p["r"])
    elif p["kind"] == "lathe":
        _lathe(mb, p["c"], p["prof"], p["seg"], p["rot"])
    else:
        _hexa(mb, p["pts"])
    return mb.v, mb.n, mb.f


# ---------------- 재질 ----------------
PALETTE = dict(
    wall=(0.96, 0.91, 0.82), body=(0.35, 0.72, 0.68), floor_base=(0.70, 0.69, 0.66),
    tile_a=(0.94, 0.91, 0.85), tile_b=(0.84, 0.80, 0.72), mat=(0.26, 0.30, 0.36),
    cabinet=(0.27, 0.56, 0.54), door=(0.42, 0.75, 0.71), kick=(0.13, 0.18, 0.18),
    steel=(0.80, 0.82, 0.84), chrome=(0.92, 0.94, 0.96), dark=(0.09, 0.09, 0.10),
    iron=(0.17, 0.17, 0.19), wood=(0.76, 0.56, 0.34), wood_dark=(0.44, 0.29, 0.17),
    board=(0.88, 0.74, 0.54), cream=(0.98, 0.95, 0.88), fridge=(0.80, 0.92, 0.90),
    tile_wall=(0.97, 0.97, 0.95), grout=(0.76, 0.78, 0.78),
    red=(0.88, 0.20, 0.15), yellow=(0.97, 0.82, 0.18), orange=(0.97, 0.55, 0.12),
    green=(0.36, 0.68, 0.26), lime=(0.62, 0.80, 0.20), purple=(0.45, 0.20, 0.50),
    brown=(0.45, 0.27, 0.12), white=(0.97, 0.97, 0.97), kraft=(0.80, 0.64, 0.44),
    screen=(0.30, 0.62, 0.90), terracotta=(0.78, 0.42, 0.28), apple=(0.55, 0.78, 0.25),
    cabbage=(0.65, 0.82, 0.50), pepper=(0.20, 0.60, 0.20), potato=(0.70, 0.55, 0.35),
    chalk=(0.92, 0.92, 0.88), board_green=(0.16, 0.24, 0.22), pink=(0.95, 0.55, 0.60),
    egg=(0.99, 0.99, 0.96), gold=(0.90, 0.72, 0.25), glass=(0.75, 0.88, 0.92),
)
METALS = {"steel", "chrome", "iron", "gold"}


def mat_props(name):
    if name in METALS:
        return PALETTE[name], 0.55, 0.30
    return PALETTE[name], 0.0, 0.65
# ============================================================
#  푸드트럭 v2 - 레이아웃
#  스케치 방향: 위쪽(화구) = 뒷벽(Y=W), 아래쪽(POS) = 앞면(Y=0, 판매 창구)
# ============================================================
L, W, H = 6.4, 3.6, 2.7       # 실내 길이 / 폭 / 높이 (m)  -> 플레이어가 돌아다니기 넉넉한 크기
T = 0.10                      # 벽 두께
CH, TOP = 0.90, 0.05          # 캐비닛 높이 / 상판 두께
CT = CH + TOP                 # 상판 윗면 높이 0.95
BD, FD = 0.70, 0.60           # 뒤쪽 / 앞쪽 카운터 깊이
BY = W - BD                   # 뒤 카운터 앞면 Y = 2.9
RX = L - 0.75                 # 오른쪽 식재료 구역 시작 X = 5.65
WX0, WX1, WZ1 = 2.2, 4.4, 2.05   # 판매 창구 범위


# ---------------- 헬퍼 ----------------
def cyl(name, cx, cy, z0, r, h, color, seg=24):
    lathe(name, cx, cy, z0, [(0, 0), (r, 0), (r, h), (0, h)], color, seg)


def ball(name, cx, cy, cz, r, color, seg=14):
    pts = [(r * math.sin(math.radians(i * 22.5)), r - r * math.cos(math.radians(i * 22.5))) for i in range(9)]
    lathe(name, cx, cy, cz - r, pts, color, seg)


def bowl_profile(R, t, amax=90, n=7):
    ang = [amax * i / (n - 1) for i in range(n)]
    out = [(R * math.sin(math.radians(a)), R - R * math.cos(math.radians(a))) for a in ang]
    inn = [((R - t) * math.sin(math.radians(a)), R - (R - t) * math.cos(math.radians(a))) for a in reversed(ang)]
    return out + inn


def pan_profile(s=1.0):
    base = [(0, 0), (0.10, 0), (0.125, 0.035), (0.135, 0.045), (0.128, 0.047),
            (0.118, 0.036), (0.095, 0.012), (0.0, 0.012)]
    return [(r * s, z * s) for r, z in base]


def wall(name, x0, x1, y0, y1, z0, z1, out):
    """안쪽은 크림색, 바깥 3cm 는 트럭 바디색"""
    t = 0.03
    if out == "+y":
        a, b = (x0, x1, y0, y1 - t), (x0, x1, y1 - t, y1)
    elif out == "-y":
        a, b = (x0, x1, y0 + t, y1), (x0, x1, y0, y0 + t)
    elif out == "-x":
        a, b = (x0 + t, x1, y0, y1), (x0, x0 + t, y0, y1)
    else:
        a, b = (x0, x1 - t, y0, y1), (x1 - t, x1, y0, y1)
    box(name + "_In", a[0], a[1], a[2], a[3], z0, z1, "wall", r=0)
    box(name + "_Out", b[0], b[1], b[2], b[3], z0, z1, "body", r=0)


def cabinet(name, x0, x1, y0, y1, face, top="steel", body="cabinet"):
    box(name + "_Kick", x0 + 0.02, x1 - 0.02, y0 + 0.02, y1 - 0.02, 0, 0.09, "kick", r=0)
    box(name + "_Body", x0, x1, y0, y1, 0.09, CH, body, r=0.01)
    tx0, tx1, ty0, ty1 = x0, x1, y0, y1
    if face == "-y":
        ty0 -= 0.03
    elif face == "+y":
        ty1 += 0.03
    elif face == "-x":
        tx0 -= 0.03
    box(name + "_Top", tx0, tx1, ty0, ty1, CH, CT, top, r=0.012)


def _pbox(face, plane, nm, sa, sb, za, zb, d0, d1, col, r):
    if face == "-y":
        box(nm, sa, sb, plane - d1, plane - d0, za, zb, col, r)
    elif face == "+y":
        box(nm, sa, sb, plane + d0, plane + d1, za, zb, col, r)
    else:  # -x
        box(nm, plane - d1, plane - d0, sa, sb, za, zb, col, r)


def fronts(name, face, plane, a0, a1, kinds, z0=0.12, z1=CH - 0.02):
    n = len(kinds)
    w = (a1 - a0) / n
    th = 0.022
    for i, kd in enumerate(kinds):
        s0, s1 = a0 + i * w + 0.01, a0 + (i + 1) * w - 0.01
        if kd == "door":
            _pbox(face, plane, f"{name}_{i}_Door", s0, s1, z0, z1, 0, th, "door", 0.012)
        else:
            zm = (z0 + z1) / 2
            for k, (za, zb) in enumerate([(z0, zm - 0.005), (zm + 0.005, z1)]):
                _pbox(face, plane, f"{name}_{i}_Drawer{k}", s0, s1, za, zb, 0, th, "door", 0.012)


def produce_crate(name, x0, x1, y0, y1, z, kind):
    col, rad = kind
    box(name + "_Base", x0, x1, y0, y1, z, z + 0.015, "wood", r=0.004)
    box(name + "_WallF", x0, x0 + 0.012, y0, y1, z, z + 0.13, "wood", r=0.004)
    box(name + "_WallB", x1 - 0.012, x1, y0, y1, z, z + 0.13, "wood", r=0.004)
    box(name + "_WallL", x0, x1, y0, y0 + 0.012, z, z + 0.13, "wood", r=0.004)
    box(name + "_WallR", x0, x1, y1 - 0.012, y1, z, z + 0.13, "wood", r=0.004)
    ny = max(2, int((y1 - y0 - 0.03) / (rad * 2.1)))
    xs = [x0 + (x1 - x0) * 0.30, x0 + (x1 - x0) * 0.72]
    for ix, px in enumerate(xs):
        for iy in range(ny):
            py = y0 + (y1 - y0) * (iy + 0.5) / ny
            ball(f"{name}_F{ix}{iy}", px, py, z + 0.015 + rad, rad, col)
    for iy in range(ny - 1):
        py = y0 + (y1 - y0) * (iy + 1.0) / ny
        ball(f"{name}_T{iy}", (xs[0] + xs[1]) / 2, py, z + 0.015 + rad * 2.0, rad, col)


# ============================================================
# 1) 바닥 / 벽 / 지붕
# ============================================================
group("Floor")
box("Floor_Base", -T, L + T, -T, W + T, -0.10, -0.02, "floor_base", r=0)
TS = 0.4
for i in range(int(round(L / TS))):
    for j in range(int(round(W / TS))):
        box(f"Tile_{i}_{j}", i * TS + 0.004, (i + 1) * TS - 0.004, j * TS + 0.004, (j + 1) * TS - 0.004,
            -0.02, 0, "tile_a" if (i + j) % 2 == 0 else "tile_b", r=0)

group("Walls")
wall("Wall_Back", -T, L + T, W, W + T, 0, H, "+y")
wall("Wall_Left", -T, 0, 0, W, 0, H, "-x")
wall("Wall_Right", L, L + T, 0, W, 0, H, "+x")
wall("Wall_Front_L", -T, WX0, -T, 0, 0, H, "-y")
wall("Wall_Front_R", WX1, L + T, -T, 0, 0, H, "-y")
wall("Wall_Front_Below", WX0, WX1, -T, 0, 0, CT - TOP, "-y")
wall("Wall_Front_Above", WX0, WX1, -T, 0, WZ1, H, "-y")
box("Roof", -T, L + T, -T, W + T, H, H + T, "wall", r=0, roof=True)

# ============================================================
# 2) 판매 창구 + 어닝
# ============================================================
group("ServiceWindow")
box("Window_Sill", WX0, WX1, -T, 0, CT - TOP, CT, "wood", r=0.01)
box("Window_Ledge", WX0 - 0.05, WX1 + 0.05, -0.50, -T, CT - TOP, CT, "wood", r=0.012)
box("Window_Frame_Top", WX0, WX1, -T - 0.01, 0.01, WZ1 - 0.06, WZ1, "cream", r=0.01)
box("Window_Frame_L", WX0, WX0 + 0.06, -T - 0.01, 0.01, CT, WZ1 - 0.06, "cream", r=0.01)
box("Window_Frame_R", WX1 - 0.06, WX1, -T - 0.01, 0.01, CT, WZ1 - 0.06, "cream", r=0.01)

group("Awning")
ax0 = WX0 - 0.2
n_st = 13
sw = (WX1 + 0.2 - ax0) / n_st
for i in range(n_st):
    c = "red" if i % 2 == 0 else "white"
    x0, x1 = ax0 + i * sw, ax0 + (i + 1) * sw
    hexa(f"Awning_Stripe_{i}", [(x0, -0.80, 1.98), (x1, -0.80, 1.98), (x1, -T, 2.34), (x0, -T, 2.34),
                                (x0, -0.80, 2.02), (x1, -0.80, 2.02), (x1, -T, 2.38), (x0, -T, 2.38)], c)
    box(f"Awning_Valance_{i}", x0, x1, -0.82, -0.79, 1.84, 2.02, c, r=0.004)

# ============================================================
# 3) 뒷벽 : 화구 카운터 / 후드 / 조리도구 카운터
# ============================================================
group("Stove")
cabinet("Stove_Cab", 0, 4.0, BY, W, "-y", top="steel")
fronts("Stove_Front", "-y", BY, 0, 4.0, ["door", "drawers", "door", "drawers"])

group("Hood")
hexa("Hood_Body", [(0.05, 2.45, 1.90), (3.95, 2.45, 1.90), (3.95, W, 1.90), (0.05, W, 1.90),
                   (1.00, 3.15, 2.45), (3.00, 3.15, 2.45), (3.00, W, 2.45), (1.00, W, 2.45)], "steel")
box("Hood_Duct", 1.3, 2.7, 3.3, W, 2.45, H, "steel", r=0.01)

group("Utensil")
cabinet("Prep_Cab", 4.0, RX, BY, W, "-y", top="wood")
fronts("Prep_Front", "-y", BY, 4.0, RX, ["door", "door"])

# ============================================================
# 4) 오른쪽 뒤 모서리 : 냉장고 (싱크대 자리, 문이 앞쪽 = 어닝 방향(-Y)을 봄)
# ============================================================
group("Fridge")
box("Fridge_Kick", RX + 0.03, L - 0.03, BY + 0.03, W - 0.03, 0, 0.07, "kick", r=0)
box("Fridge_Body", RX, L, BY, W, 0.07, 1.95, "fridge", r=0.03)
box("Fridge_Door_Top", RX + 0.02, L - 0.02, BY - 0.04, BY, 1.14, 1.93, "fridge", r=0.02)
box("Fridge_Door_Bottom", RX + 0.02, L - 0.02, BY - 0.04, BY, 0.09, 1.12, "fridge", r=0.02)
box("Fridge_Handle_Top", RX + 0.06, RX + 0.085, BY - 0.075, BY - 0.04, 1.45, 1.80, "chrome", r=0.01)
box("Fridge_Handle_Bottom", RX + 0.06, RX + 0.085, BY - 0.075, BY - 0.04, 0.75, 1.05, "chrome", r=0.01)

# ============================================================
# 5) 오른쪽 벽 : 식재료 선반 뼈대 (빈 선반, 냉장고 문이 열릴 공간 확보)
# ============================================================
group("Shelves")
SY1 = BY - 0.80
bays = [FD, FD + (SY1 - FD) / 3, FD + 2 * (SY1 - FD) / 3, SY1]
tiers = [0.04, 0.50, 0.96, 1.42, 1.88]
for j, yy in enumerate(bays):
    box(f"Shelf_Post_F{j}", RX, RX + 0.04, yy - 0.02, yy + 0.02, 0.0, 1.91, "cabinet", r=0.006)
    box(f"Shelf_Post_B{j}", L - 0.04, L, yy - 0.02, yy + 0.02, 0.0, 1.91, "cabinet", r=0.006)
for k, z in enumerate(tiers):
    box(f"Shelf_Board_{k}", RX, L, FD, SY1, z, z + 0.03, "steel", r=0.006)

group("Storage")
box("Storage_Cab_Right", RX, L, 0, FD, 0, CH, "cabinet", r=0.01)
box("Storage_Cab_Right_Top", RX, L, 0, FD, CH, CT, "steel", r=0.012)

# ============================================================
# 6) 앞 카운터 (소스 / 포장지 / POS 자리)
# ============================================================
group("FrontCounter")
cabinet("Front_Cab", 0, RX, 0, FD, "+y", top="wood")
fronts("Front_Doors", "+y", FD, 0, RX, ["door", "drawers", "door", "drawers", "door", "door", "drawers", "door"])


# ============================================================
# 파츠 → 오브젝트 그룹핑 (같은 기능의 파츠를 하나의 메쉬로 합침)
# ============================================================
UNIT_RENAME = {"Stove": "Stove_Cabinet", "Utensil": "Prep_Cabinet", "FrontCounter": "Front_Counter",
               "Storage": "Storage_Cabinet", "ServiceWindow": "Service_Window"}

# 열리는 문: 오브젝트 원점(피벗) = 힌지 위치. 냉장고 문은 오른쪽 힌지, 앞쪽(-Y)으로 열림.
HINGE_X, HINGE_Y = L - 0.02, BY - 0.02
DOOR_Z0 = {"Fridge_Door_Top": 1.14, "Fridge_Door_Bottom": 0.09}
PIVOTS = {k: (HINGE_X, HINGE_Y, z) for k, z in DOOR_Z0.items()}
DOOR_OPEN_DEG = 100          # 열렸을 때 각도 (Blender Z축 +회전 = 앞쪽으로 열림)


def unit_of(p):
    n = p["name"]
    if n.startswith("Tile_") or n == "Floor_Base":
        return "Floor"
    if n.startswith("Wall_"):
        return "_".join(n.split("_")[:2])
    if n.endswith("_Top") and n.startswith(("Fridge_Door", "Fridge_Handle")):
        return "Fridge_Door_Top"
    if n.endswith("_Bottom") and n.startswith(("Fridge_Door", "Fridge_Handle")):
        return "Fridge_Door_Bottom"
    return UNIT_RENAME.get(p["group"], p["group"])


def merged_units(parts):
    """{unit: dict(group, pivot, v, n, f, m)}  정점은 pivot 기준 상대좌표 (pivot=(0,0,0)이면 월드좌표)"""
    out = {}
    for p in parts:
        u = unit_of(p)
        grp = "Walls" if u.startswith("Wall") else ("Floor" if u == "Floor" else p["group"])
        d = out.setdefault(u, dict(group=grp, pivot=PIVOTS.get(u, (0.0, 0.0, 0.0)), v=[], n=[], f=[], m=[]))
        v, n, f = build_mesh(p)
        o = len(d["v"])
        px, py, pz = d["pivot"]
        d["v"] += [(a[0] - px, a[1] - py, a[2] - pz) for a in v]
        d["n"] += n
        d["f"] += [tuple(i + o for i in fc) for fc in f]
        d["m"] += [p["color"]] * len(f)
    return out


# ============================================================
#  Blender 빌더  -  Scripting 탭에 이 파일 전체를 붙여넣고 Run Script
#  파츠를 기능별로 합쳐 오브젝트 14개로 만듭니다 (재질은 오브젝트 안에서 슬롯으로 분리)
# ============================================================
import bpy

SHOW_ROOF = False          # True: 지붕 표시 (기본은 단면 보기용으로 숨김)
SHOW_HOOD = True           # False: 후드 숨김
ADD_CAMERA_LIGHT = True    # 쿼터뷰 카메라 + 조명 추가
ANIMATE_DOORS = True       # 냉장고 문 열림/닫힘 애니메이션(키프레임) 추가. 재생(Space)하면 열렸다 닫힘
EXPORT_PATH = ""           # 예: r"C:/work/foodtruck.fbx" 또는 .glb  (비우면 내보내기 안 함)


def get_material(name, cache={}):
    if name in cache and cache[name].name in bpy.data.materials:
        return cache[name]
    rgb, metal, rough = mat_props(name)
    m = bpy.data.materials.new("M_" + name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    m.diffuse_color = (*rgb, 1)
    cache[name] = m
    return m


def build():
    root = bpy.data.collections.get("FoodTruck")
    if root:                                   # 다시 실행하면 이전 결과를 지우고 새로 만듦
        for c in list(root.children):
            for o in list(c.objects):
                bpy.data.objects.remove(o, do_unlink=True)
            bpy.data.collections.remove(c)
        for o in list(root.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    else:
        root = bpy.data.collections.new("FoodTruck")
        bpy.context.scene.collection.children.link(root)

    parts = list(PARTS) + (list(ROOF_PARTS) if SHOW_ROOF else [])
    if not SHOW_HOOD:
        parts = [p for p in parts if p["group"] != "Hood"]

    subs, made = {}, []
    for uname, d in merged_units(parts).items():
        me = bpy.data.meshes.new(uname)
        me.from_pydata(d["v"], [], d["f"])
        me.update()
        slots = []
        for c in d["m"]:
            if c not in slots:
                slots.append(c)
        for c in slots:
            me.materials.append(get_material(c))
        me.polygons.foreach_set("material_index", [slots.index(c) for c in d["m"]])
        try:                                    # 부드러운 노멀 (Blender 버전별 대응)
            if hasattr(me, "use_auto_smooth"):
                me.use_auto_smooth = True
            me.polygons.foreach_set("use_smooth", [True] * len(me.polygons))
            me.normals_split_custom_set_from_vertices(d["n"])
        except Exception as e:
            print("custom normals skipped:", e)
        ob = bpy.data.objects.new(uname, me)
        ob.location = d["pivot"]                # 문은 힌지 위치가 원점
        g = d["group"]
        if g not in subs:
            subs[g] = bpy.data.collections.new(g)
            root.children.link(subs[g])
        subs[g].objects.link(ob)
        made.append(ob)
        if uname in PIVOTS and ANIMATE_DOORS:   # 열림 애니메이션: 1프레임 닫힘 → 24 열림 → 48 닫힘
            ob.rotation_mode = "XYZ"
            for fr, deg in ((1, 0), (24, DOOR_OPEN_DEG), (48, 0)):
                ob.rotation_euler = (0, 0, math.radians(deg))
                ob.keyframe_insert("rotation_euler", index=2, frame=fr)
            ob.rotation_euler = (0, 0, 0)

    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end = 48

    if ADD_CAMERA_LIGHT:
        cam = bpy.data.objects.new("TruckCam", bpy.data.cameras.new("TruckCam"))
        cam.location = (L / 2 + 2.5, -6.5, 5.5)
        cam.rotation_euler = (math.radians(55), 0, math.radians(20))
        root.objects.link(cam)
        bpy.context.scene.camera = cam
        sun = bpy.data.objects.new("TruckSun", bpy.data.lights.new("TruckSun", "SUN"))
        sun.data.energy = 3.5
        sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(30))
        root.objects.link(sun)
        pt = bpy.data.objects.new("TruckLamp", bpy.data.lights.new("TruckLamp", "POINT"))
        pt.data.energy = 600
        pt.location = (L / 2, W / 2, H - 0.3)
        root.objects.link(pt)

    if EXPORT_PATH:
        bpy.ops.object.select_all(action="DESELECT")
        for o in made:
            o.select_set(True)
        if EXPORT_PATH.lower().endswith(".fbx"):
            bpy.ops.export_scene.fbx(filepath=EXPORT_PATH, use_selection=True, apply_scale_options="FBX_SCALE_ALL")
        else:
            bpy.ops.export_scene.gltf(filepath=EXPORT_PATH, use_selection=True)


build()
