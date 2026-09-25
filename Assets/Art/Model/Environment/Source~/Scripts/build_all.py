# 全パーツを 1 つずつ生成・検証し、.blend 保存と FBX 書き出しを行う。
# ONLY に名前を入れるとそのパーツだけ処理する。EXPORT=False なら生成と検証のみ。
import importlib
import os
import sys

SCRIPT_DIR = os.path.dirname(__file__)
ROOT = os.path.normpath(os.path.join(SCRIPT_DIR, "..", ".."))
BLEND_DIR = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "BlendFile"))
os.makedirs(BLEND_DIR, exist_ok=True)
if SCRIPT_DIR not in sys.path:
    sys.path.append(SCRIPT_DIR)

import envlib
import parts

importlib.reload(envlib)
importlib.reload(parts)

ONLY = globals().get("ONLY", None)
EXPORT = globals().get("EXPORT", True)

results = []
for fn in parts.ALL_PARTS:
    if ONLY and fn.__name__ not in ONLY:
        continue
    envlib.clear_scene()
    obj = fn()
    line = envlib.report(obj)
    if EXPORT:
        line += " | " + envlib.save_and_export(obj, BLEND_DIR, ROOT)
    results.append(line)
print("\n".join(results))
