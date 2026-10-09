using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace UsefulToolkit.Editor.Ai
{
    /// <summary>
    /// VFX Graph に追加できるノードを探す uloop のカスタムツール。edit-vfx-graph の add 系の操作に渡す名前を調べるのに使う。
    /// 外部からは <c>uloop list-vfx-nodes --kind Block --query "position sphere"</c> で呼ぶ。
    /// </summary>
    [UnityCliLoopTool]
    public class ListVfxNodesTool : UnityCliLoopTool<ListVfxNodesSchema, ListVfxNodesResponse>
    {
        public override string ToolName => "list-vfx-nodes";

        protected override Task<ListVfxNodesResponse> ExecuteAsync(ListVfxNodesSchema parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var response = new ListVfxNodesResponse();
            if (!VfxGraphReflection.IsVfxGraphAvailable)
            {
                response.Success = false;
                response.ErrorMessage = "VFX Graph パッケージ (com.unity.visualeffectgraph) が見つかりません。";
                return Task.FromResult(response);
            }

            string[] words = (parameters.Query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            List<VfxNodeCatalog.Entry> matches = VfxNodeCatalog.GetEntries(parameters.Kind)
                .Where(e => words.All(w => Contains(e.Name, w) || Contains(e.Category, w) || Contains(e.TypeName, w)))
                .ToList();

            response.TotalCount = matches.Count;
            response.Nodes = matches.Take(Math.Max(0, parameters.MaxCount))
                .Select(e => new ListVfxNodesResponse.Node { Name = e.Name, Category = e.Category, TypeName = e.TypeName })
                .ToList();
            return Task.FromResult(response);
        }

        private static bool Contains(string text, string word)
        {
            return text != null && text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
