using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// ワールド空間の刃(Blade)を、各オブジェクトのローカル空間に変換する。
    /// V2ではAwaitable.BackgroundThreadAsync上のplainループだった処理をJob化したもの。
    /// </summary>
    [BurstCompile]
    public struct BladeToLocalJob : IJobParallelFor
    {
        [ReadOnly] public NativePlane WorldBlade;
        [ReadOnly] public NativeArray<NativeTransform> Transforms;

        [WriteOnly] public NativeArray<NativePlane> Blades;

        public void Execute(int index)
        {
            NativeTransform t = Transforms[index];

            quaternion invRot = math.inverse(t.Rotation);
            float3 reciprocal = math.rcp(t.Scale);

            float3 position = WorldBlade.Position - t.Position;
            position = math.mul(invRot, position);
            position *= reciprocal;

            float3 normal = math.mul(invRot, WorldBlade.Normal);
            normal *= reciprocal;

            Blades[index] = new NativePlane(position, normal);
        }
    }
}
