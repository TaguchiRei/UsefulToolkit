using System;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// 頂点座標を整数量子化した際のNativeParallelHashMapキー。
    /// System.HashCode.CombineはBurst互換性が保証されないため、手書きの空間ハッシュを使用する。
    /// </summary>
    public struct QuantKey : IEquatable<QuantKey>
    {
        public long X;
        public long Y;
        public long Z;

        public QuantKey(long x, long y, long z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(QuantKey other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is QuantKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                long h = X * 73856093L ^ Y * 19349663L ^ Z * 83492791L;
                return (int)(h ^ (h >> 32));
            }
        }
    }
}
