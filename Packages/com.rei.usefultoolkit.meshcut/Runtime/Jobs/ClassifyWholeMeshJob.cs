using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// オブジェクト単位(Execute(objIndex)は1オブジェクトを丸ごと逐次処理する)で、
    /// 各三角形が刃に対して完全に表/裏/切断対象のどれかを判定し、バッファの容量決めに必要な数だけを数える。
    /// - 完全に表・裏の三角形: その側のフラグメントが使う頂点数(元の頂点インデックスで重複除去)と、サブメッシュ別のインデックス数
    /// - 切断対象の三角形: オブジェクト全体とサブメッシュ別の件数
    /// 実データの書き込みは、容量が決まった後に WriteWholeTrianglesJob が同じ判定でやり直す。
    /// </summary>
    [BurstCompile]
    public struct ClassifyWholeMeshJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int2> ObjectVertexRange;
        [ReadOnly] public NativeArray<int2> ObjectTriangleRange;
        [ReadOnly] public NativeArray<int> ObjectStoreTriangleStart;

        /// <summary> NativeMeshDataStore.Triangles(メッシュローカルな頂点番号) </summary>
        [ReadOnly] public NativeArray<int3> StoreTriangles;

        /// <summary> NativeMeshDataStore.TriangleSubmesh </summary>
        [ReadOnly] public NativeArray<int> StoreTriangleSubmesh;

        [ReadOnly] public NativeArray<int> BaseVertexSide;

        public int MaxSubmeshSlots;

        [NativeDisableParallelForRestriction] [WriteOnly]
        public NativeArray<int> FragmentWholeVertexCount;

        [NativeDisableParallelForRestriction] [WriteOnly]
        public NativeArray<int> FragmentWholeIndexCount;

        [NativeDisableParallelForRestriction] [WriteOnly]
        public NativeArray<int> CutFaceCountPerObject;

        [NativeDisableParallelForRestriction] [WriteOnly]
        public NativeArray<int> CutFaceCountPerObjectSubmesh;

        public void Execute(int objIndex)
        {
            int2 vRange = ObjectVertexRange[objIndex];
            int2 tRange = ObjectTriangleRange[objIndex];

            int frontFrag = MultiCutContext.FragmentIndex(objIndex, 0);
            int backFrag = MultiCutContext.FragmentIndex(objIndex, 1);

            // 頂点はどちらか一方の側にしか属さないため、使用済みフラグは1つで表裏を兼ねられる
            var used = new NativeArray<bool>(vRange.y, Allocator.Temp, NativeArrayOptions.ClearMemory);

            var frontIdx = new NativeArray<int>(MaxSubmeshSlots, Allocator.Temp, NativeArrayOptions.ClearMemory);
            var backIdx = new NativeArray<int>(MaxSubmeshSlots, Allocator.Temp, NativeArrayOptions.ClearMemory);
            var cutPerSubmesh = new NativeArray<int>(MaxSubmeshSlots, Allocator.Temp, NativeArrayOptions.ClearMemory);

            int frontVerts = 0;
            int backVerts = 0;
            int cutCount = 0;

            int storeTriStart = ObjectStoreTriangleStart[objIndex];

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
                    backVerts += CountNewVertices(tri, vRange.x, used);
                    backIdx[submesh] += 3;
                }
                else if (result == 7)
                {
                    frontVerts += CountNewVertices(tri, vRange.x, used);
                    frontIdx[submesh] += 3;
                }
                else
                {
                    cutCount++;
                    cutPerSubmesh[submesh]++;
                }
            }

            FragmentWholeVertexCount[frontFrag] = frontVerts;
            FragmentWholeVertexCount[backFrag] = backVerts;
            CutFaceCountPerObject[objIndex] = cutCount;

            for (int s = 0; s < MaxSubmeshSlots; s++)
            {
                FragmentWholeIndexCount[frontFrag * MaxSubmeshSlots + s] = frontIdx[s];
                FragmentWholeIndexCount[backFrag * MaxSubmeshSlots + s] = backIdx[s];
                CutFaceCountPerObjectSubmesh[objIndex * MaxSubmeshSlots + s] = cutPerSubmesh[s];
            }

            used.Dispose();
            frontIdx.Dispose();
            backIdx.Dispose();
            cutPerSubmesh.Dispose();
        }

        /// <summary> 三角形の3頂点のうち、まだ数えていない頂点の数を返し、数えた印を付ける </summary>
        private static int CountNewVertices(int3 globalTri, int vStart, NativeArray<bool> used)
        {
            int added = 0;
            added += MarkUsed(globalTri.x - vStart, used);
            added += MarkUsed(globalTri.y - vStart, used);
            added += MarkUsed(globalTri.z - vStart, used);
            return added;
        }

        private static int MarkUsed(int localIndex, NativeArray<bool> used)
        {
            if (used[localIndex]) return 0;

            used[localIndex] = true;
            return 1;
        }
    }
}
