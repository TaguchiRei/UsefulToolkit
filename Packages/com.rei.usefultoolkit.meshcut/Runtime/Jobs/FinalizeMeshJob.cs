using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// フラグメント単位で、フラットバッファの頂点・インデックスを Mesh.MeshData へ書き込む。
    /// フラグメント i は MeshData[i] だけに書き込むため、フラグメント間で競合しない。
    /// 書き込んだ MeshData はメインスレッドで Mesh.ApplyAndDisposeWritableMeshData により Mesh へ反映する。
    /// </summary>
    [BurstCompile]
    public struct FinalizeMeshJob : IJobParallelFor
    {
        public Mesh.MeshDataArray MeshData;

        [ReadOnly] public NativeArray<VertexAttributeDescriptor> VertexLayout;

        [ReadOnly] public NativeArray<int2> FragmentVertexRange;
        [ReadOnly] public NativeArray<int> FragmentVertexCount;
        [ReadOnly] public NativeArray<float3> FragmentVerticesFlat;
        [ReadOnly] public NativeArray<float3> FragmentNormalsFlat;
        [ReadOnly] public NativeArray<float2> FragmentUvsFlat;

        [ReadOnly] public NativeArray<int2> FragmentIndexRange;
        [ReadOnly] public NativeArray<int> FragmentIndexCount;
        [ReadOnly] public NativeArray<int> FragmentIndicesFlat;

        [ReadOnly] public NativeArray<int> ObjectCapSlot;
        public int MaxSubmeshSlots;

        public void Execute(int fragIdx)
        {
            Mesh.MeshData data = MeshData[fragIdx];

            int2 vRange = FragmentVertexRange[fragIdx];
            int vertexCount = FragmentVertexCount[fragIdx];

            data.SetVertexBufferParams(vertexCount, VertexLayout);

            // NativeArray.Copy は length が 0 でも dstIndex == dstLength を範囲外として弾くため、0件のときはコピーしない
            // (刃が実際には切らなかった側のフラグメントは頂点数0になる)
            if (vertexCount > 0)
            {
                NativeArray<float3>.Copy(FragmentVerticesFlat, vRange.x, data.GetVertexData<float3>(0), 0, vertexCount);
                NativeArray<float3>.Copy(FragmentNormalsFlat, vRange.x, data.GetVertexData<float3>(1), 0, vertexCount);
                NativeArray<float2>.Copy(FragmentUvsFlat, vRange.x, data.GetVertexData<float2>(2), 0, vertexCount);
            }

            // 断面スロットは常に最後のサブメッシュになるので、+1 がサブメッシュ数
            int submeshCount = ObjectCapSlot[fragIdx / 2] + 1;
            int slotStart = fragIdx * MaxSubmeshSlots;

            int totalIndexCount = 0;
            for (int s = 0; s < submeshCount; s++)
            {
                totalIndexCount += FragmentIndexCount[slotStart + s];
            }

            data.SetIndexBufferParams(totalIndexCount, IndexFormat.UInt32);
            NativeArray<int> indices = data.GetIndexData<int>();

            data.subMeshCount = submeshCount;

            int indexOffset = 0;

            for (int s = 0; s < submeshCount; s++)
            {
                int2 idxRange = FragmentIndexRange[slotStart + s];
                int subCount = FragmentIndexCount[slotStart + s];

                // 断面ループが閉じずキャップが生成されなかった場合など、末尾のサブメッシュが0件になることがある。
                // そのときの indexOffset は配列長と等しく、Copy が範囲外として弾くためコピーしない
                if (subCount > 0)
                {
                    NativeArray<int>.Copy(FragmentIndicesFlat, idxRange.x, indices, indexOffset, subCount);
                }

                // SetSubMeshのデフォルト(Bounds再計算あり)で呼ぶ。DontRecalculateBoundsを付けるとBoundsが未計算になり破片がカリングで消える。
                data.SetSubMesh(s, new SubMeshDescriptor(indexOffset, subCount));

                indexOffset += subCount;
            }
        }
    }
}
