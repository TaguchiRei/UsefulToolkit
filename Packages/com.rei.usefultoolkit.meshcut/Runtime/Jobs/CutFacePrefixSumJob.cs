using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// ClassifyWholeMeshJob が数えたオブジェクト毎の切断三角形数からプレフィックス和(各オブジェクトの書き込み開始位置)を求め、
    /// 切断三角形数に応じて後続Jobの出力リストのサイズを決める。
    /// 切断三角形 i あたり、新規頂点は2つ、新規三角形は3つ生成される。
    /// </summary>
    [BurstCompile]
    public struct CutFacePrefixSumJob : IJob
    {
        [ReadOnly] public NativeArray<int> CutFaceCountPerObject;

        [WriteOnly] public NativeArray<int> CutFaceStartPerObject;

        public NativeList<int3> CutFaces;
        public NativeList<int> CutStatus;
        public NativeList<int> CutFaceSubmeshId;
        public NativeList<int> CutFaceObjectIndex;

        public NativeList<float3> NewVertices;
        public NativeList<float3> NewNormals;
        public NativeList<float2> NewUvs;
        public NativeList<NewTriangle> NewTriangles;

        public void Execute()
        {
            int total = 0;

            for (int i = 0; i < CutFaceCountPerObject.Length; i++)
            {
                CutFaceStartPerObject[i] = total;
                total += CutFaceCountPerObject[i];
            }

            CutFaces.ResizeUninitialized(total);
            CutStatus.ResizeUninitialized(total);
            CutFaceSubmeshId.ResizeUninitialized(total);
            CutFaceObjectIndex.ResizeUninitialized(total);

            NewVertices.ResizeUninitialized(total * 2);
            NewNormals.ResizeUninitialized(total * 2);
            NewUvs.ResizeUninitialized(total * 2);
            NewTriangles.ResizeUninitialized(total * 3);
        }
    }
}
