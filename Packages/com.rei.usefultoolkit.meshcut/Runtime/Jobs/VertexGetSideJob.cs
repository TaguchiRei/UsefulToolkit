using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// 頂点の通し番号ごとに、その頂点が刃の表裏どちらにあるかを判定する。
    /// 頂点座標はストアから直接読む。所属オブジェクトは ObjectVertexRange の二分探索で求める。
    /// </summary>
    [BurstCompile]
    public struct VertexGetSideJob : IJobParallelFor
    {
        /// <summary> NativeMeshDataStore.Vertices </summary>
        [ReadOnly] public NativeArray<float3> StoreVertices;

        /// <summary> per object: (通し番号の先頭, 頂点数)。先頭の昇順に並んでいること </summary>
        [ReadOnly] public NativeArray<int2> ObjectVertexRange;

        [ReadOnly] public NativeArray<int> ObjectStoreVertexOffset;
        [ReadOnly] public NativeArray<NativePlane> Blades;

        [WriteOnly] public NativeArray<int> VertexSides;

        public void Execute(int index)
        {
            int objIndex = FindObject(index);
            NativePlane blade = Blades[objIndex];
            float3 vertex = StoreVertices[index + ObjectStoreVertexOffset[objIndex]];

            VertexSides[index] = math.dot(vertex - blade.Position, blade.Normal) > 0f ? 1 : 0;
        }

        /// <summary> 通し番号 index を含むオブジェクト(先頭が index 以下で最大のもの)を返す </summary>
        private int FindObject(int index)
        {
            int lo = 0;
            int hi = ObjectVertexRange.Length - 1;

            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;

                if (ObjectVertexRange[mid].x <= index)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return lo;
        }
    }
}
