using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UsefulToolkit.MeshCut
{
    /// <summary> V2版と同一ロジック。各頂点が刃の表裏どちらにあるかを判定する。 </summary>
    [BurstCompile]
    public struct VertexGetSideJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> Vertices;
        [ReadOnly] public NativeArray<int> BladeIndex;
        [ReadOnly] public NativeArray<NativePlane> Blades;

        [WriteOnly] public NativeArray<int> VertexSides;

        public void Execute(int index)
        {
            var blade = Blades[BladeIndex[index]];
            VertexSides[index] = math.dot(Vertices[index] - blade.Position, blade.Normal) > 0f ? 1 : 0;
        }
    }
}
