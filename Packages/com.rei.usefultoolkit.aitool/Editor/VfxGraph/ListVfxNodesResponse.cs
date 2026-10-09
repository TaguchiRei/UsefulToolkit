using System.Collections.Generic;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace UsefulToolkit.Editor.Ai
{
    /// <summary>
    /// list-vfx-nodes ツールの結果。
    /// </summary>
    public class ListVfxNodesResponse : UnityCliLoopToolResponse
    {
        /// <summary>条件に合った候補の総数。MaxCount を超えた分は Nodes に入らない。</summary>
        public int TotalCount { get; set; }

        /// <summary>候補。edit-vfx-graph の type には Name を、同名が複数あるときは category に Category を渡す。</summary>
        public List<Node> Nodes { get; set; } = new();

        /// <summary>失敗の理由。成功時は空。</summary>
        public string ErrorMessage { get; set; } = "";

        /// <summary>候補 1 つ。</summary>
        public class Node
        {
            public string Name { get; set; } = "";
            public string Category { get; set; } = "";
            public string TypeName { get; set; } = "";
        }
    }
}
