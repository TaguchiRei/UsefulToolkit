# 遺跡アリーナ環境アセットの各パーツ生成関数。寸法は MODELING_RULES.md の基本寸法表に対応する。
import math
import random

from mathutils import Vector

import envlib as L
from envlib import ACCENT, BASE, EMIT, STRUCT, Builder, rot_matrix


# ---------------------------------------------------------------- 床・地形

ARENA_TOP_Z = 0.35
ARENA_STEP_Z = 0.18
ARENA_R = 12.0
ARENA_STEP_R = 13.0
ARENA_SEG = 32
# 上面の同心円帯: (内半径, 外半径, マテリアル指定)。"tile" は区画ごとに Base/Accent を交互にする
ARENA_BANDS = [
    (0.0, 2.5, ACCENT),
    (2.5, 3.0, STRUCT),
    (3.0, 7.0, "tile"),
    (7.0, 7.5, STRUCT),
    (7.5, 11.5, "tile2"),
    (11.5, 12.0, STRUCT),
]


def _ring_pt(r, i, seg, z):
    a = 2 * math.pi * i / seg
    return (r * math.cos(a), r * math.sin(a), z)


def arena_floor():
    b = Builder()
    up = (0, 0, 1)
    for r0, r1, m in ARENA_BANDS:
        for i in range(ARENA_SEG):
            j = i + 1
            if m == "tile":
                mat = BASE if (i // 2) % 2 == 0 else ACCENT
            elif m == "tile2":
                mat = ACCENT if (i // 2) % 2 == 0 else BASE
            else:
                mat = m
            if r0 == 0.0:
                pts = [(0, 0, ARENA_TOP_Z), _ring_pt(r1, i, ARENA_SEG, ARENA_TOP_Z), _ring_pt(r1, j, ARENA_SEG, ARENA_TOP_Z)]
            else:
                pts = [_ring_pt(r0, i, ARENA_SEG, ARENA_TOP_Z), _ring_pt(r1, i, ARENA_SEG, ARENA_TOP_Z),
                       _ring_pt(r1, j, ARENA_SEG, ARENA_TOP_Z), _ring_pt(r0, j, ARENA_SEG, ARENA_TOP_Z)]
            b.add_face(pts, mat, expected=up)
    for i in range(ARENA_SEG):
        j = i + 1
        a = 2 * math.pi * (i + 0.5) / ARENA_SEG
        out = (math.cos(a), math.sin(a), 0)
        # 上段の側面
        b.add_face([_ring_pt(ARENA_R, i, ARENA_SEG, ARENA_STEP_Z), _ring_pt(ARENA_R, j, ARENA_SEG, ARENA_STEP_Z),
                    _ring_pt(ARENA_R, j, ARENA_SEG, ARENA_TOP_Z), _ring_pt(ARENA_R, i, ARENA_SEG, ARENA_TOP_Z)],
                   STRUCT, expected=out)
        # 下段の踏面
        b.add_face([_ring_pt(ARENA_R, i, ARENA_SEG, ARENA_STEP_Z), _ring_pt(ARENA_STEP_R, i, ARENA_SEG, ARENA_STEP_Z),
                    _ring_pt(ARENA_STEP_R, j, ARENA_SEG, ARENA_STEP_Z), _ring_pt(ARENA_R, j, ARENA_SEG, ARENA_STEP_Z)],
                   BASE, expected=up)
        # 下段の側面
        b.add_face([_ring_pt(ARENA_STEP_R, i, ARENA_SEG, 0.0), _ring_pt(ARENA_STEP_R, j, ARENA_SEG, 0.0),
                    _ring_pt(ARENA_STEP_R, j, ARENA_SEG, ARENA_STEP_Z), _ring_pt(ARENA_STEP_R, i, ARENA_SEG, ARENA_STEP_Z)],
                   STRUCT, expected=out)
    return b.build("Ruin_Floor_Arena_A")


GROUND_SEG = 32
GROUND_RINGS = [0.0, 14.0, 20.0, 28.0, 38.0, 50.0, 64.0, 78.0, 90.0]
GROUND_FLAT_R = 20.0


def ground():
    rng = random.Random(7)
    b = Builder()
    rings = []
    for r in GROUND_RINGS:
        ring = []
        for i in range(GROUND_SEG):
            a = 2 * math.pi * i / GROUND_SEG
            if r <= GROUND_FLAT_R:
                z = 0.0
            else:
                t = (r - GROUND_FLAT_R) / (GROUND_RINGS[-1] - GROUND_FLAT_R)
                z = rng.uniform(0.0, 1.0) * (0.6 + 2.4 * t)
            jr = 0.0 if r <= GROUND_FLAT_R else rng.uniform(-1.5, 1.5)
            ring.append(((r + jr) * math.cos(a), (r + jr) * math.sin(a), z))
        rings.append(ring)
    up = (0, 0, 1)
    for k in range(len(GROUND_RINGS) - 1):
        for i in range(GROUND_SEG):
            j = (i + 1) % GROUND_SEG
            mat = BASE
            if GROUND_RINGS[k] >= GROUND_FLAT_R - 6 and rng.random() < 0.35:
                mat = ACCENT
            if k == 0:
                b.add_face([(0, 0, 0), rings[1][i], rings[1][j]], BASE, expected=up)
            else:
                # 起伏で非平面にならないよう三角2枚に割る
                b.add_face([rings[k][i], rings[k + 1][i], rings[k + 1][j]], mat, expected=up)
                b.add_face([rings[k][i], rings[k + 1][j], rings[k][j]], mat, expected=up)
    return b.build("Ruin_Terrain_Ground_A")


def platform():
    b = Builder()
    b.box((0, 0), 0.0, (6.0, 4.0, 0.9), BASE, bottom=False)
    b.box((0, 0), 0.85, (6.2, 4.2, 0.15), STRUCT, mat_fn=L.up_is(ACCENT, STRUCT))
    return b.build("Ruin_Floor_Platform_A")


def stair():
    b = Builder()
    rise, run, steps = 0.25, 0.5, 4
    depth = run * steps
    poly = [(-depth / 2, 0.0), (depth / 2, 0.0), (depth / 2, rise * steps)]
    for s in range(steps - 1, 0, -1):
        y = -depth / 2 + run * s
        poly.append((y, rise * (s + 1)))
        poly.append((y, rise * s))
    poly.append((-depth / 2, rise))
    # U=+Y, V=+Z, 押し出し方向 W=+X
    b.extrude(poly, (0, 1, 0), (0, 0, 1), 3.0, BASE, cap_mat=BASE,
              side_mat_fn=lambda n: ACCENT if n.z > 0.5 else BASE)
    return b.build("Ruin_Stair_Stone_A")


# ---------------------------------------------------------------- 柱・アーチ・壁

def _pillar_base(b):
    b.box((0, 0), 0.0, (1.4, 1.4, 0.3), STRUCT, bottom=False)
    b.box((0, 0), 0.28, (1.15, 1.15, 0.22), STRUCT)


SHAFT_R = 0.5
SHAFT_SIDES = 8


def pillar_straight():
    b = Builder()
    _pillar_base(b)
    b.prism((0, 0), 0.48, SHAFT_R, SHAFT_R * 0.92, 5.04, SHAFT_SIDES, BASE, rot=22.5, top=False, bottom=False)
    b.box((0, 0), 5.5, (1.15, 1.15, 0.25), STRUCT)
    b.box((0, 0), 5.73, (1.4, 1.4, 0.27), STRUCT, mat_fn=L.up_is(ACCENT, STRUCT))
    return b.build("Ruin_Pillar_Stone_Straight")


def _broken_offsets(seed, amp_lo, amp_hi):
    rng = random.Random(seed)
    return [rng.uniform(amp_lo, amp_hi) for _ in range(SHAFT_SIDES)]


def pillar_broken_a():
    b = Builder()
    _pillar_base(b)
    b.prism((0, 0), 0.48, SHAFT_R, SHAFT_R * 0.96, 2.6, SHAFT_SIDES, BASE, rot=22.5, bottom=False,
            top_offsets=_broken_offsets(11, -0.1, 0.25), top_mat=ACCENT)
    return b.build("Ruin_Pillar_Stone_BrokenA")


def pillar_broken_b():
    b = Builder()
    _pillar_base(b)
    b.prism((0, 0), 0.48, SHAFT_R, SHAFT_R * 0.97, 0.95, SHAFT_SIDES, BASE, rot=22.5, bottom=False,
            top_offsets=_broken_offsets(23, -0.05, 0.3), top_mat=ACCENT)
    # 倒れた柱の断片（横倒しで地面に接する）
    m = rot_matrix(rx=90, rz=35)
    b.prism((1.6, 0.9), 0.0, SHAFT_R, SHAFT_R * 0.95, 1.8, SHAFT_SIDES, BASE, rot=22.5,
            top_offsets=_broken_offsets(31, -0.15, 0.2), top_mat=ACCENT, matrix=m,
            extra_offset=(0, 0, SHAFT_R * math.cos(math.pi / SHAFT_SIDES)))
    return b.build("Ruin_Pillar_Stone_BrokenB")


def arch():
    b = Builder()
    for sx in (-3.0, 3.0):
        b.box((sx, 0), 0.0, (1.3, 1.3, 0.4), STRUCT, bottom=False)
        b.box((sx, 0), 0.38, (1.0, 1.0, 4.24), BASE)
        b.box((sx, 0), 4.6, (1.3, 1.3, 0.27), STRUCT)
    b.box((0, 0), 4.85, (7.2, 1.2, 0.85), BASE, mat_fn=L.up_is(ACCENT, BASE))
    b.box((-0.9, 0), 5.68, (3.0, 1.0, 0.52), STRUCT, mat_fn=L.up_is(ACCENT, STRUCT))
    # 崩れて傾いた上段の石
    b.box((1.9, 0.05), 5.62, (1.4, 0.9, 0.45), BASE, matrix=rot_matrix(ry=-9, rz=4), mat_fn=L.up_is(ACCENT, BASE))
    return b.build("Ruin_Arch_Stone_A")


def wall_broken():
    b = Builder()
    poly = [(-3.0, 0.0), (3.0, 0.0), (3.0, 1.2), (2.4, 1.6), (2.0, 1.45), (1.6, 2.4), (0.9, 2.6),
            (0.5, 3.2), (-0.4, 3.0), (-0.9, 2.2), (-1.6, 2.5), (-2.2, 1.4), (-3.0, 1.0)]
    # U=+X, V=+Z, 押し出し方向 W=-Y
    b.extrude(poly, (1, 0, 0), (0, 0, 1), 0.8, BASE, cap_mat=BASE,
              side_mat_fn=lambda n: ACCENT if n.z > 0.3 else BASE)
    b.box((0, 0), 0.0, (6.3, 1.0, 0.4), STRUCT, bottom=False)
    b.box((2.2, -0.95), 0.0, (0.7, 0.5, 0.45), BASE, rz=18, mat_fn=L.up_is(ACCENT, BASE))
    b.box((-1.3, 0.9), 0.0, (0.6, 0.45, 0.4), BASE, rz=-25, mat_fn=L.up_is(ACCENT, BASE))
    return b.build("Ruin_Wall_Stone_BrokenA")


# ---------------------------------------------------------------- 小物

def brazier():
    b = Builder()
    b.prism((0, 0), 0.0, 0.36, 0.3, 0.15, 8, STRUCT, rot=22.5, bottom=False)
    b.prism((0, 0), 0.13, 0.13, 0.11, 0.7, 8, STRUCT, rot=22.5, top=False, bottom=False)
    b.prism((0, 0), 0.8, 0.2, 0.45, 0.3, 8, STRUCT, rot=22.5, top=False)
    # 燠火の面（器の縁より少し下）
    b.prism((0, 0), 1.0, 0.39, 0.4, 0.06, 8, EMIT, rot=22.5, bottom=False)
    rng = random.Random(5)
    flames = [((0.0, 0.0), 0.26, 0.78), ((0.13, 0.08), 0.17, 0.5), ((-0.12, 0.1), 0.16, 0.45),
              ((0.02, -0.14), 0.15, 0.42)]
    for (fx, fy), r, h in flames:
        pts = []
        a0 = rng.uniform(0, 90)
        for i in range(5):
            a = math.radians(a0 + 72 * i)
            pts.append(Vector((fx + r * math.cos(a), fy + r * math.sin(a), 1.02)))
        for i in range(5):
            a = math.radians(a0 + 36 + 72 * i)
            pts.append(Vector((fx + r * 0.6 * math.cos(a), fy + r * 0.6 * math.sin(a), 1.02 + h * 0.45)))
        pts.append(Vector((fx + rng.uniform(-0.06, 0.06), fy + rng.uniform(-0.06, 0.06), 1.02 + h)))
        b.hull(pts, EMIT)
    return b.build("Ruin_Prop_Brazier_A")


def crate():
    b = Builder()
    s, t = 1.0, 0.12
    h = s / 2
    b.box((0, 0), 0.0, (s - 0.04, s - 0.04, s - 0.02), BASE, bottom=False)
    # X 方向の枠材は全長、Y 方向はその間、縦材は上下の枠材の間に収める（同一平面の重なりを作らない）
    for y in (-h + t / 2, h - t / 2):
        for z in (0.0, s - t):
            b.box((0, y), z, (s, t, t), STRUCT)
    for x in (-h + t / 2, h - t / 2):
        for z in (0.0, s - t):
            b.box((x, 0), z, (t, s - 2 * t, t), STRUCT)
    for x in (-h + t / 2, h - t / 2):
        for y in (-h + t / 2, h - t / 2):
            b.box((x, y), t, (t, t, s - 2 * t), STRUCT)
    return b.build("Ruin_Prop_Crate_A")


def barrel():
    b = Builder()
    b.lathe([(0.28, 0.0), (0.325, 0.3), (0.325, 0.65), (0.28, 0.95)], 8, BASE, top_mat=ACCENT, rot=22.5)
    for z0, z1, r0, r1 in ((0.1, 0.18, 0.318, 0.328), (0.77, 0.85, 0.321, 0.31)):
        b.lathe([(r0 + 0.012, z0), (r1 + 0.012, z1)], 8, STRUCT, rot=22.5)
    return b.build("Ruin_Prop_Barrel_A")


def banner():
    b = Builder()
    b.box((0, 0), 0.0, (0.6, 0.6, 0.4), BASE, bottom=False, mat_fn=L.up_is(ACCENT, BASE))
    b.prism((0, 0), 0.35, 0.07, 0.06, 5.15, 8, STRUCT, rot=22.5, bottom=False, top=False)
    b.prism((0, 0), 5.48, 0.1, 0.0001, 0.27, 4, STRUCT, rot=45)
    b.box((0, 0.13), 4.95, (1.5, 0.1, 0.1), STRUCT)
    cloth = [(-0.6, 2.2), (0.0, 2.6), (0.6, 2.2), (0.6, 4.96), (-0.6, 4.96)]
    b.extrude(cloth, (1, 0, 0), (0, 0, 1), 0.04, ACCENT, offset=(0, 0.13, 0))
    return b.build("Ruin_Prop_Banner_A")


def crystal():
    b = Builder()
    b.hull(L.ellipsoid_points(random.Random(3), 14, 0.8, 0.7, 0.35, z_floor=0.0), BASE,
           mat_fn=L.up_is(ACCENT, BASE, 0.9))
    specs = [((0.0, 0.0), 0.28, 2.4, (0, 0, 0)), ((0.35, 0.2), 0.2, 1.5, (18, 10, 30)),
             ((-0.3, 0.25), 0.18, 1.3, (-16, 12, 10)), ((0.1, -0.35), 0.16, 1.1, (20, -15, 50)),
             ((-0.35, -0.2), 0.14, 0.9, (-12, -20, 20))]
    for (cx, cy), r, h, (rx, ry, rz) in specs:
        m = rot_matrix(rx, ry, rz)
        pts = []
        for i in range(6):
            a = math.radians(60 * i)
            pts.append(Vector((r * math.cos(a), r * math.sin(a), 0.0)))
            pts.append(Vector((r * math.cos(a), r * math.sin(a), h * 0.72)))
        pts.append(Vector((0, 0, h)))
        pts = [m @ p + Vector((cx, cy, 0.12)) for p in pts]
        b.hull(pts, ACCENT)
    obj = b.build("Ruin_Prop_Crystal_A")
    return obj


# ---------------------------------------------------------------- 岩・崖

def _grounded_hull(b, pts, mat_fn):
    b.hull(pts, BASE, mat_fn=mat_fn)


def _rock(name, seed, rx, ry, rz, n=40):
    rng = random.Random(seed)
    pts = L.ellipsoid_points(rng, n, rx, ry, rz, jitter=0.18, z_floor=-rz * 0.35)
    pts = [p + Vector((0, 0, rz * 0.35)) for p in pts]
    b = Builder()
    b.hull(pts, BASE, mat_fn=L.up_is(ACCENT, BASE, 0.75))
    return b.build(name)


def rock_a():
    return _rock("Ruin_Rock_Boulder_A", 101, 0.6, 0.5, 0.5)


def rock_b():
    return _rock("Ruin_Rock_Boulder_B", 202, 1.1, 0.8, 0.45)


def rock_c():
    return _rock("Ruin_Rock_Boulder_C", 303, 0.9, 0.8, 1.3)


def _cliff(name, seed, chunks):
    rng = random.Random(seed)
    b = Builder()
    for (cx, cy), (rx, ry, rz) in chunks:
        pts = L.ellipsoid_points(rng, 60, rx, ry, rz, jitter=0.25, z_floor=0.0)
        # 下側を広げて崖らしい裾にする
        pts = [Vector((p.x * (1.15 - 0.3 * p.z / rz), p.y * (1.15 - 0.3 * p.z / rz), p.z)) + Vector((cx, cy, 0))
               for p in pts]
        b.hull(pts, BASE, mat_fn=L.up_is(ACCENT, BASE, 0.7))
    return b.build(name)


def cliff_a():
    return _cliff("Ruin_Cliff_Backdrop_A", 404, [
        ((0.0, 0.0), (7.0, 5.5, 18.0)),
        ((-8.0, 1.0), (6.0, 5.0, 12.0)),
        ((7.5, -0.5), (6.5, 5.0, 14.0)),
        ((-13.0, -1.0), (4.0, 4.0, 7.0)),
    ])


def cliff_b():
    return _cliff("Ruin_Cliff_Backdrop_B", 505, [
        ((0.0, 0.0), (9.0, 6.0, 11.0)),
        ((9.0, 1.0), (6.0, 5.0, 16.0)),
        ((-9.0, -0.5), (6.0, 5.5, 8.0)),
        ((14.0, 0.0), (3.5, 4.0, 6.0)),
    ])


ALL_PARTS = [
    arena_floor, ground, platform, stair,
    pillar_straight, pillar_broken_a, pillar_broken_b, arch, wall_broken,
    brazier, crate, barrel, banner, crystal,
    rock_a, rock_b, rock_c, cliff_a, cliff_b,
]
