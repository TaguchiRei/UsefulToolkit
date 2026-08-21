using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// オブジェクト単位で、NativeMeshDataStoreにキャッシュ済みのメッシュデータを
    /// 1回のCut呼び出し用に結合されたバッファへコピーする。三角形の頂点インデックスは
    /// オブジェクトごとのグローバルオフセットを加算した値に変換する。
    /// </summary>
    [BurstCompile]
    public struct CopyMeshDataJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> SrcVertices;
        [ReadOnly] public NativeArray<float3> SrcNormals;
        [ReadOnly] public NativeArray<float2> SrcUvs;
        [ReadOnly] public NativeArray<int3> SrcTriangles;
        [ReadOnly] public NativeArray<int> SrcTriangleSubmesh;

        [ReadOnly] public NativeArray<int2> SrcMeshVertexRange;
        [ReadOnly] public NativeArray<int2> SrcMeshTriangleRange;

        [ReadOnly] public NativeArray<int> ObjectMeshId;
        [ReadOnly] public NativeArray<int2> ObjectVertexRange;
        [ReadOnly] public NativeArray<int2> ObjectTriangleRange;

        [NativeDisableParallelForRestriction] public NativeArray<float3> DstVertices;
        [NativeDisableParallelForRestriction] public NativeArray<float3> DstNormals;
        [NativeDisableParallelForRestriction] public NativeArray<float2> DstUvs;
        [NativeDisableParallelForRestriction] public NativeArray<int> VertexObjectIndex;

        [NativeDisableParallelForRestriction] public NativeArray<int3> DstTriangles;
        [NativeDisableParallelForRestriction] public NativeArray<int> DstTriangleSubmesh;

        public void Execute(int objIndex)
        {
            int meshId = ObjectMeshId[objIndex];

            int2 srcV = SrcMeshVertexRange[meshId];
            int2 dstV = ObjectVertexRange[objIndex];

            NativeArray<float3>.Copy(SrcVertices, srcV.x, DstVertices, dstV.x, dstV.y);
            NativeArray<float3>.Copy(SrcNormals, srcV.x, DstNormals, dstV.x, dstV.y);
            NativeArray<float2>.Copy(SrcUvs, srcV.x, DstUvs, dstV.x, dstV.y);

            for (int i = 0; i < dstV.y; i++)
            {
                VertexObjectIndex[dstV.x + i] = objIndex;
            }

            int2 srcT = SrcMeshTriangleRange[meshId];
            int2 dstT = ObjectTriangleRange[objIndex];

            for (int i = 0; i < dstT.y; i++)
            {
                int3 localTri = SrcTriangles[srcT.x + i];
                DstTriangles[dstT.x + i] = localTri + dstV.x;
                DstTriangleSubmesh[dstT.x + i] = SrcTriangleSubmesh[srcT.x + i];
            }
        }
    }
}
