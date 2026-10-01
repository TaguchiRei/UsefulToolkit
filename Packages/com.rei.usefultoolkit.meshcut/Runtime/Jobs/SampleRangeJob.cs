using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// フラグメント毎の実頂点数から、コライダー用サンプリング点の出力範囲を決める。
    /// 頂点数が FullSampleThreshold 以下なら全頂点、それを超えれば SamplingCount 点を取る。
    /// 出力先はフラグメント毎に CapacityPerFragment ずつ予約した領域で、その先頭からの (offset, count) を書き出す。
    /// </summary>
    [BurstCompile]
    public struct SampleRangeJob : IJob
    {
        /// <summary> この頂点数以下のフラグメントは全頂点をサンプリング点にする </summary>
        public const int FullSampleThreshold = 200;

        [ReadOnly] public NativeArray<int> FragmentVertexCount;
        public int SamplingCount;
        public int CapacityPerFragment;

        [WriteOnly] public NativeArray<int2> SampleRange;

        public void Execute()
        {
            for (int i = 0; i < FragmentVertexCount.Length; i++)
            {
                int vertCount = FragmentVertexCount[i];
                int sampleCount = vertCount <= FullSampleThreshold ? vertCount : SamplingCount;
                SampleRange[i] = new int2(i * CapacityPerFragment, sampleCount);
            }
        }
    }
}
