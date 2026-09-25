# 遺跡アリーナ環境アセット用の共通処理。
# 形状は Builder に頂点・面を積み上げ、最後に 1 メッシュとして生成する。
import math
import random

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

SLOTS = ["M_Base", "M_Accent", "M_Emit", "M_Struct"]
BASE, ACCENT, EMIT, STRUCT = 0, 1, 2, 3

# Workbench 確認用のビューポート色
SLOT_VIEW_COLORS = {
    "M_Base": (0.62, 0.58, 0.52, 1.0),
    "M_Accent": (0.40, 0.55, 0.30, 1.0),
    "M_Emit": (1.00, 0.55, 0.10, 1.0),
    "M_Struct": (0.30, 0.25, 0.22, 1.0),
}


# ---------------------------------------------------------------- シーン

def clear_scene():
    """全オブジェクトと孤立データを削除する"""
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.cameras, bpy.data.lights):
        for d in list(coll):
            if d.users == 0:
                coll.remove(d)


def ensure_slot_materials():
    """規約の 4 スロット分のマテリアルを用意し、ビューポート色を設定する"""
    mats = []
    for name in SLOTS:
        mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        mat.diffuse_color = SLOT_VIEW_COLORS[name]
        mats.append(mat)
    return mats


# ---------------------------------------------------------------- 幾何ユーティリティ

def _newell_normal(pts):
    n = Vector((0.0, 0.0, 0.0))
    for i, a in enumerate(pts):
        b = pts[(i + 1) % len(pts)]
        n.x += (a.y - b.y) * (a.z + b.z)
        n.y += (a.z - b.z) * (a.x + b.x)
        n.z += (a.x - b.x) * (a.y + b.y)
    return n


def _center(pts):
    c = Vector((0.0, 0.0, 0.0))
    for p in pts:
        c += p
    return c / len(pts)


def rot_matrix(rx=0.0, ry=0.0, rz=0.0):
    """度数指定の XYZ オイラー回転行列 (3x3)"""
    return Euler((math.radians(rx), math.radians(ry), math.radians(rz)), "XYZ").to_matrix()


class Builder:
    """頂点・面・面ごとのマテリアル番号を積み上げる"""

    def __init__(self):
        self.verts = []
        self.faces = []
        self.mats = []

    def add(self, verts, faces, mat, orient="centroid", matrix=None, offset=(0, 0, 0), mat_fn=None):
        """verts/faces を追加する。

        orient: "centroid" は部品の重心から外向きになるよう各面を反転する（凸な部品用）。
                Vector を渡すと全面をその向きに揃える。None は与えられた順序のまま。
        matrix: 3x3 回転行列。offset より先に適用する。
        mat_fn: (法線, 面中心) -> マテリアル番号。指定時は mat より優先する。
        """
        off = Vector(offset)
        pts = [Vector(v) for v in verts]
        if matrix is not None:
            pts = [matrix @ p for p in pts]
        pts = [p + off for p in pts]
        centroid = _center(pts)
        base = len(self.verts)
        for f in faces:
            fp = [pts[i] for i in f]
            n = _newell_normal(fp)
            fc = _center(fp)
            if orient == "centroid":
                if n.dot(fc - centroid) < 0:
                    f = list(reversed(f))
                    n = -n
            elif isinstance(orient, Vector):
                if n.dot(orient) < 0:
                    f = list(reversed(f))
                    n = -n
            self.faces.append([base + i for i in f])
            m = mat_fn(n.normalized(), fc) if mat_fn else mat
            self.mats.append(m)
        self.verts.extend([tuple(p) for p in pts])

    def add_face(self, pts, mat, expected):
        """単独の面を1枚追加し、法線を expected 方向に揃える"""
        self.add(pts, [list(range(len(pts)))], mat, orient=Vector(expected))

    # ------------------------------------------------------------ プリミティブ

    def box(self, center_xy, z0, size, mat, rz=0.0, bottom=True, mat_fn=None, matrix=None):
        """底面が z0 の直方体。size=(sx, sy, sz)"""
        sx, sy, sz = size
        hx, hy = sx / 2, sy / 2
        v = [(-hx, -hy, 0), (hx, -hy, 0), (hx, hy, 0), (-hx, hy, 0),
             (-hx, -hy, sz), (hx, -hy, sz), (hx, hy, sz), (-hx, hy, sz)]
        f = [[4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
        if bottom:
            f.append([0, 3, 2, 1])
        m = matrix if matrix is not None else rot_matrix(rz=rz)
        self.add(v, f, mat, matrix=m, offset=(center_xy[0], center_xy[1], z0), mat_fn=mat_fn)

    def prism(self, center_xy, z0, r0, r1, h, sides, mat, rot=0.0, top=True, bottom=True,
              top_offsets=None, top_mat=None, matrix=None, extra_offset=(0, 0, 0)):
        """底面半径 r0、上面半径 r1 の角柱。top_offsets を渡すと上端が不揃いになり、上面は扇状になる"""
        v = []
        a0 = math.radians(rot)
        for i in range(sides):
            a = a0 + 2 * math.pi * i / sides
            v.append((r0 * math.cos(a), r0 * math.sin(a), 0.0))
        for i in range(sides):
            a = a0 + 2 * math.pi * i / sides
            dz = top_offsets[i] if top_offsets else 0.0
            v.append((r1 * math.cos(a), r1 * math.sin(a), h + dz))
        faces = []
        for i in range(sides):
            j = (i + 1) % sides
            faces.append([i, j, sides + j, sides + i])
        if bottom:
            faces.append(list(reversed(range(sides))))
        mats = [mat] * len(faces)
        if top:
            if top_offsets:
                cz = h + sum(top_offsets) / sides
                v.append((0.0, 0.0, cz))
                c = len(v) - 1
                for i in range(sides):
                    j = (i + 1) % sides
                    faces.append([sides + i, sides + j, c])
                    mats.append(top_mat if top_mat is not None else mat)
            else:
                faces.append(list(range(sides, 2 * sides)))
                mats.append(top_mat if top_mat is not None else mat)
        tm = matrix if matrix is not None else Matrix.Identity(3)
        off = Vector((center_xy[0], center_xy[1], z0)) + Vector(extra_offset)
        # 面ごとにマテリアルが違うので 1 面ずつ追加する（向きは部品全体の重心基準）
        pts = [tm @ Vector(p) + off for p in v]
        centroid = _center(pts)
        base = len(self.verts)
        for f, m in zip(faces, mats):
            fp = [pts[i] for i in f]
            if _newell_normal(fp).dot(_center(fp) - centroid) < 0:
                f = list(reversed(f))
            self.faces.append([base + i for i in f])
            self.mats.append(m)
        self.verts.extend([tuple(p) for p in pts])

    def lathe(self, profile, sides, mat, center_xy=(0, 0), z0=0.0, cap_bottom=True, cap_top=True,
              top_mat=None, rot=0.0):
        """profile=[(r, z), ...]（下から上）を Z 軸まわりに回転した立体"""
        v = []
        a0 = math.radians(rot)
        for r, z in profile:
            for i in range(sides):
                a = a0 + 2 * math.pi * i / sides
                v.append((center_xy[0] + r * math.cos(a), center_xy[1] + r * math.sin(a), z0 + z))
        n = len(profile)
        for k in range(n - 1):
            for i in range(sides):
                j = (i + 1) % sides
                q = [k * sides + i, k * sides + j, (k + 1) * sides + j, (k + 1) * sides + i]
                self.add_face([v[x] for x in q], mat,
                              expected=_center([Vector(v[x]) for x in q]) - Vector((center_xy[0], center_xy[1], z0 + (profile[k][1] + profile[k + 1][1]) / 2)))
        if cap_bottom:
            self.add_face([v[i] for i in range(sides)], mat, expected=(0, 0, -1))
        if cap_top:
            self.add_face([v[(n - 1) * sides + i] for i in range(sides)],
                          top_mat if top_mat is not None else mat, expected=(0, 0, 1))

    def hull(self, points, mat, mat_fn=None):
        """点群の凸包を追加する"""
        bm = bmesh.new()
        for p in points:
            bm.verts.new(p)
        bmesh.ops.convex_hull(bm, input=bm.verts)
        # 凸包に使われなかった内部点を除く
        loose = [v for v in bm.verts if not v.link_faces]
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
        bm.verts.index_update()
        verts = [tuple(v.co) for v in bm.verts]
        faces = [[v.index for v in f.verts] for f in bm.faces]
        bm.free()
        self.add(verts, faces, mat, orient="centroid", mat_fn=mat_fn)

    def extrude(self, poly, basis_u, basis_v, thickness, mat, offset=(0, 0, 0), cap_mat=None, side_mat_fn=None):
        """2D 多角形 poly（(u, v) の反時計回り）を W=U×V 方向に thickness だけ押し出す。凹多角形可"""
        U, V = Vector(basis_u), Vector(basis_v)
        W = U.cross(V)
        off = Vector(offset)
        area = sum(poly[i][0] * poly[(i + 1) % len(poly)][1] - poly[(i + 1) % len(poly)][0] * poly[i][1]
                   for i in range(len(poly)))
        if area < 0:
            poly = list(reversed(poly))
        front = [off + U * u + V * v - W * (thickness / 2) for u, v in poly]
        back = [off + U * u + V * v + W * (thickness / 2) for u, v in poly]
        cm = cap_mat if cap_mat is not None else mat
        self.add_face(back, cm, expected=W)
        self.add_face(list(reversed(front)), cm, expected=-W)
        n = len(poly)
        for i in range(n):
            j = (i + 1) % n
            du, dv = poly[j][0] - poly[i][0], poly[j][1] - poly[i][1]
            out = (U * dv - V * du).normalized()
            q = [front[i], front[j], back[j], back[i]]
            m = side_mat_fn(out) if side_mat_fn else mat
            self.add_face(q, m, expected=out)

    # ------------------------------------------------------------ 生成

    def build(self, name, merge_dist=1e-5):
        """積み上げた形状から、規約どおりの名前・スロットを持つオブジェクトを作る"""
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(self.verts, [], self.faces)
        mesh.update()
        for poly, m in zip(mesh.polygons, self.mats):
            poly.material_index = m
            poly.use_smooth = False
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=merge_dist)
        bm.to_mesh(mesh)
        bm.free()
        mesh.name = name
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        for mat in ensure_slot_materials():
            mesh.materials.append(mat)
        return obj


# ---------------------------------------------------------------- 乱数形状

def ellipsoid_points(rng, n, rx, ry, rz, jitter=0.15, z_floor=None):
    """楕円体表面付近の点群。z_floor 指定時はそれより下を切り詰める"""
    pts = []
    for _ in range(n):
        u = rng.uniform(-1, 1)
        t = rng.uniform(0, 2 * math.pi)
        s = math.sqrt(1 - u * u)
        k = 1 + rng.uniform(-jitter, jitter)
        p = Vector((rx * s * math.cos(t) * k, ry * s * math.sin(t) * k, rz * u * k))
        if z_floor is not None and p.z < z_floor:
            p.z = z_floor
        pts.append(p)
    return pts


def up_is(mat_up, mat_other, threshold=0.6):
    """上向きの面だけ mat_up にする mat_fn"""
    return lambda n, c: mat_up if n.z > threshold else mat_other


# ---------------------------------------------------------------- 検証・保存

def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def report(obj):
    """寸法・三角数・原点・命名・スロットを検証して 1 行で返す"""
    d = obj.dimensions
    zs = [v.co.z for v in obj.data.vertices]
    xs = [v.co.x for v in obj.data.vertices]
    ys = [v.co.y for v in obj.data.vertices]
    issues = []
    if obj.name != obj.data.name:
        issues.append("name!=data")
    if abs(min(zs)) > 1e-4:
        issues.append(f"minZ={min(zs):.3f}")
    if tuple(obj.scale) != (1.0, 1.0, 1.0):
        issues.append("scale")
    if [m.name for m in obj.data.materials] != SLOTS:
        issues.append("slots")
    used = sorted({p.material_index for p in obj.data.polygons})
    return (f"{obj.name}: tris={tri_count(obj)} verts={len(obj.data.vertices)} "
            f"dim=({d.x:.2f},{d.y:.2f},{d.z:.2f}) "
            f"bboxXY=({min(xs):.2f}..{max(xs):.2f},{min(ys):.2f}..{max(ys):.2f}) "
            f"slotsUsed={used} {'OK' if not issues else 'ISSUES ' + ','.join(issues)}")


def render_check(path, center=(0, 0, 0), rot_x=65, rot_z=-35, ortho_scale=10.0, res=(900, 650), dist=200.0):
    """オルソカメラを立てて Workbench で png に焼く"""
    scene = bpy.context.scene
    cam_data = bpy.data.cameras.new("_shot_cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = ortho_scale
    cam_data.clip_end = dist * 3
    cam = bpy.data.objects.new("_shot_cam", cam_data)
    scene.collection.objects.link(cam)
    rot = Euler((math.radians(rot_x), 0, math.radians(rot_z)), "XYZ")
    cam.rotation_euler = rot
    cam.location = Vector(center) + rot.to_matrix() @ Vector((0, 0, dist))
    prev_cam, prev_engine = scene.camera, scene.render.engine
    scene.camera = cam
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_cavity = False
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    scene.camera = prev_cam
    try:
        scene.render.engine = prev_engine
    except TypeError:
        pass
    bpy.data.objects.remove(cam, do_unlink=True)
    bpy.data.cameras.remove(cam_data)


def save_and_export(obj, blend_dir, export_dir):
    """シーンのメッシュが obj 1 つであることを確認してから .blend 保存と FBX 書き出しを行う"""
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    if len(meshes) != 1 or meshes[0] != obj:
        return f"SKIP {obj.name}: meshes={[o.name for o in meshes]}"
    bpy.ops.wm.save_as_mainfile(filepath=f"{blend_dir}/{obj.name}.blend", check_existing=False)
    bpy.ops.export_scene.fbx(
        filepath=f"{export_dir}/{obj.name}.fbx",
        use_selection=False,
        object_types={"MESH"},
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        mesh_smooth_type="FACE",
        use_triangles=True,
        add_leaf_bones=False,
        bake_anim=False,
        use_custom_props=False,
    )
    return f"EXPORTED {obj.name}"
