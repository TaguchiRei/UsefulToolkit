# 確認用: パーツを並べて人体サイズの箱と一緒に Workbench で撮影する（書き出しはしない）。
import importlib
import os
import sys

SCRIPT_DIR = os.path.dirname(__file__)
if SCRIPT_DIR not in sys.path:
    sys.path.append(SCRIPT_DIR)
import rlib
import rparts
importlib.reload(rlib)
importlib.reload(rparts)

OUT = globals().get("OUT", os.path.join(SCRIPT_DIR, "..", "preview"))
os.makedirs(OUT, exist_ok=True)


def human(x, y):
    b = rlib.RB()
    b.box((0, 0, 0), (0.5, 0.4, 1.7), rlib.EMIT, bevel=0.0)
    o = b.build("_human")
    o.location = (x, y, 0)


rlib.clear_scene()
for fn, loc in [(rparts.wall_straight, (0, 0)), (rparts.tower_round, (-9, 0)), (rparts.gatehouse, (11, 0)),
                (rparts.portcullis, (11, -3)), (rparts.terrace, (0, -9)), (rparts.well, (-9, -9))]:
    fn().location = (loc[0], loc[1], 0)
human(3, -4)
rlib.render_check(os.path.join(OUT, "arch.png"), center=(1, -3, 5), rot_x=70, rot_z=-25, ortho_scale=34, res=(1400, 950))

rlib.clear_scene()
for fn, loc in [(rparts.brazier, (0, 0)), (rparts.torch_sconce, (1.2, 0)), (rparts.crate, (2.6, 0)),
                (rparts.barrel, (4.0, 0)), (rparts.weapon_rack, (6.2, 0)), (rparts.awning_stall, (10.0, 0)),
                (rparts.banner_wall, (13.5, 0)), (rparts.scaffold, (17.5, 0)),
                (rparts.puddle_b, (4, -3)), (rparts.rock_a, (8, -3)), (rparts.rock_b, (11, -3))]:
    fn().location = (loc[0], loc[1], 0)
human(-1.5, 0)
rlib.render_check(os.path.join(OUT, "props.png"), center=(9, -1, 1.5), rot_x=72, rot_z=-12, ortho_scale=24, res=(1600, 900))

rlib.clear_scene()
rparts.keep().location = (0, 0, 0)
rparts.tower_round().location = (14, 0, 0)
human(10, -6)
rlib.render_check(os.path.join(OUT, "keep.png"), center=(6, 0, 12), rot_x=75, rot_z=-20, ortho_scale=40, res=(1200, 900))
print("done")
