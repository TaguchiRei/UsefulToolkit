using System;

namespace UsefulToolkit.Framework.BlackBoard
{
    /// <summary> シーングループを指し示す識別子(NodeId + GroupIndex) </summary>
    public readonly struct SceneGroupId : IEquatable<SceneGroupId>
    {
        private const int NoneNodeId = -1;

        /// <summary> まだ一度も遷移していないことを表す値 </summary>
        public static readonly SceneGroupId None = new(NoneNodeId, 0);

        public int NodeId { get; }
        public int GroupIndex { get; }

        /// <summary> Noneでないかどうか </summary>
        public bool HasValue => NodeId != NoneNodeId;

        public SceneGroupId(int nodeId, int groupIndex)
        {
            NodeId = nodeId;
            GroupIndex = groupIndex;
        }

        public bool Equals(SceneGroupId other) => NodeId == other.NodeId && GroupIndex == other.GroupIndex;

        public override bool Equals(object obj) => obj is SceneGroupId other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(NodeId, GroupIndex);

        public override string ToString() => HasValue ? $"node[{NodeId}] group[{GroupIndex}]" : "(none)";

        public static bool operator ==(SceneGroupId left, SceneGroupId right) => left.Equals(right);

        public static bool operator !=(SceneGroupId left, SceneGroupId right) => !left.Equals(right);
    }
}
