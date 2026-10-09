using System.ComponentModel;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace UsefulToolkit.Editor.Ai
{
    /// <summary>
    /// list-vfx-nodes ツールの引数。
    /// </summary>
    public class ListVfxNodesSchema : UnityCliLoopToolSchema
    {
        /// <summary>探すノードの種類。</summary>
        [Description("Kind of node to list: Block, Operator, Context or Property")]
        public VfxNodeKind Kind { get; set; } = VfxNodeKind.Block;

        /// <summary>名前・カテゴリ・型名の部分一致。空白区切りの語はすべて含むものに絞る。</summary>
        [Description("Words that must all appear in the name, category or type name (case-insensitive). Empty lists everything")]
        public string Query { get; set; } = "";

        /// <summary>返す件数の上限。</summary>
        [Description("Maximum number of entries to return")]
        public int MaxCount { get; set; } = 50;
    }
}
