using Unity.Mathematics;
using UnityEngine;

namespace UsefulToolkit.MeshCut
{
    /// <summary> Job内から扱えるTransformのスナップショット。 </summary>
    public struct NativeTransform
    {
        public float3 Position;
        public quaternion Rotation;
        public float3 Scale;

        public NativeTransform(float3 position, quaternion rotation, float3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public NativeTransform(Transform transform)
        {
            Position = transform.position;
            Rotation = transform.rotation;
            Scale = transform.localScale;
        }
    }
}
