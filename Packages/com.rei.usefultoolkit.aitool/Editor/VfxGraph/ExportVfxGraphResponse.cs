using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace UsefulToolkit.Editor.Ai
{
    /// <summary>
    /// export-vfx-graph ツールの結果。
    /// </summary>
    public class ExportVfxGraphResponse : UnityCliLoopToolResponse
    {
        /// <summary>書き出したアセットのパス。</summary>
        public string AssetPath { get; set; } = "";

        /// <summary>グラフを Markdown にしたもの。失敗時は空。</summary>
        public string Markdown { get; set; } = "";

        /// <summary>グラフの内容のハッシュ。edit-vfx-graph に渡して、ID が古くなっていないかを照合する。</summary>
        public string Revision { get; set; } = "";

        /// <summary>失敗の理由。成功時は空。</summary>
        public string ErrorMessage { get; set; } = "";
    }
}
