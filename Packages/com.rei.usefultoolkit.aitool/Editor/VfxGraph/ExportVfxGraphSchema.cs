using System.ComponentModel;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace UsefulToolkit.Editor.Ai
{
    /// <summary>
    /// export-vfx-graph ツールの引数。
    /// </summary>
    public class ExportVfxGraphSchema : UnityCliLoopToolSchema
    {
        /// <summary>書き出す VFX Graph アセットのパス。</summary>
        [Description("Project-relative path of the VFX Graph asset (.vfx / .vfxoperator / .vfxblock), starting with Assets/ or Packages/")]
        public string AssetPath { get; set; } = "";
    }
}
