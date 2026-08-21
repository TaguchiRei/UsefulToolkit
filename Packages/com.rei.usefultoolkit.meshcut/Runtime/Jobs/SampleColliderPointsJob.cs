using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// 「200件以下は全点、それ以上は指定数まで間引き」でコライダー用サンプリング点を作る。
    /// 出力先の範囲(SampleRange)はメインスレッドで各フラグメントの実頂点数から事前に計算しておく。
    /// </summary>
    [BurstCompile]
    public struct SampleColliderPointsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> FragmentVerticesFlat;
        [ReadOnly] public NativeArray<int2> FragmentVertexRange;
        [ReadOnly] public NativeArray<int> FragmentVertexCount;
        [ReadOnly] public NativeArray<int2> SampleRange;

        [NativeDisableParallelForRestriction] [WriteOnly]
        public NativeArray<float3> SamplePoints;

        public void Execute(int fragIdx)
        {
            int vertBase = FragmentVertexRange[fragIdx].x;
            int totalCount = FragmentVertexCount[fragIdx];

            int2 range = SampleRange[fragIdx];
            int outStart = range.x;
            int outCount = range.y;

            if (totalCount <= 200)
            {
                for (int j = 0; j < outCount; j++)
                {
                    SamplePoints[outStart + j] = FragmentVerticesFlat[vertBase + j];
                }
            }
            else
            {
                float step = (float)totalCount / outCount;

                for (int j = 0; j < outCount; j++)
                {
                    SamplePoints[outStart + j] = FragmentVerticesFlat[vertBase + (int)(j * step)];
                }
            }
        }
    }
}
