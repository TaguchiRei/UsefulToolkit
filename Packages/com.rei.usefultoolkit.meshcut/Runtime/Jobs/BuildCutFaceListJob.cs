using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// ClassifyWholeMeshJobで数え上げたオブジェクト毎の切断三角形数のプレフィックス和
    /// (CutFaceStartPerObject)を使い、切断三角形をオブジェクトでグルーピングされた
    /// 連続領域へ書き出す。各オブジェクトは自分専有の書き込み範囲にのみ書き込むため
    /// アトミック操作は不要。
    /// </summary>
    [BurstCompile]
    public struct BuildCutFaceListJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int2> ObjectTriangleRange;
        [ReadOnly] public NativeArray<int3> AllTriangles;
        [ReadOnly] public NativeArray<int> AllTriangleSubmesh;
        [ReadOnly] public NativeArray<int> BaseVertexSide;
        [ReadOnly] public NativeArray<int> CutFaceStartPerObject;

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
            int2 tRange = ObjectTriangleRange[objIndex];
            int writeIndex = CutFaceStartPerObject[objIndex];

            for (int i = 0; i < tRange.y; i++)
            {
                int triIdx = tRange.x + i;
                int3 tri = AllTriangles[triIdx];

                int side1 = BaseVertexSide[tri.x];
                int side2 = BaseVertexSide[tri.y];
                int side3 = BaseVertexSide[tri.z];
                int result = (side1 << 2) | (side2 << 1) | side3;

                if (result == 0 || result == 7) continue;

                CutFaces[writeIndex] = tri;
                CutStatus[writeIndex] = result;
                CutFaceSubmeshId[writeIndex] = AllTriangleSubmesh[triIdx];
                CutFaceObjectIndex[writeIndex] = objIndex;
                writeIndex++;
            }
        }
    }
}
