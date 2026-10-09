using System.Collections.Generic;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace UsefulToolkit.Editor.Ai
{
    /// <summary>
    /// edit-vfx-graph ツールの結果。
    /// </summary>
    public class EditVfxGraphResponse : UnityCliLoopToolResponse
    {
        /// <summary>編集したアセットのパス。</summary>
        public string AssetPath { get; set; } = "";

        /// <summary>適用した操作の数。失敗時は 0（すべて取り消している）。</summary>
        public int AppliedOperations { get; set; }

        /// <summary>保存したか。</summary>
        public bool Saved { get; set; }

        /// <summary>操作の as で付けた別名と、追加されたノードの ID の対応。</summary>
        public Dictionary<string, string> Aliases { get; set; } = new();

        /// <summary>適用はできたが、確かめてほしいこと（コンパイルの失敗など）。</summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>適用後のグラフの Revision。続けて編集するときはこれを渡す。</summary>
        public string Revision { get; set; } = "";

        /// <summary>適用後のグラフの Markdown。保存していない変更も含む。</summary>
        public string Markdown { get; set; } = "";

        /// <summary>失敗の理由。成功時は空。</summary>
        public string ErrorMessage { get; set; } = "";
    }
}
