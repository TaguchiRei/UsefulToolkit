using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop.ToolContracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UsefulToolkit.Editor.Ai
{
    /// <summary>
    /// 操作リスト（JSON ファイル）を VFX Graph に適用する uloop のカスタムツール。
    /// 外部からは <c>uloop edit-vfx-graph --asset-path &lt;path&gt; --revision &lt;rev&gt; --operations-file &lt;json&gt;</c> で呼ぶ。
    /// </summary>
    [UnityCliLoopTool]
    public class EditVfxGraphTool : UnityCliLoopTool<EditVfxGraphSchema, EditVfxGraphResponse>
    {
        public override string ToolName => "edit-vfx-graph";

        protected override Task<EditVfxGraphResponse> ExecuteAsync(EditVfxGraphSchema parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string assetPath = parameters.AssetPath?.Trim() ?? "";
            var response = new EditVfxGraphResponse { AssetPath = assetPath };
            if (!TryReadOperations(parameters.OperationsFile, out JArray operations, out string error))
            {
                response.Success = false;
                response.ErrorMessage = error;
                return Task.FromResult(response);
            }

            VfxGraphEditor.Result result;
            try
            {
                result = VfxGraphEditor.Apply(assetPath, parameters.Revision, operations, parameters.Save);
            }
            catch (Exception e)
            {
                // VFX Graph の内部実装が変わってリフレクションが合わなくなったときはここに来る
                result = new VfxGraphEditor.Result
                {
                    ErrorMessage = $"編集に失敗しました（VFX Graph のバージョンが想定と異なる可能性があります）: {e}",
                };
            }

            response.Success = result.Success;
            response.ErrorMessage = result.ErrorMessage;
            response.AppliedOperations = result.AppliedOperations;
            response.Saved = result.Saved;
            response.Aliases = result.Aliases;
            response.Warnings = result.Warnings;
            response.Revision = result.Revision;
            response.Markdown = result.Markdown;
            return Task.FromResult(response);
        }

        private static bool TryReadOperations(string path, out JArray operations, out string error)
        {
            operations = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "OperationsFile を指定してください。";
                return false;
            }

            // 相対パスは Unity のカレントディレクトリ（プロジェクトのルート）から解決される
            if (!File.Exists(path))
            {
                error = $"操作リストのファイルが見つかりません: {path}";
                return false;
            }

            try
            {
                JToken root = JToken.Parse(File.ReadAllText(path));
                operations = root as JArray ?? (root as JObject)?["operations"] as JArray;
            }
            catch (JsonException e)
            {
                error = $"操作リストの JSON を読めません: {e.Message}";
                return false;
            }

            if (operations == null)
            {
                error = "操作リストは配列か、\"operations\" に配列を持つオブジェクトで書いてください。";
                return false;
            }

            error = "";
            return true;
        }
    }
}
