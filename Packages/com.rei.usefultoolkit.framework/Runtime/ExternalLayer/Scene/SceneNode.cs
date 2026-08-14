using System;
using System.Collections.Generic;

namespace UsefulToolkit.Framework.External
{
    /// <summary>
    /// シーン遷移図上の1地点。同じ地点でもシーンの組み合わせを差し替えられるよう、
    /// 複数のSceneGroupを持てる。
    /// 遷移先はSceneNodeの参照ではなくNodeIdで保持する——ノード同士の相互参照は
    /// Unityのシリアライザで表現できないため、ID経由で引く形に統一している。
    /// </summary>
    public sealed class SceneNode
    {
        /// <summary> SceneFlow内で一意なID </summary>
        public int NodeId { get; }

        /// <summary> ノードエディタで付けた表示名。ログ用途のみで、遷移の挙動には影響しない </summary>
        public string DisplayName { get; }

        /// <summary> このノードで選べるシーンの組み合わせ </summary>
        public IReadOnlyList<SceneGroup> Groups { get; }

        /// <summary> このノードから遷移できるノードのID一覧 </summary>
        public IReadOnlyList<int> NextNodeIds { get; }

        public SceneNode(int nodeId, string displayName, IReadOnlyList<SceneGroup> groups,
            IReadOnlyList<int> nextNodeIds)
        {
            NodeId = nodeId;
            DisplayName = displayName ?? string.Empty;
            Groups = groups ?? throw new ArgumentNullException(nameof(groups));
            NextNodeIds = nextNodeIds ?? throw new ArgumentNullException(nameof(nextNodeIds));
        }

        /// <summary>
        /// インデックスを指定してSceneGroupを取得する。範囲外なら例外ではなくfalseを返す。
        /// </summary>
        public bool TryGetGroup(int groupIndex, out SceneGroup group)
        {
            if (groupIndex < 0 || groupIndex >= Groups.Count)
            {
                group = null;
                return false;
            }

            group = Groups[groupIndex];
            return true;
        }
    }
}
