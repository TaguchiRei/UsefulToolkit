using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace UsefulToolkit.Editor.Ai
{
    /// <summary>
    /// VFX Graph アセットを AI が読める Markdown にして返す uloop のカスタムツール。
    /// 外部からは <c>uloop export-vfx-graph --asset-path &lt;path&gt;</c> で呼ぶ。
    /// </summary>
    [UnityCliLoopTool]
    public class ExportVfxGraphTool : UnityCliLoopTool<ExportVfxGraphSchema, ExportVfxGraphResponse>
    {
        public override string ToolName => "export-vfx-graph";

        protected override Task<ExportVfxGraphResponse> ExecuteAsync(ExportVfxGraphSchema parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string assetPath = parameters.AssetPath?.Trim() ?? "";
            var response = new ExportVfxGraphResponse { AssetPath = assetPath };

            if (VfxGraphTextExporter.TryExport(assetPath, out string markdown, out string error))
            {
                response.Markdown = markdown;
            }
            else
            {
                response.Success = false;
                response.ErrorMessage = error;
            }

            return Task.FromResult(response);
        }
    }
}
