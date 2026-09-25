# 雨上がりの城塞中庭（リアル調）の各パーツ生成関数。寸法はすべて実寸（m）。
# 中庭側を向く面は Blender の -Y 側に作る（Unity では +Z 側になる）。
import math
import random

import bmesh
from mathutils import Vector

import rlib as L
from rlib import ACCENT, BASE, EMIT, STRUCT, RB, rot, up_is


def pole(b, p0, p1, r, sides, mat, uv="box"):
    """p0 から p1 へ伸びる円柱"""
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    m = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix()
    b.cyl(p0, r, r, d.length, sides, mat, r=m, uv=uv)


def beam(b, p0, p1, w, h, mat, bevel=0.01):
    """p0 から p1 へ伸びる角材（断面 w × h）"""
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    m = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix()
    b.box(p0, (w, h, d.length), mat, r=m, bevel=bevel)


def arc_pts(cx, cz, r, a0, a1, n):
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / n)),
             cz + r * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]


def radial_box(b, radius, ang, z, size, mat):
    """円周上の ang 度の位置に、厚み方向（size[1]）を半径方向に向けた箱を置く"""
    a = math.radians(ang)
    b.box((radius * math.cos(a), radius * math.sin(a), z), size, mat, r=rot(rz=ang - 90), bevel=0.01)


# ---------------------------------------------------------------- 床・地形

def floor_courtyard():
    b = RB()
    b.grid(36.0, 36.0, 12, 12, BASE)
    return b.build("RC_Floor_Courtyard_A")


def terrain_outside():
    rng = random.Random(9)
    radii = [0, 20, 27, 40, 60, 90, 130, 170]
    seg = 40
    tmp = bmesh.new()
    rings = []
    for r in radii:
        ring = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            if r <= 27:
                z = -0.03
            else:
                t = (r - 27) / (radii[-1] - 27)
                z = -0.03 + rng.uniform(0.2, 1.0) * (0.5 + 9.0 * t * t)
            rr = r if r <= 27 else r + rng.uniform(-3, 3)
            ring.append(tmp.verts.new((rr * math.cos(a), rr * math.sin(a), z)))
        rings.append(ring)
    for k in range(len(radii) - 1):
        for i in range(seg):
            j = (i + 1) % seg
            if k == 0:
                continue
            tmp.faces.new([rings[k][i], rings[k + 1][i], rings[k + 1][j]])
            tmp.faces.new([rings[k][i], rings[k + 1][j], rings[k][j]])
    center = tmp.verts.new((0, 0, -0.03))
    for i in range(seg):
        tmp.faces.new([center, rings[1][i], rings[1][(i + 1) % seg]])
    for f in tmp.faces:
        f.normal_update()
        if f.normal.z < 0:
            f.normal_flip()
    b = RB()
    b._merge_no_recalc(tmp, BASE)
    obj = b.build("RC_Terrain_Outside_A", sharp_angle=math.radians(80))
    # 最低点を Z=0 に揃える（中庭の床より 3cm 下に置く前提で、原点を持ち上げない）
    return obj


# ---------------------------------------------------------------- 城壁・塔・門

WALL_L = 8.0


def wall_straight():
    b = RB()
    b.box((0, 0, 0), (WALL_L, 2.4, 0.7), STRUCT, bevel=0.05)
    b.box((0, 0, 0.65), (WALL_L, 2.0, 6.6), BASE, bevel=0.03)
    b.box((0, 0, 6.55), (WALL_L, 2.2, 0.3), STRUCT, bevel=0.03)
    b.box((0, 0.8, 7.2), (WALL_L, 0.4, 1.0), BASE, bevel=0.03)
    for x in (-3.0, -1.0, 1.0, 3.0):
        b.box((x, 0.8, 8.15), (1.1, 0.4, 0.9), BASE, bevel=0.03)
    b.box((0, -0.85, 7.2), (WALL_L, 0.3, 0.55), STRUCT, bevel=0.02)
    # 中庭側の控え壁
    b.box((0, -1.3, 0.0), (1.2, 0.6, 5.8), BASE, bevel=0.03)
    b.extrude([(-1.0, 5.8), (-1.6, 5.8), (-1.0, 6.5)], (0, 1, 0), (0, 0, 1), 1.2, STRUCT)
    return b.build("RC_Wall_Castle_Straight")


def tower_round():
    b = RB()
    b.cyl((0, 0, 0), 3.95, 3.85, 1.0, 24, STRUCT, bevel=0.04)
    b.cyl((0, 0, 0.95), 3.6, 3.35, 11.5, 24, BASE)
    b.cyl((0, 0, 12.2), 3.4, 3.75, 0.55, 24, STRUCT, bevel=0.03)
    b.cyl((0, 0, 12.7), 4.15, 0.0, 6.8, 24, ACCENT, uv="cyl")
    b.cyl((0, 0, 19.3), 0.06, 0.0, 1.2, 6, STRUCT)
    for ang in (20, 110, 200, 290):
        radial_box(b, 3.47, ang, 4.5, (0.4, 0.22, 1.2), EMIT)
    for ang in (65, 155, 245, 335):
        radial_box(b, 3.4, ang, 8.6, (0.4, 0.22, 1.2), EMIT)
    return b.build("RC_Tower_Round_A")


GATE_W, GATE_H, GATE_T = 10.0, 11.0, 3.2
ARCH_HALF, ARCH_SPRING = 2.2, 4.2


def gatehouse():
    b = RB()
    ah, sp = ARCH_HALF, ARCH_SPRING
    poly = [(-GATE_W / 2, 0), (-ah, 0), (-ah, sp)] + arc_pts(0, sp, ah, 180, 0, 12)[1:-1] + \
           [(ah, sp), (ah, 0), (GATE_W / 2, 0), (GATE_W / 2, GATE_H), (-GATE_W / 2, GATE_H)]
    b.extrude(poly, (1, 0, 0), (0, 0, 1), GATE_T, BASE)
    ro = ah + 0.55
    trim = [(-ro, 0), (-ro, sp)] + arc_pts(0, sp, ro, 180, 0, 12)[1:-1] + [(ro, sp), (ro, 0), (ah, 0), (ah, sp)] + \
           arc_pts(0, sp, ah, 0, 180, 12)[1:-1] + [(-ah, sp), (-ah, 0)]
    for y in (-GATE_T / 2 - 0.1, GATE_T / 2 + 0.1):
        b.extrude(trim, (1, 0, 0), (0, 0, 1), 0.3, STRUCT, offset=(0, y, 0))
    b.box((0, 0, 0), (GATE_W + 0.3, GATE_T + 0.3, 0.6), STRUCT, bevel=0.04)
    b.box((0, 0, 9.5), (GATE_W + 0.4, GATE_T + 0.4, 0.35), STRUCT, bevel=0.03)
    for y in (-GATE_T / 2 + 0.2, GATE_T / 2 - 0.2):
        b.box((0, y, GATE_H - 0.05), (GATE_W, 0.4, 0.9), BASE, bevel=0.03)
        for x in (-4.2, -2.1, 0.0, 2.1, 4.2):
            b.box((x, y, GATE_H + 0.8), (1.0, 0.4, 0.85), BASE, bevel=0.03)
    for x in (-3.5, 3.5):
        b.box((x, -GATE_T / 2, 7.2), (0.35, 0.3, 1.1), EMIT, bevel=0.01)
    return b.build("RC_Gatehouse_A")


def portcullis():
    b = RB()
    w, h = 4.3, 4.6
    xs = [-2.0 + 0.5 * i for i in range(9)]
    for x in xs:
        b.box((x, 0, 0.4), (0.1, 0.1, h - 0.4), BASE, bevel=0.01)
        b.cyl((x, 0, 0.4), 0.07, 0.0, 0.4, 4, BASE, r=rot(180, 0, 45), uv="box")
    for z in (0.9, 1.7, 2.5, 3.3, 4.1, 4.5):
        b.box((0, 0, z - 0.045), (w, 0.07, 0.09), BASE, bevel=0.01)
    return b.build("RC_Gate_Portcullis_A")


def keep():
    b = RB()
    s, hgt = 14.0, 20.0
    b.box((0, 0, 0), (s + 0.6, s + 0.6, 1.2), STRUCT, bevel=0.05)
    b.box((0, 0, 1.1), (s, s, hgt - 1.1), BASE, bevel=0.04)
    b.box((0, 0, hgt - 0.4), (s + 0.5, s + 0.5, 0.4), STRUCT, bevel=0.03)
    b.cyl((0, 0, hgt - 0.05), s / 2 * math.sqrt(2) * 0.92, 0.0, 8.5, 4, ACCENT, rot_deg=45, uv="box")
    for cx in (-s / 2, s / 2):
        for cy in (-s / 2, s / 2):
            b.cyl((cx, cy, 0.0), 1.9, 1.75, hgt + 3.0, 16, BASE)
            b.cyl((cx, cy, hgt + 2.9), 2.05, 2.2, 0.4, 16, STRUCT)
            b.cyl((cx, cy, hgt + 3.2), 2.35, 0.0, 4.5, 16, ACCENT)
    for face in range(4):
        ang = face * 90
        for i, off in enumerate((-3.5, 0.0, 3.5)):
            for z in (7.0, 13.0):
                a = math.radians(ang)
                n = Vector((math.cos(a), math.sin(a), 0))
                t = Vector((-math.sin(a), math.cos(a), 0))
                p = n * (s / 2) + t * off
                b.box((p.x, p.y, z), (0.6, 0.25, 1.6), EMIT, r=rot(rz=ang + 90), bevel=0.01)
    return b.build("RC_Keep_A")


def terrace():
    b = RB()
    b.box((0, 0, 0), (8.0, 5.0, 1.45), BASE, bevel=0.03)
    b.box((0, 0, 1.4), (8.2, 5.2, 0.2), up_is(ACCENT, STRUCT), bevel=0.03)
    rise, run, steps = 1.6 / 6, 0.35, 6
    y0, y1 = -2.5 - run * steps, -2.5
    poly = [(y0, 0.0), (y1, 0.0), (y1, rise * steps)]
    for s in range(steps - 1, 0, -1):
        y = y0 + run * s
        poly += [(y, rise * (s + 1)), (y, rise * s)]
    poly += [(y0, rise)]
    b.extrude(poly, (0, 1, 0), (0, 0, 1), 3.2, up_is(ACCENT, STRUCT, 0.5))
    for x in (-1.78, 1.78):
        b.extrude([(y0, 0.0), (y1, 0.0), (y1, 2.3), (y0, 0.55)], (0, 1, 0), (0, 0, 1), 0.36, STRUCT,
                  offset=(x, 0, 0))
    for x in (-3.95, 3.95):
        b.box((x, 0, 1.55), (0.3, 5.2, 0.8), BASE, bevel=0.03)
    for x in (-2.8, 2.8):
        b.box((x, -2.45, 1.55), (2.3, 0.3, 0.8), BASE, bevel=0.03)
    return b.build("RC_Terrace_A")


# ---------------------------------------------------------------- 小物

def brazier():
    b = RB()
    for a in (90, 210, 330):
        r = math.radians(a)
        foot = (0.4 * math.cos(r), 0.4 * math.sin(r), 0.0)
        top = (0.2 * math.cos(r), 0.2 * math.sin(r), 0.98)
        beam(b, foot, top, 0.05, 0.05, BASE, bevel=0.008)
    b.lathe([(0.25, 0.52), (0.29, 0.52), (0.29, 0.57), (0.25, 0.57)], 16, BASE, closed=True)
    b.lathe([(0.12, 0.9), (0.3, 0.96), (0.43, 1.08), (0.46, 1.2), (0.42, 1.2), (0.39, 1.1), (0.28, 1.01),
             (0.06, 0.97)], 16, BASE, closed=True)
    rng = random.Random(8)
    for i in range(5):
        a = rng.uniform(0, 2 * math.pi)
        rr = rng.uniform(0.0, 0.22)
        c = Vector((rr * math.cos(a), rr * math.sin(a), 1.08))
        b.hull([c + Vector((rng.uniform(-0.12, 0.12), rng.uniform(-0.12, 0.12), rng.uniform(-0.05, 0.08)))
                for _ in range(10)], EMIT)
    return b.build("RC_Prop_Brazier_A")


def torch_sconce():
    b = RB()
    b.box((0, -0.02, 0.0), (0.18, 0.04, 0.36), BASE, bevel=0.008)
    beam(b, (0, -0.03, 0.2), (0, -0.3, 0.12), 0.035, 0.035, BASE, bevel=0.005)
    b.cyl((0, -0.32, 0.03), 0.045, 0.065, 0.12, 10, BASE)
    m = rot(rx=15)
    d = m @ Vector((0, 0, 1))
    base = Vector((0, -0.32, 0.05))
    b.cyl(base, 0.028, 0.03, 0.55, 8, STRUCT, r=m, uv="box")
    head = base + d * 0.45
    b.cyl(head, 0.045, 0.05, 0.15, 10, ACCENT, r=m, uv="box")
    tip = head + d * 0.15
    rng = random.Random(3)
    b.hull([tip + Vector((rng.uniform(-0.04, 0.04), rng.uniform(-0.04, 0.04), rng.uniform(-0.01, 0.04)))
            for _ in range(10)], EMIT)
    return b.build("RC_Prop_TorchSconce_A")


def crate():
    b = RB()
    s, t = 1.0, 0.1
    h = s / 2
    b.box((0, 0, 0.01), (s - 0.04, s - 0.04, s - 0.03), BASE, bevel=0.01)
    for y in (-h + t / 2, h - t / 2):
        for z in (0.0, s - t):
            b.box((0, y, z), (s, t, t), STRUCT, bevel=0.01)
    for x in (-h + t / 2, h - t / 2):
        for z in (0.0, s - t):
            b.box((x, 0, z), (t, s - 2 * t, t), STRUCT, bevel=0.01)
        for y in (-h + t / 2, h - t / 2):
            b.box((x, y, t), (t, t, s - 2 * t), STRUCT, bevel=0.01)
    for x in (-h + 0.06, h - 0.06):
        for y in (-h + 0.06, h - 0.06):
            for z in (0.0, s - 0.125):
                b.box((x, y, z), (0.135, 0.135, 0.125), ACCENT, bevel=0.008)
    return b.build("RC_Prop_Crate_A")


BARREL_PROFILE = [(0.27, 0.0), (0.31, 0.12), (0.335, 0.3), (0.34, 0.48), (0.335, 0.66), (0.31, 0.84), (0.27, 0.96)]


def _barrel_r(z):
    for (r0, z0), (r1, z1) in zip(BARREL_PROFILE, BARREL_PROFILE[1:]):
        if z0 <= z <= z1:
            return r0 + (r1 - r0) * (z - z0) / (z1 - z0)
    return BARREL_PROFILE[-1][0]


def barrel():
    b = RB()
    b.lathe(BARREL_PROFILE, 16, BASE, uv="cyl_rot")
    for z0 in (0.07, 0.3, 0.6, 0.83):
        z1 = z0 + 0.06
        r0, r1 = _barrel_r(z0) + 0.006, _barrel_r(z1) + 0.006
        b.lathe([(r0 - 0.02, z0), (r0, z0), (r1, z1), (r1 - 0.02, z1)], 16, ACCENT, closed=True)
    return b.build("RC_Prop_Barrel_A")


def weapon_rack():
    b = RB()
    for x in (-0.85, 0.85):
        b.box((x, 0, 0.06), (0.08, 0.08, 1.55), STRUCT, bevel=0.01)
        b.box((x, 0, 0), (0.1, 0.55, 0.07), STRUCT, bevel=0.01)
    b.box((0, 0, 1.46), (1.9, 0.08, 0.08), STRUCT, bevel=0.01)
    b.box((0, -0.12, 0.3), (1.9, 0.06, 0.06), STRUCT, bevel=0.01)
    m = rot(rx=-8)
    d = m @ Vector((0, 0, 1))
    for x in (-0.55, -0.2, 0.15, 0.5):
        p0 = Vector((x, -0.2, 0.02))
        b.cyl(p0, 0.022, 0.022, 2.3, 8, STRUCT, r=m, uv="box")
        b.cyl(p0 + d * 2.3, 0.045, 0.0, 0.28, 4, ACCENT, r=m, uv="box")
    shield = rot(rx=-78)
    b.cyl((-1.25, -0.02, 0.37), 0.36, 0.36, 0.035, 20, BASE, r=shield, uv="box")
    b.cyl((-1.25, -0.03, 0.37), 0.38, 0.38, 0.025, 20, ACCENT, r=shield, uv="box")
    b.cyl((-1.25, -0.06, 0.38), 0.08, 0.05, 0.06, 10, ACCENT, r=shield, uv="box")
    return b.build("RC_Prop_WeaponRack_A")


def awning_stall():
    b = RB()
    for x in (-1.5, 1.5):
        b.box((x, -1.0, 0), (0.1, 0.1, 2.5), STRUCT, bevel=0.01)
        b.box((x, 1.0, 0), (0.1, 0.1, 2.1), STRUCT, bevel=0.01)
    b.box((0, -1.0, 2.38), (3.2, 0.09, 0.09), STRUCT, bevel=0.01)
    b.box((0, 1.0, 1.98), (3.2, 0.09, 0.09), STRUCT, bevel=0.01)
    b.box((0, -0.55, 0), (3.0, 0.7, 0.88), BASE, bevel=0.015)
    b.box((0, -0.55, 0.87), (3.1, 0.8, 0.06), BASE, bevel=0.01)
    grid = []
    for i in range(9):
        x = -1.7 + 3.4 * i / 8
        row = []
        for j in range(6):
            t = j / 5
            y = -1.25 + 2.45 * t
            z = 2.52 + (2.12 - 2.52) * t - 0.1 * math.sin(math.pi * i / 8) * math.sin(math.pi * t)
            row.append(Vector((x, y, z)))
        grid.append(row)
    b.sheet(grid, 0.02, ACCENT)
    b.box((0, -1.26, 2.2), (3.4, 0.02, 0.32), ACCENT, bevel=0.0)
    rng = random.Random(4)
    for x in (-1.0, -0.45, 0.35, 0.95):
        c = Vector((x, -0.55, 0.93))
        b.hull([c + Vector((rng.uniform(-0.18, 0.18), rng.uniform(-0.15, 0.15), rng.uniform(0.0, 0.28)))
                for _ in range(14)], ACCENT)
    return b.build("RC_Prop_Stall_A")


def banner_wall():
    b = RB()
    b.cyl((-0.75, -0.06, 3.62), 0.03, 0.03, 1.5, 8, STRUCT, r=rot(ry=90), uv="box")
    for x in (-0.78, 0.78):
        b.cyl((x, -0.06, 3.62), 0.05, 0.05, 0.06, 8, STRUCT, r=rot(ry=90), uv="box")
    b.extrude([(-0.6, 0.0), (0.0, 0.4), (0.6, 0.0), (0.6, 3.58), (-0.6, 3.58)], (1, 0, 0), (0, 0, 1), 0.03,
              ACCENT, offset=(0, -0.06, 0))
    return b.build("RC_Prop_BannerWall_A")


def scaffold():
    b = RB()
    xs, ys = (-2.1, 0.0, 2.1), (-0.6, 0.6)
    for x in xs:
        for y in ys:
            pole(b, (x, y, 0), (x, y, 6.3), 0.06, 8, BASE)
    for z in (2.2, 4.4):
        for y in ys:
            pole(b, (-2.3, y, z), (2.3, y, z), 0.05, 8, BASE)
        for x in xs:
            pole(b, (x, -0.75, z), (x, 0.75, z), 0.05, 8, BASE)
        for k in range(5):
            y = -0.56 + 0.28 * k
            b.box((0, y, z + 0.05), (4.5, 0.25, 0.05), STRUCT, bevel=0.008)
    pole(b, (-2.1, 0.6, 0.1), (0.0, 0.6, 2.2), 0.04, 6, BASE)
    pole(b, (0.0, 0.6, 2.25), (2.1, 0.6, 4.35), 0.04, 6, BASE)
    for x in (2.35, 2.75):
        beam(b, (x, -0.9, 0.0), (x, -0.3, 2.35), 0.06, 0.06, STRUCT)
    d = Vector((0, 0.6, 2.35)).normalized()
    for k in range(1, 8):
        p = Vector((2.55, -0.9, 0.0)) + d * (k * 0.3)
        beam(b, (2.35, p.y, p.z), (2.75, p.y, p.z), 0.04, 0.04, STRUCT)
    return b.build("RC_Prop_Scaffold_A")


def well():
    b = RB()
    b.lathe([(0.75, 0.0), (1.05, 0.0), (1.05, 0.85), (0.75, 0.85)], 20, BASE, closed=True)
    b.lathe([(0.7, 0.83), (1.12, 0.83), (1.12, 0.96), (0.7, 0.96)], 20, BASE, closed=True, bevel=0.0)
    b.cyl((0, 0, 0.3), 0.76, 0.76, 0.05, 20, ACCENT)
    for x in (-1.25, 1.25):
        b.box((x, 0, 0), (0.16, 0.16, 2.6), STRUCT, bevel=0.01)
    pole(b, (-1.35, 0, 2.2), (1.35, 0, 2.2), 0.07, 10, STRUCT)
    for sgn in (-1, 1):
        b.box((0, sgn * 0.42, 2.55), (3.0, 1.0, 0.06), STRUCT, r=rot(rx=sgn * 32), bevel=0.01)
    pole(b, (0.1, 0, 2.15), (0.1, 0, 1.55), 0.008, 6, STRUCT)
    b.cyl((0.1, 0, 1.3), 0.13, 0.15, 0.26, 12, STRUCT)
    return b.build("RC_Prop_Well_A")


def _puddle(name, seed, radius):
    rng = random.Random(seed)
    n = 18
    phases = [rng.uniform(0, 2 * math.pi) for _ in range(3)]
    pts = []
    for i in range(n):
        a = 2 * math.pi * i / n
        rr = radius * (1 + 0.22 * math.sin(2 * a + phases[0]) + 0.12 * math.sin(3 * a + phases[1])
                       + 0.06 * math.sin(5 * a + phases[2]))
        pts.append((rr * math.cos(a) * 1.3, rr * math.sin(a)))
    b = RB()
    b.flat_poly(pts, ACCENT)
    return b.build(name)


def puddle_a():
    return _puddle("RC_Prop_Puddle_A", 1, 0.8)


def puddle_b():
    return _puddle("RC_Prop_Puddle_B", 2, 1.4)


def puddle_c():
    return _puddle("RC_Prop_Puddle_C", 3, 2.2)


# ---------------------------------------------------------------- 岩・遠景

def _ellipsoid(rng, n, rx, ry, rz, jitter):
    pts = []
    for _ in range(n):
        u = rng.uniform(-1, 1)
        t = rng.uniform(0, 2 * math.pi)
        s = math.sqrt(1 - u * u)
        k = 1 + rng.uniform(-jitter, jitter)
        pts.append(Vector((rx * s * math.cos(t) * k, ry * s * math.sin(t) * k, max(rz * u * k, 0.0))))
    return pts


def rock_a():
    b = RB()
    b.hull_detail(_ellipsoid(random.Random(11), 40, 0.7, 0.55, 0.6, 0.2), BASE, cuts=2, amp=0.12, freq=1.6, seed=1)
    return b.build("RC_Rock_A", sharp_angle=math.radians(50))


def rock_b():
    b = RB()
    b.hull_detail(_ellipsoid(random.Random(12), 50, 1.4, 1.0, 1.1, 0.22), BASE, cuts=2, amp=0.22, freq=0.9, seed=2)
    return b.build("RC_Rock_B", sharp_angle=math.radians(50))


def _mountain(name, seed, chunks):
    rng = random.Random(seed)
    b = RB()
    for (cx, cy), (rx, ry, rz) in chunks:
        pts = _ellipsoid(rng, 70, rx, ry, rz, 0.25)
        pts = [Vector((p.x * (1.2 - 0.4 * p.z / rz), p.y * (1.2 - 0.4 * p.z / rz), p.z)) + Vector((cx, cy, 0))
               for p in pts]
        b.hull_detail(pts, up_is(ACCENT, BASE, 0.75), cuts=2, amp=4.0, freq=0.05, seed=seed)
    return b.build(name, sharp_angle=math.radians(55))


def mountain_a():
    return _mountain("RC_Mountain_A", 21, [((0, 0), (22, 16, 48)), ((-24, 4), (18, 14, 32)),
                                           ((22, -3), (20, 15, 36)), ((-42, 0), (14, 12, 18))])


def mountain_b():
    return _mountain("RC_Mountain_B", 22, [((0, 0), (26, 18, 30)), ((28, 2), (18, 14, 42)),
                                           ((-26, -2), (18, 14, 22)), ((46, 0), (12, 12, 14))])


ALL_PARTS = [
    floor_courtyard, terrain_outside, wall_straight, tower_round, gatehouse, portcullis, keep, terrace,
    brazier, torch_sconce, crate, barrel, weapon_rack, awning_stall, banner_wall, scaffold, well,
    puddle_a, puddle_b, puddle_c, rock_a, rock_b, mountain_a, mountain_b,
]
