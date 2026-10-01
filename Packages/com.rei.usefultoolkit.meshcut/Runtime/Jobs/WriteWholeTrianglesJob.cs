using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// オブジェクト単位(Execute(objIndex)は1オブジェクトを丸ごと逐次処理する)で、ClassifyWholeMeshJob と同じ判定をやり直し、
    /// - 完全に表・裏の三角形を、FragmentLayoutJob が決めた領域へ重複除去(元の頂点インデックスをキー)しながら書き込む
    /// - 切断対象の三角形を、CutFaceStartPerObject から始まるこのオブジェクト専有の領域へ書き出す
    /// 書き込み先はオブジェクトごとに重ならないため、アトミック操作は不要。
    /// フラットなリストと切断面リストは実行時にサイズが決まるため、AsDeferredJobArray() で受け取る。
    /// </summary>
    [BurstCompile]
    public struct WriteWholeTrianglesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int2> ObjectVertexRange;
        [ReadOnly] public NativeArray<int> ObjectStoreVertexOffset;
        [ReadOnly] public NativeArray<int2> ObjectTriangleRange;
        [ReadOnly] public NativeArray<int> ObjectStoreTriangleStart;
        [ReadOnly] public NativeArray<int> BaseVertexSide;

        // NativeMeshDataStore のデータ。三角形はメッシュローカルな頂点番号
        [ReadOnly] public NativeArray<int3> StoreTriangles;
        [ReadOnly] public NativeArray<int> StoreTriangleSubmesh;
        [ReadOnly] public NativeArray<float3> StoreVertices;
        [ReadOnly] public NativeArray<float3> StoreNormals;
        [ReadOnly] public NativeArray<float2> StoreUvs;

        [ReadOnly] public NativeArray<int2> FragmentVertexRange;
        [ReadOnly] public NativeArray<int2> FragmentIndexRange;
        [ReadOnly] public NativeArray<int> CutFaceStartPerObject;
        public int MaxSubmeshSlots;

        [NativeDisableParallelForRestriction] public NativeArray<float3> FragmentVerticesFlat;
        [NativeDisableParallelForRestriction] public NativeArray<float3> FragmentNormalsFlat;
        [NativeDisableParallelForRestriction] public NativeArray<float2> FragmentUvsFlat;
        [NativeDisableParallelForRestriction] public NativeArray<int> FragmentIndicesFlat;

        [NativeDisableParallelForRestriction] [WriteOnly]
        public NativeArray<int> FragmentVertexCount;

        [NativeDisableParallelForRestriction] [WriteOnly]
        public NativeArray<int> FragmentIndexCount;

        [NativeDisableParallelForRestriction] [WriteOnly]
        public NativeArray<int3> CutFaces;

        [NativeDisableParallelForRestriction] [WriteOnly]
        public NativeArray<int> CutStatus;

        [NativeDisableParallelForRestriction] [WriteOnly]
        public NativeArray<int> CutFaceSubmeshId;

        [NativeDisableParallelForRestriction] [WriteOnly]
        public NativeArray<int> CutFaceObjectIndex;

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
            int cutWriteIndex = CutFaceStartPerObject[objIndex];

            int storeTriStart = ObjectStoreTriangleStart[objIndex];
            int storeVertexOffset = ObjectStoreVertexOffset[objIndex];

            for (int i = 0; i < tRange.y; i++)
            {
                int triIdx = storeTriStart + i;

                // メッシュローカルな頂点番号を、このオブジェクトの通し番号へ変換する
                int3 tri = StoreTriangles[triIdx] + vRange.x;
                int submesh = StoreTriangleSubmesh[triIdx];

                int side1 = BaseVertexSide[tri.x];
                int side2 = BaseVertexSide[tri.y];
                int side3 = BaseVertexSide[tri.z];
                int result = (side1 << 2) | (side2 << 1) | side3;

                if (result == 0)
                {
                    backVertCursor = AddWholeTriangle(backFrag, submesh, tri, vRange.x, storeVertexOffset, dedupBack,
                        backVertCursor, backIdxCursor);
                }
                else if (result == 7)
                {
                    frontVertCursor = AddWholeTriangle(frontFrag, submesh, tri, vRange.x, storeVertexOffset, dedupFront,
                        frontVertCursor, frontIdxCursor);
                }
                else
                {
                    CutFaces[cutWriteIndex] = tri;
                    CutStatus[cutWriteIndex] = result;
                    CutFaceSubmeshId[cutWriteIndex] = submesh;
                    CutFaceObjectIndex[cutWriteIndex] = objIndex;
                    cutWriteIndex++;
                }
            }

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
            int fragIdx, int submesh, int3 globalTri, int vStart, int storeVertexOffset,
            NativeArray<int> dedup, int vertCursor, NativeArray<int> idxCursor)
        {
            int i1 = GetOrAddVertex(fragIdx, globalTri.x - vStart, globalTri.x + storeVertexOffset, dedup, ref vertCursor);
            int i2 = GetOrAddVertex(fragIdx, globalTri.y - vStart, globalTri.y + storeVertexOffset, dedup, ref vertCursor);
            int i3 = GetOrAddVertex(fragIdx, globalTri.z - vStart, globalTri.z + storeVertexOffset, dedup, ref vertCursor);

            int2 idxRange = FragmentIndexRange[fragIdx * MaxSubmeshSlots + submesh];
            int cursor = idxCursor[submesh];

            FragmentIndicesFlat[idxRange.x + cursor + 0] = i1;
            FragmentIndicesFlat[idxRange.x + cursor + 1] = i2;
            FragmentIndicesFlat[idxRange.x + cursor + 2] = i3;

            idxCursor[submesh] = cursor + 3;

            return vertCursor;
        }

        /// <param name="localIndex">オブジェクト内の頂点番号(重複除去の表の添字)</param>
        /// <param name="storeIndex">ストア上の頂点番号</param>
        private int GetOrAddVertex(int fragIdx, int localIndex, int storeIndex, NativeArray<int> dedup,
            ref int vertCursor)
        {
            int existing = dedup[localIndex];
            if (existing != -1) return existing;

            int2 vRange = FragmentVertexRange[fragIdx];
            int newIndex = vertCursor;

            FragmentVerticesFlat[vRange.x + newIndex] = StoreVertices[storeIndex];
            FragmentNormalsFlat[vRange.x + newIndex] = StoreNormals[storeIndex];
            FragmentUvsFlat[vRange.x + newIndex] = StoreUvs[storeIndex];

            dedup[localIndex] = newIndex;
            vertCursor++;

            return newIndex;
        }
    }
}
