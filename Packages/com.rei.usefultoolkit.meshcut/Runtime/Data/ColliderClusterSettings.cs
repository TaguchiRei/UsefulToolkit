namespace UsefulToolkit.MeshCut
{
    /// <summary> 破片の球コライダーを k-means で求めるときの、破片1つぶんの設定値。 </summary>
    public struct ColliderClusterSettings
    {
        /// <summary> 球コライダーの数(= クラスタ数)。軸方向の固定6点を必ず含むため 7 以上 </summary>
        public int ClusterCount;

        /// <summary> 基本縮小率 </summary>
        public float BaseShrink;

        /// <summary> 所属点が少ないクラスタに掛ける縮小率の下限 </summary>
        public float DensityShrinkMin;

        /// <summary> 所属点がこの数を下回るクラスタは、少ないほど DensityShrinkMin に近づけて縮小する </summary>
        public int DensityThreshold;

        /// <summary> 半径の上限 </summary>
        public float MaxRadius;
    }
}
