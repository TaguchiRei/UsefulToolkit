using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// 破片単位で、サンプリング点を k-means でクラスタリングし、各クラスタを覆う球(中心と半径)を求める。
    /// 初期中心は、点群のバウンディングボックス内のランダムな (クラスタ数 - 6) 点と、各軸方向の面の中心6点。
    /// 結果は破片 i について Spheres[OutputStart[i] .. + ClusterCount] に (中心xyz, 半径) で書き込む。
    /// 点が1つも所属しなかったクラスタは中心が初期位置のまま残るため、半径を負(Disabled)にして無効を表す。
    /// ClusterCount は 6 以上であること(軸方向の固定6点を必ず書き込むため。CuttableObject 側で 7 以上に制限している)。
    /// </summary>
    [BurstCompile]
    public struct ColliderClusterJob : IJobParallelFor
    {
        /// <summary> 無効な球を表す半径 </summary>
        public const float Disabled = -1f;

        private const int MaxIteration = 20;
        private const float Epsilon = 1e-6f;

        [ReadOnly] public NativeArray<float3> Points;

        /// <summary> 破片ごとの Points 上の (先頭, 点数) </summary>
        [ReadOnly] public NativeArray<int2> PointRange;

        [ReadOnly] public NativeArray<ColliderClusterSettings> Settings;

        /// <summary> 破片ごとの Spheres 上の書き込み開始位置 </summary>
        [ReadOnly] public NativeArray<int> OutputStart;

        /// <summary> 初期中心の乱数の種。破片ごとに破片番号を足した値から乱数列を作る </summary>
        public uint Seed;

        [NativeDisableParallelForRestriction] [WriteOnly]
        public NativeArray<float4> Spheres;

        public void Execute(int fragIdx)
        {
            ColliderClusterSettings settings = Settings[fragIdx];
            int clusterCount = settings.ClusterCount;
            int outStart = OutputStart[fragIdx];

            int2 range = PointRange[fragIdx];
            int pointStart = range.x;
            int sampleCount = range.y;

            if (sampleCount == 0)
            {
                for (int i = 0; i < clusterCount; i++)
                {
                    Spheres[outStart + i] = new float4(0f, 0f, 0f, Disabled);
                }

                return;
            }

            var centers = new NativeArray<float3>(clusterCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            InitializeCenters(pointStart, sampleCount, clusterCount, fragIdx, centers);

            var sum = new NativeArray<float3>(clusterCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var count = new NativeArray<int>(clusterCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            for (int iter = 0; iter < MaxIteration; iter++)
            {
                for (int c = 0; c < clusterCount; c++)
                {
                    sum[c] = float3.zero;
                    count[c] = 0;
                }

                for (int p = 0; p < sampleCount; p++)
                {
                    float3 point = Points[pointStart + p];
                    int nearest = FindNearest(centers, point);

                    sum[nearest] += point;
                    count[nearest]++;
                }

                bool moved = false;

                for (int c = 0; c < clusterCount; c++)
                {
                    if (count[c] == 0) continue;

                    float3 newCenter = sum[c] / count[c];

                    if (math.lengthsq(newCenter - centers[c]) > Epsilon)
                    {
                        centers[c] = newCenter;
                        moved = true;
                    }
                }

                if (!moved) break;
            }

            // 確定した中心に対して所属を決め直し、各クラスタの点を覆う半径を求める
            var maxDistSq = new NativeArray<float>(clusterCount, Allocator.Temp, NativeArrayOptions.ClearMemory);

            for (int c = 0; c < clusterCount; c++)
            {
                count[c] = 0;
            }

            for (int p = 0; p < sampleCount; p++)
            {
                float3 point = Points[pointStart + p];
                int nearest = FindNearest(centers, point);

                count[nearest]++;
                maxDistSq[nearest] = math.max(maxDistSq[nearest], math.lengthsq(centers[nearest] - point));
            }

            for (int c = 0; c < clusterCount; c++)
            {
                if (count[c] == 0)
                {
                    Spheres[outStart + c] = new float4(centers[c], Disabled);
                    continue;
                }

                float radius = math.sqrt(maxDistSq[c]) * settings.BaseShrink;

                if (count[c] < settings.DensityThreshold)
                {
                    float t = 1f - count[c] / (float)settings.DensityThreshold;
                    radius *= math.lerp(settings.BaseShrink, settings.DensityShrinkMin, t);
                }

                radius = math.min(radius, settings.MaxRadius);

                Spheres[outStart + c] = new float4(centers[c], radius);
            }

            centers.Dispose();
            sum.Dispose();
            count.Dispose();
            maxDistSq.Dispose();
        }

        private void InitializeCenters(int pointStart, int sampleCount, int clusterCount, int fragIdx,
            NativeArray<float3> centers)
        {
            float3 min = new float3(float.MaxValue);
            float3 max = new float3(float.MinValue);

            for (int p = 0; p < sampleCount; p++)
            {
                float3 point = Points[pointStart + p];
                min = math.min(min, point);
                max = math.max(max, point);
            }

            var random = Random.CreateFromIndex(Seed + (uint)fragIdx);
            int randomCount = math.max(0, clusterCount - 6);

            for (int c = 0; c < randomCount; c++)
            {
                centers[c] = random.NextFloat3(min, max);
            }

            float3 mid = (min + max) * 0.5f;

            centers[randomCount + 0] = new float3(mid.x, mid.y, max.z);
            centers[randomCount + 1] = new float3(mid.x, mid.y, min.z);
            centers[randomCount + 2] = new float3(mid.x, max.y, mid.z);
            centers[randomCount + 3] = new float3(mid.x, min.y, mid.z);
            centers[randomCount + 4] = new float3(max.x, mid.y, mid.z);
            centers[randomCount + 5] = new float3(min.x, mid.y, mid.z);
        }

        private static int FindNearest(NativeArray<float3> centers, float3 point)
        {
            float minDist = float.MaxValue;
            int nearest = 0;

            for (int c = 0; c < centers.Length; c++)
            {
                float dist = math.lengthsq(centers[c] - point);

                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = c;
                }
            }

            return nearest;
        }
    }
}
