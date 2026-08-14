using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UsefulToolkit.Framework.External;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// SceneFlowAssetのシリアライズデータとノードグラフの橋渡しを一手に引き受けるクラス。
    /// アセットへの書き込みはすべてSerializedObject経由で行うため、Undoは自動的に効く。
    ///
    /// SceneFlowAssetはシーンenumを型引数に取るが、ここではSerializedPropertyしか触らないので
    /// 型引数を知らないまま編集できる。グラフ側のクラスも同様に型引数を知らない。
    /// </summary>
    internal sealed class SceneFlowGraphSerializer
    {
        private const string NodesPath = "_nodes";
        private const string NodeIdField = "NodeId";
        private const string DisplayNameField = "DisplayName";
        private const string GroupsField = "Groups";
        private const string NextNodeIdsField = "NextNodeIds";
        private const string EditorPositionField = "EditorPosition";

        /// <summary> 編集対象のアセット </summary>
        public SceneFlowAssetBase Asset { get; }

        /// <summary> ノード内のPropertyFieldをバインドするために外へ公開している </summary>
        public SerializedObject SerializedObject { get; }

        public SceneFlowGraphSerializer(SceneFlowAssetBase asset)
        {
            Asset = asset;
            SerializedObject = new SerializedObject(asset);
        }

        /// <summary> _nodesが見つからない場合は編集できない。想定外の派生クラスへの保険 </summary>
        public bool IsValid => NodesProperty != null;

        /// <summary> ノード数。呼ぶ前にRefreshしておくこと </summary>
        public int NodeCount => NodesProperty?.arraySize ?? 0;

        private SerializedProperty NodesProperty => SerializedObject.FindProperty(NodesPath);

        /// <summary> 外部(Undoやインスペクタ)からの変更を取り込む </summary>
        public void Refresh() => SerializedObject.Update();

        public int GetNodeId(int index) => GetField(index, NodeIdField).intValue;

        public string GetDisplayName(int index) => GetField(index, DisplayNameField).stringValue;

        public Vector2 GetPosition(int index) => GetField(index, EditorPositionField).vector2Value;

        /// <summary> ノード内のグループ編集UI(PropertyField)にそのまま渡すためのプロパティ </summary>
        public SerializedProperty GetGroupsProperty(int index) => GetField(index, GroupsField);

        public int GetGroupCount(int index) => GetField(index, GroupsField).arraySize;

        public List<int> GetNextNodeIds(int index)
        {
            var nextIds = GetField(index, NextNodeIdsField);
            var result = new List<int>(nextIds.arraySize);

            for (var i = 0; i < nextIds.arraySize; i++)
            {
                result.Add(nextIds.GetArrayElementAtIndex(i).intValue);
            }

            return result;
        }

        /// <summary>
        /// 指定座標に新しいノードを追加する。NodeIdは既存の最大値+1で自動採番する。
        /// </summary>
        /// <returns>追加したノードのNodeId</returns>
        public int AddNode(Vector2 position)
        {
            SerializedObject.Update();

            var nodes = NodesProperty;
            var index = nodes.arraySize;
            var newId = CreateUniqueNodeId();

            nodes.InsertArrayElementAtIndex(index);

            var node = nodes.GetArrayElementAtIndex(index);
            node.FindPropertyRelative(NodeIdField).intValue = newId;
            node.FindPropertyRelative(DisplayNameField).stringValue = $"Node {newId}";
            node.FindPropertyRelative(EditorPositionField).vector2Value = position;

            // InsertArrayElementAtIndexは直前の要素の値を引き継ぐため、必ず作り直す
            var groups = node.FindPropertyRelative(GroupsField);
            groups.ClearArray();
            groups.arraySize = 1;
            node.FindPropertyRelative(NextNodeIdsField).ClearArray();

            SerializedObject.ApplyModifiedProperties();
            return newId;
        }

        /// <summary>
        /// ノードを削除する。他ノードの遷移先からも消すので、宙に浮いたNodeIdは残らない。
        /// </summary>
        public void RemoveNodes(IEnumerable<int> indices)
        {
            SerializedObject.Update();

            var nodes = NodesProperty;
            var sorted = new List<int>(indices);
            var removedIds = new List<int>(sorted.Count);

            foreach (var index in sorted)
            {
                if (index < 0 || index >= nodes.arraySize) continue;
                removedIds.Add(nodes.GetArrayElementAtIndex(index).FindPropertyRelative(NodeIdField).intValue);
            }

            // 後ろから消さないと、削除するたびに前方の要素のインデックスがずれる
            sorted.Sort();
            for (var i = sorted.Count - 1; i >= 0; i--)
            {
                var index = sorted[i];
                if (index < 0 || index >= nodes.arraySize) continue;
                nodes.DeleteArrayElementAtIndex(index);
            }

            foreach (var removedId in removedIds)
            {
                RemoveIdFromAllLinks(nodes, removedId);
            }

            SerializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// fromのノードからtoNodeIdへの遷移を足す。すでにある場合は何もしない。
        /// </summary>
        public void AddLink(int fromIndex, int toNodeId)
        {
            SerializedObject.Update();

            var nextIds = GetField(fromIndex, NextNodeIdsField);
            if (ContainsId(nextIds, toNodeId)) return;

            nextIds.arraySize++;
            nextIds.GetArrayElementAtIndex(nextIds.arraySize - 1).intValue = toNodeId;

            SerializedObject.ApplyModifiedProperties();
        }

        /// <summary> fromのノードからtoNodeIdへの遷移を取り除く </summary>
        public void RemoveLink(int fromIndex, int toNodeId)
        {
            SerializedObject.Update();

            var nextIds = GetField(fromIndex, NextNodeIdsField);

            for (var i = nextIds.arraySize - 1; i >= 0; i--)
            {
                if (nextIds.GetArrayElementAtIndex(i).intValue == toNodeId)
                {
                    nextIds.DeleteArrayElementAtIndex(i);
                }
            }

            SerializedObject.ApplyModifiedProperties();
        }

        /// <summary> ドラッグ後の座標をまとめて書き戻す。1ドラッグ = Undo1回になるようまとめている </summary>
        public void SetPositions(IReadOnlyList<(int index, Vector2 position)> positions)
        {
            if (positions.Count == 0) return;

            SerializedObject.Update();

            foreach (var (index, position) in positions)
            {
                GetField(index, EditorPositionField).vector2Value = position;
            }

            SerializedObject.ApplyModifiedProperties();
        }

        public void SetDisplayName(int index, string displayName)
        {
            SerializedObject.Update();
            GetField(index, DisplayNameField).stringValue = displayName;
            SerializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// NodeIdを変更する。他ノードが持つ遷移先のIDも一緒に付け替えるので、接続は維持される。
        /// 変更先のIDが他のノードで使われている場合は何もせずfalseを返す。
        /// </summary>
        public bool TryChangeNodeId(int index, int newId)
        {
            SerializedObject.Update();

            var nodes = NodesProperty;
            var oldId = nodes.GetArrayElementAtIndex(index).FindPropertyRelative(NodeIdField).intValue;
            if (oldId == newId) return true;

            for (var i = 0; i < nodes.arraySize; i++)
            {
                if (i == index) continue;
                if (nodes.GetArrayElementAtIndex(i).FindPropertyRelative(NodeIdField).intValue == newId) return false;
            }

            nodes.GetArrayElementAtIndex(index).FindPropertyRelative(NodeIdField).intValue = newId;

            for (var i = 0; i < nodes.arraySize; i++)
            {
                var nextIds = nodes.GetArrayElementAtIndex(i).FindPropertyRelative(NextNodeIdsField);

                for (var j = 0; j < nextIds.arraySize; j++)
                {
                    var element = nextIds.GetArrayElementAtIndex(j);
                    if (element.intValue == oldId) element.intValue = newId;
                }
            }

            SerializedObject.ApplyModifiedProperties();
            return true;
        }

        /// <summary>
        /// Buildが例外を投げる前にエディタ上で気づけるよう、遷移図の不備を集める。
        /// </summary>
        public List<string> Validate()
        {
            var messages = new List<string>();
            var nodes = NodesProperty;
            if (nodes == null) return messages;

            var seenIds = new HashSet<int>();
            var duplicatedIds = new HashSet<int>();
            var allIds = new HashSet<int>();

            for (var i = 0; i < nodes.arraySize; i++)
            {
                var nodeId = nodes.GetArrayElementAtIndex(i).FindPropertyRelative(NodeIdField).intValue;
                allIds.Add(nodeId);
                if (!seenIds.Add(nodeId)) duplicatedIds.Add(nodeId);
            }

            foreach (var duplicatedId in duplicatedIds)
            {
                messages.Add($"NodeId [{duplicatedId}] が重複しています。このままではBuildが例外を投げます。");
            }

            for (var i = 0; i < nodes.arraySize; i++)
            {
                var node = nodes.GetArrayElementAtIndex(i);
                var nodeId = node.FindPropertyRelative(NodeIdField).intValue;

                if (node.FindPropertyRelative(GroupsField).arraySize == 0)
                {
                    messages.Add($"ノード [{nodeId}] にシーングループがありません。このノードへは遷移できません。");
                }

                var nextIds = node.FindPropertyRelative(NextNodeIdsField);

                for (var j = 0; j < nextIds.arraySize; j++)
                {
                    var nextId = nextIds.GetArrayElementAtIndex(j).intValue;
                    if (!allIds.Contains(nextId))
                    {
                        messages.Add($"ノード [{nodeId}] の遷移先 [{nextId}] に対応するノードがありません。");
                    }
                }
            }

            return messages;
        }

        private SerializedProperty GetField(int index, string fieldName)
        {
            return NodesProperty.GetArrayElementAtIndex(index).FindPropertyRelative(fieldName);
        }

        private int CreateUniqueNodeId()
        {
            var nodes = NodesProperty;
            var maxId = -1;

            for (var i = 0; i < nodes.arraySize; i++)
            {
                var nodeId = nodes.GetArrayElementAtIndex(i).FindPropertyRelative(NodeIdField).intValue;
                if (nodeId > maxId) maxId = nodeId;
            }

            return maxId + 1;
        }

        private static void RemoveIdFromAllLinks(SerializedProperty nodes, int removedId)
        {
            for (var i = 0; i < nodes.arraySize; i++)
            {
                var nextIds = nodes.GetArrayElementAtIndex(i).FindPropertyRelative(NextNodeIdsField);

                for (var j = nextIds.arraySize - 1; j >= 0; j--)
                {
                    if (nextIds.GetArrayElementAtIndex(j).intValue == removedId)
                    {
                        nextIds.DeleteArrayElementAtIndex(j);
                    }
                }
            }
        }

        private static bool ContainsId(SerializedProperty intArray, int value)
        {
            for (var i = 0; i < intArray.arraySize; i++)
            {
                if (intArray.GetArrayElementAtIndex(i).intValue == value) return true;
            }

            return false;
        }
    }
}
