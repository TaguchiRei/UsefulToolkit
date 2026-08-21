using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// オブジェクト単位(Execute(objIndex)は1オブジェクトを丸ごと逐次処理する)で、
    /// 各三角形が刃に対して完全に表/裏/切断対象のどれかを判定する。
    /// 完全に表・裏の三角形はその場でFront/Backフラグメントバッファへdedupして追加する
    /// (元の頂点インデックスをキーにしたdedup方式)。
    /// 切断対象の三角形は、このパスでは件数のみをカウントする(実データはBuildCutFaceListJobで書き出す)。
    /// </summary>
    [BurstCompile]
    public struct ClassifyWholeMeshJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int2> ObjectVertexRange;
        [ReadOnly] public NativeArray<int2> ObjectTriangleRange;
        [ReadOnly] public NativeArray<int3> AllTriangles;
        [ReadOnly] public NativeArray<int> AllTriangleSubmesh;
        [ReadOnly] public NativeArray<int> BaseVertexSide;
        [ReadOnly] public NativeArray<float3> BaseVertices;
        [ReadOnly] public NativeArray<float3> BaseNormals;
        [ReadOnly] public NativeArray<float2> BaseUvs;

        [ReadOnly] public NativeArray<int2> FragmentVertexRange;
        [ReadOnly] public NativeArray<int2> FragmentIndexRange;
        public int MaxSubmeshSlots;

        [NativeDisableParallelForRestriction] public NativeArray<float3> FragmentVerticesFlat;
        [NativeDisableParallelForRestriction] public NativeArray<float3> FragmentNormalsFlat;
        [NativeDisableParallelForRestriction] public NativeArray<float2> FragmentUvsFlat;
        [NativeDisableParallelForRestriction] public NativeArray<int> FragmentIndicesFlat;

        [NativeDisableParallelForRestriction] public NativeArray<int> FragmentVertexCount;
        [NativeDisableParallelForRestriction] public NativeArray<int> FragmentIndexCount;

        [NativeDisableParallelForRestriction] [WriteOnly]
        public NativeArray<int> CutFaceCountPerObject;

        public void Execute(int objIndex)
        {
            int2 vRange = ObjectVertexRange[objIndex];
            int2 tRange = ObjectTriangleRange[objIndex];

            int frontFrag = MultiCutContext.FragmentIndex(objIndex, 0);
            int backFrag = MultiCutContext.FragmentIndex(objIndex, 1);

            var dedupFront = new NativeArray<int>(vRange.y, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var dedupBack = new NativeArray<int>(vRange.y, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            for (int i = 0; i < vRange.y; i++)
            {
                dedupFront[i] = -1;
                dedupBack[i] = -1;
            }

            var frontIdxCursor = new NativeArray<int>(MaxSubmeshSlots, Allocator.Temp, NativeArrayOptions.ClearMemory);
            var backIdxCursor = new NativeArray<int>(MaxSubmeshSlots, Allocator.Temp, NativeArrayOptions.ClearMemory);

            int frontVertCursor = 0;
            int backVertCursor = 0;
            int cutCount = 0;

            for (int i = 0; i < tRange.y; i++)
            {
                int triIdx = tRange.x + i;
                int3 tri = AllTriangles[triIdx];
                int submesh = AllTriangleSubmesh[triIdx];

                int side1 = BaseVertexSide[tri.x];
                int side2 = BaseVertexSide[tri.y];
                int side3 = BaseVertexSide[tri.z];
                int result = (side1 << 2) | (side2 << 1) | side3;

                if (result == 0)
                {
                    backVertCursor = AddWholeTriangle(backFrag, submesh, tri, vRange.x, dedupBack, backVertCursor,
                        backIdxCursor);
                }
                else if (result == 7)
                {
                    frontVertCursor = AddWholeTriangle(frontFrag, submesh, tri, vRange.x, dedupFront, frontVertCursor,
                        frontIdxCursor);
                }
                else
                {
                    cutCount++;
                }
            }

            CutFaceCountPerObject[objIndex] = cutCount;
            FragmentVertexCount[frontFrag] = frontVertCursor;
            FragmentVertexCount[backFrag] = backVertCursor;

            for (int s = 0; s < MaxSubmeshSlots; s++)
            {
                FragmentIndexCount[frontFrag * MaxSubmeshSlots + s] = frontIdxCursor[s];
                FragmentIndexCount[backFrag * MaxSubmeshSlots + s] = backIdxCursor[s];
            }

            dedupFront.Dispose();
            dedupBack.Dispose();
            frontIdxCursor.Dispose();
            backIdxCursor.Dispose();
        }

        /// <returns>更新後の頂点カーソル(呼び出し側で保持している変数へ書き戻すこと)</returns>
        private int AddWholeTriangle(
            int fragIdx, int submesh, int3 globalTri, int vStart,
            NativeArray<int> dedup, int vertCursor, NativeArray<int> idxCursor)
        {
            int i1 = GetOrAddVertex(fragIdx, globalTri.x - vStart, globalTri.x, dedup, ref vertCursor);
            int i2 = GetOrAddVertex(fragIdx, globalTri.y - vStart, globalTri.y, dedup, ref vertCursor);
            int i3 = GetOrAddVertex(fragIdx, globalTri.z - vStart, globalTri.z, dedup, ref vertCursor);

            int2 idxRange = FragmentIndexRange[fragIdx * MaxSubmeshSlots + submesh];
            int cursor = idxCursor[submesh];

            FragmentIndicesFlat[idxRange.x + cursor + 0] = i1;
            FragmentIndicesFlat[idxRange.x + cursor + 1] = i2;
            FragmentIndicesFlat[idxRange.x + cursor + 2] = i3;

            idxCursor[submesh] = cursor + 3;

            return vertCursor;
        }

        private int GetOrAddVertex(int fragIdx, int localIndex, int globalIndex, NativeArray<int> dedup,
            ref int vertCursor)
        {
            int existing = dedup[localIndex];
            if (existing != -1) return existing;

            int2 vRange = FragmentVertexRange[fragIdx];
            int newIndex = vertCursor;

            FragmentVerticesFlat[vRange.x + newIndex] = BaseVertices[globalIndex];
            FragmentNormalsFlat[vRange.x + newIndex] = BaseNormals[globalIndex];
            FragmentUvsFlat[vRange.x + newIndex] = BaseUvs[globalIndex];

            dedup[localIndex] = newIndex;
            vertCursor++;

            return newIndex;
        }
    }
}
