namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// MultiCutBlade.ExecuteCut で1つの対象を切断した結果。
    /// 元の対象と、切断で生まれた表と裏の破片の組。
    /// </summary>
    public readonly struct MultiCutResult
    {
        /// <summary>
        /// 切断した元の対象。切断後は非アクティブで、もう切れない。
        /// ただし元の対象がプールの破片で、同じ切断の中で別の組の Front / Back として使い回された場合は、その破片として有効になっている
        /// </summary>
        public CuttableObject Original { get; }

        /// <summary> 刃の法線(transform.up)の側の破片 </summary>
        public CuttableObject Front { get; }

        /// <summary> 刃の法線と反対の側の破片 </summary>
        public CuttableObject Back { get; }

        internal MultiCutResult(CuttableObject original, CuttableObject front, CuttableObject back)
        {
            Original = original;
            Front = front;
            Back = back;
        }
    }
}
