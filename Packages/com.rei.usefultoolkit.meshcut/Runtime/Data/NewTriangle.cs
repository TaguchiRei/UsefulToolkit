namespace UsefulToolkit.MeshCut
{
    /// <summary> TriangleCutJobが生成する、切断後の三角形1枚を表すデータ。 </summary>
    public struct NewTriangle
    {
        public int Vertex1;
        public int Vertex2;
        public int Vertex3;

        public int Submesh;

        /// <summary> 面のどちら側か。0なら裏、1なら表 </summary>
        public int Side;
    }
}
