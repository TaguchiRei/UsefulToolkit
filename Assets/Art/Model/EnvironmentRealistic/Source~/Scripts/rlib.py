# リアル調環境アセット用の共通処理。
# 部品ごとに一時 bmesh で形状を作ってベベルをかけ、1 つの bmesh に合流させる。
# UV は 1 UV = 1m の実寸投影（テクスチャ側のタイリングはマテリアルで決める）。
import math

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

SLOTS = ["M_Base", "M_Accent", "M_Emit", "M_Struct"]
BASE, ACCENT, EMIT, STRUCT = 0, 1, 2, 3
SLOT_VIEW_COLORS = {
    "M_Base": (0.55, 0.53, 0.50, 1.0),
    "M_Accent": (0.25, 0.35, 0.55, 1.0),
    "M_Emit": (1.00, 0.55, 0.10, 1.0),
    "M_Struct": (0.35, 0.25, 0.18, 1.0),
}
SHARP_ANGLE = math.radians(35)


def clear_scene():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.cameras, bpy.data.lights):
        for d in list(coll):
            if d.users == 0:
                coll.remove(d)


def ensure_slot_materials():
    mats = []
    for name in SLOTS:
        mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        mat.diffuse_color = SLOT_VIEW_COLORS[name]
        mats.append(mat)
    return mats


def rot(rx=0.0, ry=0.0, rz=0.0):
    """度数指定の XYZ オイラー回転行列 (3x3)"""
    return Euler((math.radians(rx), math.radians(ry), math.radians(rz)), "XYZ").to_matrix()


def up_is(mat_up, mat_other, threshold=0.6):
    return lambda n, c: mat_up if n.z > threshold else mat_other


class RB:
    """部品を積み上げて 1 メッシュを作るビルダー"""

    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")

    # ------------------------------------------------------------ 合流

    def _merge(self, tmp, mat, matrix=None, offset=(0, 0, 0), bevel=0.0, bevel_seg=1, uv="box",
               axis_center=(0.0, 0.0)):
        """一時 bmesh を法線整列・ベベル・変換してから本体に合流させる。

        mat: マテリアル番号、または (法線, 面中心) -> 番号 の関数（ワールド座標で評価）
        uv: "box" | "cyl"（Z 軸まわりの円筒投影）| "cyl_rot"（円筒投影で u/v を入れ替え、板目を縦にする）
        axis_center: 円筒投影の中心軸（変換後の XY 座標）
        """
        bmesh.ops.recalc_face_normals(tmp, faces=tmp.faces)
        if bevel > 0:
            edges = [e for e in tmp.edges if e.is_manifold and e.calc_face_angle(0) > math.radians(30)]
            if edges:
                bmesh.ops.bevel(tmp, geom=edges, offset=bevel, offset_type="OFFSET", segments=bevel_seg,
                                profile=0.5, affect="EDGES", clamp_overlap=True, material=-1)
        m = matrix if matrix is not None else Matrix.Identity(3)
        off = Vector(offset)
        vmap = {}
        for v in tmp.verts:
            vmap[v] = self.bm.verts.new(m @ v.co + off)
        cx, cy = axis_center
        for f in tmp.faces:
            try:
                nf = self.bm.faces.new([vmap[v] for v in f.verts])
            except ValueError:
                continue
            nf.normal_update()
            n = nf.normal
            c = nf.calc_center_median()
            nf.material_index = mat(n, c) if callable(mat) else mat
            nf.smooth = True
            use_cyl = uv in ("cyl", "cyl_rot") and abs(n.z) < 0.7
            if use_cyl:
                angs = [math.atan2(l.vert.co.y - cy, l.vert.co.x - cx) for l in nf.loops]
                if max(angs) - min(angs) > math.pi:
                    angs = [a + 2 * math.pi if a < 0 else a for a in angs]
                for l, a in zip(nf.loops, angs):
                    r = math.hypot(l.vert.co.x - cx, l.vert.co.y - cy)
                    uvv = (a * max(r, 0.05), l.vert.co.z)
                    l[self.uv].uv = (uvv[1], uvv[0]) if uv == "cyl_rot" else uvv
            else:
                ax = max(range(3), key=lambda i: abs(n[i]))
                for l in nf.loops:
                    p = l.vert.co
                    if ax == 2:
                        l[self.uv].uv = (p.x, p.y)
                    elif ax == 0:
                        l[self.uv].uv = (p.y, p.z)
                    else:
                        l[self.uv].uv = (p.x, p.z)
        tmp.free()

    # ------------------------------------------------------------ プリミティブ

    def box(self, center, size, mat, r=None, bevel=0.03, bevel_seg=1, z_is_bottom=True):
        """直方体。center は (x, y, z)。z_is_bottom なら z は底面の高さ"""
        tmp = bmesh.new()
        bmesh.ops.create_cube(tmp, size=1.0)
        sx, sy, sz = size
        for v in tmp.verts:
            v.co = Vector((v.co.x * sx, v.co.y * sy, v.co.z * sz + (sz / 2 if z_is_bottom else 0)))
        self._merge(tmp, mat, matrix=r, offset=center, bevel=bevel, bevel_seg=bevel_seg)

    def cyl(self, center, r0, r1, h, sides, mat, r=None, bevel=0.0, caps=True, uv="cyl", rot_deg=0.0):
        """底面中心 center、高さ h の円柱（円錐台）。r1=0 で円錐"""
        tmp = bmesh.new()
        bmesh.ops.create_cone(tmp, cap_ends=caps, cap_tris=False, segments=sides, radius1=r0,
                              radius2=max(r1, 1e-4), depth=h,
                              matrix=Matrix.Translation((0, 0, h / 2)) @ Matrix.Rotation(math.radians(rot_deg), 4, "Z"))
        if r1 <= 0:
            top = [v for v in tmp.verts if v.co.z > h - 1e-5]
            bmesh.ops.pointmerge(tmp, verts=top, merge_co=Vector((0, 0, h)))
        m = r if r is not None else Matrix.Identity(3)
        ac = Vector(center) + m @ Vector((0, 0, 0))
        self._merge(tmp, mat, matrix=r, offset=center, bevel=bevel, uv=uv, axis_center=(ac.x, ac.y))

    def lathe(self, profile, sides, mat, center=(0, 0, 0), closed=False, cap_bottom=True, cap_top=True,
              uv="cyl", bevel=0.0, rot_deg=0.0):
        """profile=[(r, z), ...] を Z 軸まわりに回転。closed=True なら断面を閉じたリング（中空）にする"""
        tmp = bmesh.new()
        rings = []
        a0 = math.radians(rot_deg)
        for rr, z in profile:
            ring = []
            for i in range(sides):
                a = a0 + 2 * math.pi * i / sides
                ring.append(tmp.verts.new((rr * math.cos(a), rr * math.sin(a), z)))
            rings.append(ring)
        n = len(rings)
        pairs = [(k, k + 1) for k in range(n - 1)]
        if closed:
            pairs.append((n - 1, 0))
        for k0, k1 in pairs:
            for i in range(sides):
                j = (i + 1) % sides
                tmp.faces.new([rings[k0][i], rings[k0][j], rings[k1][j], rings[k1][i]])
        if not closed:
            if cap_bottom and profile[0][0] > 1e-4:
                tmp.faces.new(list(reversed(rings[0])))
            if cap_top and profile[-1][0] > 1e-4:
                tmp.faces.new(rings[-1])
        self._merge(tmp, mat, offset=center, bevel=bevel, uv=uv, axis_center=(center[0], center[1]))

    def extrude(self, poly, basis_u, basis_v, thickness, mat, offset=(0, 0, 0), bevel=0.0):
        """2D 多角形（凹可）を W=U×V 方向に厚み thickness で押し出す（W 方向の中央が offset）"""
        U, V = Vector(basis_u), Vector(basis_v)
        W = U.cross(V)
        tmp = bmesh.new()
        front = [tmp.verts.new(U * u + V * v - W * (thickness / 2)) for u, v in poly]
        back = [tmp.verts.new(U * u + V * v + W * (thickness / 2)) for u, v in poly]
        tmp.faces.new(front)
        tmp.faces.new(back)
        k = len(poly)
        for i in range(k):
            j = (i + 1) % k
            tmp.faces.new([front[i], front[j], back[j], back[i]])
        self._merge(tmp, mat, offset=offset, bevel=bevel)

    def hull(self, points, mat, bevel=0.0):
        tmp = bmesh.new()
        for p in points:
            tmp.verts.new(p)
        bmesh.ops.convex_hull(tmp, input=tmp.verts)
        loose = [v for v in tmp.verts if not v.link_faces]
        bmesh.ops.delete(tmp, geom=loose, context="VERTS")
        self._merge(tmp, mat, bevel=bevel)

    def hull_detail(self, points, mat, cuts=2, amp=0.1, freq=1.0, seed=0):
        """凸包を細分化し、フラクタルノイズで法線方向に凹凸を付ける（岩・山）。Z<0 に出た頂点は Z=0 に揃える"""
        from mathutils import noise
        tmp = bmesh.new()
        for p in points:
            tmp.verts.new(p)
        bmesh.ops.convex_hull(tmp, input=tmp.verts)
        loose = [v for v in tmp.verts if not v.link_faces]
        bmesh.ops.delete(tmp, geom=loose, context="VERTS")
        for _ in range(cuts):
            bmesh.ops.subdivide_edges(tmp, edges=tmp.edges[:], cuts=1, use_grid_fill=True)
        bmesh.ops.recalc_face_normals(tmp, faces=tmp.faces)
        tmp.normal_update()
        off = Vector((seed * 7.31, seed * 3.17, seed * 5.53))
        moved = [(v, v.normal.copy(), noise.fractal(v.co * freq + off, 0.6, 2.0, 4)) for v in tmp.verts]
        for v, n, k in moved:
            v.co += n * k * amp
            if v.co.z < 0:
                v.co.z = 0.0
        self._merge(tmp, mat)

    def sheet(self, grid, thickness, mat):
        """grid[i][j] = Vector の曲面に厚みを付けた板（布など）。上面の法線方向へ厚みを付ける"""
        tmp = bmesh.new()
        ni, nj = len(grid), len(grid[0])
        top = [[tmp.verts.new(grid[i][j]) for j in range(nj)] for i in range(ni)]
        bot = [[tmp.verts.new(grid[i][j] - Vector((0, 0, thickness))) for j in range(nj)] for i in range(ni)]
        for i in range(ni - 1):
            for j in range(nj - 1):
                tmp.faces.new([top[i][j], top[i + 1][j], top[i + 1][j + 1], top[i][j + 1]])
                tmp.faces.new([bot[i][j + 1], bot[i + 1][j + 1], bot[i + 1][j], bot[i][j]])
        edge = [(i, 0) for i in range(ni)] + [(ni - 1, j) for j in range(1, nj)] + \
               [(i, nj - 1) for i in range(ni - 2, -1, -1)] + [(0, j) for j in range(nj - 2, 0, -1)]
        for a in range(len(edge)):
            (i0, j0), (i1, j1) = edge[a], edge[(a + 1) % len(edge)]
            tmp.faces.new([top[i0][j0], top[i1][j1], bot[i1][j1], bot[i0][j0]])
        self._merge(tmp, mat)

    def flat_poly(self, pts, mat, z=0.0):
        """上向きの単一面（水たまりなど）"""
        tmp = bmesh.new()
        vs = [tmp.verts.new((x, y, z)) for x, y in pts]
        f = tmp.faces.new(vs)
        f.normal_update()
        if f.normal.z < 0:
            f.normal_flip()
        # recalc_face_normals は開いた面で向きを保証しないので、ここで向きを確定してから合流する
        self._merge_no_recalc(tmp, mat)

    def _merge_no_recalc(self, tmp, mat):
        vmap = {v: self.bm.verts.new(v.co) for v in tmp.verts}
        for f in tmp.faces:
            nf = self.bm.faces.new([vmap[v] for v in f.verts])
            nf.material_index = mat
            nf.smooth = True
            for l in nf.loops:
                l[self.uv].uv = (l.vert.co.x, l.vert.co.y)
        tmp.free()

    def grid(self, sx, sy, nx, ny, mat, z_fn=None):
        """XY 平面の格子（床・地面）。z_fn(x, y) で高さを与える"""
        tmp = bmesh.new()
        vs = [[tmp.verts.new((-sx / 2 + sx * i / nx, -sy / 2 + sy * j / ny,
                              z_fn(-sx / 2 + sx * i / nx, -sy / 2 + sy * j / ny) if z_fn else 0.0))
               for j in range(ny + 1)] for i in range(nx + 1)]
        for i in range(nx):
            for j in range(ny):
                tmp.faces.new([vs[i][j], vs[i + 1][j], vs[i + 1][j + 1], vs[i][j + 1]])
        for f in tmp.faces:
            f.normal_update()
            if f.normal.z < 0:
                f.normal_flip()
        self._merge_no_recalc(tmp, mat)

    # ------------------------------------------------------------ 生成

    def build(self, name, sharp_angle=SHARP_ANGLE):
        bm = self.bm
        for e in bm.edges:
            e.smooth = not (len(e.link_faces) == 2 and e.calc_face_angle(0) > sharp_angle)
        mesh = bpy.data.meshes.new(name)
        bm.to_mesh(mesh)
        bm.free()
        mesh.name = name
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        for mat in ensure_slot_materials():
            mesh.materials.append(mat)
        return obj


# ---------------------------------------------------------------- 検証・保存

def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def report(obj):
    d = obj.dimensions
    vs = obj.data.vertices
    zs = [v.co.z for v in vs]
    xs = [v.co.x for v in vs]
    ys = [v.co.y for v in vs]
    issues = []
    if obj.name != obj.data.name:
        issues.append("name!=data")
    if abs(min(zs)) > 1e-3:
        issues.append(f"minZ={min(zs):.3f}")
    if [m.name for m in obj.data.materials] != SLOTS:
        issues.append("slots")
    if not obj.data.uv_layers:
        issues.append("noUV")
    used = sorted({p.material_index for p in obj.data.polygons})
    return (f"{obj.name}: tris={tri_count(obj)} dim=({d.x:.2f},{d.y:.2f},{d.z:.2f}) "
            f"x=({min(xs):.2f}..{max(xs):.2f}) y=({min(ys):.2f}..{max(ys):.2f}) slots={used} "
            f"{'OK' if not issues else 'ISSUES ' + ','.join(issues)}")


def render_check(path, center=(0, 0, 0), rot_x=65, rot_z=-35, ortho_scale=10.0, res=(1000, 700), dist=300.0):
    scene = bpy.context.scene
    cam_data = bpy.data.cameras.new("_shot_cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = ortho_scale
    cam_data.clip_end = dist * 3
    cam = bpy.data.objects.new("_shot_cam", cam_data)
    scene.collection.objects.link(cam)
    r = Euler((math.radians(rot_x), 0, math.radians(rot_z)), "XYZ")
    cam.rotation_euler = r
    cam.location = Vector(center) + r.to_matrix() @ Vector((0, 0, dist))
    prev_cam, prev_engine = scene.camera, scene.render.engine
    scene.camera = cam
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_cavity = True
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
        mesh_smooth_type="OFF",
        use_triangles=True,
        add_leaf_bones=False,
        bake_anim=False,
        use_custom_props=False,
    )
    return f"EXPORTED {obj.name}"
