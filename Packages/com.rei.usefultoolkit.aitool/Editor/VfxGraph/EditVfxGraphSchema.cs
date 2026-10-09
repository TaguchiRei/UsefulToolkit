using System.ComponentModel;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace UsefulToolkit.Editor.Ai
{
    /// <summary>
    /// edit-vfx-graph ツールの引数。
    /// </summary>
    public class EditVfxGraphSchema : UnityCliLoopToolSchema
    {
        /// <summary>編集する VFX Graph アセットのパス。</summary>
        [Description("Project-relative path of the VFX Graph asset to edit, starting with Assets/ or Packages/")]
        public string AssetPath { get; set; } = "";

        /// <summary>export-vfx-graph が返した Revision。</summary>
        [Description("Revision returned by export-vfx-graph. The edit is rejected if the graph has changed since then")]
        public string Revision { get; set; } = "";

        /// <summary>操作リストの JSON ファイルのパス。</summary>
        [Description("Path of a JSON file holding the operations (an array, or an object with an \"operations\" array). Absolute or project-relative")]
        public string OperationsFile { get; set; } = "";

        /// <summary>true のとき、適用後にグラフ画面の保存処理で保存する。</summary>
        [Description("Save the asset after applying. Without it the changes stay unsaved in the open graph window for review")]
        public bool Save { get; set; }
    }
}
