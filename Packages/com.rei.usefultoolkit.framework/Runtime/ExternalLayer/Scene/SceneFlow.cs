using System;
using System.Collections.Generic;

namespace UsefulToolkit.Framework.External
{
    /// <summary>
    /// シーン遷移図の実行時表現。SceneFlowAssetのBuildが生成し、生成後は不変。
    /// NodeIdからノードを引くための辞書を構築済みなので、遷移のたびに線形探索しない。
    /// </summary>
    public sealed class SceneFlow
    {
        private readonly Dictionary<int, SceneNode> _nodesById;

        /// <summary> 定義順のノード一覧 </summary>
        public IReadOnlyList<SceneNode> Nodes { get; }

        /// <exception cref="ArgumentException">NodeIdが重複しているときに出力</exception>
        public SceneFlow(IReadOnlyList<SceneNode> nodes)
        {
            if (nodes is null) throw new ArgumentNullException(nameof(nodes));

            _nodesById = new Dictionary<int, SceneNode>(nodes.Count);

            foreach (var node in nodes)
            {
                if (!_nodesById.TryAdd(node.NodeId, node))
                {
                    throw new ArgumentException($"NodeId [{node.NodeId}] が重複しています。");
                }
            }

            Nodes = nodes;
        }

        /// <summary>
        /// NodeIdからノードを取得する。存在しなければfalseを返す。
        /// </summary>
        public bool TryGetNode(int nodeId, out SceneNode node)
        {
            return _nodesById.TryGetValue(nodeId, out node);
        }
    }
}
