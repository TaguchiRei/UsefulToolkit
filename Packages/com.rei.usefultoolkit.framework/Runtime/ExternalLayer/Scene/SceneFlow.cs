using System;
using System.Collections.Generic;

namespace UsefulToolkit.Framework.External
{
    /// <summary>
    /// シーン遷移図の実行時表現。SceneFlowAssetのBuildが生成し、生成後は不変。
    /// </summary>
    public sealed class SceneFlow
    {
        /// <summary> 起動ノードが設定されていないことを表すNodeId </summary>
        public const int NoEntryNodeId = -1;

        private readonly Dictionary<int, SceneNode> _nodesById;

        /// <summary> 定義順のノード一覧 </summary>
        public IReadOnlyList<SceneNode> Nodes { get; }

        /// <summary>
        /// ゲーム中ずっと読み込まれ続けるシーン名の一覧。この順番で読み込まれ、
        /// Unloadも読み直しも行われない。
        /// </summary>
        public IReadOnlyList<string> PersistentScenes { get; }

        /// <summary> 起動時に最初に遷移するノードのID。未設定ならNoEntryNodeId </summary>
        public int EntryNodeId { get; }

        /// <summary> 起動時に読み込むシーングループのインデックス </summary>
        public int EntryGroupIndex { get; }

        /// <summary> 起動ノードが設定されているかどうか </summary>
        public bool HasEntry => EntryNodeId != NoEntryNodeId;

        /// <exception cref="ArgumentException">NodeIdが重複している、または起動ノードが存在しないときに出力</exception>
        public SceneFlow(
            IReadOnlyList<SceneNode> nodes,
            IReadOnlyList<string> persistentScenes,
            int entryNodeId,
            int entryGroupIndex)
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
            PersistentScenes = persistentScenes ?? throw new ArgumentNullException(nameof(persistentScenes));
            EntryNodeId = entryNodeId;
            EntryGroupIndex = entryGroupIndex;

            if (!HasEntry) return;

            if (!_nodesById.TryGetValue(entryNodeId, out var entryNode))
            {
                throw new ArgumentException($"起動ノード [{entryNodeId}] に対応するノードがありません。");
            }

            if (!entryNode.TryGetGroup(entryGroupIndex, out _))
            {
                throw new ArgumentException(
                    $"起動ノード [{entryNodeId}] にシーングループ [{entryGroupIndex}] は存在しません。");
            }
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
