using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// ClassifyWholeMeshJob が数えた実数から、フラグメント・サブメッシュスロットごとの容量と書き込み位置を決め、
    /// フラットなリストのサイズを確保する。
    /// 切断三角形1つあたりの増分は、DistributeAndCapJob が頂点を重複除去しなかった場合の上限を使う
    /// (実際には重複除去で頂点はこれより少なくなる):
    /// - 新規三角形3つ(片側に最大2つ)を追加するため、片側あたり頂点・インデックスとも最大6
    /// - 断面のファン三角形はループの1辺につき1つで、辺の数は切断三角形数以下のため、片側あたりインデックス最大3。
    ///   頂点はループの頂点数 + 中心1つで、ループの頂点数は辺の数以下・ループ1本は3辺以上のため、これも最大3に収まる
    /// </summary>
    [BurstCompile]
    public struct FragmentLayoutJob : IJob
    {
        private const int CutSidePerFace = 6;
        private const int CapPerFace = 3;

        [ReadOnly] public NativeArray<int> FragmentWholeVertexCount;
        [ReadOnly] public NativeArray<int> FragmentWholeIndexCount;
        [ReadOnly] public NativeArray<int> CutFaceCountPerObject;
        [ReadOnly] public NativeArray<int> CutFaceCountPerObjectSubmesh;
        [ReadOnly] public NativeArray<int> ObjectCapSlot;

        public int MaxSubmeshSlots;

        [WriteOnly] public NativeArray<int2> FragmentVertexRange;
        [WriteOnly] public NativeArray<int2> FragmentIndexRange;

        public NativeList<float3> FragmentVerticesFlat;
        public NativeList<float3> FragmentNormalsFlat;
        public NativeList<float2> FragmentUvsFlat;
        public NativeList<int> FragmentIndicesFlat;

        public void Execute()
        {
            int objectCount = CutFaceCountPerObject.Length;
            int vertTotal = 0;
            int idxTotal = 0;

            for (int objIndex = 0; objIndex < objectCount; objIndex++)
            {
                int cutCount = CutFaceCountPerObject[objIndex];
                int capSlot = ObjectCapSlot[objIndex];

                for (int side = 0; side < 2; side++)
                {
                    int fragIdx = MultiCutContext.FragmentIndex(objIndex, side);

                    int vertCap = FragmentWholeVertexCount[fragIdx] + (CutSidePerFace + CapPerFace) * cutCount;
                    FragmentVertexRange[fragIdx] = new int2(vertTotal, vertCap);
                    vertTotal += vertCap;

                    for (int s = 0; s < MaxSubmeshSlots; s++)
                    {
                        int slot = fragIdx * MaxSubmeshSlots + s;

                        int capacity = FragmentWholeIndexCount[slot]
                                       + CutSidePerFace * CutFaceCountPerObjectSubmesh[objIndex * MaxSubmeshSlots + s];

                        // 断面スロットはキャップも受け取る。既に断面を持つメッシュを切り直す場合は、
                        // そのスロットの元の三角形・新規三角形・キャップが同じスロットに積まれる
                        if (s == capSlot)
                        {
                            capacity += CapPerFace * cutCount;
                        }

                        FragmentIndexRange[slot] = new int2(idxTotal, capacity);
                        idxTotal += capacity;
                    }
                }
            }

            FragmentVerticesFlat.ResizeUninitialized(vertTotal);
            FragmentNormalsFlat.ResizeUninitialized(vertTotal);
            FragmentUvsFlat.ResizeUninitialized(vertTotal);
            FragmentIndicesFlat.ResizeUninitialized(idxTotal);
        }
    }
}
