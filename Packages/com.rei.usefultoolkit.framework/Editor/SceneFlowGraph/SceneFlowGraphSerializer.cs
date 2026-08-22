using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UsefulToolkit.External.Scene;

namespace UsefulToolkit.Editor.SceneFlowGraph
{
    /// <summary>
    /// SceneFlowAssetのシリアライズデータとノードグラフの橋渡しを一手に引き受けるクラス。
    /// アセットへの書き込みはすべてSerializedObject経由で行うため、Undoは自動的に効く。
    ///
    /// SceneFlowAssetはシーンenumを型引数に取るが、ここではSerializedPropertyしか触らないので
    /// 型引数を知らないまま編集できる。グラフ側のクラスも同様に型引数を知らない。
    ///
    /// ノードは_nodes(通常ノード)と_simpleNodes(シンプルノード)の2配列に分かれているが、
    /// フィールド名(NodeId/DisplayName/Groups/NextNodeIds/EditorPosition)は両者で共通なので、
    /// GetField以下の個別フィールドアクセスはどちらの配列でも同じコードで済む。
    /// グラフ側(SceneFlowNodeView/SceneFlowGraphView)には「通常ノードのインデックス0..N-1、
    /// 続けてシンプルノードのインデックス」というグローバルな通し番号だけを見せ、
    /// どちらの配列に属すかはこのクラスの中で解決する。
    /// </summary>
    internal sealed class SceneFlowGraphSerializer
    {
        private const string NodesPath = "_nodes";
        private const string SimpleNodesPath = "_simpleNodes";
        private const string BootNodePath = "_bootNode";
        private const string NodeIdField = "NodeId";
        private const string DisplayNameField = "DisplayName";
        private const string GroupsField = "Groups";
        private const string NextNodeIdsField = "NextNodeIds";
        private const string EditorPositionField = "EditorPosition";
        private const string PersistentScenesField = "PersistentScenes";
        private const string EntryNodeIdField = "EntryNodeId";
        private const string EntryGroupIndexField = "EntryGroupIndex";

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

        /// <summary> 通常ノードの数。呼ぶ前にRefreshしておくこと </summary>
        public int RegularNodeCount => NodesProperty?.arraySize ?? 0;

        /// <summary> シンプルノードの数。呼ぶ前にRefreshしておくこと </summary>
        public int SimpleNodeCount => SimpleNodesProperty?.arraySize ?? 0;

        /// <summary> ノードの総数(通常+シンプル)。呼ぶ前にRefreshしておくこと </summary>
        public int NodeCount => RegularNodeCount + SimpleNodeCount;

        private SerializedProperty NodesProperty => SerializedObject.FindProperty(NodesPath);

        private SerializedProperty SimpleNodesProperty => SerializedObject.FindProperty(SimpleNodesPath);

        private SerializedProperty BootNodeProperty => SerializedObject.FindProperty(BootNodePath);

        /// <summary> Bootノードのデータを持っているかどうか。想定外の派生クラスへの保険 </summary>
        public bool HasBootNode => BootNodeProperty != null;

        /// <summary> 外部(Undoやインスペクタ)からの変更を取り込む </summary>
        public void Refresh() => SerializedObject.Update();

        /// <summary> 指定したグローバルインデックスがシンプルノード側かどうか </summary>
        public bool IsSimpleNode(int index) => index >= RegularNodeCount;

        /// <summary> 常駐シーンの編集UI(PropertyField)にそのまま渡すためのプロパティ </summary>
        public SerializedProperty GetPersistentScenesProperty()
        {
            return BootNodeProperty?.FindPropertyRelative(PersistentScenesField);
        }

        public Vector2 GetBootPosition()
        {
            return BootNodeProperty?.FindPropertyRelative(EditorPositionField).vector2Value ?? Vector2.zero;
        }

        /// <summary> Bootノードが指す起動ノードのID。未設定ならSceneFlow.NoEntryNodeId </summary>
        public int GetEntryNodeId()
        {
            return BootNodeProperty?.FindPropertyRelative(EntryNodeIdField).intValue ?? SceneFlow.NoEntryNodeId;
        }

        /// <summary> 起動ノードを設定する。線を外したときはSceneFlow.NoEntryNodeIdを渡す </summary>
        public void SetEntryNodeId(int nodeId)
        {
            if (BootNodeProperty == null) return;

            SerializedObject.Update();
            BootNodeProperty.FindPropertyRelative(EntryNodeIdField).intValue = nodeId;
            SerializedObject.ApplyModifiedProperties();
        }

        public int GetEntryGroupIndex()
        {
            return BootNodeProperty?.FindPropertyRelative(EntryGroupIndexField).intValue ?? 0;
        }

        public void SetEntryGroupIndex(int groupIndex)
        {
            if (BootNodeProperty == null) return;

            SerializedObject.Update();
            BootNodeProperty.FindPropertyRelative(EntryGroupIndexField).intValue = groupIndex;
            SerializedObject.ApplyModifiedProperties();
        }

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
        /// 指定座標に新しい通常ノードを追加する。NodeIdは両ノード配列を通して既存の最大値+1で自動採番する。
        /// </summary>
        /// <returns>追加したノードのNodeId</returns>
        public int AddNode(Vector2 position)
        {
            SerializedObject.Update();
            var newId = AddNodeCore(NodesProperty, position);
            SerializedObject.ApplyModifiedProperties();
            return newId;
        }

        /// <summary>
        /// 指定座標に新しいシンプルノードを追加する。Main+Additionalのみのシーングループを持つ、
        /// 役割分けをしないノード種別。NodeIdの採番規則はAddNodeと同じ。
        /// </summary>
        /// <returns>追加したノードのNodeId</returns>
        public int AddSimpleNode(Vector2 position)
        {
            SerializedObject.Update();
            var newId = AddNodeCore(SimpleNodesProperty, position);
            SerializedObject.ApplyModifiedProperties();
            return newId;
        }

        private int AddNodeCore(SerializedProperty nodesProperty, Vector2 position)
        {
            var index = nodesProperty.arraySize;
            var newId = CreateUniqueNodeId();

            nodesProperty.InsertArrayElementAtIndex(index);

            var node = nodesProperty.GetArrayElementAtIndex(index);
            node.FindPropertyRelative(NodeIdField).intValue = newId;
            node.FindPropertyRelative(DisplayNameField).stringValue = $"Node {newId}";
            node.FindPropertyRelative(EditorPositionField).vector2Value = position;

            // InsertArrayElementAtIndexは直前の要素の値を引き継ぐため、必ず作り直す
            var groups = node.FindPropertyRelative(GroupsField);
            groups.ClearArray();
            groups.arraySize = 1;
            node.FindPropertyRelative(NextNodeIdsField).ClearArray();

            return newId;
        }

        /// <summary>
        /// ノードを削除する。他ノードの遷移先からも消すので、宙に浮いたNodeIdは残らない。
        /// indicesはグローバルインデックス(通常ノードのあとにシンプルノードが続く通し番号)。
        /// </summary>
        public void RemoveNodes(IEnumerable<int> indices)
        {
            SerializedObject.Update();

            var nodes = NodesProperty;
            var simpleNodes = SimpleNodesProperty;
            var regularCount = nodes?.arraySize ?? 0;

            var regularIndices = new List<int>();
            var simpleIndices = new List<int>();

            foreach (var index in indices)
            {
                if (index < 0) continue;
                if (index < regularCount) regularIndices.Add(index);
                else simpleIndices.Add(index - regularCount);
            }

            var removedIds = new List<int>();

            RemoveByLocalIndices(nodes, regularIndices, removedIds);
            RemoveByLocalIndices(simpleNodes, simpleIndices, removedIds);

            foreach (var removedId in removedIds)
            {
                RemoveIdFromAllLinks(nodes, removedId);
                RemoveIdFromAllLinks(simpleNodes, removedId);
            }

            // 起動ノードが消えたなら、宙に浮いたIDを残さないようBootノード側も外す
            var entryNodeIdProperty = BootNodeProperty?.FindPropertyRelative(EntryNodeIdField);

            if (entryNodeIdProperty != null && removedIds.Contains(entryNodeIdProperty.intValue))
            {
                entryNodeIdProperty.intValue = SceneFlow.NoEntryNodeId;
            }

            SerializedObject.ApplyModifiedProperties();
        }

        /// <summary> 指定した配列内のローカルインデックスを削除しつつ、削除したNodeIdをremovedIdsへ積む </summary>
        private static void RemoveByLocalIndices(SerializedProperty array, List<int> localIndices, List<int> removedIds)
        {
            if (array == null) return;

            foreach (var index in localIndices)
            {
                if (index < 0 || index >= array.arraySize) continue;
                removedIds.Add(array.GetArrayElementAtIndex(index).FindPropertyRelative(NodeIdField).intValue);
            }

            // 後ろから消さないと、削除するたびに前方の要素のインデックスがずれる
            localIndices.Sort();
            for (var i = localIndices.Count - 1; i >= 0; i--)
            {
                var index = localIndices[i];
                if (index < 0 || index >= array.arraySize) continue;
                array.DeleteArrayElementAtIndex(index);
            }
        }

        /// <summary>
        /// fromのノードからtoNodeIdへの遷移を足す。すでにある場合は何もしない。
        /// fromは通常ノード/シンプルノードのどちらでもよく、toNodeIdも両配列を横断して構わない。
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
        public void SetPositions(IReadOnlyList<(int index, Vector2 position)> positions, Vector2? bootPosition = null)
        {
            if (positions.Count == 0 && bootPosition == null) return;

            SerializedObject.Update();

            foreach (var (index, position) in positions)
            {
                GetField(index, EditorPositionField).vector2Value = position;
            }

            if (bootPosition.HasValue && BootNodeProperty != null)
            {
                BootNodeProperty.FindPropertyRelative(EditorPositionField).vector2Value = bootPosition.Value;
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
        /// 変更先のIDが他のノード(通常/シンプルを問わない)で使われている場合は何もせずfalseを返す。
        /// </summary>
        public bool TryChangeNodeId(int index, int newId)
        {
            SerializedObject.Update();

            var nodeProperty = ResolveNodeProperty(index);
            if (nodeProperty == null) return false;

            var oldId = nodeProperty.FindPropertyRelative(NodeIdField).intValue;
            if (oldId == newId) return true;

            if (IsNodeIdUsed(newId, index)) return false;

            nodeProperty.FindPropertyRelative(NodeIdField).intValue = newId;

            ReplaceIdInAllLinks(NodesProperty, oldId, newId);
            ReplaceIdInAllLinks(SimpleNodesProperty, oldId, newId);

            // Bootノードからの線もIDで持っているので、一緒に付け替える
            var entryNodeIdProperty = BootNodeProperty?.FindPropertyRelative(EntryNodeIdField);

            if (entryNodeIdProperty != null && entryNodeIdProperty.intValue == oldId)
            {
                entryNodeIdProperty.intValue = newId;
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
            var simpleNodes = SimpleNodesProperty;
            if (nodes == null && simpleNodes == null) return messages;

            var seenIds = new HashSet<int>();
            var duplicatedIds = new HashSet<int>();
            var allIds = new HashSet<int>();

            void CollectIds(SerializedProperty array)
            {
                if (array == null) return;

                for (var i = 0; i < array.arraySize; i++)
                {
                    var nodeId = array.GetArrayElementAtIndex(i).FindPropertyRelative(NodeIdField).intValue;
                    allIds.Add(nodeId);
                    if (!seenIds.Add(nodeId)) duplicatedIds.Add(nodeId);
                }
            }

            CollectIds(nodes);
            CollectIds(simpleNodes);

            foreach (var duplicatedId in duplicatedIds)
            {
                messages.Add($"NodeId [{duplicatedId}] が重複しています。このままではBuildが例外を投げます。");
            }

            void ValidateGroupsAndLinks(SerializedProperty array)
            {
                if (array == null) return;

                for (var i = 0; i < array.arraySize; i++)
                {
                    var node = array.GetArrayElementAtIndex(i);
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
            }

            ValidateGroupsAndLinks(nodes);
            ValidateGroupsAndLinks(simpleNodes);

            ValidateBootNode(messages);

            return messages;
        }

        /// <summary>
        /// Bootノードの検証。起動ノードはBuildの時点で存在確認されるため、
        /// ここで気づけないと実行時まで分からない。起動ノードは通常/シンプルのどちらでもよい。
        /// </summary>
        private void ValidateBootNode(List<string> messages)
        {
            if (BootNodeProperty == null)
            {
                messages.Add("Bootノードのデータ(_bootNode)を読み取れませんでした。SceneFlowAsset<T>を継承しているか確認してください。");
                return;
            }

            if (GetPersistentScenesProperty() is { arraySize: 0 })
            {
                messages.Add("Bootノードに常駐シーンがありません。常駐システムを置くシーンがある場合はここに指定してください。");
            }

            var entryNodeId = GetEntryNodeId();

            if (entryNodeId == SceneFlow.NoEntryNodeId)
            {
                messages.Add("Bootノードから起動ノードへ線が引かれていません。起動時にどのノードへ遷移するか決まりません。");
                return;
            }

            if (!TryFindNodeById(entryNodeId, out var entryNode))
            {
                messages.Add($"起動ノード [{entryNodeId}] に対応するノードがありません。このままではBuildが例外を投げます。");
                return;
            }

            var entryGroupIndex = GetEntryGroupIndex();
            var groupCount = entryNode.FindPropertyRelative(GroupsField).arraySize;

            if (entryGroupIndex < 0 || entryGroupIndex >= groupCount)
            {
                messages.Add($"起動ノード [{entryNodeId}] にシーングループ [{entryGroupIndex}] は存在しません。" +
                             $"このままではBuildが例外を投げます。");
            }
        }

        /// <summary> NodeIdから該当ノードのプロパティを探す。通常/シンプルの両配列を横断して探す </summary>
        private bool TryFindNodeById(int nodeId, out SerializedProperty node)
        {
            var nodes = NodesProperty;

            if (nodes != null)
            {
                for (var i = 0; i < nodes.arraySize; i++)
                {
                    var candidate = nodes.GetArrayElementAtIndex(i);
                    if (candidate.FindPropertyRelative(NodeIdField).intValue != nodeId) continue;

                    node = candidate;
                    return true;
                }
            }

            var simpleNodes = SimpleNodesProperty;

            if (simpleNodes != null)
            {
                for (var i = 0; i < simpleNodes.arraySize; i++)
                {
                    var candidate = simpleNodes.GetArrayElementAtIndex(i);
                    if (candidate.FindPropertyRelative(NodeIdField).intValue != nodeId) continue;

                    node = candidate;
                    return true;
                }
            }

            node = null;
            return false;
        }

        /// <summary> グローバルインデックスから該当ノードのプロパティを解決する </summary>
        private SerializedProperty ResolveNodeProperty(int globalIndex)
        {
            var nodes = NodesProperty;
            var regularCount = nodes?.arraySize ?? 0;

            if (globalIndex >= 0 && globalIndex < regularCount)
            {
                return nodes.GetArrayElementAtIndex(globalIndex);
            }

            var simpleNodes = SimpleNodesProperty;
            var simpleIndex = globalIndex - regularCount;

            if (simpleNodes != null && simpleIndex >= 0 && simpleIndex < simpleNodes.arraySize)
            {
                return simpleNodes.GetArrayElementAtIndex(simpleIndex);
            }

            return null;
        }

        private SerializedProperty GetField(int index, string fieldName)
        {
            return ResolveNodeProperty(index).FindPropertyRelative(fieldName);
        }

        /// <summary> idが、excludeGlobalIndexのノードを除く他のノード(通常/シンプル問わず)で使われているか </summary>
        private bool IsNodeIdUsed(int id, int excludeGlobalIndex)
        {
            var nodes = NodesProperty;
            var regularCount = nodes?.arraySize ?? 0;

            for (var i = 0; i < regularCount; i++)
            {
                if (i == excludeGlobalIndex) continue;
                if (nodes.GetArrayElementAtIndex(i).FindPropertyRelative(NodeIdField).intValue == id) return true;
            }

            var simpleNodes = SimpleNodesProperty;
            var simpleCount = simpleNodes?.arraySize ?? 0;

            for (var i = 0; i < simpleCount; i++)
            {
                if (regularCount + i == excludeGlobalIndex) continue;
                if (simpleNodes.GetArrayElementAtIndex(i).FindPropertyRelative(NodeIdField).intValue == id) return true;
            }

            return false;
        }

        private int CreateUniqueNodeId()
        {
            var maxId = -1;

            void Scan(SerializedProperty array)
            {
                if (array == null) return;

                for (var i = 0; i < array.arraySize; i++)
                {
                    var nodeId = array.GetArrayElementAtIndex(i).FindPropertyRelative(NodeIdField).intValue;
                    if (nodeId > maxId) maxId = nodeId;
                }
            }

            Scan(NodesProperty);
            Scan(SimpleNodesProperty);

            return maxId + 1;
        }

        private static void RemoveIdFromAllLinks(SerializedProperty nodes, int removedId)
        {
            if (nodes == null) return;

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

        private static void ReplaceIdInAllLinks(SerializedProperty nodes, int oldId, int newId)
        {
            if (nodes == null) return;

            for (var i = 0; i < nodes.arraySize; i++)
            {
                var nextIds = nodes.GetArrayElementAtIndex(i).FindPropertyRelative(NextNodeIdsField);

                for (var j = 0; j < nextIds.arraySize; j++)
                {
                    var element = nextIds.GetArrayElementAtIndex(j);
                    if (element.intValue == oldId) element.intValue = newId;
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
