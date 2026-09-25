# 全パーツを 1 つずつ生成・検証し、.blend 保存と FBX 書き出しを行う。
# ONLY に関数名を入れるとそのパーツだけ処理する。EXPORT=False なら生成と検証のみ。
import importlib
import os
import sys

SCRIPT_DIR = os.path.dirname(__file__)
ROOT = os.path.normpath(os.path.join(SCRIPT_DIR, "..", ".."))
BLEND_DIR = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "BlendFile"))
os.makedirs(BLEND_DIR, exist_ok=True)
if SCRIPT_DIR not in sys.path:
    sys.path.append(SCRIPT_DIR)

import rlib
import rparts

importlib.reload(rlib)
importlib.reload(rparts)

ONLY = globals().get("ONLY", None)
EXPORT = globals().get("EXPORT", True)

results = []
for fn in rparts.ALL_PARTS:
    if ONLY and fn.__name__ not in ONLY:
        continue
    rlib.clear_scene()
    try:
        obj = fn()
    except Exception as ex:
        results.append(f"FAIL {fn.__name__}: {ex!r}")
        continue
    line = rlib.report(obj)
    if EXPORT:
        line += " | " + rlib.save_and_export(obj, BLEND_DIR, ROOT)
    results.append(line)
print("\n".join(results))
