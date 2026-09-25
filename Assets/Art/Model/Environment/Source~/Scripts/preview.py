# 確認用: 小〜中サイズのパーツを並べ、人体サイズの箱と一緒に Workbench で撮影する（書き出しはしない）。
import importlib
import os
import sys

SCRIPT_DIR = os.path.dirname(__file__)
if SCRIPT_DIR not in sys.path:
    sys.path.append(SCRIPT_DIR)

import envlib
import parts

importlib.reload(envlib)
importlib.reload(parts)

OUT = globals().get("OUT", os.path.join(SCRIPT_DIR, "..", "preview"))
os.makedirs(OUT, exist_ok=True)

envlib.clear_scene()
layout = [
    (parts.pillar_straight, (0, 0)), (parts.pillar_broken_a, (3, 0)), (parts.pillar_broken_b, (6, 0)),
    (parts.arch, (14, 0)), (parts.wall_broken, (0, 7)), (parts.platform, (9, 7)), (parts.stair, (9, 3.9)),
    (parts.brazier, (-3, 3)), (parts.crate, (-3, 5)), (parts.barrel, (-3, 7)), (parts.banner, (-5.5, 3)),
    (parts.crystal, (-6, 7)), (parts.rock_a, (4, 11)), (parts.rock_b, (7, 11)), (parts.rock_c, (10.5, 11)),
]
for fn, (x, y) in layout:
    o = fn()
    o.location = (x, y, 0)

b = envlib.Builder()
b.box((0, 0), 0.0, (0.5, 0.4, 1.7), envlib.EMIT)
h = b.build("_human")
h.location = (-1.5, -1.5, 0)

envlib.render_check(os.path.join(OUT, "props_persp.png"), center=(4, 5, 1.5), rot_x=62, rot_z=-30, ortho_scale=26, res=(1400, 900))
envlib.render_check(os.path.join(OUT, "props_front.png"), center=(4, 5, 2.5), rot_x=88, rot_z=0, ortho_scale=26, res=(1400, 700))

envlib.clear_scene()
for fn in (parts.cliff_a, parts.cliff_b):
    o = fn()
    o.location = (0, 0 if fn is parts.cliff_a else 25, 0)
h = envlib.Builder(); h.box((0, 0), 0.0, (0.5, 0.4, 1.7), envlib.EMIT); h = h.build("_human"); h.location = (0, -10, 0)
envlib.render_check(os.path.join(OUT, "cliffs.png"), center=(0, 12, 6), rot_x=70, rot_z=-25, ortho_scale=60, res=(1400, 900))

envlib.clear_scene()
parts.ground()
parts.arena_floor()
envlib.render_check(os.path.join(OUT, "arena.png"), center=(0, 0, 0), rot_x=50, rot_z=-30, ortho_scale=70, res=(1400, 900))
print("done", os.path.abspath(OUT))
