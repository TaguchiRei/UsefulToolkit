using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// シーン内に登録された全ユニークメッシュを、Burst Jobから直接読めるフラットなNativeArrayとして
    /// 一度だけ構築・保持するストア。
    /// 切断結果のフラグメントを実行時に追加登録することもできる。
    /// </summary>
    public class NativeMeshDataStore : IDisposable
    {
        public NativeList<float3> Vertices;
        public NativeList<float3> Normals;
        public NativeList<float2> Uvs;

        /// <summary> メッシュローカルな頂点インデックスを持つ、サブメッシュをまたいでフラット化済みの三角形リスト </summary>
        public NativeList<int3> Triangles;

        /// <summary> Trianglesと1対1対応するサブメッシュ番号 </summary>
        public NativeList<int> TriangleSubmesh;

        /// <summary> メッシュIDごとの (Vertices/Normals/Uvs内でのstart, count) </summary>
        public NativeList<int2> MeshVertexRange;

        /// <summary> メッシュIDごとの (Triangles/TriangleSubmesh内でのstart, count) </summary>
        public NativeList<int2> MeshTriangleRange;

        /// <summary> メッシュIDごとのサブメッシュ数 </summary>
        public NativeList<int> MeshSubmeshCount;

        /// <summary>
        /// メッシュIDごとの断面(キャップ)サブメッシュ番号。まだ切断されていないメッシュは -1。
        /// 既に断面を持つメッシュを再度切るときは、新しいサブメッシュを足さずにこの番号へ断面を追記する。
        /// </summary>
        public NativeList<int> MeshCapSubmesh;

        public int MeshCount => MeshVertexRange.Length;

        public NativeMeshDataStore()
        {
            Vertices = new NativeList<float3>(Allocator.Persistent);
            Normals = new NativeList<float3>(Allocator.Persistent);
            Uvs = new NativeList<float2>(Allocator.Persistent);
            Triangles = new NativeList<int3>(Allocator.Persistent);
            TriangleSubmesh = new NativeList<int>(Allocator.Persistent);
            MeshVertexRange = new NativeList<int2>(Allocator.Persistent);
            MeshTriangleRange = new NativeList<int2>(Allocator.Persistent);
            MeshSubmeshCount = new NativeList<int>(Allocator.Persistent);
            MeshCapSubmesh = new NativeList<int>(Allocator.Persistent);
        }

        /// <summary> メッシュを登録し、割り当てられたメッシュIDを返す </summary>
        public int Add(Mesh mesh)
        {
            int vStart = Vertices.Length;
            Vector3[] verts = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uvs = mesh.uv;
            int vertexCount = mesh.vertexCount;

            Vertices.Resize(vStart + vertexCount, NativeArrayOptions.UninitializedMemory);
            Normals.Resize(vStart + vertexCount, NativeArrayOptions.UninitializedMemory);
            Uvs.Resize(vStart + vertexCount, NativeArrayOptions.UninitializedMemory);

            Vertices.AsArray().GetSubArray(vStart, vertexCount).Reinterpret<Vector3>().CopyFrom(verts);
            Normals.AsArray().GetSubArray(vStart, vertexCount).Reinterpret<Vector3>().CopyFrom(normals);
            Uvs.AsArray().GetSubArray(vStart, vertexCount).Reinterpret<Vector2>().CopyFrom(uvs);

            int subCount = mesh.subMeshCount;
            int tStart = Triangles.Length;

            for (int s = 0; s < subCount; s++)
            {
                int[] tris = mesh.GetTriangles(s);
                int triCount = tris.Length / 3;

                for (int i = 0; i < triCount; i++)
                {
                    Triangles.Add(new int3(tris[i * 3 + 0], tris[i * 3 + 1], tris[i * 3 + 2]));
                    TriangleSubmesh.Add(s);
                }
            }

            MeshVertexRange.Add(new int2(vStart, vertexCount));
            MeshTriangleRange.Add(new int2(tStart, Triangles.Length - tStart));
            MeshSubmeshCount.Add(subCount);

            // 未切断のメッシュには断面サブメッシュが無い
            MeshCapSubmesh.Add(-1);

            return MeshVertexRange.Length - 1;
        }

        /// <summary>
        /// 切断結果のフラグメントを新しいメッシュとして登録し、割り当てられたメッシュIDを返します。
        /// 生成済みの Mesh から読み直すのではなく、切断Jobが書き出したNativeバッファから直接コピーするため、
        /// メインスレッドでの配列コピーが発生しません。
        /// </summary>
        /// <param name="vertexStart">FragmentVerticesFlat 等における、このフラグメントの先頭位置</param>
        /// <param name="vertexCount">このフラグメントの実頂点数</param>
        /// <param name="slotStart">FragmentIndexRange/FragmentIndexCount における、このフラグメントの先頭スロット</param>
        /// <param name="submeshCount">このフラグメントのサブメッシュ数(断面サブメッシュを含む)</param>
        /// <param name="capSubmesh">断面サブメッシュの番号</param>
        public int AppendFragment(
            NativeArray<float3> srcVertices,
            NativeArray<float3> srcNormals,
            NativeArray<float2> srcUvs,
            int vertexStart,
            int vertexCount,
            NativeArray<int> srcIndices,
            NativeArray<int2> indexRanges,
            NativeArray<int> indexCounts,
            int slotStart,
            int submeshCount,
            int capSubmesh)
        {
            int vStart = Vertices.Length;

            // 刃が実際には切っていない側のフラグメントは頂点数0になる。
            // NativeArray.Copy は dstIndex == dstLength を範囲外として弾くため、コピー自体を行わない
            if (vertexCount > 0)
            {
                Vertices.Resize(vStart + vertexCount, NativeArrayOptions.UninitializedMemory);
                Normals.Resize(vStart + vertexCount, NativeArrayOptions.UninitializedMemory);
                Uvs.Resize(vStart + vertexCount, NativeArrayOptions.UninitializedMemory);

                NativeArray<float3>.Copy(srcVertices, vertexStart, Vertices.AsArray(), vStart, vertexCount);
                NativeArray<float3>.Copy(srcNormals, vertexStart, Normals.AsArray(), vStart, vertexCount);
                NativeArray<float2>.Copy(srcUvs, vertexStart, Uvs.AsArray(), vStart, vertexCount);
            }

            int tStart = Triangles.Length;

            for (int s = 0; s < submeshCount; s++)
            {
                int2 range = indexRanges[slotStart + s];
                int indexCount = indexCounts[slotStart + s];

                if (indexCount <= 0) continue;

                // インデックス3つ = 三角形1つ。int3として再解釈すればmemcpyで移せる
                var triangles = srcIndices
                    .GetSubArray(range.x, indexCount)
                    .Reinterpret<int3>(UnsafeUtility.SizeOf<int>());

                Triangles.AddRange(triangles);

                int triangleCount = indexCount / 3;
                int submeshStart = TriangleSubmesh.Length;

                TriangleSubmesh.Resize(submeshStart + triangleCount, NativeArrayOptions.UninitializedMemory);

                for (int i = 0; i < triangleCount; i++)
                {
                    TriangleSubmesh[submeshStart + i] = s;
                }
            }

            MeshVertexRange.Add(new int2(vStart, vertexCount));
            MeshTriangleRange.Add(new int2(tStart, Triangles.Length - tStart));
            MeshSubmeshCount.Add(submeshCount);
            MeshCapSubmesh.Add(capSubmesh);

            return MeshVertexRange.Length - 1;
        }

        /// <summary>
        /// 別のストアが持つメッシュを1件そのままコピーして登録し、割り当てられたメッシュIDを返します。
        /// ストアの再構築(不要になったエントリの破棄)に使います。
        /// </summary>
        public int CopyMeshFrom(NativeMeshDataStore source, int meshId)
        {
            int2 srcV = source.MeshVertexRange[meshId];
            int2 srcT = source.MeshTriangleRange[meshId];

            int vStart = Vertices.Length;

            if (srcV.y > 0)
            {
                Vertices.Resize(vStart + srcV.y, NativeArrayOptions.UninitializedMemory);
                Normals.Resize(vStart + srcV.y, NativeArrayOptions.UninitializedMemory);
                Uvs.Resize(vStart + srcV.y, NativeArrayOptions.UninitializedMemory);

                NativeArray<float3>.Copy(source.Vertices.AsArray(), srcV.x, Vertices.AsArray(), vStart, srcV.y);
                NativeArray<float3>.Copy(source.Normals.AsArray(), srcV.x, Normals.AsArray(), vStart, srcV.y);
                NativeArray<float2>.Copy(source.Uvs.AsArray(), srcV.x, Uvs.AsArray(), vStart, srcV.y);
            }

            int tStart = Triangles.Length;

            if (srcT.y > 0)
            {
                Triangles.Resize(tStart + srcT.y, NativeArrayOptions.UninitializedMemory);
                TriangleSubmesh.Resize(tStart + srcT.y, NativeArrayOptions.UninitializedMemory);

                NativeArray<int3>.Copy(source.Triangles.AsArray(), srcT.x, Triangles.AsArray(), tStart, srcT.y);
                NativeArray<int>.Copy(source.TriangleSubmesh.AsArray(), srcT.x, TriangleSubmesh.AsArray(), tStart,
                    srcT.y);
            }

            MeshVertexRange.Add(new int2(vStart, srcV.y));
            MeshTriangleRange.Add(new int2(tStart, srcT.y));
            MeshSubmeshCount.Add(source.MeshSubmeshCount[meshId]);
            MeshCapSubmesh.Add(source.MeshCapSubmesh[meshId]);

            return MeshVertexRange.Length - 1;
        }

        public void Clear()
        {
            Vertices.Clear();
            Normals.Clear();
            Uvs.Clear();
            Triangles.Clear();
            TriangleSubmesh.Clear();
            MeshVertexRange.Clear();
            MeshTriangleRange.Clear();
            MeshSubmeshCount.Clear();
            MeshCapSubmesh.Clear();
        }

        public void Dispose()
        {
            if (Vertices.IsCreated) Vertices.Dispose();
            if (Normals.IsCreated) Normals.Dispose();
            if (Uvs.IsCreated) Uvs.Dispose();
            if (Triangles.IsCreated) Triangles.Dispose();
            if (TriangleSubmesh.IsCreated) TriangleSubmesh.Dispose();
            if (MeshVertexRange.IsCreated) MeshVertexRange.Dispose();
            if (MeshTriangleRange.IsCreated) MeshTriangleRange.Dispose();
            if (MeshSubmeshCount.IsCreated) MeshSubmeshCount.Dispose();
            if (MeshCapSubmesh.IsCreated) MeshCapSubmesh.Dispose();
        }
    }
}
