using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// 実行された時刻(Stopwatch.GetTimestamp)を Stamps[Index] に書き込む計測用Job。
    /// Stopwatch を呼ぶためBurstコンパイルしない。
    /// 並行して走る複数の段階が同じ配列の別要素へ書き込むため、コンテナのセーフティチェックを外している。
    /// 読み出しは、対応するJobHandleの完了後にメインスレッドで行うこと。
    /// </summary>
    internal struct TimestampJob : IJob
    {
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<long> Stamps;

        public int Index;

        public void Execute()
        {
            Stamps[Index] = Stopwatch.GetTimestamp();
        }
    }
}
