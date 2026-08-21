using Unity.Mathematics;
using UnityEngine;

namespace UsefulToolkit.MeshCut
{
    /// <summary> Job内から扱える切断平面。Normalの向いている側が「表(Front)」になる。 </summary>
    public struct NativePlane
    {
        public float3 Position;
        public float3 Normal;
        public float Distance;

        public NativePlane(float3 pos, float3 normal)
        {
            Position = pos;
            Normal = normal;
            Distance = -math.dot(Normal, Position);
        }

        public NativePlane(Transform transform)
        {
            Position = transform.position;
            Normal = transform.up;
            Distance = -math.dot(Normal, Position);
        }
    }
}
