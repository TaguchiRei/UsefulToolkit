# リアル調環境用のシームレステクスチャ（ベースカラー / 法線 / マスク）を手続き的に生成する。
# マスクマップは ToonRealistic の規約（R=Metallic, G=Occlusion, A=Smoothness）に従う。
# 実行: python gen_textures.py  （numpy, Pillow が必要）
import os

import numpy as np
from PIL import Image

OUT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "..", "..", "..", "Texture", "EnvironmentRealistic"))
os.makedirs(OUT, exist_ok=True)


# ---------------------------------------------------------------- ノイズ

def fbm(n, beta, seed, low_cut=1.0):
    """1/f^beta スペクトルの周期ノイズ（0..1）。FFT で作るので上下左右が必ずつながる"""
    rng = np.random.default_rng(seed)
    white = rng.standard_normal((n, n))
    f = np.fft.fftfreq(n) * n
    kx, ky = np.meshgrid(f, f)
    k = np.sqrt(kx ** 2 + ky ** 2)
    k[0, 0] = 1.0
    filt = 1.0 / np.maximum(k, low_cut) ** beta
    filt[0, 0] = 0.0
    out = np.real(np.fft.ifft2(np.fft.fft2(white) * filt))
    out -= out.min()
    return out / out.max()


def stretched_fbm(n, beta, seed, sx, sy):
    """方向に引き伸ばした周期ノイズ（木目など）。sx, sy は周波数の縮尺"""
    rng = np.random.default_rng(seed)
    white = rng.standard_normal((n, n))
    f = np.fft.fftfreq(n) * n
    kx, ky = np.meshgrid(f, f)
    k = np.sqrt((kx * sx) ** 2 + (ky * sy) ** 2)
    k[0, 0] = 1.0
    filt = 1.0 / np.maximum(k, 1.0) ** beta
    filt[0, 0] = 0.0
    out = np.real(np.fft.ifft2(np.fft.fft2(white) * filt))
    out -= out.min()
    return out / out.max()


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


# ---------------------------------------------------------------- 石積みパターン

def block_pattern(n, rows, min_w, max_w, seed, jitter_rows=False):
    """行ごとに幅の違うブロックを並べた周期パターン。
    返り値: (ブロックID, 縁までの距離[px], 行番号)
    """
    rng = np.random.default_rng(seed)
    ids = np.zeros((n, n), np.int32)
    dist = np.zeros((n, n), np.float32)
    row_idx = np.zeros((n, n), np.int32)
    # 行の境界（周期）
    if jitter_rows:
        hs = rng.uniform(0.7, 1.3, rows)
        hs = hs / hs.sum() * n
    else:
        hs = np.full(rows, n / rows)
    ys = np.concatenate([[0], np.cumsum(hs)])
    next_id = 0
    xs_px = np.arange(n)
    for r in range(rows):
        y0, y1 = int(round(ys[r])), int(round(ys[r + 1]))
        widths = []
        total = 0.0
        while total < n:
            w = rng.uniform(min_w, max_w) * n
            widths.append(w)
            total += w
        widths = np.array(widths) * (n / total)
        bounds = np.concatenate([[0], np.cumsum(widths)])
        offset = rng.uniform(0, n)
        xr = (xs_px - offset) % n
        seg = np.clip(np.searchsorted(bounds, xr, side="right") - 1, 0, len(widths) - 1)
        dx = np.minimum(xr - bounds[seg], bounds[seg + 1] - xr)
        seg_ids = seg + next_id
        next_id += len(widths)
        for y in range(y0, y1):
            dy = min(y - y0 + 0.5, y1 - y - 0.5)
            ids[y] = seg_ids
            dist[y] = np.minimum(dx, dy)
            row_idx[y] = r
    return ids, dist, row_idx


def per_id_random(ids, seed, lo=0.0, hi=1.0):
    rng = np.random.default_rng(seed)
    table = rng.uniform(lo, hi, ids.max() + 1)
    return table[ids]


# ---------------------------------------------------------------- 書き出し

def height_to_normal(h, strength):
    """高さマップ（0..1）から接空間法線（OpenGL 規約、+Y が UV の v 正方向）"""
    dx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * 0.5
    drow = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * 0.5
    nx = -dx * strength
    ny = drow * strength  # 画像の行は v と逆向き
    nz = np.ones_like(h)
    l = np.sqrt(nx ** 2 + ny ** 2 + nz ** 2)
    return np.stack([nx / l, ny / l, nz / l], -1) * 0.5 + 0.5


def save_rgb(name, rgb):
    Image.fromarray((np.clip(rgb, 0, 1) * 255 + 0.5).astype(np.uint8), "RGB").save(os.path.join(OUT, name + ".png"))


def save_rgba(name, rgba):
    Image.fromarray((np.clip(rgba, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(OUT, name + ".png"))


def write_set(name, albedo, height, normal_strength, metallic, ao, smooth):
    save_rgb(name + "_BaseColor", albedo)
    save_rgb(name + "_Normal", height_to_normal(height, normal_strength))
    save_rgba(name + "_Mask", np.stack([metallic, ao, np.zeros_like(ao), smooth], -1))
    print("wrote", name)


def colorize(base, var, *fields):
    """base 色に、各 field(0..1) × 係数色 の変化を足す"""
    out = np.broadcast_to(np.array(base, np.float32), var.shape + (3,)).copy()
    for f, c in fields:
        out += f[..., None] * np.array(c, np.float32)
    return out


N = 1024


# ---------------------------------------------------------------- 石畳（4m 四方）

def flagstone():
    ids, dist, _ = block_pattern(N, 8, 0.09, 0.2, seed=1, jitter_rows=True)
    warp = fbm(N, 1.6, 11)
    edge = dist + (warp - 0.5) * 10.0
    stone = smoothstep(1.5, 9.0, edge)
    dome = np.sqrt(np.clip(edge / 40.0, 0, 1))
    tone = per_id_random(ids, 2, -0.07, 0.07)
    tilt = per_id_random(ids, 3, 0.0, 0.25)
    n1, n2, n3 = fbm(N, 1.8, 12), fbm(N, 1.2, 13), fbm(N, 2.2, 14)
    height = stone * (0.55 + 0.25 * dome + 0.12 * n2 + tilt * 0.3) + (1 - stone) * 0.05 * n1
    wet = smoothstep(0.55, 0.75, n3)
    moss = (1 - stone) * smoothstep(0.45, 0.7, n1)
    albedo = colorize((0.42, 0.405, 0.385), tone,
                      (tone, (1, 1, 1)), (n2 - 0.5, (0.12, 0.11, 0.10)),
                      (1 - stone, (-0.2, -0.2, -0.19)), (moss, (-0.05, 0.03, -0.08)),
                      (wet, (-0.08, -0.08, -0.07)))
    ao = 0.55 + 0.45 * np.clip(height * 1.4, 0, 1)
    smooth = np.clip(0.32 + 0.12 * n2 + 0.45 * wet + 0.3 * (1 - stone), 0, 0.9)
    write_set("T_Flagstone", albedo, height, 9.0, np.zeros_like(ao), ao, smooth)


# ---------------------------------------------------------------- 石壁（2m 四方）

def stone_wall():
    ids, dist, row = block_pattern(N, 6, 0.22, 0.42, seed=4)
    warp = fbm(N, 1.7, 21)
    edge = dist + (warp - 0.5) * 6.0
    stone = smoothstep(2.0, 7.0, edge)
    bulge = np.sqrt(np.clip(edge / 30.0, 0, 1))
    tone = per_id_random(ids, 5, -0.06, 0.06)
    hue = per_id_random(ids, 6, -0.02, 0.02)
    n1, n2 = fbm(N, 2.0, 22), fbm(N, 1.3, 23)
    streak = stretched_fbm(N, 1.6, 24, 0.4, 6.0)
    height = stone * (0.6 + 0.25 * bulge + 0.15 * n2) + (1 - stone) * 0.1 * n1
    damp = smoothstep(0.5, 0.8, streak)
    albedo = colorize((0.50, 0.49, 0.47), tone,
                      (tone, (1, 1, 1)), (hue, (1, 0.5, -0.5)), (n2 - 0.5, (0.10, 0.10, 0.09)),
                      (1 - stone, (-0.12, -0.12, -0.12)), (damp, (-0.10, -0.09, -0.08)))
    ao = 0.5 + 0.5 * np.clip(height * 1.3, 0, 1)
    smooth = np.clip(0.18 + 0.1 * n2 + 0.3 * damp, 0, 0.8)
    write_set("T_StoneWall", albedo, height, 8.0, np.zeros_like(ao), ao, smooth)


# ---------------------------------------------------------------- 木材（1m 四方、板は u 方向）

def wood():
    rows = 5
    grain = stretched_fbm(N, 1.4, 31, 0.08, 1.0)
    fine = stretched_fbm(N, 1.0, 32, 0.15, 1.0)
    v = np.arange(N)[:, None] / N * rows
    plank = np.floor(v).astype(int)
    frac = v - plank
    gap = 1 - smoothstep(0.0, 0.03, frac) * smoothstep(0.0, 0.03, 1 - frac)
    ptone = np.random.default_rng(33).uniform(-0.05, 0.05, rows)[plank % rows]
    ptone = np.broadcast_to(ptone, (N, N))
    rings = 0.5 + 0.5 * np.sin((grain * 18.0 + np.roll(fine, 100, 0) * 3.0) * np.pi)
    albedo = colorize((0.34, 0.23, 0.14), ptone,
                      (ptone, (1, 0.8, 0.6)), (rings - 0.5, (0.08, 0.05, 0.03)),
                      (fine - 0.5, (0.06, 0.04, 0.02)), (np.broadcast_to(gap, (N, N)), (-0.2, -0.14, -0.09)))
    height = 0.7 + 0.15 * rings + 0.1 * fine - 0.6 * np.broadcast_to(gap, (N, N))
    ao = 0.6 + 0.4 * np.clip(height, 0, 1)
    smooth = np.clip(0.22 + 0.1 * fine, 0, 1)
    write_set("T_Wood", albedo, height, 5.0, np.zeros_like(ao), ao, smooth)


# ---------------------------------------------------------------- 鉄（1m 四方）

def iron():
    n1, n2, n3 = fbm(N, 1.5, 41), fbm(N, 2.2, 42), fbm(N, 1.0, 43)
    rust = smoothstep(0.58, 0.75, n1 * 0.7 + n3 * 0.3)
    albedo = colorize((0.20, 0.20, 0.21), n2,
                      (n2 - 0.5, (0.05, 0.05, 0.05)), (rust, (0.18, 0.02, -0.08)), (rust * n3, (0.1, 0.03, -0.02)))
    height = 0.5 + 0.2 * n2 + 0.2 * rust * n3
    metallic = 0.9 * (1 - rust)
    ao = 0.75 + 0.25 * n2
    smooth = np.clip(0.55 - 0.35 * rust + 0.1 * (n3 - 0.5), 0, 1)
    write_set("T_Iron", albedo, height, 3.0, metallic, ao, smooth)


# ---------------------------------------------------------------- 布（0.5m 四方、色はマテリアル側で乗算）

def cloth():
    t = np.arange(N) / N * 2 * np.pi * 96
    u, v = np.meshgrid(t, t)
    weave = 0.5 + 0.25 * np.sin(u) * np.sign(np.sin(v * 0.5)) + 0.25 * np.sin(v) * np.sign(np.sin(u * 0.5))
    n1 = fbm(N, 1.3, 51)
    albedo = np.repeat((0.78 + 0.12 * weave + 0.1 * (n1 - 0.5))[..., None], 3, -1)
    height = 0.5 + 0.3 * weave + 0.2 * n1
    ao = 0.75 + 0.25 * weave
    write_set("T_Cloth", albedo, height, 3.0, np.zeros_like(ao), ao, np.full_like(ao, 0.08))


# ---------------------------------------------------------------- 屋根スレート（2m 四方）

def roof_slate():
    rows = 8
    ids, dist, row = block_pattern(N, rows, 0.1, 0.16, seed=61)
    y = np.arange(N)[:, None] / N * rows
    frac = np.broadcast_to(1 - (y - np.floor(y)), (N, N))  # 各段の上端 0 → 下端 1
    n1 = fbm(N, 1.8, 62)
    tone = per_id_random(ids, 63, -0.05, 0.05)
    side = smoothstep(1.0, 4.0, dist)
    height = (0.3 + 0.6 * frac) * side + 0.08 * n1
    albedo = colorize((0.23, 0.25, 0.28), tone, (tone, (1, 1, 1.05)), (n1 - 0.5, (0.06, 0.06, 0.06)),
                      (1 - side, (-0.08, -0.08, -0.08)))
    ao = 0.5 + 0.5 * np.clip(height, 0, 1)
    smooth = np.clip(0.45 + 0.15 * n1, 0, 1)
    write_set("T_RoofSlate", albedo, height, 7.0, np.zeros_like(ao), ao, smooth)


# ---------------------------------------------------------------- 岩（8m 四方）

def rock():
    n1, n2, n3 = fbm(N, 1.9, 71), fbm(N, 1.2, 72), fbm(N, 2.4, 73)
    cracks = 1 - smoothstep(0.0, 0.04, np.abs(n2 - 0.5))
    height = 0.6 * n1 + 0.3 * n3 - 0.25 * cracks
    lichen = smoothstep(0.6, 0.8, n3)
    albedo = colorize((0.40, 0.39, 0.37), n1, (n1 - 0.5, (0.18, 0.17, 0.16)), (cracks, (-0.12, -0.12, -0.12)),
                      (lichen, (0.02, 0.05, -0.04)))
    ao = 0.55 + 0.45 * np.clip(height + 0.2, 0, 1)
    smooth = np.clip(0.15 + 0.2 * n3, 0, 1)
    write_set("T_Rock", albedo, height, 10.0, np.zeros_like(ao), ao, smooth)


# ---------------------------------------------------------------- 地面（草と土、6m 四方）

def ground():
    n1, n2, n3 = fbm(N, 1.6, 81), fbm(N, 2.4, 82), fbm(N, 1.1, 83)
    grass = smoothstep(0.42, 0.6, n1)
    blades = fbm(N, 0.6, 84)
    albedo = colorize((0.30, 0.25, 0.18), n2,
                      (n2 - 0.5, (0.08, 0.06, 0.04)),
                      (grass, (-0.06, 0.07, -0.06)), (grass * (blades - 0.5), (0.05, 0.12, 0.02)))
    height = 0.4 * n2 + 0.3 * grass * blades + 0.2 * n3
    ao = 0.65 + 0.35 * np.clip(height * 1.5, 0, 1)
    smooth = np.clip(0.15 + 0.35 * smoothstep(0.6, 0.8, n3) * (1 - grass), 0, 1)
    write_set("T_Ground", albedo, height, 6.0, np.zeros_like(ao), ao, smooth)


if __name__ == "__main__":
    for fn in (flagstone, stone_wall, wood, iron, cloth, roof_slate, rock, ground):
        fn()
    print("out:", OUT)
